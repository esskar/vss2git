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
using System.IO;

namespace Hpdi.VssPhysicalLib
{
    /// <summary>
    /// Enumeration of physical VSS revision actions.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public enum Action
    {
        // project actions
        Label = 0,
        CreateProject = 1,
        AddProject = 2,
        AddFile = 3,
        DestroyProject = 4,
        DestroyFile = 5,
        DeleteProject = 6,
        DeleteFile = 7,
        RecoverProject = 8,
        RecoverFile = 9,
        RenameProject = 10,
        RenameFile = 11,
        MoveFrom = 12,
        MoveTo = 13,
        ShareFile = 14,
        BranchFile = 15,

        // file actions
        CreateFile = 16,
        EditFile = 17,
        CreateBranch = 19,

        // archive actions
        ArchiveProject = 23,
        RestoreFile = 24,
        RestoreProject = 25
    }

    /// <summary>
    /// VSS record representing a project/file revision.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public class RevisionRecord : VssRecord
    {
        public const string SIGNATURE = "EL";

        protected int prevRevOffset;
        protected Action action;
        protected int revision;
        protected DateTime dateTime;
        protected string user;
        protected string label;
        protected int commentOffset; // or next revision if no comment
        protected int labelCommentOffset; // or label comment
        protected int commentLength;
        protected int labelCommentLength;

        public override string Signature { get { return SIGNATURE; } }
        public int PrevRevOffset { get { return prevRevOffset; } }
        public Action Action { get { return action; } }
        public int Revision { get { return revision; } }
        public DateTime DateTime { get { return dateTime; } }
        public string User { get { return user; } }
        public string Label { get { return label; } }
        public int CommentOffset { get { return commentOffset; } }
        public int LabelCommentOffset { get { return labelCommentOffset; } }
        public int CommentLength { get { return commentLength; } }
        public int LabelCommentLength { get { return labelCommentLength; } }

        public static Action PeekAction(BufferReader reader)
        {
            var saveOffset = reader.Offset;
            try
            {
                reader.Skip(4);
                return (Action)reader.ReadInt16();
            }
            finally
            {
                reader.Offset = saveOffset;
            }
        }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            prevRevOffset = reader.ReadInt32();
            action = (Action)reader.ReadInt16();
            revision = reader.ReadInt16();
            dateTime = reader.ReadDateTime();
            user = reader.ReadString(32);
            label = reader.ReadString(32);
            commentOffset = reader.ReadInt32();
            labelCommentOffset = reader.ReadInt32();
            commentLength = reader.ReadInt16();
            labelCommentLength = reader.ReadInt16();
        }

        public override void Dump(TextWriter writer)
        {
            writer.WriteLine("  Prev rev offset: {0:X6}", prevRevOffset);
            writer.WriteLine("  #{0:D3} {1} by '{2}' at {3}",
                revision, action, user, dateTime);
            writer.WriteLine("  Label: {0}", label);
            writer.WriteLine("  Comment: length {0}, offset {1:X6}", commentLength, commentOffset);
            writer.WriteLine("  Label comment: length {0}, offset {1:X6}", labelCommentLength, labelCommentOffset);
        }
    }

    public class CommonRevisionRecord : RevisionRecord
    {
        VssName name;

        public VssName Name { get { return name; } }
        public string Physical { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            name = reader.ReadName();
            Physical = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
        }
    }

    public class DestroyRevisionRecord : RevisionRecord
    {
        VssName name;
        short unkShort;

        public VssName Name { get { return name; } }
        public string Physical { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            name = reader.ReadName();
            unkShort = reader.ReadInt16(); // 0 or 1
            Physical = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
        }
    }

    public class RenameRevisionRecord : RevisionRecord
    {
        VssName name;
        VssName oldName;

        public VssName Name { get { return name; } }
        public VssName OldName { get { return oldName; } }
        public string Physical { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            name = reader.ReadName();
            oldName = reader.ReadName();
            Physical = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Name: {0} -> {1} ({2})",
                oldName.ShortName, name.ShortName, Physical);
        }
    }

    public class MoveRevisionRecord : RevisionRecord
    {
        VssName name;

        public string ProjectPath { get; private set; }
        public VssName Name { get { return name; } }
        public string Physical { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            ProjectPath = reader.ReadString(260);
            name = reader.ReadName();
            Physical = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Project path: {0}", ProjectPath);
            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
        }
    }

    public class ShareRevisionRecord : RevisionRecord
    {
        VssName name;
        short unkShort;

        public string ProjectPath { get; private set; }
        public VssName Name { get { return name; } }
        public short UnpinnedRevision { get; private set; }
        public short PinnedRevision { get; private set; }
        public string Physical { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            ProjectPath = reader.ReadString(260);
            name = reader.ReadName();
            UnpinnedRevision = reader.ReadInt16();
            PinnedRevision = reader.ReadInt16();
            unkShort = reader.ReadInt16(); // often seems to increment
            Physical = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Project path: {0}", ProjectPath);
            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
            if (UnpinnedRevision == 0)
            {
                writer.WriteLine("  Pinned at revision {0}", PinnedRevision);
            }
            else if (UnpinnedRevision > 0)
            {
                writer.WriteLine("  Unpinned at revision {0}", UnpinnedRevision);
            }
        }
    }

    public class BranchRevisionRecord : RevisionRecord
    {
        VssName name;

        public VssName Name { get { return name; } }
        public string Physical { get; private set; }
        public string BranchFile { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            name = reader.ReadName();
            Physical = reader.ReadString(10);
            BranchFile = reader.ReadString(10);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
            writer.WriteLine("  Branched from file: {0}", BranchFile);
        }
    }

    public class EditRevisionRecord : RevisionRecord
    {
        public int PrevDeltaOffset { get; private set; }
        public string ProjectPath { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            PrevDeltaOffset = reader.ReadInt32();
            reader.Skip(4); // reserved; always 0
            ProjectPath = reader.ReadString(260);
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Prev delta offset: {0:X6}", PrevDeltaOffset);
            writer.WriteLine("  Project path: {0}", ProjectPath);
        }
    }

    public class ArchiveRevisionRecord : RevisionRecord
    {
        VssName name;

        public VssName Name { get { return name; } }
        public string Physical { get; private set; }
        public string ArchivePath { get; private set; }

        public override void Read(BufferReader reader, RecordHeader header)
        {
            base.Read(reader, header);

            name = reader.ReadName();
            Physical = reader.ReadString(10);
            reader.Skip(2); // 0?
            ArchivePath = reader.ReadString(260);
            reader.Skip(4); // ?
        }

        public override void Dump(TextWriter writer)
        {
            base.Dump(writer);

            writer.WriteLine("  Name: {0} ({1})", name.ShortName, Physical);
            writer.WriteLine("  Archive path: {0}", ArchivePath);
        }
    }
}
