using System;

namespace SAMCS
{

    public class AudioBuffer
    {
        private static readonly int BUFFER_SIZE = 0x1000;
        private byte[] mBuffer;
        private int mCursor;
        private int mSize;

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

        public void Append(byte b)
        {
            mBuffer ??= new byte[BUFFER_SIZE];

            if (mBuffer.Length <= mSize + 1)
            {
                byte[] buff = new byte[mBuffer.Length + BUFFER_SIZE];
                Array.Copy(mBuffer, buff, mBuffer.Length);
                mBuffer = buff;
            }

            mBuffer[mSize] = b;
            mSize++;
        }

        public void Append(byte[] buffer)
        {
            mBuffer ??= new byte[BUFFER_SIZE];

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

        public void Write(int cursor, byte b)
        {
            mBuffer ??= new byte[BUFFER_SIZE];

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