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
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CommandLine;
using Hpdi.VssLogicalLib;
using VssContracts;
using Timer = System.Threading.Timer;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Entrypoint to the application.
    /// </summary>
    /// <author>Trevor Robinson</author>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            else
            {
                Parser.Default
                    .ParseArguments<ProgramOptions>(args)
                    .WithParsed(RunFromCommandLine);
            }
        }

        private static void RunFromCommandLine(ProgramOptions options)
        {
            var logger = string.IsNullOrEmpty(options.LogFile) ? Logger.Null : new Logger(options.LogFile);
            var workQueue = new WorkQueue(1);
            var messageDispatcher = new ConsoleLoggerMessageDispatcher(logger, options.IgnoreErrors);
            messageDispatcher.Dispatch(MessageType.Info, workQueue.LastStatus ?? "Idle", MessageChoice.Ok);

            var encoding = Encoding.GetEncoding(options.CodePage);
            var df = new VssDatabaseFactory(options.VssDirectory)
            {
                Encoding = encoding
            };
            var db = df.Open();
            var item = db.GetItem(options.VssProject);

            if (!(item is VssProject project))
            {
                logger.WriteLine("Error: Not a vss project.");
                return;
            }

            var revisionAnalyzer = new RevisionAnalyzer(workQueue, logger, db, messageDispatcher);

            var changesetBuilder = new ChangesetBuilder(workQueue, logger, revisionAnalyzer, messageDispatcher)
            {
                AnyCommentThreshold = TimeSpan.FromSeconds(options.AnyCommentSeconds),
                SameCommentThreshold = TimeSpan.FromSeconds(options.SameCommentSeconds)
            };

            void OnTimer(object state)
            {
                messageDispatcher.Dispatch(MessageType.Info, workQueue.LastStatus ?? "Idle", MessageChoice.Ok);
                messageDispatcher.Dispatch(MessageType.Info, $"Files: {revisionAnalyzer.FileCount}, Revisions: {revisionAnalyzer.RevisionCount}", MessageChoice.Ok);
                messageDispatcher.Dispatch(MessageType.Info, $"Changesets: {changesetBuilder.Changesets.Count}", MessageChoice.Ok);

                var exceptions = workQueue.FetchExceptions();
                if (exceptions != null)
                {
                    foreach (var exception in exceptions)
                    {
                        var message = ExceptionFormatter.Format(exception);
                        logger.WriteLine("ERROR: {0}", message);
                        logger.WriteLine(exception);
                    }
                }
            }

            var timer = new Timer(OnTimer, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            
            if (!string.IsNullOrWhiteSpace(options.VssExcludePaths))
                revisionAnalyzer.ExcludeFiles = options.VssExcludePaths;
            revisionAnalyzer.AddItem(project);
            
            changesetBuilder.BuildChangesets();

            if (!string.IsNullOrEmpty(options.GitDirectory))
            {
                var gitExporter = new GitExporter(workQueue, logger,
                    revisionAnalyzer, changesetBuilder, messageDispatcher);
                if (!string.IsNullOrEmpty(options.DefaultEmailDomain))
                {
                    gitExporter.EmailDomain = options.DefaultEmailDomain;
                }
                if (!string.IsNullOrEmpty(options.EmailMapFile))
                {
                    if (File.Exists(options.EmailMapFile))
                        gitExporter.EmailMapFile = options.EmailMapFile;
                    else
                        logger.WriteLine($"Warn: {options.EmailMapFile} does not exist.");
                }
                if (!string.IsNullOrEmpty(options.DefaultComment))
                {
                    gitExporter.DefaultComment = options.DefaultComment;
                }
                if (!options.TranscodeComments)
                {
                    gitExporter.CommitEncoding = encoding;
                }
                gitExporter.IgnoreErrors = options.IgnoreErrors;
                gitExporter.ExportToGit(options.GitDirectory);
            }

            workQueue.WaitIdle();
            timer.Dispose();

            messageDispatcher.Dispatch(MessageType.Info, "Done", MessageChoice.Ok);

            Application.Exit();
        }
    }
}
