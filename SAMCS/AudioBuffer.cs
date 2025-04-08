using System;

namespace SAMCS
{

    public class AudioBuffer
    {
        private static readonly int BUFFER_SIZE = 0x1000;
        private byte[] mBuffer;
        private int mCursor;
        private int mSize;

        public int Samplerate => 22050;
        public int BitsPerSample => 8;
        public int Channels => 1;

        public byte[] Buffer
        {
            get => mBuffer;
            set => throw new NotSupportedException();
        }

        public int Size
        {
            get => mSize;
            set => throw new NotSupportedException();
        }

        public int Cursor
        {
            get => mCursor;
            set => mCursor = value;
        }

        public AudioBuffer()
        {
            mBuffer = null;
            mCursor = 0;
            mSize = 0;
        }

        /// <summary>
        /// Append a byte of audio data to the end of the buffer
        /// </summary>
        /// <param name="b">The single byte audio data</param>
        public void Append(byte b)
        {
            if (mBuffer == null)
                mBuffer = new byte[BUFFER_SIZE];

            if (mBuffer.Length <= mSize + 1)
            {
                byte[] buff = new byte[mBuffer.Length + BUFFER_SIZE];
                Array.Copy(mBuffer, buff, mBuffer.Length);
                mBuffer = buff;
            }

            mBuffer[mSize] = b;
            mSize++;
        }

        /// <summary>
        /// Append a buffer to the end of this audio buffer
        /// </summary>
        /// <param name="buffer">Audio data</param>
        public void Append(byte[] buffer)
        {
            if (mBuffer == null)
                mBuffer = new byte[BUFFER_SIZE];

            if (mBuffer.Length <= mSize + buffer.Length)
            {
                int extraSpace = (int)(buffer.Length / BUFFER_SIZE) + 1;

                byte[] buff = new byte[mBuffer.Length + extraSpace*BUFFER_SIZE];
                Array.Copy(mBuffer, buff, mBuffer.Length);
                mBuffer = buff;
            }

            buffer.CopyTo(mBuffer, mSize);
            mSize += buffer.Length;
        }

        /// <summary>
        /// Append a single byte of data into the buffer at a specific position 
        /// </summary>
        /// <param name="cursor">Where in the buffer we should append</param>
        /// <param name="b">The single byte of audio data</param>
        public void AppendAt(int cursor, byte b)
        {
            if (mBuffer == null)
                mBuffer = new byte[BUFFER_SIZE];

            if (cursor >= mBuffer.Length && mBuffer.Length <= cursor + 1)
            {
                byte[] buff = new byte[mBuffer.Length + BUFFER_SIZE];
                Array.Copy(mBuffer, buff, mBuffer.Length);
                mBuffer = buff;
            }

            mBuffer[cursor] = b;
            mSize = mBuffer.Length;
        }
    }

}