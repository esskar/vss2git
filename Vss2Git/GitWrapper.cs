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
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Wraps execution of Git and implements the common Git commands.
    /// </summary>
    /// <author>Trevor Robinson</author>
    class GitWrapper
    {
        private readonly string repoPath;
        private readonly Logger logger;
        private readonly Stopwatch stopwatch = new Stopwatch();

        public TimeSpan ElapsedTime
        {
            get { return stopwatch.Elapsed; }
        }

        public string GitExecutable { get; set; } = "git.exe";

        public string GitInitialArguments { get; set; } = null;

        public bool ShellQuoting { get; set; } = false;

        public Encoding CommitEncoding { get; set; } = Encoding.UTF8;

        public GitWrapper(string repoPath, Logger logger)
        {
            this.repoPath = repoPath;
            this.logger = logger;
        }

        public bool FindExecutable()
        {
            if (FindInPathVar("git.exe", out var foundPath))
            {
                GitExecutable = foundPath;
                GitInitialArguments = null;
                ShellQuoting = false;
                return true;
            }
            if (FindInPathVar("git.cmd", out foundPath))
            {
                GitExecutable = "cmd.exe";
                GitInitialArguments = "/c git";
                ShellQuoting = true;
                return true;
            }
            return false;
        }

        public void Init()
        {
            GitExec("init");
        }

        public void SetConfig(string name, string value)
        {
            GitExec("config " + name + " " + Quote(value));
        }

        public bool Add(string path)
        {
            var startInfo = GetStartInfo("add -- " + Quote(path));

            // add fails if there are no files (directories don't count)
            return ExecuteUnless(startInfo, "did not match any files");
        }

        public bool AddAll()
        {
            var startInfo = GetStartInfo("add -A");

            // add fails if there are no files (directories don't count)
            return ExecuteUnless(startInfo, "did not match any files");
        }

        public void Remove(string path, bool recursive)
        {
            GitExec("rm " + (recursive ? "-r " : "") + "-- " + Quote(path));
        }

        public void Move(string sourcePath, string destPath, bool force)
        {
            if (force)
            {
                var tempPath = destPath + ".mvtmp";
                GitExec("mv -- " + Quote(sourcePath) + " " + Quote(tempPath));
                GitExec("mv -- " + Quote(tempPath) + " " + Quote(destPath));
            }
            else
            {
                GitExec("mv -- " + Quote(sourcePath) + " " + Quote(destPath));
            }
        }

        class TempFile : IDisposable
        {
            private readonly FileStream fileStream;

            public string Name { get; }

            public TempFile()
            {
                Name = Path.GetTempFileName();
                fileStream = new FileStream(Name, FileMode.Truncate, FileAccess.Write, FileShare.Read);
            }

            public void Write(string text, Encoding encoding)
            {
                var bytes = encoding.GetBytes(text);
                fileStream.Write(bytes, 0, bytes.Length);
                fileStream.Flush();
            }

            public void Dispose()
            {
                fileStream?.Dispose();
                if (Name != null)
                {
                    File.Delete(Name);
                }
            }
        }

        private void AddComment(string comment, ref string args, out TempFile tempFile)
        {
            tempFile = null;
            if (string.IsNullOrEmpty(comment))
            {
                args += " --allow-empty-message --no-edit -m \"\"";
            }
            else
            {
                // need to use a temporary file to specify the comment when not
                // using the system default code page or it contains newlines
                if (CommitEncoding.CodePage != Encoding.Default.CodePage || comment.IndexOf('\n') >= 0)
                {
                    logger.WriteLine("Generating temp file for comment: {0}", comment);
                    tempFile = new TempFile();
                    tempFile.Write(comment, CommitEncoding);

                    // temporary path might contain spaces (e.g. "Documents and Settings")
                    args += " -F " + Quote(tempFile.Name);
                }
                else
                {
                    args += " -m " + Quote(comment);
                }
            }
        }

        public bool Commit(string authorName, string authorEmail, string comment, DateTime localTime)
        {
            var args = "commit";
            AddComment(comment, ref args, out var commentFile);

            using (commentFile)
            {
                var startInfo = GetStartInfo(args);
                startInfo.EnvironmentVariables["GIT_AUTHOR_NAME"] = authorName;
                startInfo.EnvironmentVariables["GIT_AUTHOR_EMAIL"] = authorEmail;
                startInfo.EnvironmentVariables["GIT_AUTHOR_DATE"] = GetUtcTimeString(localTime);

                // also setting the committer is supposedly useful for converting to Mercurial
                startInfo.EnvironmentVariables["GIT_COMMITTER_NAME"] = authorName;
                startInfo.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = authorEmail;
                startInfo.EnvironmentVariables["GIT_COMMITTER_DATE"] = GetUtcTimeString(localTime);

                // ignore empty commits, since they are non-trivial to detect
                // (e.g. when renaming a directory)
                return ExecuteUnless(startInfo, "nothing to commit");
            }
        }

        public void Tag(string name, string taggerName, string taggerEmail, string comment, DateTime localTime)
        {
            var args = "tag";
            AddComment(comment, ref args, out var commentFile);

            // tag names are not quoted because they cannot contain whitespace or quotes
            args += " -- " + name;

            using (commentFile)
            {
                var startInfo = GetStartInfo(args);
                startInfo.EnvironmentVariables["GIT_COMMITTER_NAME"] = taggerName;
                startInfo.EnvironmentVariables["GIT_COMMITTER_EMAIL"] = taggerEmail;
                startInfo.EnvironmentVariables["GIT_COMMITTER_DATE"] = GetUtcTimeString(localTime);

                ExecuteUnless(startInfo, null);
            }
        }

        private static string GetUtcTimeString(DateTime localTime)
        {
            // convert local time to UTC based on whether DST was in effect at the time
            var utcTime = TimeZoneInfo.ConvertTimeToUtc(localTime);

            // format time according to ISO 8601 (avoiding locale-dependent month/day names)
            return utcTime.ToString("yyyy'-'MM'-'dd HH':'mm':'ss +0000");
        }

        private void GitExec(string args)
        {
            var startInfo = GetStartInfo(args);
            ExecuteUnless(startInfo, null);
        }

        private ProcessStartInfo GetStartInfo(string args)
        {
            if (!string.IsNullOrEmpty(GitInitialArguments))
            {
                args = GitInitialArguments + " " + args;
            }

            var startInfo = new ProcessStartInfo(GitExecutable, args);
            startInfo.UseShellExecute = false;
            startInfo.RedirectStandardInput = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.WorkingDirectory = repoPath;
            startInfo.CreateNoWindow = true;
            return startInfo;
        }

        private bool ExecuteUnless(ProcessStartInfo startInfo, string unless)
        {
            var exitCode = Execute(startInfo, out var stdout, out var stderr);
            if (exitCode != 0)
            {
                if (string.IsNullOrEmpty(unless) ||
                    ((string.IsNullOrEmpty(stdout) || !stdout.Contains(unless)) &&
                     (string.IsNullOrEmpty(stderr) || !stderr.Contains(unless))))
                {
                    FailExitCode(startInfo.FileName, startInfo.Arguments, stdout, stderr, exitCode);
                }
            }
            return exitCode == 0;
        }

        private static void FailExitCode(string exec, string args, string stdout, string stderr, int exitCode)
        {
            throw new ProcessExitException(
                string.Format("git returned exit code {0}", exitCode),
                exec, args, stdout, stderr);
        }

        private int Execute(ProcessStartInfo startInfo, out string stdout, out string stderr)
        {
            logger.WriteLine("Executing: {0} {1}", startInfo.FileName, startInfo.Arguments);
            stopwatch.Start();
            try
            {
                using (var process = Process.Start(startInfo))
                {
                    process.StandardInput.Close();
                    var stdoutReader = new AsyncLineReader(process.StandardOutput.BaseStream);
                    var stderrReader = new AsyncLineReader(process.StandardError.BaseStream);

                    var activityEvent = new ManualResetEvent(false);
                    EventHandler activityHandler = delegate { activityEvent.Set(); };
                    process.Exited += activityHandler;
                    stdoutReader.DataReceived += activityHandler;
                    stderrReader.DataReceived += activityHandler;

                    var stdoutBuffer = new StringBuilder();
                    var stderrBuffer = new StringBuilder();
                    while (true)
                    {
                        activityEvent.Reset();

                        while (true)
                        {
                            var line = stdoutReader.ReadLine();
                            if (line != null)
                            {
                                line = line.TrimEnd();
                                if (stdoutBuffer.Length > 0)
                                {
                                    stdoutBuffer.AppendLine();
                                }
                                stdoutBuffer.Append(line);
                                logger.Write('>');
                            }
                            else
                            {
                                line = stderrReader.ReadLine();
                                if (line != null)
                                {
                                    line = line.TrimEnd();
                                    if (stderrBuffer.Length > 0)
                                    {
                                        stderrBuffer.AppendLine();
                                    }
                                    stderrBuffer.Append(line);
                                    logger.Write('!');
                                }
                                else
                                {
                                    break;
                                }
                            }
                            logger.WriteLine(line);
                        }

                        if (process.HasExited)
                        {
                            break;
                        }

                        activityEvent.WaitOne(1000);
                    }

                    stdout = stdoutBuffer.ToString();
                    stderr = stderrBuffer.ToString();
                    return process.ExitCode;
                }
            }
            catch (FileNotFoundException e)
            {
                throw new ProcessException("Executable not found.",
                    e, startInfo.FileName, startInfo.Arguments);
            }
            catch (Win32Exception e)
            {
                throw new ProcessException("Error executing external process.",
                    e, startInfo.FileName, startInfo.Arguments);
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private bool FindInPathVar(string filename, out string foundPath)
        {
            var path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                return FindInPaths(filename, path.Split(Path.PathSeparator), out foundPath);
            }
            foundPath = null;
            return false;
        }

        private bool FindInPaths(string filename, IEnumerable<string> searchPaths, out string foundPath)
        {
            foreach (var searchPath in searchPaths)
            {
                var path = Path.Combine(searchPath, filename);
                if (File.Exists(path))
                {
                    foundPath = path;
                    return true;
                }
            }
            foundPath = null;
            return false;
        }

        private const char QuoteChar = '"';
        private const char EscapeChar = '\\';

        /// <summary>
        /// Puts quotes around a command-line argument if it includes whitespace
        /// or quotes.
        /// </summary>
        /// <remarks>
        /// There are two things going on in this method: quoting and escaping.
        /// Quoting puts the entire string in quotes, whereas escaping is per-
        /// character. Quoting happens only if necessary, when whitespace or a
        /// quote is encountered somewhere in the string, and escaping happens
        /// only within quoting. Spaces don't need escaping, since that's what
        /// the quotes are for. Slashes don't need escaping because apparently a
        /// backslash is only interpreted as an escape when it precedes a quote.
        /// Otherwise both slash and backslash are just interpreted as directory
        /// separators.
        /// </remarks>
        /// <param name="arg">A command-line argument to quote.</param>
        /// <returns>The given argument, possibly in quotes, with internal
        /// quotes escaped with backslashes.</returns>
        private string Quote(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            StringBuilder buf = null;
            for (var i = 0; i < arg.Length; ++i)
            {
                var c = arg[i];
                if (buf == null && NeedsQuoting(c))
                {
                    buf = new StringBuilder(arg.Length + 2);
                    buf.Append(QuoteChar);
                    buf.Append(arg, 0, i);
                }
                if (buf != null)
                {
                    if (c == QuoteChar)
                    {
                        buf.Append(EscapeChar);
                    }
                    buf.Append(c);
                }
            }
            if (buf != null)
            {
                buf.Append(QuoteChar);
                return buf.ToString();
            }
            return arg;
        }

        private bool NeedsQuoting(char c)
        {
            return char.IsWhiteSpace(c) || c == QuoteChar ||
                (ShellQuoting && (c == '&' || c == '|' || c == '<' || c == '>' || c == '^' || c == '%'));
        }
    }
}
