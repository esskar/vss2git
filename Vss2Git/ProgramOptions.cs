using System.Text;
using CommandLine;

namespace Hpdi.Vss2Git
{
    public class ProgramOptions
    {
        [Option('v', "vssDir", Required = true, HelpText = "Sets the vss directory.")]
        public string VssDirectory { get; set; }

        [Option('p', "vssProj", Default = "$", Required = false, HelpText = "Sets the vss project.")]
        public string VssProject { get; set; }

        [Option('x', "vssExclude", Required = false, HelpText = "Sets the vss exlude paths.")]
        public string VssExcludePaths { get; set; }

        [Option('g', "gitDir", Required = true, HelpText = "Sets the git directory.")]
        public string GitDirectory { get; set; }

        [Option('d', "emailDomain", Required = false, HelpText = "Sets the default email domain.")]
        public string DefaultEmailDomain { get; set; }

        [Option('l', "logFile", Required = false, Default = "Vss2Git.log", HelpText = "Sets the log file.")]
        public string LogFile { get; set; }

        [Option('e', "codepage", Required = false, Default = 1252, HelpText = "Sets the encoding of the vss comments.")]
        public int CodePage { get; set; }

        [Option('u', "transCode", Required = false, Default = true, HelpText = "Sets a value indicating whether to transcode comments to UTF-8.")]
        public bool TranscodeComments { get; set; }

        [Option('a', "anySec", Required = false, Default = 30, HelpText = "Sets the seconds within revisions should be combined regardless of the comments. ")]
        public int AnyCommentSeconds { get; set; }

        [Option('s', "sameSec", Required = false, Default = 600, HelpText = "Sets the seconds within revisions should be combined if the comments are the same. ")]
        public int SameCommentSeconds { get; set; }

        [Option('t', "tags", Required = false, Default = true, HelpText = "Sets a value indicating whether to force annotated tags objects.")]
        public bool ForceAnnotatedTags { get; set; }

        [Option('i', "ignore", Required = false, Default = false, HelpText = "Sets a value indicating whether to ignore errors.")]
        public bool IgnoreErrors { get; set; }

        [Option('c', "comment", Required = false, HelpText = "Sets the default comment when no comments is given.")]
        public string DefaultComment { get; set; }

        [Option('m', "enailMap", Required = false, HelpText = "Sets the email map file to map vss users to git email addresses.")]
        public string EmailMapFile { get; set; }
    }
}
