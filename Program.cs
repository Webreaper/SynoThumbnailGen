using System;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace SynoThumbnailGen
{
    class MainClass
    {
        private class SynoThumb
        {
            public int width;
            public int height;
            public string fileFormatString;
        }

        /// <summary>
        /// This is the set of thumb resolutions that Syno PhotoStation and moments expects
        /// </summary>
        private static SynoThumb[] thumbConfigs = {
            new SynoThumb{ width = 1280, height = 1280, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_XL.jpg" },
            new SynoThumb{ width = 800, height = 800, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_L.jpg" },
            new SynoThumb{ width = 640, height = 640, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_B.jpg" },
            new SynoThumb{ width = 320, height = 320, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_M.jpg" },
            new SynoThumb{ width = 120, height = 120, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_S.jpg" }
        };

        private static string[] extensions = { ".jpg", ".jpeg" };
        private static bool verbose = false;

        private static void Usage()
        {
            Console.WriteLine("Usage: ThumbnailGen src");
            Console.WriteLine("Generates Synology thumbnails in the within the specified folder.");
        }

        public static void Log(string format, params object[] args)
        {
            string msg = string.Format(format, args);
            string log = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}", DateTime.UtcNow, msg);
            Console.WriteLine(log);
        }

        private static void ProcessFolderRecursive(DirectoryInfo root, bool recurse)
        {
            ProcessFolder(root);

            if (recurse)
            {
                var folders = root.GetDirectories()
                                 .Where(IncludeFolder)
                                 .OrderByDescending(x => x.CreationTimeUtc)
                                 .ToList();

                int total = folders.Count();
                int current = 0;

                foreach (var subdir in folders)
                {
                    current++;
                    Log("Processing folder ({0} of {1}) {2}...", current, total, subdir.FullName);

                    ProcessFolderRecursive(subdir, recurse);
                }
            }
        }

        private static bool IncludeFolder(DirectoryInfo dir)
        {
            if (dir.Attributes.HasFlag(FileAttributes.Hidden))
                return false;

            if (dir.Name.StartsWith(".", StringComparison.OrdinalIgnoreCase))
                return false;

            if (dir.Name.Equals("@eaDir", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static void ProcessFolder(DirectoryInfo from)
        {
            var files = from.EnumerateFiles()
                    .Where(f => extensions.Contains(
                                f.Extension, StringComparer.OrdinalIgnoreCase))
                            .ToList();

            int total = files.Count();
            int current = 0;

            foreach (var f in files)
            {
                current++;

                if (verbose)
                    Log("Processing ({0} of {1}) - {2} into {3} for {4} sizes.", current, total, f.FullName, f.FullName, thumbConfigs.Length);
                else
                    Log("Processing ({0} of {1}) {2}.", current, total, f.FullName);

                ConvertFile(f, thumbConfigs);
            }
        }

        /// <summary>
        /// Do the actual imagemagick conversion. The key point here is that we pass a set of 
        /// sizes to be converted, and this means that IM reads the source file once, and writes
        /// it out multiple times after conversion. This is fast - no need to read the files many
        /// times. By adding the -auto-orient arg, it works around the Syno PhotoStation bug where
        /// images' EXIF rotation isn't honoured - meaning that photos aren't displayed with their
        /// correct orientation in PhotoStation.
        /// </summary>
        /// <param name="source">Source.</param>
        /// <param name="sizes">Sizes.</param>
        private static void ConvertFile(FileInfo source, SynoThumb[] sizes)
        {
            // Some useful unsharp and quality settings, plus by defining the max size of the JPEG, it 
            // makes imagemagic more efficient with its memory allocation, so significantly faster. 
            string args = string.Format(" -define jpeg:size=3000x2250 \"{0}\" -quality 90 -unsharp 0.5x0.5+1.25+0.0 ", source.FullName);
            bool workToDo = false;

            // First pre-check whether the thumbs exist - don't want to create them if they don't.
            foreach (var size in sizes)
            {
                string destFile = string.Format(string.Format(size.fileFormatString, source.DirectoryName, source.Name));

                string destDir = Path.GetDirectoryName(destFile);
                if (!Directory.Exists(destDir))
                {
                    if( verbose)
                        Log("Creating directory: {0}", destDir);
                    Directory.CreateDirectory(destDir);
                }

                if (File.Exists(destFile))
                {
                    if (verbose)
                        Log("File {0} already exists.", destFile);
                    continue; // Don't re-gen it, we're done
                }

                // File didn't exist, so add it to the command-line. 
                args += string.Format(" -resize {0}x{1}> -auto-orient -write \"{2}\" ", size.height, size.width, destFile);
                workToDo = true;
            }

            if( workToDo )
            {
                Process process = new Process();
                process.StartInfo.FileName = "convert";
                process.StartInfo.Arguments = args;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.OutputDataReceived += Process_OutputDataReceived;
                process.ErrorDataReceived += Process_OutputDataReceived;

                try
                {
                    if (verbose)
                        Log("  Executing: {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);
                    else 
                        Log("  Executing ImageMagic: {0}", process.StartInfo.FileName);

                    bool success = process.Start();

                    if (success)
                    {
                        process.BeginErrorReadLine();
                        process.BeginOutputReadLine();
                        process.WaitForExit();

                        if( verbose)
                            Log("Execution complete.");
                    }
                }
                catch (Exception ex)
                {
                    Log("Unable to start process: {0}", ex.Message);
                }
            }
            else
                Log("Thumbs already exist in all resolutions. Skipping...");
        }

        static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!verbose)
                return;

            if (!string.IsNullOrEmpty(e.Data))
                Log(e.Data);
        }

        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Usage();
                return;
            }

            bool recurse = false;
            var rootFolder = args[0];

            if (args.Any(x => x.ToLower() == "-r"))
            {
                Log("Recursive mode enabled.");
                recurse = true;
            }

            if (args.Any(x => x.ToLower() == "-v"))
            {
                Log("Verbose mode enabled.");
                verbose = true;
            }

            var root = new DirectoryInfo(rootFolder);

            Log("Starting Synology thumbnail creation for folder {0}", root);

            ProcessFolderRecursive(root, recurse);

            Log("Completed Synology thumbnail creation.", root);
        }
    }
}
