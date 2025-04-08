using System;
using System.Linq;
using System.Text;

namespace SAMCS
{

    public class SoftwareAutomaticMouth
    {
        private readonly SoftwareAutomaticMouthTables mSoftwareAutomaticMouthTables = new SoftwareAutomaticMouthTables();

        private byte[] mPhonemeLengthOutput = new byte[60];
        private byte[] mPhonemeIndexOutput = new byte[60];
        private byte[] mPhonemeLength = new byte[256];
        private byte[] mPhonemeIndex = new byte[256];
        private byte[] mStressOutput = new byte[60];
        private byte[] mStress = new byte[256];
        private SpeechRenderer mSpeechRenderer;
        private bool mSing = false;
        private Reciter mReciter;
        private byte mSpeed = 72;
        private byte mPitch = 64;

        public byte[] PhonemeLengthOutput => mPhonemeLengthOutput;
        public byte[] PhonemeIndexOutput => mPhonemeIndexOutput;
        public byte[] StressOutput => mStressOutput;

        public bool Sing
        {
            get => mSing;
            set => mSing = value;
        }

        public byte Speed
        {
            get => mSpeed;
            set => mSpeed = value;
        }

        public byte Pitch
        {
            get => mPitch;
            set => mPitch = value;
        }

        public byte Mouth
        {
            get => mSpeechRenderer.Mouth;
            set => mSpeechRenderer.Mouth = value;
        }

        public byte Throat
        {
            get => mSpeechRenderer.Throat;
            set => mSpeechRenderer.Throat = value;
        }

        /// <summary>
        /// Constructor for SAM
        /// </summary>
        /// <param name="pitch">Voice pitch of SAM</param>
        /// <param name="speed">Talking speed of SAM</param>
        /// <param name="mouth">Mouth size of SAM</param>
        /// <param name="throat">Throat size of SAM</param>
        /// <param name="sing">Should SAM sing?</param>
        public SoftwareAutomaticMouth(byte pitch = 64, byte speed = 72, byte mouth = 128, byte throat = 128, bool sing = false)
        {
            mPitch = pitch;
            mSpeed = speed;
            mSing = sing;
            mReciter = new Reciter();
            mSpeechRenderer = new SpeechRenderer();
            mSpeechRenderer.SetMouthThroat(mouth, throat);

            Array.Clear(mStress, 0, mStress.Length);
            Array.Clear(mPhonemeLength, 0, mPhonemeLength.Length);
            Array.Clear(mStressOutput, 0, 60);
            Array.Clear(mPhonemeIndexOutput, 0, 60);
            Array.Clear(mPhonemeLengthOutput, 0, 60);

            mPhonemeIndex[255] = 32; // prevents buffer overflow apparently
        }

        /// <summary>
        /// Speaks the given text and returns an audio buffer.
        /// </summary>
        /// <param name="text">Input text that will be spoken</param>
        /// <param name="phonetic">Whether the phonemes should be generated or not</param>
        /// <returns>A non-null audio buffer object if no errors occurred, otherwise null</returns>
        public AudioBuffer Speak(string text, bool phonetic = false)
        {

            AudioBuffer buffer = new AudioBuffer();
            byte[] input = new byte[256];
            Array.Clear(input, 0, input.Length);
            byte[] tmp = Encoding.ASCII.GetBytes(text);
            Array.Copy(tmp, input, tmp.Length);
            int offset = tmp.Length;

            if (!phonetic)
            {
                input[offset++] = (byte)'['; // Add terminator for text input

                if (mReciter.TextToPhonemes(input) <= 0)
                    return null;
            }
            else
                input[offset++] = 0x9b; // special phonetic character

            if (!Parser1(input))
                return null;
            
            Parser2();
            CopyStress();
            SetPhonemeLength();
            AdjustLengths();
            byte currentIndex = Code41240();; // cpux
            do
            {
                byte phIndex = mPhonemeIndex[currentIndex];
                if (phIndex > 80)
                {
                    mPhonemeIndex[currentIndex] = 255;
                    break;
                }
                currentIndex++;
            } while (currentIndex != 0);

            InsertBreath(ref currentIndex);

            PrepareOutput(ref buffer);

            return buffer;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////
        ///                                                                                     ///
        ///                                                                                     ///
        ///                        !!!NOT NEEDED FOR THE END USER!!!                            ///
        ///   Intended for internal use only, no need to look at this or even understand this   ///
        ///                                                                                     ///
        ///                                                                                     ///
        ///////////////////////////////////////////////////////////////////////////////////////////

        // The input[] buffer contains a string of phonemes and stress markers along
        // the lines of:
        //
        //     DHAX KAET IHZ AH5GLIY. <0x9B>
        //
        // The byte 0x9B marks the end of the buffer. Some phonemes are 2 bytes
        // long, such as "DH" and "AX". Others are 1 byte long, such as "T" and "Z".
        // There are also stress markers, such as "5" and ".".
        //
        // The first character of the phonemes are stored in the table SamTabs.signInputTable1[].
        // The second character of the phonemes are stored in the table SamTabs.signInputTable2[].
        // The stress characters are arranged in low to high stress order in stressInputTable[].
        //
        // The following process is used to parse the input[] buffer:
        //
        // Repeat until the <0x9B> character is reached:
        //
        //        First, a search is made for a 2 character match for phonemes that do not
        //        end with the '*' (wildcard) character. On a match, the index of the phoneme
        //        is added to phonemeIndex[] and the buffer position is advanced 2 bytes.
        //
        //        If this fails, a search is made for a 1 character match against all
        //        phoneme names ending with a '*' (wildcard). If this succeeds, the
        //        phoneme is added to phonemeIndex[] and the buffer position is advanced
        //        1 byte.
        //
        //        If this fails, search for a 1 character match in the stressInputTable[].
        //        If this succeeds, the stress value is placed in the last mStress[] table
        //        at the same index of the last added phoneme, and the buffer position is
        //        advanced by 1 byte.
        //
        //        If this fails, return a 0.
        //
        // On success:
        //
        //    1. phonemeIndex[] will contain the index of all the phonemes.
        //    2. The last index in phonemeIndex[] will be 255.
        //    3. mStress[] will contain the stress value for each phoneme

        // input[] holds the string of phonemes, each two bytes wide
        // signInputTable1[] holds the first character of each phoneme
        // signInputTable2[] holds te second character of each phoneme
        // phonemeIndex[] holds the indexes of the phonemes after parsing input[]
        //
        // The parser scans through the input[], finding the names of the phonemes
        // by searching signInputTable1[] and signInputTable2[]. On a match, it
        // copies the index of the phoneme into the phonemeIndexTable[].
        //
        // The character <0x9B> marks the end of text in input[]. When it is reached,
        // the index 255 is placed at the end of the phonemeIndexTable[], and the
        // function returns with a 1 indicating success.
        private bool Parser1(byte[] input)
        {
            byte sign1;
            byte sign2;
            byte position = 0;
            byte charIndex = 0;

            // THIS CODE MATCHES THE PHONEME LETTERS TO THE TABLE
            while (true)
            {
                // GET THE FIRST CHARACTER FROM THE PHONEME BUFFER
                sign1 = (byte)input[charIndex];
                // TEST FOR 155 (�) END OF LINE MARKER
                if (sign1 == 155)
                {
                    // MARK ENDPOINT AND RETURN
                    mPhonemeIndex[position] = 255; //mark endpoint
                    // REACHED END OF PHONEMES, SO EXIT
                    return true; //all ok
                }

                // GET THE NEXT CHARACTER FROM THE BUFFER
                charIndex++;
                sign2 = (byte)input[charIndex];

                // NOW sign1 = FIRST CHARACTER OF PHONEME, AND sign2 = SECOND CHARACTER OF PHONEME

                // TRY TO MATCH PHONEMES ON TWO TWO-CHARACTER NAME
                // IGNORE PHONEMES IN TABLE ENDING WITH WILDCARDS

                // SET INDEX TO 0
                byte subIndex = 0;
                byte currentChar;
            pos41095:

                // GET FIRST CHARACTER AT POSITION Y IN signInputTable
                // --> should change name to PhonemeNameTable1
                currentChar = (byte)mSoftwareAutomaticMouthTables.signInputTable1[subIndex];

                // FIRST CHARACTER MATCHES?
                if (currentChar == sign1)
                {
                    // GET THE CHARACTER FROM THE PhonemeSecondLetterTable
                    currentChar = (byte)mSoftwareAutomaticMouthTables.signInputTable2[subIndex];
                    // NOT A SPECIAL AND MATCHES SECOND CHARACTER?
                    if ((currentChar != '*') && (currentChar == sign2))
                    {
                        // STORE THE INDEX OF THE PHONEME INTO THE phomeneIndexTable
                        mPhonemeIndex[position] = subIndex;

                        // ADVANCE THE POINTER TO THE phonemeIndexTable
                        position++;
                        // ADVANCE THE POINTER TO THE phonemeInputBuffer
                        charIndex++;

                        // CONTINUE PARSING
                        continue;
                    }
                }

                // NO MATCH, TRY TO MATCH ON FIRST CHARACTER TO WILDCARD NAMES (ENDING WITH '*')

                // ADVANCE TO THE NEXT POSITION
                subIndex++;
                // IF NOT END OF TABLE, CONTINUE
                if (subIndex != 81) goto pos41095;

                // REACHED END OF TABLE WITHOUT AN EXACT (2 CHARACTER) MATCH.
                // THIS TIME, SEARCH FOR A 1 CHARACTER MATCH AGAINST THE WILDCARDS

                // RESET THE INDEX TO POINT TO THE START OF THE PHONEME NAME TABLE
                subIndex = 0;
            pos41134:
                // DOES THE PHONEME IN THE TABLE END WITH '*'?
                if (mSoftwareAutomaticMouthTables.signInputTable2[subIndex] == '*')
                {
                    // DOES THE FIRST CHARACTER MATCH THE FIRST LETTER OF THE PHONEME
                    if (mSoftwareAutomaticMouthTables.signInputTable1[subIndex] == sign1)
                    {
                        // SAVE THE POSITION AND MOVE AHEAD
                        mPhonemeIndex[position] = subIndex;

                        // ADVANCE THE POINTER
                        position++;

                        // CONTINUE THROUGH THE LOOP
                        continue;
                    }
                }
                subIndex++;
                if (subIndex != 81) goto pos41134; //81 is size of PHONEME NAME table

                // FAILED TO MATCH WITH A WILDCARD. ASSUME THIS IS A STRESS
                // CHARACTER. SEARCH THROUGH THE STRESS TABLE

                // SET INDEX TO POSITION 8 (END OF STRESS TABLE)
                subIndex = 8;

                // WALK BACK THROUGH TABLE LOOKING FOR A MATCH
                while ((sign1 != mSoftwareAutomaticMouthTables.stressInputTable[subIndex]) && (subIndex > 0))
                {
                    // DECREMENT INDEX
                    subIndex--;
                }

                // REACHED THE END OF THE SEARCH WITHOUT BREAKING OUT OF LOOP?
                if (subIndex == 0)
                {
                    // FAILED TO MATCH ANYTHING, RETURN 0 ON FAILURE
                    return false;
                }
                // SET THE STRESS FOR THE PRIOR PHONEME
                mStress[position - 1] = subIndex;
            } //while
        }

        // Rewrites the phonemes using the following rules:
        //
        //       <DIPHTONG ENDING WITH WX> -> <DIPHTONG ENDING WITH WX> WX
        //       <DIPHTONG NOT ENDING WITH WX> -> <DIPHTONG NOT ENDING WITH WX> YX
        //       UL -> AX L
        //       UM -> AX M
        //       <STRESSED VOWEL> <SILENCE> <STRESSED VOWEL> -> <STRESSED VOWEL> <SILENCE> Q <VOWEL>
        //       T R -> CH R
        //       D R -> J R
        //       <VOWEL> R -> <VOWEL> RX
        //       <VOWEL> L -> <VOWEL> LX
        //       G S -> G Z
        //       K <VOWEL OR DIPHTONG NOT ENDING WITH IY> -> KX <VOWEL OR DIPHTONG NOT ENDING WITH IY>
        //       G <VOWEL OR DIPHTONG NOT ENDING WITH IY> -> GX <VOWEL OR DIPHTONG NOT ENDING WITH IY>
        //       S P -> S B
        //       S T -> S D
        //       S K -> S G
        //       S KX -> S GX
        //       <ALVEOLAR> UW -> <ALVEOLAR> UX
        //       CH -> CH CH' (CH requires two phonemes to represent it)
        //       J -> J J' (J requires two phonemes to represent it)
        //       <UNSTRESSED VOWEL> T <PAUSE> -> <UNSTRESSED VOWEL> DX <PAUSE>
        //       <UNSTRESSED VOWEL> D <PAUSE>  -> <UNSTRESSED VOWEL> DX <PAUSE>

        private void Parser2()
        {
            byte pos = 0;
            byte stress = 0;
            byte length = 0;

            // Loop through phonemes
            while (true)
            {
                // SET X TO THE CURRENT POSITION
                byte currentPos = pos;
                // GET THE PHONEME AT THE CURRENT POSITION
                byte phIndex = mPhonemeIndex[pos];

                // Is phoneme pause?
                if (phIndex == 0)
                {
                    // Move ahead to the
                    pos++;
                    continue;
                }

                // If end of phonemes flag reached, exit routine
                if (phIndex == 255) return;

                // Copy the current phoneme index to Y
                byte tmpIndex = phIndex;

                // RULE:
                //       <DIPHTONG ENDING WITH WX> -> <DIPHTONG ENDING WITH WX> WX
                //       <DIPHTONG NOT ENDING WITH WX> -> <DIPHTONG NOT ENDING WITH WX> YX
                // Example: OIL, COW


                // Check for DIPHTONG
                if ((mSoftwareAutomaticMouthTables.flags[phIndex] & 16) == 0) goto pos41457;

                // Not a diphthong. Get the stress
                stress = mStress[pos];

                // End in IY sound?
                phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[tmpIndex] & 32);

                // If ends with IY, use YX, else use WX
                if (phIndex == 0) phIndex = 20; else phIndex = 21;    // 'WX' = 20 'YX' = 21
                //pos41443:
                // Insert at WX or YX following, copying the stress
                Insert((byte)(pos+1), phIndex, length, stress);
                currentPos = pos;
                // Jump to ???
                goto pos41749;

        pos41457:

                // RULE:
                //       UL -> AX L
                // Example: MEDDLE

                // Get phoneme
                phIndex = mPhonemeIndex[currentPos];
                // Skip this rule if phoneme is not UL
                if (phIndex != 78) goto pos41487;  // 'UL'
                phIndex = 24;         // 'L'                 //change 'UL' to 'AX L'

        pos41466:
                // Get current phoneme stress
                stress = mStress[currentPos];

                // Change UL to AX
                mPhonemeIndex[currentPos] = 13;  // 'AX'
                // Perform insert. Note code below may jump up here with different values
                Insert((byte)(currentPos+1), phIndex, length, stress);
                pos++;
                // Move to next phoneme
                continue;

        pos41487:

                // RULE:
                //       UM -> AX M
                // Example: ASTRONOMY

                // Skip rule if phoneme != UM
                if (phIndex != 79) goto pos41495;   // 'UM'
                // Jump up to branch - replaces current phoneme with AX and continues
                phIndex = 27; // 'M'  //change 'UM' to  'AX M'
                goto pos41466;
        pos41495:

                // RULE:
                //       UN -> AX N
                // Example: FUNCTION


                // Skip rule if phoneme != UN
                if (phIndex != 80) goto pos41503; // 'UN'

                // Jump up to branch - replaces current phoneme with AX and continues
                phIndex = 28;         // 'N' //change UN to 'AX N'
                goto pos41466;
        pos41503:

                // RULE:
                //       <STRESSED VOWEL> <SILENCE> <STRESSED VOWEL> -> <STRESSED VOWEL> <SILENCE> Q <VOWEL>
                // EXAMPLE: AWAY EIGHT

                tmpIndex = phIndex;
                // VOWEL set?
                phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[phIndex] & 128);

                // Skip if not a vowel
                if (phIndex != 0)
                {
                    // Get the stress
                    phIndex = mStress[currentPos];

                    // If stressed...
                    if (phIndex != 0)
                    {
                        // Get the following phoneme
                        currentPos++;
                        phIndex = mPhonemeIndex[currentPos];

                        // If following phoneme is a pause
                        if (phIndex == 0)
                        {
                            // Get the phoneme following pause
                            currentPos++;
                            tmpIndex = mPhonemeIndex[currentPos];

                            // Check for end of buffer flag
                            if (tmpIndex == 255) //buffer overflow
                                // ??? Not sure about these SamTabs.flags
                                phIndex = 65&128;
                            else
                                // And VOWEL flag to current phoneme's SamTabs.flags
                                phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[tmpIndex] & 128);

                            // If following phonemes is not a pause
                            if (phIndex != 0)
                            {
                                // If the following phoneme is not stressed
                                phIndex = mStress[currentPos];
                                if (phIndex != 0)
                                {
                                    // Insert a glottal stop and move forward
                                    // 31 = 'Q'
                                    Insert(currentPos, 31, length, 0);
                                    pos++;
                                    continue;
                                }
                            }
                        }
                    }
                }


                // RULES FOR PHONEMES BEFORE R
                //        T R -> CH R
                // Example: TRACK


                // Get current position and phoneme
                currentPos = pos;
                phIndex = mPhonemeIndex[pos];
                if (phIndex != 23) goto pos41611;     // 'R'

                // Look at prior phoneme
                currentPos--;
                phIndex = mPhonemeIndex[pos-1];
                //pos41567:
                if (phIndex == 69)                    // 'T'
                {
                    // Change T to CH
                    mPhonemeIndex[pos-1] = 42;
                    goto pos41779;
                }


                // RULES FOR PHONEMES BEFORE R
                //        D R -> J R
                // Example: DRY

                // Prior phonemes D?
                if (phIndex == 57)                    // 'D'
                {
                    // Change D to J
                    mPhonemeIndex[pos-1] = 44;
                    goto pos41788;
                }

                // RULES FOR PHONEMES BEFORE R
                //        <VOWEL> R -> <VOWEL> RX
                // Example: ART


                // If vowel flag is set change R to RX
                phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[phIndex] & 128);
                if (phIndex != 0) mPhonemeIndex[pos] = 18;  // 'RX'

                // continue to next phoneme
                pos++;
                continue;

        pos41611:

                // RULE:
                //       <VOWEL> L -> <VOWEL> LX
                // Example: ALL

                // Is phoneme L?
                if (phIndex == 24)    // 'L'
                {
                    // If prior phoneme does not have VOWEL flag set, move to next phoneme
                    if ((mSoftwareAutomaticMouthTables.flags[mPhonemeIndex[pos-1]] & 128) == 0) {pos++; continue;}
                    // Prior phoneme has VOWEL flag set, so change L to LX and move to next phoneme
                    mPhonemeIndex[currentPos] = 19;     // 'LX'
                    pos++;
                    continue;
                }

                // RULE:
                //       G S -> G Z
                //
                // Can't get to fire -
                //       1. The G -> GX rule intervenes
                //       2. Reciter already replaces GS -> GZ

                // Is current phoneme S?
                if (phIndex == 32)    // 'S'
                {
                    // If prior phoneme is not G, move to next phoneme
                    if (mPhonemeIndex[pos-1] != 60) {pos++; continue;}
                    // Replace S with Z and move on
                    mPhonemeIndex[pos] = 38;    // 'Z'
                    pos++;
                    continue;
                }

                // RULE:
                //             K <VOWEL OR DIPHTONG NOT ENDING WITH IY> -> KX <VOWEL OR DIPHTONG NOT ENDING WITH IY>
                // Example: COW

                // Is current phoneme K?
                if (phIndex == 72)    // 'K'
                {
                    // Get next phoneme
                    tmpIndex = mPhonemeIndex[pos+1];
                    // If at end, replace current phoneme with KX
                    if (tmpIndex == 255) mPhonemeIndex[pos] = 75; // ML : prevents an index out of bounds problem
                    else
                    {
                        // VOWELS AND DIPHTONGS ENDING WITH IY SOUND flag set?
                        phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[tmpIndex] & 32);
                        // Replace with KX
                        if (phIndex == 0) mPhonemeIndex[pos] = 75;  // 'KX'
                    }
                }
                else

                // RULE:
                //             G <VOWEL OR DIPHTONG NOT ENDING WITH IY> -> GX <VOWEL OR DIPHTONG NOT ENDING WITH IY>
                // Example: GO


                // Is character a G?
                if (phIndex == 60)   // 'G'
                {
                    // Get the following character
                    byte index = mPhonemeIndex[pos+1];

                    // At end of buffer?
                    if (index == 255) //prevent buffer overflow
                    {
                        pos++; continue;
                    }
                    else
                    // If diphtong ending with YX, move continue processing next phoneme
                    if ((mSoftwareAutomaticMouthTables.flags[index] & 32) != 0) {pos++; continue;}
                    // replace G with GX and continue processing next phoneme
                    mPhonemeIndex[pos] = 63; // 'GX'
                    pos++;
                    continue;
                }

                // RULE:
                //      S P -> S B
                //      S T -> S D
                //      S K -> S G
                //      S KX -> S GX
                // Examples: SPY, STY, SKY, SCOWL

                tmpIndex = mPhonemeIndex[pos];
                //pos41719:
                // Replace with softer version?
                phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[tmpIndex] & 1);
                if (phIndex == 0) goto pos41749;
                phIndex = mPhonemeIndex[pos-1];
                if (phIndex != 32)    // 'S'
                {
                    phIndex = tmpIndex;
                    goto pos41812;
                }
                // Replace with softer version
                mPhonemeIndex[pos] = (byte)(tmpIndex-12);
                pos++;
                continue;


        pos41749:

                // RULE:
                //      <ALVEOLAR> UW -> <ALVEOLAR> UX
                //
                // Example: NEW, DEW, SUE, ZOO, THOO, TOO

                //       UW -> UX

                phIndex = mPhonemeIndex[currentPos];
                if (phIndex == 53)    // 'UW'
                {
                    // ALVEOLAR flag set?
                    tmpIndex = mPhonemeIndex[currentPos-1];
                    phIndex = (byte)(mSoftwareAutomaticMouthTables.flags2[tmpIndex] & 4);
                    // If not set, continue processing next phoneme
                    if (phIndex == 0) {pos++; continue;}
                    mPhonemeIndex[currentPos] = 16;
                    pos++;
                    continue;
                }
        pos41779:

                // RULE:
                //       CH -> CH CH' (CH requires two phonemes to represent it)
                // Example: CHEW

                if (phIndex == 42)    // 'CH'
                {
                    //        pos41783:
                    Insert((byte)(currentPos+1), (byte)(phIndex+1), length, mStress[currentPos]);
                    pos++;
                    continue;
                }

        pos41788:

                // RULE:
                //       J -> J J' (J requires two phonemes to represent it)
                // Example: JAY


                if (phIndex == 44) // 'J'
                {
                    Insert((byte)(currentPos+1), (byte)(phIndex+1), length, mStress[currentPos]);
                    pos++;
                    continue;
                }

        // Jump here to continue
        pos41812:

                // RULE: Soften T following vowel
                // NOTE: This rule fails for cases such as "ODD"
                //       <UNSTRESSED VOWEL> T <PAUSE> -> <UNSTRESSED VOWEL> DX <PAUSE>
                //       <UNSTRESSED VOWEL> D <PAUSE>  -> <UNSTRESSED VOWEL> DX <PAUSE>
                // Example: PARTY, TARDY


                // Past this point, only process if phoneme is T or D

                if (phIndex != 69)    // 'T'
                if (phIndex != 57) {pos++; continue;}       // 'D'
                //pos41825:


                // If prior phoneme is not a vowel, continue processing phonemes
                if ((mSoftwareAutomaticMouthTables.flags[mPhonemeIndex[currentPos-1]] & 128) == 0) {pos++; continue;}

                // Get next phoneme
                currentPos++;
                phIndex = mPhonemeIndex[currentPos];
                //pos41841
                // Is the next phoneme a pause?
                if (phIndex != 0)
                {
                    // If next phoneme is not a pause, continue processing phonemes
                    if ((mSoftwareAutomaticMouthTables.flags[phIndex] & 128) == 0) {pos++; continue;}
                    // If next phoneme is stressed, continue processing phonemes
                    // FIXME: How does a pause get stressed?
                    if (mStress[currentPos] != 0) {pos++; continue;}
        //pos41856:
                // Set phonemes to DX
                mPhonemeIndex[pos] = 30;       // 'DX'
                } else
                {
                    phIndex = mPhonemeIndex[currentPos+1];
                    if (phIndex == 255) //prevent buffer overflow
                        phIndex = 65 & 128;
                    else
                        // Is next phoneme a vowel or ER?
                        phIndex = (byte)(mSoftwareAutomaticMouthTables.flags[phIndex] & 128);
                    if (phIndex != 0) mPhonemeIndex[pos] = 30;  // 'DX'
                }

                pos++;

            } // while
        }

        private void CopyStress()
        {
            // loop thought all the phonemes to be output
            byte pos=0;
            while(true)
            {
                // get the phomene
                byte ph = mPhonemeIndex[pos];

                // exit at end of buffer
                if (ph == 255) return;

                // if CONSONANT_FLAG set, skip - only vowels get stress
                if ((mSoftwareAutomaticMouthTables.flags[ph] & 64) == 0) {pos++; continue;}
                // get the next phoneme
                ph = mPhonemeIndex[pos+1];
                if (ph == 255) //prevent buffer overflow
                {
                    pos++; continue;
                } else
                // if the following phoneme is a vowel, skip
                if ((mSoftwareAutomaticMouthTables.flags[ph] & 128) == 0)  {pos++; continue;}

                // get the stress value at the next position
                ph = mStress[pos+1];

                // if next phoneme is not stressed, skip
                if (ph == 0)  {pos++; continue;}

                // if next phoneme is not a VOWEL OR ER, skip
                if ((ph & 128) != 0)  {pos++; continue;}

                // copy stress from prior phoneme to this one
                mStress[pos] = (byte)(ph+1);

                // advance pointer
                pos++;
            }
        }

        private void Insert(byte position, byte index, byte length, byte stress)
        {
            int i;
            for(i=253; i >= position; i--)
            {
                mPhonemeIndex[i+1] = mPhonemeIndex[i];
                mPhonemeLength[i+1] = mPhonemeLength[i];
                mStress[i+1] = mStress[i];
            }

            mPhonemeIndex[position] = index;
            mPhonemeLength[position] = length;
            mStress[position] = stress;
            return;
        }

        private void SetPhonemeLength()
        {
            byte A;
            int position = 0;
            while(mPhonemeIndex[position] != 255 )
            {
                A = mStress[position];
                if ((A == 0) || ((A&128) != 0))
                    mPhonemeLength[position] = mSoftwareAutomaticMouthTables.phonemeLengthTable[mPhonemeIndex[position]];
                else
                    mPhonemeLength[position] = mSoftwareAutomaticMouthTables.phonemeStressedLengthTable[mPhonemeIndex[position]];
                position++;
            }
        }

        private void AdjustLengths()
        {
            // LENGTHEN VOWELS PRECEDING PUNCTUATION
            //
            // Search for punctuation. If found, back up to the first vowel, then
            // process all phonemes between there and up to (but not including) the punctuation.
            // If any phoneme is found that is a either a fricative or voiced, the duration is
            // increased by (length * 1.5) + 1

            // loop index
            byte index;
            byte currentIndex = 0;
            byte length = 0;
            byte tmpLengthAndFlags = 0;

            // iterate through the phoneme list
            byte loopIndex;
            while (true)
            {
                // get a phoneme
                index = mPhonemeIndex[currentIndex];

                // exit loop if end on buffer token
                if (index == 255) break;

                // not punctuation?
                if((mSoftwareAutomaticMouthTables.flags2[index] & 1) == 0)
                {
                    // skip
                    currentIndex++;
                    continue;
                }

                // hold index
                loopIndex = currentIndex;

                // Loop backwards from this point
        pos48644:

                // back up one phoneme
                currentIndex--;

                // stop once the beginning is reached
                if(currentIndex == 0) break;

                // get the preceding phoneme
                index = mPhonemeIndex[currentIndex];

                if (index != 255) //inserted to prevent access overrun
                if((mSoftwareAutomaticMouthTables.flags[index] & 128) == 0) goto pos48644; // if not a vowel, continue looping

                //pos48657:
                do
                {
                    // test for vowel
                    index = mPhonemeIndex[currentIndex];

                    if (index != 255)//inserted to prevent access overrun
                    // test for fricative/unvoiced or not voiced
                    if(((mSoftwareAutomaticMouthTables.flags2[index] & 32) == 0) || ((mSoftwareAutomaticMouthTables.flags[index] & 4) != 0))     //nochmal �berpr�fen
                    {
                        //A = SamTabs.flags[Y] & 4;
                        //if(A == 0) goto pos48688;

                        // get the phoneme length
                        tmpLengthAndFlags = mPhonemeLength[currentIndex];

                        // change phoneme length to (length * 1.5) + 1
                        tmpLengthAndFlags = (byte)((tmpLengthAndFlags >> 1) + tmpLengthAndFlags + 1);

                        mPhonemeLength[currentIndex] = tmpLengthAndFlags;
                    }
                    // keep moving forward
                    currentIndex++;
                } while (currentIndex != loopIndex);
                //  if (currentIndex != loopIndex) goto pos48657;
                currentIndex++;
            }  // while

            // Similar to the above routine, but shorten vowels under some circumstances

            // Loop throught all phonemes
            loopIndex = 0;
            //pos48697

            while(true)
            {
                // get a phoneme
                currentIndex = loopIndex;
                index = mPhonemeIndex[currentIndex];

                // exit routine at end token
                if (index == 255) return;

                // vowel?
                tmpLengthAndFlags = (byte)(mSoftwareAutomaticMouthTables.flags[index] & 128);
                if (tmpLengthAndFlags != 0)
                {
                    // get next phoneme
                    currentIndex++;
                    index = mPhonemeIndex[currentIndex];

                    // get SamTabs.flags
                    if (index == 255)
                        length = 65; // use if end marker
                    else
                        length = mSoftwareAutomaticMouthTables.flags[index];

                    // not a consonant
                    if ((mSoftwareAutomaticMouthTables.flags[index] & 64) == 0)
                    {
                        // RX or LX?
                        if ((index == 18) || (index == 19))  // 'RX' & 'LX'
                        {
                            // get the next phoneme
                            currentIndex++;
                            index = mPhonemeIndex[currentIndex];

                            // next phoneme a consonant?
                            if ((mSoftwareAutomaticMouthTables.flags[index] & 64) != 0) {
                                // RULE: <VOWEL> RX | LX <CONSONANT>



                                // decrease length of vowel by 1 frame
                                mPhonemeLength[loopIndex]--;


                            }
                            // move ahead
                            loopIndex++;
                            continue;
                        }
                        // move ahead
                        loopIndex++;
                        continue;
                    }


                    // Got here if not <VOWEL>

                    // not voiced
                    if ((length & 4) == 0)
                    {

                        // Unvoiced
                        // *, .*, ?*, ,*, -*, DX, S*, SH, F*, TH, /H, /X, CH, P*, T*, K*, KX

                        // not an unvoiced plosive?
                        if((length & 1) == 0) {
                            // move ahead
                            loopIndex++;
                            continue;
                        }

                        // P*, T*, K*, KX


                        // RULE: <VOWEL> <UNVOICED PLOSIVE>
                        // <VOWEL> <P*, T*, K*, KX>

                        // move back
                        currentIndex--;


                        // decrease length by 1/8th
                        length = (byte)(mPhonemeLength[currentIndex] >> 3);
                        mPhonemeLength[currentIndex] -= length;


                        // move ahead
                        loopIndex++;
                        continue;
                    }

                    // RULE: <VOWEL> <VOICED CONSONANT>
                    // <VOWEL> <WH, R*, L*, W*, Y*, M*, N*, NX, DX, Q*, Z*, ZH, V*, DH, J*, B*, D*, G*, GX>



                    // decrease length
                    tmpLengthAndFlags = mPhonemeLength[currentIndex-1];
                    mPhonemeLength[currentIndex-1] = (byte)((tmpLengthAndFlags >> 2) + tmpLengthAndFlags + 1);     // 5/4*A + 1


                    // move ahead
                    loopIndex++;
                    continue;

                }


                // WH, R*, L*, W*, Y*, M*, N*, NX, Q*, Z*, ZH, V*, DH, J*, B*, D*, G*, GX

        //pos48821:

                // RULE: <NASAL> <STOP CONSONANT>
                //       Set punctuation length to 6
                //       Set stop consonant length to 5

                // nasal?
                if((mSoftwareAutomaticMouthTables.flags2[index] & 8) != 0)
                {

                    // M*, N*, NX,

                    // get the next phoneme
                    currentIndex++;
                    index = mPhonemeIndex[currentIndex];

                    // end of buffer?
                    if (index == 255)
                    tmpLengthAndFlags = 65&2;  //prevent buffer overflow
                    else
                        tmpLengthAndFlags = (byte)(mSoftwareAutomaticMouthTables.flags[index] & 2); // check for stop consonant


                    // is next phoneme a stop consonant?
                    if (tmpLengthAndFlags != 0)

                    // B*, D*, G*, GX, P*, T*, K*, KX

                    {

                        // set stop consonant length to 6
                        mPhonemeLength[currentIndex] = 6;

                        // set nasal length to 5
                        mPhonemeLength[currentIndex-1] = 5;


                    }
                    // move to next phoneme
                    loopIndex++;
                    continue;
                }


                // WH, R*, L*, W*, Y*, Q*, Z*, ZH, V*, DH, J*, B*, D*, G*, GX

                // RULE: <VOICED STOP CONSONANT> {optional silence} <STOP CONSONANT>
                //       Shorten both to (length/2 + 1)

                // (voiced) stop consonant?
                if((mSoftwareAutomaticMouthTables.flags[index] & 2) != 0)
                {
                    // B*, D*, G*, GX

                    // move past silence
                    do
                    {
                        // move ahead
                        currentIndex++;
                        index = mPhonemeIndex[currentIndex];
                    } while(index == 0);


                    // check for end of buffer
                    if (index == 255) //buffer overflow
                    {
                        // ignore, overflow code
                        if ((65 & 2) == 0) {loopIndex++; continue;}
                    } else if ((mSoftwareAutomaticMouthTables.flags[index] & 2) == 0) {
                        // if another stop consonant, move ahead
                        loopIndex++;
                        continue;
                    }

                    // RULE: <UNVOICED STOP CONSONANT> {optional silence} <STOP CONSONANT>
                    // shorten the prior phoneme length to (length/2 + 1)
                    mPhonemeLength[currentIndex] = (byte)((mPhonemeLength[currentIndex] >> 1) + 1);
                    currentIndex = loopIndex;

                    // also shorten this phoneme length to (length/2 +1)
                    mPhonemeLength[loopIndex] = (byte)((mPhonemeLength[loopIndex] >> 1) + 1);

                    // move ahead
                    loopIndex++;
                    continue;
                }

                // WH, R*, L*, W*, Y*, Q*, Z*, ZH, V*, DH, J*, **,

                // RULE: <VOICED NON-VOWEL> <DIPHTONG>
                //       Decrease <DIPHTONG> by 2

                // liquic consonant?
                if ((mSoftwareAutomaticMouthTables.flags2[index] & 16) != 0)
                {
                    // R*, L*, W*, Y*

                    // get the prior phoneme
                    index = mPhonemeIndex[currentIndex-1];

                    // prior phoneme a stop consonant>
                    if((mSoftwareAutomaticMouthTables.flags[index] & 2) != 0) {
                                    // Rule: <LIQUID CONSONANT> <DIPHTONG>


                    // decrease the phoneme length by 2 frames (20 ms)
                    mPhonemeLength[currentIndex] -= 2;

                }
                }

                // move to next phoneme
                loopIndex++;
                continue;
            }
        //            goto pos48701;
        }

        private byte Code41240()
        {
            byte pos=0;
            byte resultPos = 0;

            while(mPhonemeIndex[pos] != 255)
            {
                byte index; //register AC
                resultPos = pos;
                index = mPhonemeIndex[pos];
                if ((mSoftwareAutomaticMouthTables.flags[index]&2) == 0)
                {
                    pos++;
                    continue;
                } else
                if ((mSoftwareAutomaticMouthTables.flags[index]&1) == 0)
                {
                    Insert((byte)(pos+1), (byte)(index+1), mSoftwareAutomaticMouthTables.phonemeLengthTable[index+1], mStress[pos]);
                    Insert((byte)(pos+2), (byte)(index+2), mSoftwareAutomaticMouthTables.phonemeLengthTable[index+2], mStress[pos]);
                    pos += 3;
                    continue;
                }

                byte phIndex = 0; 
                do
                {
                    resultPos++;
                    phIndex = mPhonemeIndex[resultPos];
                } while(phIndex==0);

                if (phIndex != 255)
                {
                    if ((mSoftwareAutomaticMouthTables.flags[phIndex] & 8) != 0)  {pos++; continue;}
                    if ((phIndex == 36) || (phIndex == 37)) {pos++; continue;} // '/H' '/X'
                }

                Insert((byte)(pos+1), (byte)(index+1), mSoftwareAutomaticMouthTables.phonemeLengthTable[index+1], mStress[pos]);
                Insert((byte)(pos+2), (byte)(index+2), mSoftwareAutomaticMouthTables.phonemeLengthTable[index+2], mStress[pos]);
                pos += 3;
            };

            return resultPos;
        }

        private void InsertBreath(ref byte currpos)
        {
            byte mem54;
            byte mem55;
            byte index; //variable Y
            mem54 = 255;
            currpos++; // unsure
            mem55 = 0;
            byte position = 0;
            byte length = 0;

            while(true)
            {
                currpos = position;
                index = mPhonemeIndex[currpos];
                if (index == 255) return;
                mem55 += mPhonemeLength[currpos];

                if (mem55 < 232)
                {
                    if (index != 254)
                    {
                        byte flags = (byte)(mSoftwareAutomaticMouthTables.flags2[index] & 1);
                        if(flags != 0)
                        {
                            currpos++;
                            mem55 = 0;
                            Insert(currpos, 254, length, 0);
                            position++;
                            position++;
                            continue;
                        }
                    }
                    if (index == 0) mem54 = currpos;
                    position++;
                    continue;
                }
                currpos = mem54;
                mPhonemeIndex[currpos] = 31;   // 'Q*' glottal stop
                mPhonemeLength[currpos] = 4;
                mStress[currpos] = 0;
                currpos++;
                mem55 = 0;
                Insert(currpos, 254, length, 0);
                currpos++;
                position = currpos;
            }

        }

        private void PrepareOutput(ref AudioBuffer audioBuffer)
        {
            byte phIndex = 0;
            byte srcIndex = 0;
            byte outIndex = 0;

            //pos48551:
            while(true)
            {
                phIndex = mPhonemeIndex[srcIndex];
                if (phIndex == 255)
                {
                    phIndex = 255;
                    mPhonemeIndexOutput[outIndex] = 255;
                    mSpeechRenderer.Render(ref audioBuffer, this);
                    return;
                }
                if (phIndex == 254)
                {
                    srcIndex++;
                    int temp = srcIndex;
                    //mem[48546] = srcIndex;
                    mPhonemeIndexOutput[outIndex] = 255;
                    mSpeechRenderer.Render(ref audioBuffer, this);
                    //srcIndex = mem[48546];
                    srcIndex=(byte)temp;
                    outIndex = 0;
                    continue;
                }

                if (phIndex == 0)
                {
                    srcIndex++;
                    continue;
                }

                mPhonemeIndexOutput[outIndex] = phIndex;
                mPhonemeLengthOutput[outIndex] = mPhonemeLength[srcIndex];
                mStressOutput[outIndex] = mStress[srcIndex];
                srcIndex++;
                outIndex++;
            }
        }
    }

}
