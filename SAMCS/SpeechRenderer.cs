namespace SAMCS
{

    public class SpeechRenderer
    {
        private readonly SpeechRendererTables mSpeechRendererTables = new SpeechRendererTables();

        //timetable for more accurate c64 simulation
        private static readonly int[][] gTimetable =
        {
            new int[]{162, 167, 167, 127, 128},
            new int[]{226, 60, 60, 0, 0},
            new int[]{225, 60, 59, 0, 0},
            new int[]{200, 0, 0, 54, 55},
            new int[]{199, 0, 0, 54, 54}
        };

        private readonly byte[] mSampledConsonantFlag = new byte[256]; // tab44800
        private readonly byte[] mFrequency1 = new byte[256];
        private readonly byte[] mFrequency2 = new byte[256];
        private readonly byte[] mFrequency3 = new byte[256];
        private readonly byte[] mAmplitude1 = new byte[256];
        private readonly byte[] mAmplitude2 = new byte[256];
        private readonly byte[] mAmplitude3 = new byte[256];
        private readonly byte[] mPitches = new byte[256];
        private uint mOldtimetableindex = 0;
        private byte mThroat, mMouth;

        public byte Throat
        {
            get => mThroat;
            set => SetMouthThroat(mMouth, value);
        }
        public byte Mouth
        {
            get => mMouth;
            set => SetMouthThroat(value, mThroat);
        }

        /*
            SAM's voice can be altered by changing the frequencies of the
            mouth formant (F1) and the throat formant (F2). Only the voiced
            phonemes (5-29 and 48-53) are altered.
        */
        public void SetMouthThroat(byte mouth, byte throat)
        {
            mMouth = mouth;
            mThroat = throat;
            byte initialFrequency;
            byte newFrequency = 0;

            // mouth formants (F1) 5..29
            byte[] mouthFormants5_29 = {
                0, 0, 0, 0, 0, 10,
                14, 19, 24, 27, 23, 21, 16, 20, 14, 18, 14, 18, 18,
                16, 13, 15, 11, 18, 14, 11, 9, 6, 6, 6
            };

            // throat formants (F2) 5..29
            byte[] throatFormants5_29 = {
                255, 255,
                255, 255, 255, 84, 73, 67, 63, 40, 44, 31, 37, 45, 73, 49,
                36, 30, 51, 37, 29, 69, 24, 50, 30, 24, 83, 46, 54, 86
            };

            // there must be no zeros in this 2 tables
            // formant 1 frequencies (mouth) 48..53
            byte[] mouthFormants48_53 = {19, 27, 21, 27, 18, 13};

            // formant 2 frequencies (throat) 48..53
            byte[] throatFormants48_53 = {72, 39, 31, 43, 30, 34};

            byte pos = 5;
        //pos38942:
            // recalculate formant frequencies 5..29 for the mouth (F1) and throat (F2)
            while(pos != 30)
            {
                // recalculate mouth mFrequency
                initialFrequency = mouthFormants5_29[pos];
                if (initialFrequency != 0) newFrequency = Trans(mouth, initialFrequency);
                mSpeechRendererTables.freq1data[pos] = newFrequency;

                // recalculate throat mFrequency
                initialFrequency = throatFormants5_29[pos];
                if(initialFrequency != 0) newFrequency = Trans(throat, initialFrequency);
                mSpeechRendererTables.freq2data[pos] = newFrequency;
                pos++;
            }

        //pos39059:
            // recalculate formant frequencies 48..53
            pos = 48;
            byte tmpIndex = 0;
            while(pos != 54)
            {
                // recalculate F1 (mouth formant)
                initialFrequency = mouthFormants48_53[tmpIndex];
                newFrequency = Trans(mouth, initialFrequency);
                mSpeechRendererTables.freq1data[pos] = newFrequency;

                // recalculate F2 (throat formant)
                initialFrequency = throatFormants48_53[tmpIndex];
                newFrequency = Trans(throat, initialFrequency);
                mSpeechRendererTables.freq2data[pos] = newFrequency;
                tmpIndex++;
                pos++;
            }
        }

        public void Render(ref AudioBuffer a, in SoftwareAutomaticMouth b)
        {
            byte mem44 = 0;
            byte mem66 = 0;
            int i;
            if (b.PhonemeIndexOutput[0] == 255) return; //exit if no data

            byte outIndexRankingValuePitchSample = 0; // cpua
            byte indexSample = 0; // cpux
            byte phase1;
            byte mem56;
            byte phase2;
            byte mem48;
            byte inIndex;
            // CREATE FRAMES
            //
            // The length parameter in the list corresponds to the number of frames
            // to expand the phoneme to. Each frame represents 10 milliseconds of time.
            // So a phoneme with a length of 7 = 7 frames = 70 milliseconds duration.
            //
            // The parameters are copied from the phoneme to the frame verbatim.


            // pos47587:
            do
            {
                // get the index
                inIndex = mem44;
                // get the phoneme at the index
                outIndexRankingValuePitchSample = b.PhonemeIndexOutput[mem44];
                mem56 = outIndexRankingValuePitchSample;

                // if terminal phoneme, exit the loop
                if (outIndexRankingValuePitchSample == 255) break;

                // period phoneme *.
                if (outIndexRankingValuePitchSample == 1)
                {
                    // add rising inflection
                    outIndexRankingValuePitchSample = 1;
                    mem48 = 1;
                    //goto pos48376;
                    AddInflection(mem48, ref indexSample, ref outIndexRankingValuePitchSample);
                }
                /*
                if (outIndexRankingValuePitchSample == 2) goto pos48372;
                */

                // question mark phoneme?
                if (outIndexRankingValuePitchSample == 2)
                {
                    // create falling inflection
                    mem48 = 255;
                    AddInflection(mem48, ref indexSample, ref outIndexRankingValuePitchSample);
                }
                //  pos47615:

                // get the stress amount (more stress = higher pitch)
                phase1 = mSpeechRendererTables.tab47492[b.StressOutput[inIndex] + 1];

                // get number of frames to write
                phase2 = b.PhonemeLengthOutput[inIndex];
                inIndex = mem56;

                // copy from the source to the frames list
                do
                {
                    mFrequency1[indexSample] = mSpeechRendererTables.freq1data[inIndex];     // F1 mFrequency
                    mFrequency2[indexSample] = mSpeechRendererTables.freq2data[inIndex];     // F2 mFrequency
                    mFrequency3[indexSample] = mSpeechRendererTables.freq3data[inIndex];     // F3 mFrequency
                    mAmplitude1[indexSample] = mSpeechRendererTables.ampl1data[inIndex];     // F1 mAmplitude
                    mAmplitude2[indexSample] = mSpeechRendererTables.ampl2data[inIndex];     // F2 mAmplitude
                    mAmplitude3[indexSample] = mSpeechRendererTables.ampl3data[inIndex];     // F3 mAmplitude
                    mSampledConsonantFlag[indexSample] = mSpeechRendererTables.sampledConsonantFlags[inIndex];        // phoneme data for sampled consonants
                    mPitches[indexSample] = (byte)(b.Pitch + phase1);      // pitch
                    indexSample++;
                    phase2--;
                } while (phase2 != 0);
                mem44++;
            } while (mem44 != 0);
            // -------------------
            //pos47694:

            // CREATE TRANSITIONS
            //
            // Linear transitions are now created to smoothly connect the
            // end of one sustained portion of a phoneme to the following
            // phoneme.
            //
            // To do this, three tables are used:
            //
            //  Table         Purpose
            //  =========     ==================================================
            //  blendRank     Determines which phoneme's blend values are used.
            //
            //  blendOut      The number of frames at the end of the phoneme that
            //                will be used to transition to the following phoneme.
            //
            //  blendIn       The number of frames of the following phoneme that
            //                will be used to transition into that phoneme.
            //
            // In creating a transition between two phonemes, the phoneme
            // with the HIGHEST rank is used. Phonemes are ranked on how much
            // their identity is based on their transitions. For example,
            // vowels are and diphthongs are identified by their sustained portion,
            // rather than the transitions, so they are given low values. In contrast,
            // stop consonants (P, B, T, K) and glides (Y, L) are almost entirely
            // defined by their transitions, and are given high rank values.
            //
            // Here are the rankings used by SAM:
            //
            //     Rank    Type                         Phonemes
            //     2       All vowels                   IY, IH, etc.
            //     5       Diphthong endings            YX, WX, ER
            //     8       Terminal liquid consonants   LX, WX, YX, N, NX
            //     9       Liquid consonants            L, RX, W
            //     10      Glide                        R, OH
            //     11      Glide                        WH
            //     18      Voiceless fricatives         S, SH, F, TH
            //     20      Voiced fricatives            Z, ZH, V, DH
            //     23      Plosives, stop consonants    P, T, K, KX, DX, CH
            //     26      Stop consonants              J, GX, B, D, G
            //     27-29   Stop consonants (internal)   **
            //     30      Unvoiced consonants          /H, /X and Q*
            //     160     Nasal                        M
            //
            // To determine how many frames to use, the two phonemes are
            // compared using the blendRank[] table. The phoneme with the
            // higher rank is selected. In case of a tie, a blend of each is used:
            //
            //      if blendRank[phoneme1] ==  blendRank[phomneme2]
            //          // use lengths from each phoneme
            //          outBlendFrames = outBlend[phoneme1]
            //          inBlendFrames = outBlend[phoneme2]
            //      else if blendRank[phoneme1] > blendRank[phoneme2]
            //          // use lengths from first phoneme
            //          outBlendFrames = outBlendLength[phoneme1]
            //          inBlendFrames = inBlendLength[phoneme1]
            //      else
            //          // use lengths from the second phoneme
            //          // note that in and out are SWAPPED!
            //          outBlendFrames = inBlendLength[phoneme2]
            //          inBlendFrames = outBlendLength[phoneme2]
            //
            // Blend lengths can't be less than zero.
            //
            // Transitions are assumed to be symetrical, so if the transition
            // values for the second phoneme are used, the inBlendLength and
            // outBlendLength values are SWAPPED.
            //
            // For most of the parameters, SAM interpolates over the range of the last
            // outBlendFrames-1 and the first inBlendFrames.
            //
            // The exception to this is the Pitch[] parameter, which is interpolates the
            // pitch from the CENTER of the current phoneme to the CENTER of the next
            // phoneme.
            //
            // Here are two examples. First, For example, consider the word "SUN" (S AH N)
            //
            //    Phoneme   Duration    BlendWeight    OutBlendFrames    InBlendFrames
            //    S         2           18             1                 3
            //    AH        8           2              4                 4
            //    N         7           8              1                 2
            //
            // The formant transitions for the output frames are calculated as follows:
            //
            //     flags ampl1 freq1 ampl2 freq2 ampl3 freq3 pitch
            //    ------------------------------------------------
            // S
            //    241     0     6     0    73     0    99    61   Use S (weight 18) for transition instead of AH (weight 2)
            //    241     0     6     0    73     0    99    61   <-- (OutBlendFrames-1) = (1-1) = 0 frames
            // AH
            //      0     2    10     2    66     0    96    59 * <-- InBlendFrames = 3 frames
            //      0     4    14     3    59     0    93    57 *
            //      0     8    18     5    52     0    90    55 *
            //      0    15    22     9    44     1    87    53
            //      0    15    22     9    44     1    87    53
            //      0    15    22     9    44     1    87    53   Use N (weight 8) for transition instead of AH (weight 2).
            //      0    15    22     9    44     1    87    53   Since N is second phoneme, reverse the IN and OUT values.
            //      0    11    17     8    47     1    98    56 * <-- (InBlendFrames-1) = (2-1) = 1 frames
            // N
            //      0     8    12     6    50     1   109    58 * <-- OutBlendFrames = 1
            //      0     5     6     5    54     0   121    61
            //      0     5     6     5    54     0   121    61
            //      0     5     6     5    54     0   121    61
            //      0     5     6     5    54     0   121    61
            //      0     5     6     5    54     0   121    61
            //      0     5     6     5    54     0   121    61
            //
            // Now, consider the reverse "NUS" (N AH S):
            //
            //     flags ampl1 freq1 ampl2 freq2 ampl3 freq3 pitch
            //    ------------------------------------------------
            // N
            //     0     5     6     5    54     0   121    61
            //     0     5     6     5    54     0   121    61
            //     0     5     6     5    54     0   121    61
            //     0     5     6     5    54     0   121    61
            //     0     5     6     5    54     0   121    61
            //     0     5     6     5    54     0   121    61   Use N (weight 8) for transition instead of AH (weight 2)
            //     0     5     6     5    54     0   121    61   <-- (OutBlendFrames-1) = (1-1) = 0 frames
            // AH
            //     0     8    11     6    51     0   110    59 * <-- InBlendFrames = 2
            //     0    11    16     8    48     0    99    56 *
            //     0    15    22     9    44     1    87    53   Use S (weight 18) for transition instead of AH (weight 2)
            //     0    15    22     9    44     1    87    53   Since S is second phoneme, reverse the IN and OUT values.
            //     0     9    18     5    51     1    90    55 * <-- (InBlendFrames-1) = (3-1) = 2
            //     0     4    14     3    58     1    93    57 *
            // S
            //   241     2    10     2    65     1    96    59 * <-- OutBlendFrames = 1
            //   241     0     6     0    73     0    99    61

            outIndexRankingValuePitchSample = 0;
            mem44 = 0;
            byte mem49 = 0; // mem49 starts at as 0
            indexSample = 0;
            byte mem53 = 0;
            byte phase3;
            byte mem38;
            byte speedcounter;
            while (true) //while No. 1
            {

                // get the current and following phoneme
                inIndex = b.PhonemeIndexOutput[indexSample];
                outIndexRankingValuePitchSample = b.PhonemeIndexOutput[indexSample + 1];
                indexSample++;

                // exit loop at end token
                if (outIndexRankingValuePitchSample == 255) break;//goto pos47970;


                // get the ranking of each phoneme
                indexSample = outIndexRankingValuePitchSample;
                mem56 = mSpeechRendererTables.blendRank[outIndexRankingValuePitchSample];
                outIndexRankingValuePitchSample = mSpeechRendererTables.blendRank[inIndex];

                // compare the rank - lower rank value is stronger
                if (outIndexRankingValuePitchSample == mem56)
                {
                    // same rank, so use out blend lengths from each phoneme
                    phase1 = mSpeechRendererTables.outBlendLength[inIndex];
                    phase2 = mSpeechRendererTables.outBlendLength[indexSample];
                }
                else
                if (outIndexRankingValuePitchSample < mem56)
                {
                    // first phoneme is stronger, so us it's blend lengths
                    phase1 = mSpeechRendererTables.inBlendLength[indexSample];
                    phase2 = mSpeechRendererTables.outBlendLength[indexSample];
                }
                else
                {
                    // second phoneme is stronger, so use it's blend lengths
                    // note the out/in are swapped
                    phase1 = mSpeechRendererTables.outBlendLength[inIndex];
                    phase2 = mSpeechRendererTables.inBlendLength[inIndex];
                }

                inIndex = mem44;
                outIndexRankingValuePitchSample = (byte)(mem49 + b.PhonemeLengthOutput[mem44]); // outIndexRankingValuePitchSample is mem49 + length
                mem49 = outIndexRankingValuePitchSample; // mem49 now holds length + position
                outIndexRankingValuePitchSample += phase2; //Maybe Problem because of carry flag

                //47776: ADC 42
                speedcounter = outIndexRankingValuePitchSample;
                byte mem47 = 168;
                phase3 = (byte)(mem49 - phase1); // what is mem49
                outIndexRankingValuePitchSample = (byte)(phase1 + phase2); // total transition?
                mem38 = outIndexRankingValuePitchSample;

                indexSample = outIndexRankingValuePitchSample;
                indexSample -= 2;
                if ((indexSample & 128) == 0)
                    do   //while No. 2
                    {
                        //pos47810:

                        // mem47 is used to index the tables:
                        // 168  mPitches[]
                        // 169  mFrequency1
                        // 170  mFrequency2
                        // 171  mFrequency3
                        // 172  mAmplitude1
                        // 173  mAmplitude2
                        // 174  mAmplitude3

                        byte mem40 = mem38;

                        if (mem47 == 168)     // pitch
                        {

                            // unlike the other values, the mPitches[] interpolates from
                            // the middle of the current phoneme to the middle of the
                            // next phoneme

                            byte mem36, mem37;
                            // half the width of the current phoneme
                            mem36 = (byte)(b.PhonemeLengthOutput[mem44] >> 1);
                            // half the width of the next phoneme
                            mem37 = (byte)(b.PhonemeLengthOutput[mem44 + 1] >> 1);
                            // sum the values
                            mem40 = (byte)(mem36 + mem37); // length of both halves
                            mem37 += mem49; // center of next phoneme
                            mem36 = (byte)(mem49 - mem36); // center index of current phoneme
                            outIndexRankingValuePitchSample = Read(mem47, mem37); // value at center of next phoneme - end interpolation value
                                                                                  //outIndexRankingValuePitchSample = mem[address];

                            inIndex = mem36; // start index of interpolation
                            mem53 = (byte)(outIndexRankingValuePitchSample - Read(mem47, mem36)); // value to center of current phoneme
                        }
                        else
                        {
                            // value to interpolate to
                            outIndexRankingValuePitchSample = Read(mem47, speedcounter);
                            // position to start interpolation from
                            inIndex = phase3;
                            // value to interpolate from
                            mem53 = (byte)(outIndexRankingValuePitchSample - Read(mem47, phase3));
                        }

                        //Code47503(mem40);
                        // ML : Code47503 is division with remainder, and mem50 gets the sign

                        // calculate change per frame
                        sbyte m53 = (sbyte)mem53;
                        byte mem50 = (byte)(mem53 & 128);
                        byte m53abs = (byte)(m53 < 0 ? -m53 : m53);
                        byte mem51 = (byte)(m53abs % mem40); //abs((char)m53) % mem40;
                        mem53 = (byte)((sbyte)(m53) / mem40);

                        // interpolation range
                        indexSample = mem40; // number of frames to interpolate over
                        inIndex = phase3; // starting frame


                        // linearly interpolate values

                        mem56 = 0;
                        //47907: CLC
                        //pos47908:
                        while (true)     //while No. 3
                        {
                            outIndexRankingValuePitchSample = (byte)(Read(mem47, inIndex) + mem53); //carry alway cleared

                            mem48 = outIndexRankingValuePitchSample;
                            inIndex++;
                            indexSample--;
                            if (indexSample == 0) break;

                            mem56 += mem51;
                            if (mem56 >= mem40)  //???
                            {
                                mem56 -= mem40; //carry? is set
                                                    //if ((mem56 & 128)==0)
                                if ((mem50 & 128) == 0)
                                {
                                    //47935: BIT 50
                                    //47937: BMI 47943
                                    if (mem48 != 0) mem48++;
                                }
                                else mem48--;
                            }
                            //pos47945:
                            Write(mem47, inIndex, mem48);
                        } //while No. 3

                        //pos47952:
                        mem47++;
                        //if (mem47 != 175) goto pos47810;
                    } while (mem47 != 175);     //while No. 2
                //pos47963:
                mem44++;
                indexSample = mem44;
            }  //while No. 1

            //goto pos47701;
            //pos47970:

            // add the length of this phoneme
            mem48 = (byte)(mem49 + b.PhonemeLengthOutput[mem44]);


            // ASSIGN PITCH CONTOUR
            //
            // This subtracts the F1 mFrequency from the pitch to create a
            // pitch contour. Without this, the output would be at a single
            // pitch level (monotone).


            // don't adjust pitch if in sing mode
            if (!b.Sing)
            {
                // iterate through the buffer
                for(i=0; i<256; i++)
                {
                    // subtract half the mFrequency of the formant 1.
                    // this adds variety to the voice
                    mPitches[i] -= (byte)(mFrequency1[i] >> 1);
                }
            }

            phase1 = 0;
            phase2 = 0;
            phase3 = 0;
            mem49 = 0;
            speedcounter = 72; //sam standard speed

            // RESCALE AMPLITUDE
            //
            // Rescale volume from a linear scale to decibels.
            //

            //mAmplitude rescaling
            for(i=255; i>=0; i--)
            {
                mAmplitude1[i] = mSpeechRendererTables.amplitudeRescale[mAmplitude1[i]];
                mAmplitude2[i] = mSpeechRendererTables.amplitudeRescale[mAmplitude2[i]];
                mAmplitude3[i] = mSpeechRendererTables.amplitudeRescale[mAmplitude3[i]];
            }

            inIndex = 0;
            outIndexRankingValuePitchSample = mPitches[0];
            mem44 = outIndexRankingValuePitchSample;
            indexSample = outIndexRankingValuePitchSample;
            mem38 = (byte)(outIndexRankingValuePitchSample - (outIndexRankingValuePitchSample>>2));     // 3/4*outIndexRankingValuePitchSample ???

            // PROCESS THE FRAMES
            //
            // In traditional vocal synthesis, the glottal pulse drives filters, which
            // are attenuated to the frequencies of the formants.
            //
            // SAM generates these formants directly with sin and rectangular waves.
            // To simulate them being driven by the glottal pulse, the waveforms are
            // reset at the beginning of each glottal pulse.

            //finally the loop for sound output
            //pos48078:
            while(true)
            {
                // get the sampled information on the phoneme
                outIndexRankingValuePitchSample = mSampledConsonantFlag[inIndex];
                byte mem39 = outIndexRankingValuePitchSample;

                // unvoiced sampled phoneme?
                outIndexRankingValuePitchSample &= 248;
                if(outIndexRankingValuePitchSample != 0)
                {
                    // render the sample for the phoneme
                    mem44 = RenderSample(ref a, mem39, ref mem53, ref mem56, ref mem66, ref outIndexRankingValuePitchSample, ref indexSample, ref inIndex);

                    // skip ahead two in the phoneme buffer
                    inIndex += 2;
                    mem48 -= 2;
                } else
                {
                    // simulate the glottal pulse and formants
                    byte[] ary = new byte[5];
                    uint p1 = (uint)phase1 * 256; // Fixed point integers because we need to divide later on
                    uint p2 = (uint)phase2 * 256;
                    uint p3 = (uint)phase3 * 256;
                    int k;
                    for (k=0; k<5; k++) {
                        sbyte sp1 = (sbyte)mSpeechRendererTables.sinus[0xff & (p1>>8)];
                        sbyte sp2 = (sbyte)mSpeechRendererTables.sinus[0xff & (p2>>8)];
                        sbyte rp3 = (sbyte)mSpeechRendererTables.rectangle[0xff & (p3>>8)];
                        int sin1 = sp1 * ((byte)mAmplitude1[inIndex] & 0x0f);
                        int sin2 = sp2 * ((byte)mAmplitude2[inIndex] & 0x0f);
                        int rect = rp3 * ((byte)mAmplitude3[inIndex] & 0x0f);
                        int mux = sin1 + sin2 + rect;
                        mux /= 32;
                        mux += 128; // Go from signed to unsigned mAmplitude
                        ary[k] = (byte)mux;
                        p1 += (uint)(mFrequency1[inIndex] * 256 / 4); // Compromise, this becomes a shift and works well
                        p2 += (uint)(mFrequency2[inIndex] * 256 / 4);
                        p3 += (uint)(mFrequency3[inIndex] * 256 / 4);
                    }
                    // output the accumulated value
                    Output8BitAry(ref a, 0, ary);
                    speedcounter--;
                    if (speedcounter != 0) goto pos48155;
                    inIndex++; //go to next mAmplitude

                    // decrement the frame count
                    mem48--;
                }

                // if the frame count is zero, exit the loop
                if(mem48 == 0)  return;
                speedcounter = b.Speed;
        pos48155:

                // decrement the remaining length of the glottal pulse
                mem44--;

                // finished with a glottal pulse?
                if(mem44 == 0)
                {
                    // fetch the next glottal pulse length
                    outIndexRankingValuePitchSample = mPitches[inIndex];
                    mem44 = outIndexRankingValuePitchSample;
                    outIndexRankingValuePitchSample = (byte)(outIndexRankingValuePitchSample - (outIndexRankingValuePitchSample>>2));
                    mem38 = outIndexRankingValuePitchSample;

                    // reset the formant wave generators to keep them in
                    // sync with the glottal pulse
                    phase1 = 0;
                    phase2 = 0;
                    phase3 = 0;
                    continue;
                }

                // decrement the count
                mem38--;

                // is the count non-zero and the sampled flag is zero?
                if((mem38 != 0) || (mem39 == 0))
                {
                    // reset the phase of the formants to match the pulse
                    phase1 += mFrequency1[inIndex];
                    phase2 += mFrequency2[inIndex];
                    phase3 += mFrequency3[inIndex];
                    continue;
                }

                // voiced sampled phonemes interleave the sample with the
                // glottal pulse. The sample flag is non-zero, so render
                // the sample for the phoneme.
                mem44 = RenderSample(ref a, mem39, ref mem53, ref mem56, ref mem66, ref outIndexRankingValuePitchSample, ref indexSample, ref inIndex);
                // fetch the next glottal pulse length
                outIndexRankingValuePitchSample = mPitches[inIndex];
                mem44 = outIndexRankingValuePitchSample;
                outIndexRankingValuePitchSample = (byte)(outIndexRankingValuePitchSample - (outIndexRankingValuePitchSample>>2));
                mem38 = outIndexRankingValuePitchSample;

                // reset the formant wave generators to keep them in
                // sync with the glottal pulse
                phase1 = 0;
                phase2 = 0;
                phase3 = 0;
                continue;
            } //while
        }

        private void Output8BitAry(ref AudioBuffer a, int index, byte[] ary)
        {
            a.Cursor += gTimetable[mOldtimetableindex][index];
            mOldtimetableindex = (uint)index;
            // write a little bit in advance
            for (int i = 0; i < 5; i++)
                a.Write(a.Cursor/50 + i, ary[i]);
        }

        private void Output8Bit(ref AudioBuffer a, int index, byte A)
        {
            Output8BitAry(ref a, index, new byte[]{A,A,A,A,A});
        }

        private byte Read(byte ptr, byte index)
        {
            switch(ptr)
            {
                case 168: return mPitches[index];
                case 169: return mFrequency1[index];
                case 170: return mFrequency2[index];
                case 171: return mFrequency3[index];
                case 172: return mAmplitude1[index];
                case 173: return mAmplitude2[index];
                case 174: return mAmplitude3[index];
            }
            return 0;
        }

        private void Write(byte ptr, byte index, byte value)
        {

            switch(ptr)
            {
                case 168: mPitches[index] = value; return;
                case 169: mFrequency1[index] = value;  return;
                case 170: mFrequency2[index] = value;  return;
                case 171: mFrequency3[index] = value;  return;
                case 172: mAmplitude1[index] = value;  return;
                case 173: mAmplitude2[index] = value;  return;
                case 174: mAmplitude3[index] = value;  return;
            }
        }

        private static byte Trans(byte mem39212, byte mem39213)
        {
            //pos39008:
            byte carry;
            int temp;
            byte mem39214, mem39215;
            byte transValue = 0;
            mem39215 = 0;
            mem39214 = 0;
            byte someSortOfIndex = 8;
            do
            {
                carry = (byte)(mem39212 & 1);
                mem39212 = (byte)(mem39212 >> 1);
                if (carry != 0)
                {
                    carry = 0;
                    transValue = mem39215;
                    temp = (int)transValue + (int)mem39213;
                    transValue += mem39213;
                    if (temp > 255) carry = 1;
                    mem39215 = transValue;
                }
                temp = mem39215 & 1;
                mem39215 = (byte)((mem39215 >> 1) | (carry>0?128:0));
                carry = (byte)temp;
                someSortOfIndex--;
            } while (someSortOfIndex != 0);
            temp = mem39214 & 128;
            mem39214 = (byte)((mem39214 << 1) | (carry>0?1:0));
            carry = (byte)temp;
            temp = mem39215 & 128;
            mem39215 = (byte)((mem39215 << 1) | (carry>0?1:0));
            carry = (byte)temp;

            return mem39215;
        }

        private void AddInflection(byte mem48, ref byte cpux, ref byte cpua)
        {
            //pos48372:
            //  mem48 = 255;
            //pos48376:

            // store the location of the punctuation
            byte mem49 = cpux;
            cpua = cpux;
            int Atemp = cpua;

            // backup 30 frames
            cpua -= 30;
            // if index is before buffer, point to start of buffer
            if (Atemp <= 30) cpua=0;
            cpux = cpua;

            // FIcpuxME: Explain this fix better, it's not obvious
            // ML : A =, fixes a problem with invalid pitch with '.'
            while( (cpua=mPitches[cpux]) == 127) cpux++;


        pos48398:
            //48398: CLC
            //48399: ADC 48

            // add the inflection direction
            cpua += mem48;
            byte phase1 = cpua;

            // set the inflection
            mPitches[cpux] = cpua;
        pos48406:

            // increment the position
            cpux++;

            // exit if the punctuation has been reached
            if (cpux == mem49) return; //goto pos47615;
            if (mPitches[cpux] == 255) goto pos48406;
            cpua = phase1;
            goto pos48398;
        }

        private byte RenderSample(ref AudioBuffer a, byte mem39, ref byte mem53, ref byte mem56, ref byte mem66, ref byte cpua, ref byte cpux, ref byte cpuy)
        {
            int tempA;
            // current phoneme's index
            byte mem49 = cpuy;

            // mask low three bits and subtract 1 get value to
            // convert 0 bits on unvoiced samples.
            cpua = (byte)(mem39&7);
            cpux = (byte)(cpua-1);

            // store the result
            mem56 = cpux;

            // determine which offset to use from table { 0x18, 0x1A, 0x17, 0x17, 0x17 }
            // T, S, Z                0          0x18
            // CH, J, SH, ZH          1          0x1A
            // P, F*, V, TH, DH       2          0x17
            // /H                     3          0x17
            // /X                     4          0x17

            // get value from the table
            mem53 = mSpeechRendererTables.tab48426[cpux];
            byte mem47 = cpux;      //46016+mem[56]*256

            // voiced sample?
            cpua = (byte)(mem39 & 248);
            if(cpua == 0)
            {
                // voiced phoneme: Z*, ZH, V*, DH
                cpuy = mem49;
                cpua = (byte)(mPitches[mem49] >> 4);

                // jump to voiced portion
                goto pos48315;
            }

            cpuy = (byte)(cpua ^ 255);
        pos48274:

            // step through the 8 bits in the sample
            mem56 = 8;

            // get the next sample from the table
            // mem47*256 = offset to start of samples
            cpua = mSpeechRendererTables.sampleTable[mem47*256+cpuy];
        pos48280:

            // left shift to get the high bit
            tempA = cpua;
            cpua <<= 1;
            //48281: BCC 48290

            // bit not set?
            if ((tempA & 128) == 0)
            {
                // convert the bit to value from table
                cpux = mem53;
                //mem[54296] = X;
                // output the byte
                Output8Bit(ref a, 1, (byte)((cpux & 0x0f) * 16));
                // if X != 0, exit loop
                if(cpux != 0) goto pos48296;
            }

            // output a 5 for the on bit
            Output8Bit(ref a, 2, 5 * 16);

            //48295: NOP
        pos48296:

            cpux = 0;

            // decrement counter
            mem56--;

            // if not done, jump to top of loop
            if (mem56 != 0) goto pos48280;

            // increment position
            cpuy++;
            if (cpuy != 0) goto pos48274;

            // restore values and return
            byte mem44 = 1;
            cpuy = mem49;
            return mem44;

        pos48315:
            byte phase1;
            // handle voiced samples here

            // number of samples?
            phase1 = (byte)(cpua ^ 255);

            cpuy = mem66;
            do
            {
                //pos48321:

                // shift through all 8 bits
                mem56 = 8;
                //A = Read(mem47, Y);

                // fetch value from table
                cpua = mSpeechRendererTables.sampleTable[mem47*256+cpuy];

                // loop 8 times
                //pos48327:
                do
                {
                    //48327: ASL A
                    //48328: BCC 48337

                    // left shift and check high bit
                    tempA = cpua;
                    cpua <<= 1;
                    if ((tempA & 128) != 0)
                    {
                        // if bit set, output 26
                        cpux = 26;
                        Output8Bit(ref a, 3, (byte)((cpux & 0xf)*16));
                    } else
                    {
                        //timetable 4
                        // bit is not set, output a 6
                        cpux = 6;
                        Output8Bit(ref a, 4, (byte)((cpux & 0xf)*16));
                    }

                    mem56--;
                } while(mem56 != 0);

                // move ahead in the table
                cpuy++;

                // continue until counter done
                phase1++;

            } while (phase1 != 0);
            //  if (phase1 != 0) goto pos48321;

            // restore values and return
            cpua = 1;
            mem44 = 1;
            mem66 = cpuy;
            cpuy = mem49;
            return mem44;
        }
    }

}