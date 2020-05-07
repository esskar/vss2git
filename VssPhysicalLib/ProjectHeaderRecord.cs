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
    /// VSS header record for a project.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class ProjectHeaderRecord : ItemHeaderRecord
    {
        public string ParentProject { get; private set; }
        public string ParentFile { get; private set; }
        public int TotalItems { get; private set; }
        public int Subprojects { get; private set; }

        public ProjectHeaderRecord()
            : base(ItemType.Project)
        {
        }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            ParentProject = reader.ReadString(260);
            ParentFile = reader.ReadString(8);
            reader.Skip(4); // reserved; always 0
            TotalItems = reader.ReadInt16();
            Subprojects = reader.ReadInt16();
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Parent project: {0}", ParentProject);
            writer.WriteLine("  Parent file: {0}", ParentFile);
            writer.WriteLine("  Total items: {0}", TotalItems);
            writer.WriteLine("  Subprojects: {0}", Subprojects);
        }
    }
}
