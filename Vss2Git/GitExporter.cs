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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Threading;
using System.Windows.Forms;
using Hpdi.VssLogicalLib;
using VssContracts;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Replays and commits changesets into a new Git repository.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class GitExporter : Worker
    {
        private readonly VssDatabase database;
        private readonly RevisionAnalyzer revisionAnalyzer;
        private readonly ChangesetBuilder changesetBuilder;
        private readonly StreamCopier streamCopier = new StreamCopier();
        private readonly HashSet<string> tagsUsed = new HashSet<string>();
        private readonly IMessageDispatcher messageDispatcher;

        public string EmailMapFile { get; set; }

        public Dictionary<string, string> EmailMap { get; set; } = new Dictionary<string, string>();

        public string EmailDomain { get; set; } = "localhost";

        public Encoding CommitEncoding { get; set; } = Encoding.UTF8;

        public bool ForceAnnotatedTags { get; set; } = true;

        public bool IgnoreErrors { get; set; } = false;

        public string DefaultComment { get; set; } = "";

        public GitExporter(
            WorkQueue workQueue, Logger logger,
            RevisionAnalyzer revisionAnalyzer,
            ChangesetBuilder changesetBuilder,
            IMessageDispatcher messageDispatcher)
            : base(workQueue, logger, messageDispatcher)
        {
            database = revisionAnalyzer.Database;
            this.revisionAnalyzer = revisionAnalyzer;
            this.changesetBuilder = changesetBuilder;
            this.messageDispatcher = messageDispatcher;
        }

        public bool ReadEmailMap()
        {
            if (string.IsNullOrEmpty(EmailMapFile))
                return true;

            try
            {
                var xdoc = XDocument.Load(EmailMapFile);
                foreach (var map in xdoc.Elements("map"))
                {
                    EmailMap.Add(map.Attribute("name").Value.ToLower(), map.Attribute("email").Value);
                }
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            return true;
        }

        public void ExportToGit(string repoPath)
        {
            workQueue.AddLast(delegate(object work)
            {
                var stopwatch = Stopwatch.StartNew();

                logger.WriteSectionSeparator();
                LogStatus(work, "Initializing Git repository");

                // create repository directory if it does not exist
                if (!Directory.Exists(repoPath))
                {
                    Directory.CreateDirectory(repoPath);
                }

                while(!ReadEmailMap())
                {
                    var result = messageDispatcher.Dispatch(
                        MessageType.Error, "Email map file not found.", 
                        MessageChoice.OkCancelIgnore);
                    if (result == MessageHandleResult.Cancel)
                    {
                        workQueue.Abort();
                        return;
                    }
                    if (result == MessageHandleResult.Ignore)
                        break;
                }

                var git = new GitWrapper(repoPath, logger) {CommitEncoding = CommitEncoding};

                while (!git.FindExecutable())
                {
                    var result = messageDispatcher.Dispatch(MessageType.Error, 
                        "Git not found in PATH. " +
                        "If you need to modify your PATH variable, please " +
                        "restart the program for the changes to take effect.", MessageChoice.OkCancel);
                    if (result == MessageHandleResult.Cancel)
                    {
                        workQueue.Abort();
                        return;
                    }
                }

                if (!RetryCancel(delegate { git.Init(); }))
                {
                    return;
                }

                if (CommitEncoding.WebName != "utf-8")
                {
                    AbortRetryIgnore(delegate
                    {
                        git.SetConfig("i18n.commitencoding", CommitEncoding.WebName);
                    });
                }

                var pathMapper = new VssPathMapper();

                // create mappings for root projects
                foreach (var rootProject in revisionAnalyzer.RootProjects)
                {
                    var rootPath = VssPathMapper.GetWorkingPath(repoPath, rootProject.Path);
                    pathMapper.SetProjectPath(rootProject.PhysicalName, rootPath, rootProject.Path);
                }

                // replay each changeset
                var changesetId = 1;
                var changesets = changesetBuilder.Changesets;
                var commitCount = 0;
                var tagCount = 0;
                var replayStopwatch = new Stopwatch();
                var labels = new LinkedList<Revision>();
                tagsUsed.Clear();
                foreach (var changeset in changesets)
                {
                    var changesetDesc = string.Format(CultureInfo.InvariantCulture,
                        "changeset {0} from {1}", changesetId, changeset.DateTime);

                    // replay each revision in changeset
                    LogStatus(work, "Replaying " + changesetDesc);
                    labels.Clear();
                    replayStopwatch.Start();
                    bool needCommit;
                    try
                    {
                        needCommit = ReplayChangeset(pathMapper, changeset, git, labels);
                    }
                    finally
                    {
                        replayStopwatch.Stop();
                    }

                    if (workQueue.IsAborting)
                    {
                        return;
                    }

                    // commit changes
                    if (needCommit)
                    {
                        LogStatus(work, "Committing " + changesetDesc);
                        if (CommitChangeset(git, changeset))
                        {
                            ++commitCount;
                        }
                    }

                    if (workQueue.IsAborting)
                    {
                        return;
                    }

                    // create tags for any labels in the changeset
                    if (labels.Count > 0)
                    {
                        foreach (var label in labels)
                        {
                            var labelName = ((VssLabelAction)label.Action).Label;
                            if (string.IsNullOrEmpty(labelName))
                            {
                                logger.WriteLine("NOTE: Ignoring empty label");
                            }
                            else if (commitCount == 0)
                            {
                                logger.WriteLine("NOTE: Ignoring label '{0}' before initial commit", labelName);
                            }
                            else
                            {
                                var tagName = GetTagFromLabel(labelName);

                                var tagMessage = "Creating tag " + tagName;
                                if (tagName != labelName)
                                {
                                    tagMessage += " for label '" + labelName + "'";
                                }
                                LogStatus(work, tagMessage);

                                // annotated tags require (and are implied by) a tag message;
                                // tools like Mercurial's git converter only import annotated tags
                                var tagComment = label.Comment;
                                if (string.IsNullOrEmpty(tagComment) && ForceAnnotatedTags)
                                {
                                    // use the original VSS label as the tag message if none was provided
                                    tagComment = labelName;
                                }

                                if (AbortRetryIgnore(
                                    delegate
                                    {
                                        git.Tag(tagName, label.User, GetEmail(label.User),
                                            tagComment, label.DateTime);
                                    }))
                                {
                                    ++tagCount;
                                }
                            }
                        }
                    }

                    ++changesetId;
                }

                stopwatch.Stop();

                logger.WriteSectionSeparator();
                logger.WriteLine("Git export complete in {0:HH:mm:ss}", new DateTime(stopwatch.ElapsedTicks));
                logger.WriteLine("Replay time: {0:HH:mm:ss}", new DateTime(replayStopwatch.ElapsedTicks));
                logger.WriteLine("Git time: {0:HH:mm:ss}", new DateTime(git.ElapsedTime.Ticks));
                logger.WriteLine("Git commits: {0}", commitCount);
                logger.WriteLine("Git tags: {0}", tagCount);
            });
        }

        private bool ReplayChangeset(VssPathMapper pathMapper, Changeset changeset,
            GitWrapper git, LinkedList<Revision> labels)
        {
            var needCommit = false;
            foreach (var revision in changeset.Revisions)
            {
                if (workQueue.IsAborting)
                {
                    break;
                }

                AbortRetryIgnore(delegate
                {
                    needCommit |= ReplayRevision(pathMapper, revision, git, labels);
                });
            }
            return needCommit;
        }

        private bool ReplayRevision(VssPathMapper pathMapper, Revision revision,
            GitWrapper git, LinkedList<Revision> labels)
        {
            var needCommit = false;
            var actionType = revision.Action.Type;
            if (revision.Item.IsProject)
            {
                // note that project path (and therefore target path) can be
                // null if a project was moved and its original location was
                // subsequently destroyed
                var project = revision.Item;
                var projectPath = pathMapper.GetProjectPath(project.PhysicalName);
                var projectDesc = projectPath;
                if (projectPath == null)
                {
                    projectDesc = revision.Item.ToString();
                    logger.WriteLine("NOTE: {0} is currently unmapped", project);
                }

                VssItemName target = null;
                string targetPath = null;
                if (revision.Action is VssNamedAction namedAction)
                {
                    target = namedAction.Name;
                    if (projectPath != null)
                    {
                        targetPath = Path.Combine(projectPath, target.LogicalName);
                    }
                }

                var isAddAction = false;
                var writeProject = false;
                var writeFile = false;
                VssItemInfo itemInfo = null;
                switch (actionType)
                {
                    case VssActionType.Label:
                        // defer tagging until after commit
                        labels.AddLast(revision);
                        break;

                    case VssActionType.Create:
                        // ignored; items are actually created when added to a project
                        break;

                    case VssActionType.Add:
                    case VssActionType.Share:
                        logger.WriteLine("{0}: {1} {2}", projectDesc, actionType, target.LogicalName);
                        itemInfo = pathMapper.AddItem(project, target);
                        isAddAction = true;
                        break;

                    case VssActionType.Recover:
                        logger.WriteLine("{0}: {1} {2}", projectDesc, actionType, target.LogicalName);
                        itemInfo = pathMapper.RecoverItem(project, target);
                        isAddAction = true;
                        break;

                    case VssActionType.Delete:
                    case VssActionType.Destroy:
                        {
                            logger.WriteLine("{0}: {1} {2}", projectDesc, actionType, target.LogicalName);
                            itemInfo = pathMapper.DeleteItem(project, target);
                            if (targetPath != null && !itemInfo.Destroyed)
                            {
                                if (target.IsProject)
                                {
                                    if (Directory.Exists(targetPath))
                                    {
                                        if (((VssProjectInfo)itemInfo).ContainsFiles())
                                        {
                                            git.Remove(targetPath, true);
                                            needCommit = true;
                                        }
                                        else
                                        {
                                            // git doesn't care about directories with no files
                                            Directory.Delete(targetPath, true);
                                        }
                                    }
                                }
                                else
                                {
                                    if (File.Exists(targetPath))
                                    {
                                        // not sure how it can happen, but a project can evidently
                                        // contain another file with the same logical name, so check
                                        // that this is not the case before deleting the file
                                        if (pathMapper.ProjectContainsLogicalName(project, target))
                                        {
                                            logger.WriteLine("NOTE: {0} contains another file named {1}; not deleting file",
                                                projectDesc, target.LogicalName);
                                        }
                                        else
                                        {
                                            File.Delete(targetPath);
                                            needCommit = true;
                                        }
                                    }
                                }
                            }
                        }
                        break;

                    case VssActionType.Rename:
                        {
                            var renameAction = (VssRenameAction)revision.Action;
                            logger.WriteLine("{0}: {1} {2} to {3}",
                                projectDesc, actionType, renameAction.OriginalName, target.LogicalName);
                            itemInfo = pathMapper.RenameItem(target);
                            if (targetPath != null && !itemInfo.Destroyed)
                            {
                                var sourcePath = Path.Combine(projectPath, renameAction.OriginalName);
                                if (target.IsProject ? Directory.Exists(sourcePath) : File.Exists(sourcePath))
                                {
                                    // renaming a file or a project that contains files?
                                    if (!(itemInfo is VssProjectInfo projectInfo) || (projectInfo.ContainsFiles() && projectInfo.Items.All(x => !x.Destroyed)))
                                    {
                                        CaseSensitiveRename(sourcePath, targetPath, git.Move);
                                        needCommit = true;
                                    }
                                    else
                                    {
                                        // git doesn't care about directories with no files
                                        CaseSensitiveRename(sourcePath, targetPath, CaseSensitiveDirectoryMove);
                                    }
                                }
                                else
                                {
                                    logger.WriteLine("NOTE: Skipping rename because {0} does not exist", sourcePath);
                                }
                            }
                        }
                        break;

                    case VssActionType.MoveFrom:
                        // if both MoveFrom & MoveTo are present (e.g.
                        // one of them has not been destroyed), only one
                        // can succeed, so check that the source exists
                        {
                            var moveFromAction = (VssMoveFromAction)revision.Action;
                            logger.WriteLine("{0}: Move from {1} to {2}",
                                projectDesc, moveFromAction.OriginalProject, targetPath ?? target.LogicalName);
                            var sourcePath = pathMapper.GetProjectPath(target.PhysicalName);
                            var projectInfo = pathMapper.MoveProjectFrom(
                                project, target, moveFromAction.OriginalProject);
                            if (targetPath != null && !projectInfo.Destroyed)
                            {
                                if (sourcePath != null && Directory.Exists(sourcePath))
                                {
                                    var isSamePath = sourcePath.Equals(targetPath, StringComparison.OrdinalIgnoreCase);
                                    if (projectInfo.ContainsFiles())
                                    {
                                        git.Move(sourcePath, targetPath, isSamePath);
                                        needCommit = true;
                                    }
                                    else
                                    {
                                        // git doesn't care about directories with no files
                                        CaseSensitiveDirectoryMove(sourcePath, targetPath, isSamePath);
                                    }
                                }
                                else
                                {
                                    // project was moved from a now-destroyed project
                                    writeProject = true;
                                }
                            }
                        }
                        break;

                    case VssActionType.MoveTo:
                        {
                            // handle actual moves in MoveFrom; this just does cleanup of destroyed projects
                            var moveToAction = (VssMoveToAction)revision.Action;
                            logger.WriteLine("{0}: Move to {1} from {2}",
                                projectDesc, moveToAction.NewProject, targetPath ?? target.LogicalName);
                            var projectInfo = pathMapper.MoveProjectTo(
                                project, target, moveToAction.NewProject);
                            if (projectInfo.Destroyed && targetPath != null && Directory.Exists(targetPath))
                            {
                                // project was moved to a now-destroyed project; remove empty directory
                                Directory.Delete(targetPath, true);
                            }
                        }
                        break;

                    case VssActionType.Pin:
                        {
                            var pinAction = (VssPinAction)revision.Action;
                            if (pinAction.Pinned)
                            {
                                logger.WriteLine("{0}: Pin {1}", projectDesc, target.LogicalName);
                                itemInfo = pathMapper.PinItem(project, target);
                            }
                            else
                            {
                                logger.WriteLine("{0}: Unpin {1}", projectDesc, target.LogicalName);
                                itemInfo = pathMapper.UnpinItem(project, target);
                                writeFile = !itemInfo.Destroyed;
                            }
                        }
                        break;

                    case VssActionType.Branch:
                        {
                            var branchAction = (VssBranchAction)revision.Action;
                            logger.WriteLine("{0}: {1} {2}", projectDesc, actionType, target.LogicalName);
                            itemInfo = pathMapper.BranchFile(project, target, branchAction.Source);
                            // branching within the project might happen after branching of the file
                            writeFile = true;
                        }
                        break;

                    case VssActionType.Archive:
                        // currently ignored
                        {
                            var archiveAction = (VssArchiveAction)revision.Action;
                            logger.WriteLine("{0}: Archive {1} to {2} (ignored)",
                                projectDesc, target.LogicalName, archiveAction.ArchivePath);
                        }
                        break;

                    case VssActionType.Restore:
                        {
                            var restoreAction = (VssRestoreAction)revision.Action;
                            logger.WriteLine("{0}: Restore {1} from archive {2}",
                                projectDesc, target.LogicalName, restoreAction.ArchivePath);
                            itemInfo = pathMapper.AddItem(project, target);
                            isAddAction = true;
                        }
                        break;
                }

                if (targetPath != null)
                {
                    if (isAddAction)
                    {
                        if (revisionAnalyzer.IsDestroyed(target.PhysicalName) &&
                            !database.ItemExists(target.PhysicalName))
                        {
                            logger.WriteLine("NOTE: Skipping destroyed file: {0}", targetPath);
                            itemInfo.Destroyed = true;
                        }
                        else if (target.IsProject)
                        {
                            Directory.CreateDirectory(targetPath);
                            writeProject = true;
                        }
                        else
                        {
                            writeFile = true;
                        }
                    }

                    if (writeProject && pathMapper.IsProjectRooted(target.PhysicalName))
                    {
                        // create all contained subdirectories
                        foreach (var projectInfo in pathMapper.GetAllProjects(target.PhysicalName))
                        {
                            logger.WriteLine("{0}: Creating subdirectory {1}",
                                projectDesc, projectInfo.LogicalName);
                            Directory.CreateDirectory(projectInfo.GetPath());
                        }

                        // write current rev of all contained files
                        foreach (var fileInfo in pathMapper.GetAllFiles(target.PhysicalName))
                        {
                            if (WriteRevision(pathMapper, actionType, fileInfo.PhysicalName,
                                fileInfo.Version, target.PhysicalName, git))
                            {
                                // one or more files were written
                                needCommit = true;
                            }
                        }
                    }
                    else if (writeFile)
                    {
                        // write current rev to working path
                        var version = pathMapper.GetFileVersion(target.PhysicalName);
                        if (WriteRevisionTo(target.PhysicalName, version, targetPath))
                        {
                            // add file explicitly, so it is visible to subsequent git operations
                            git.Add(targetPath);
                            needCommit = true;
                        }
                    }
                }
            }
            // item is a file, not a project
            else if (actionType == VssActionType.Edit || actionType == VssActionType.Branch)
            {
                // if the action is Branch, the following code is necessary only if the item
                // was branched from a file that is not part of the migration subset; it will
                // make sure we start with the correct revision instead of the first revision

                var target = revision.Item;

                // update current rev
                pathMapper.SetFileVersion(target, revision.Version);

                // write current rev to all sharing projects
                WriteRevision(pathMapper, actionType, target.PhysicalName,
                    revision.Version, null, git);
                needCommit = true;
            }
            return needCommit;
        }

        private bool CommitChangeset(GitWrapper git, Changeset changeset)
        {
            var result = false;
            AbortRetryIgnore(delegate
            {
                result = git.AddAll() &&
                    git.Commit(changeset.User, GetEmail(changeset.User),
                    changeset.Comment ?? DefaultComment, changeset.DateTime);
            });
            return result;
        }

        private bool RetryCancel(ThreadStart work)
        {
            return AbortRetryIgnore(work, MessageChoice.OkCancel);
        }

        private bool AbortRetryIgnore(ThreadStart work)
        {
            return AbortRetryIgnore(work, MessageChoice.OkCancelIgnore);
        }

        private bool AbortRetryIgnore(ThreadStart work, MessageChoice choice)
        {
            bool retry;
            do
            {
                try
                {
                    work();
                    return true;
                }
                catch (Exception e)
                {
                    var message = LogException(e);

                    message += "\nSee log file for more information.";

                    if (IgnoreErrors)
                    {
                        retry = false;
                        continue;
                    }

                    var result = messageDispatcher.Dispatch(MessageType.Error, message, choice);
                    switch (result)
                    {
                        case MessageHandleResult.Ok:
                            retry = true;
                            break;
                        case MessageHandleResult.Ignore:
                            retry = false;
                            break;
                        default:
                            retry = false;
                            workQueue.Abort();
                            break;
                    }
                }
            } while (retry);
            return false;
        }

        private string GetEmail(string user)
        {
            // check user-defined mapping of user names to email addresses
            var username = user.ToLower();
            if (EmailMap.TryGetValue(username, out var email))
            {
                if (!email.Contains("@"))
                    email += "@" + EmailDomain;
                return email;
            }
            return username.Replace(' ', '.') + "@" + EmailDomain;
        }

        private string GetTagFromLabel(string label)
        {
            // git tag names must be valid filenames, so replace sequences of
            // invalid characters with an underscore
            var baseTag = Regex.Replace(label, "[^A-Za-z0-9_-]+", "_");

            // git tags are global, whereas VSS tags are local, so ensure
            // global uniqueness by appending a number; since the file system
            // may be case-insensitive, ignore case when hashing tags
            var tag = baseTag;
            for (var i = 2; !tagsUsed.Add(tag.ToUpperInvariant()); ++i)
            {
                tag = baseTag + "-" + i;
            }

            return tag;
        }

        private bool WriteRevision(VssPathMapper pathMapper, VssActionType actionType,
            string physicalName, int version, string underProject, GitWrapper git)
        {
            var needCommit = false;
            var paths = pathMapper.GetFilePaths(physicalName, underProject);
            foreach (var path in paths)
            {
                logger.WriteLine("{0}: {1} revision {2}", path, actionType, version);
                if (WriteRevisionTo(physicalName, version, path))
                {
                    // add file explicitly, so it is visible to subsequent git operations
                    git.Add(path);
                    needCommit = true;
                }
            }
            return needCommit;
        }

        private bool WriteRevisionTo(string physical, int version, string destPath)
        {
            VssFile item;
            VssFileRevision revision;
            Stream contents;
            try
            {
                item = (VssFile)database.GetItemPhysical(physical);
                revision = item.GetRevision(version);
                contents = revision.GetContents();
            }
            catch (Exception e)
            {
                // log an error for missing data files or versions, but keep processing
                var message = ExceptionFormatter.Format(e);
                logger.WriteLine("ERROR: {0}", message);
                logger.WriteLine(e);
                return false;
            }

            // propagate exceptions here (e.g. disk full) to abort/retry/ignore
            using (contents)
            {
                WriteStream(contents, destPath);
            }

            // try to use the first revision (for this branch) as the create time,
            // since the item creation time doesn't seem to be meaningful
            var createDateTime = item.Created;
            using (var revEnum = item.Revisions.GetEnumerator())
            {
                if (revEnum.MoveNext() && revEnum.Current != null)
                {
                    createDateTime = revEnum.Current.DateTime;
                }
            }

            // set file creation and update timestamps
            File.SetCreationTimeUtc(destPath, TimeZoneInfo.ConvertTimeToUtc(createDateTime));
            File.SetLastWriteTimeUtc(destPath, TimeZoneInfo.ConvertTimeToUtc(revision.DateTime));

            return true;
        }

        private void WriteStream(Stream inputStream, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (var outputStream = new FileStream(
                path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                streamCopier.Copy(inputStream, outputStream);
            }
        }

        private delegate void RenameDelegate(string sourcePath, string destPath, bool force);

        private static void CaseSensitiveRename(string sourcePath, string destPath, RenameDelegate renamer)
        {
            renamer(sourcePath, destPath, sourcePath.Equals(destPath, StringComparison.OrdinalIgnoreCase));
        }

        private void CaseSensitiveDirectoryMove(string sourcePath, string targetPath, bool force)
        {
            if (force)
            {
                var tmpPath = targetPath + ".mvtmp";
                Directory.Move(sourcePath, tmpPath);
                Directory.Move(tmpPath, targetPath);
            }
            else
            {
                Directory.Move(sourcePath, targetPath);
            }
        }
    }
}
