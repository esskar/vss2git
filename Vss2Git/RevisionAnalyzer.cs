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
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Hpdi.VssLogicalLib;
using Hpdi.VssPhysicalLib;
using VssContracts;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Enumerates revisions in a VSS database.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class RevisionAnalyzer : Worker
    {
        public string ExcludeFiles { get; set; }

        public VssDatabase Database { get; }

        private readonly LinkedList<VssProject> rootProjects = new LinkedList<VssProject>();
        public IEnumerable<VssProject> RootProjects
        {
            get { return rootProjects; }
        }

        public SortedDictionary<DateTime, ICollection<Revision>> SortedRevisions { get; } = new SortedDictionary<DateTime, ICollection<Revision>>();

        public HashSet<string> ProcessedFiles { get; } = new HashSet<string>();

        public HashSet<string> DestroyedFiles { get; } = new HashSet<string>();

        private int projectCount;
        public int ProjectCount
        {
            get { return Thread.VolatileRead(ref projectCount); }
        }

        private int fileCount;
        public int FileCount
        {
            get { return Thread.VolatileRead(ref fileCount); }
        }

        private int revisionCount;
        public int RevisionCount
        {
            get { return Thread.VolatileRead(ref revisionCount); }
        }

        public RevisionAnalyzer(WorkQueue workQueue, Logger logger, VssDatabase database, IMessageDispatcher messageDispatcher)
            : base(workQueue, logger, messageDispatcher)
        {
            this.Database = database;
        }

        public bool IsDestroyed(string physicalName)
        {
            return DestroyedFiles.Contains(physicalName);
        }

        public void AddItem(VssProject project)
        {
            if (project == null)
            {
                throw new ArgumentNullException("project");
            }
            else if (project.Database != Database)
            {
                throw new ArgumentException("Project database mismatch", "project");
            }

            rootProjects.AddLast(project);

            PathMatcher exclusionMatcher = null;
            if (!string.IsNullOrEmpty(ExcludeFiles))
            {
                var excludeFileArray = ExcludeFiles.Split(
                    new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                exclusionMatcher = new PathMatcher(excludeFileArray);
            }

            workQueue.AddLast(delegate(object work)
            {
                logger.WriteSectionSeparator();
                LogStatus(work, "Building revision list");

                logger.WriteLine("Root project: {0}", project.Path);
                logger.WriteLine("Excluded files: {0}", ExcludeFiles);

                var excludedProjects = 0;
                var excludedFiles = 0;
                var stopwatch = Stopwatch.StartNew();
                VssUtil.RecurseItems(project,
                    delegate(VssProject subproject)
                    {
                        if (workQueue.IsAborting)
                        {
                            return RecursionStatus.Abort;
                        }

                        var path = subproject.Path;
                        if (exclusionMatcher != null && exclusionMatcher.Matches(path))
                        {
                            logger.WriteLine("Excluding project {0}", path);
                            ++excludedProjects;
                            return RecursionStatus.Skip;
                        }

                        ProcessItem(subproject, path, exclusionMatcher);
                        ++projectCount;
                        return RecursionStatus.Continue;
                    },
                    delegate(VssProject subproject, VssFile file)
                    {
                        if (workQueue.IsAborting)
                        {
                            return RecursionStatus.Abort;
                        }

                        var path = file.GetPath(subproject);
                        if (exclusionMatcher != null && exclusionMatcher.Matches(path))
                        {
                            logger.WriteLine("Excluding file {0}", path);
                            ++excludedFiles;
                            return RecursionStatus.Skip;
                        }

                        // only process shared files once (projects are never shared)
                        if (!ProcessedFiles.Contains(file.PhysicalName))
                        {
                            ProcessedFiles.Add(file.PhysicalName);
                            ProcessItem(file, path, exclusionMatcher);
                            ++fileCount;
                        }
                        return RecursionStatus.Continue;
                    });
                stopwatch.Stop();

                logger.WriteSectionSeparator();
                logger.WriteLine("Analysis complete in {0:HH:mm:ss}", new DateTime(stopwatch.ElapsedTicks));
                logger.WriteLine("Projects: {0} ({1} excluded)", projectCount, excludedProjects);
                logger.WriteLine("Files: {0} ({1} excluded)", fileCount, excludedFiles);
                logger.WriteLine("Revisions: {0}", revisionCount);
            });
        }

        private void ProcessItem(VssItem item, string path, PathMatcher exclusionMatcher)
        {
            try
            {
                foreach (var vssRevision in item.Revisions)
                {
                    var actionType = vssRevision.Action.Type;
                    if (vssRevision.Action is VssNamedAction namedAction)
                    {
                        if (actionType == VssActionType.Destroy)
                        {
                            // track destroyed files so missing history can be anticipated
                            // (note that Destroy actions on shared files simply delete
                            // that copy, so destroyed files can't be completely ignored)
                            DestroyedFiles.Add(namedAction.Name.PhysicalName);
                        }

                        var targetPath = path + VssDatabase.ProjectSeparator + namedAction.Name.LogicalName;
                        if (exclusionMatcher != null && exclusionMatcher.Matches(targetPath))
                        {
                            // project action targets an excluded file
                            continue;
                        }
                    }

                    var revision = new Revision(vssRevision.DateTime,
                        vssRevision.User, item.ItemName, vssRevision.Version,
                        vssRevision.Comment, vssRevision.Action);

                    ICollection<Revision> revisionSet;
                    if (!SortedRevisions.TryGetValue(vssRevision.DateTime, out revisionSet))
                    {
                        revisionSet = new LinkedList<Revision>();
                        SortedRevisions[vssRevision.DateTime] = revisionSet;
                    }
                    revisionSet.Add(revision);
                    ++revisionCount;
                }
            }
            catch (RecordException e)
            {
                var message = string.Format("Failed to read revisions for {0} ({1}): {2}",
                    path, item.PhysicalName, ExceptionFormatter.Format(e));
                LogException(e, message);
                ReportError(message);
            }
        }
    }
}
