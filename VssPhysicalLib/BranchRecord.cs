﻿/* Copyright 2009 HPDI, LLC
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
    /// VSS record representing a branch file.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class BranchRecord : VssRecord
    {
        public const string SIGNATURE = "BF";

        public override string Signature { get { return SIGNATURE; } }
        public int PrevBranchOffset { get; private set; }
        public string BranchFile { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            PrevBranchOffset = reader.ReadInt32();
            BranchFile = reader.ReadString(12);
        }

        public override void Dump(TextWriter writer)
        {
            writer.WriteLine("  Prev branch offset: {0:X6}", PrevBranchOffset);
            writer.WriteLine("  Branch file: {0}", BranchFile);
        }
    }
}
