/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Text;
using Hpdi.HashLib;

namespace Hpdi.VssPhysicalLib
{
    /// <summary>
    /// Reads VSS data types from a byte buffer.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class BufferReader
    {
        private readonly Encoding encoding;
        private readonly byte[] data;
        private int limit;

        public BufferReader(Encoding encoding, byte[] data)
            : this(encoding, data, 0, data.Length)
        {
        }

        public BufferReader(Encoding encoding, byte[] data, int offset, int limit)
        {
            this.encoding = encoding;
            this.data = data;
            this.Offset = offset;
            this.limit = limit;
        }

        public int Offset { get; set; }

        public int Remaining
        {
            get { return limit - Offset; }
        }

        public ushort Checksum16()
        {
            ushort sum = 0;
            for (var i = Offset; i < limit; ++i)
            {
                sum += data[i];
            }
            return sum;
        }

        private static Hash16 crc16 = new XorHash32To16(new Crc32(Crc32.IEEE));

        public ushort Crc16()
        {
            return crc16.Compute(data, Offset, limit);
        }

        public ushort Crc16(int bytes)
        {
            CheckRead(bytes);
            return crc16.Compute(data, Offset, Offset + bytes);
        }

        public void Skip(int bytes)
        {
            CheckRead(bytes);
            Offset += bytes;
        }

        public short ReadInt16()
        {
            CheckRead(2);
            return (short)(data[Offset++] | (data[Offset++] << 8));
        }

        public int ReadInt32()
        {
            CheckRead(4);
            return data[Offset++] | (data[Offset++] << 8) |
                (data[Offset++] << 16) | (data[Offset++] << 24);
        }

        private static readonly DateTime EPOCH =
            new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Local);

        public DateTime ReadDateTime()
        {
            return EPOCH + TimeSpan.FromSeconds(ReadInt32());
        }

        public string ReadSignature(int length)
        {
            CheckRead(length);
            var buf = new StringBuilder(length);
            for (var i = 0; i < length; ++i)
            {
                buf.Append((char)data[Offset++]);
            }
            return buf.ToString();
        }

        public VssName ReadName()
        {
            CheckRead(2 + 34 + 4);
            return new VssName(ReadInt16(), ReadString(34), ReadInt32());
        }

        public string ReadString(int fieldSize)
        {
            CheckRead(fieldSize);

            var count = 0;
            for (var i = 0; i < fieldSize; ++i)
            {
                if (data[Offset + i] == 0) break;
                ++count;
            }

            var str = encoding.GetString(data, Offset, count);

            Offset += fieldSize;

            return str;
        }

        public string ReadByteString(int bytes)
        {
            CheckRead(bytes);
            var result = FormatBytes(bytes);
            Offset += bytes;
            return result;
        }

        public BufferReader Extract(int bytes)
        {
            CheckRead(bytes);
            return new BufferReader(encoding, data, Offset, Offset += bytes);
        }

        public ArraySegment<byte> GetBytes(int bytes)
        {
            CheckRead(bytes);
            var result = new ArraySegment<byte>(data, Offset, bytes);
            Offset += bytes;
            return result;
        }

        public string FormatBytes(int bytes)
        {
            var formatLimit = Math.Min(limit, Offset + bytes);
            var buf = new StringBuilder((formatLimit - Offset) * 3);
            for (var i = Offset; i < formatLimit; ++i)
            {
                buf.AppendFormat("{0:X2} ", data[i]);
            }
            return buf.ToString();
        }

        public string FormatRemaining()
        {
            return FormatBytes(Remaining);
        }

        private void CheckRead(int bytes)
        {
            if (Offset + bytes > limit)
            {
                throw new EndOfBufferException(string.Format(
                    "Attempted read of {0} bytes with only {1} bytes remaining in buffer",
                    bytes, Remaining));
            }
        }
    }
}
