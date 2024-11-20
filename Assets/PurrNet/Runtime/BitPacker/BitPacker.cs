using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using PurrNet.Transports;

namespace PurrNet.Packing
{
    [UsedImplicitly]
    public partial class BitPacker : IDisposable
    {
        private byte[] _buffer;
        private int _positionInBits;
        private bool _isReading;

        public int length
        {
            get
            {
                int pos = _positionInBits / 8;
                int len = pos + (_positionInBits % 8 == 0 ? 0 : 1);
                return len;
            }
        }
        
        public bool isReading => _isReading;
        
        public bool isWriting => !_isReading;
        
        public BitPacker(int initialSize = 1024)
        {
            _buffer = new byte[initialSize];
        }

        public void Dispose()
        {
            BitStreamPool.Free(this);
        }
        
        public ByteData ToByteData()
        {
            return new ByteData(_buffer, 0, length);
        }
        
        public void ResetPosition()
        {
            _positionInBits = 0;
        }
        
        public void ResetMode(bool readMode)
        {
            _isReading = readMode;
        }
        
        public void ResetPositionAndMode(bool readMode)
        {
            _positionInBits = 0;
            _isReading = readMode;
        }
        
        private void EnsureBitsExist(int bits)
        {
            int targetPos = (_positionInBits + bits) / 8;

            if (targetPos >= _buffer.Length)
            {
                if (_isReading)
                    throw new IndexOutOfRangeException("Not enough bits in the buffer.");
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }
        }
        
        public void WriteBits(ulong data, byte bits)
        {
            EnsureBitsExist(bits);
            
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits), "Cannot write more than 64 bits at a time.");
            
            int bitsLeft = bits;

            while (bitsLeft > 0)
            {
                int bytePos = _positionInBits / 8;
                int bitOffset = _positionInBits % 8;
                int bitsToWrite = Math.Min(bitsLeft, 8 - bitOffset);

                byte mask = (byte)((1 << bitsToWrite) - 1);
                byte value = (byte)((data >> (bits - bitsLeft)) & mask);

                _buffer[bytePos] &= (byte)~(mask << bitOffset); // Clear the bits to be written
                _buffer[bytePos] |= (byte)(value << bitOffset); // Set the bits

                bitsLeft -= bitsToWrite;
                _positionInBits += bitsToWrite;
            }
        }

        public ulong ReadBits(byte bits)
        {
            if (bits > 64)
                throw new ArgumentOutOfRangeException(nameof(bits), "Cannot read more than 64 bits at a time.");
            
            ulong result = 0;
            int bitsLeft = bits;

            while (bitsLeft > 0)
            {
                int bytePos = _positionInBits / 8;
                int bitOffset = _positionInBits % 8;
                int bitsToRead = Math.Min(bitsLeft, 8 - bitOffset);

                byte mask = (byte)((1 << bitsToRead) - 1);
                byte value = (byte)((_buffer[bytePos] >> bitOffset) & mask);

                result |= (ulong)value << (bits - bitsLeft);

                bitsLeft -= bitsToRead;
                _positionInBits += bitsToRead;
            }

            return result;
        }

        public void ReadBytes(IList<byte> bytes)
        {
            int count = bytes.Count;

            EnsureBitsExist(count * 8);

            int excess = count % 8;
            int fullChunks = count / 8;

            int index = 0;

            // Process excess bytes (remaining bytes before full 64-bit chunks)
            for (int i = 0; i < excess; i++)
            {
                bytes[index++] = (byte)ReadBits(8);
            }

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                var longValue = ReadBits(64);

                for (int j = 0; j < 8; j++)
                {
                    if (index < count)
                    {
                        bytes[index++] = (byte)(longValue >> (j * 8));
                    }
                }
            }
        }
        
        public void WriteBytes(IReadOnlyList<byte> bytes)
        {
            EnsureBitsExist(bytes.Count * 8);

            int count = bytes.Count;
            int fullChunks = count / 8; // Number of full 64-bit chunks
            int excess = count % 8;     // Remaining bytes after full chunks

            int index = 0;

            // Process full 64-bit chunks
            for (int i = 0; i < fullChunks; i++)
            {
                ulong longValue = 0;

                // Combine 8 bytes into a single 64-bit value
                for (int j = 0; j < 8; j++)
                    longValue |= (ulong)bytes[index++] << (j * 8);

                // Write the 64-bit chunk
                WriteBits(longValue, 64);
            }

            // Process remaining excess bytes
            for (int i = 0; i < excess; i++)
            {
                WriteBits(bytes[index++], 8);
            }
        }
    }
}