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

using System.IO;

namespace Hpdi.VssPhysicalLib
{
    /// <summary>
    /// Represents the header of a VSS record.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class RecordHeader
    {
        public const int LENGTH = 8;

        public int Offset { get; private set; }
        public int Length { get; private set; }
        public string Signature { get; private set; }
        public ushort FileCrc { get; private set; }
        public ushort ActualCrc { get; private set; }
        public bool IsCrcValid { get { return FileCrc == ActualCrc; } }

        public void CheckSignature(string expected)
        {
            if (Signature != expected)
            {
                throw new RecordNotFoundException(string.Format(
                    "Unexpected record signature: expected={0}, actual={1}",
                    expected, Signature));
            }
        }

        public void CheckCrc()
        {
            if (!IsCrcValid)
            {
                throw new RecordCrcException(this, string.Format(
                    "CRC error in {0} record: expected={1}, actual={2}",
                    Signature, FileCrc, ActualCrc));
            }
        }

        public void Read(BufferReader reader)
        {
            Offset = reader.Offset;
            Length = reader.ReadInt32();
            Signature = reader.ReadSignature(2);
            FileCrc = (ushort)reader.ReadInt16();
            ActualCrc = reader.Crc16(Length);
        }

        public void Dump(TextWriter writer)
        {
            writer.WriteLine(
                "Signature: {0} - Length: {1} - Offset: {2:X6} - CRC: {3:X4} ({5}: {4:X4})",
                Signature, Length, Offset, FileCrc, ActualCrc, IsCrcValid ? "valid" : "INVALID");
        }
    }
}
