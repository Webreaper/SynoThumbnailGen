using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace SynoThumbnailGen
{
    class MainClass
    {
        private class SynoThumb
        {
            public bool useAsSource;
            public int width;
            public int height;
            public string fileFormatString;
        }

        /// <summary>
        /// This is the set of thumb resolutions that Syno PhotoStation and moments expects
        /// </summary>
        private static SynoThumb[] thumbConfigs = {
            new SynoThumb{ width = 1280, height = 1280, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_XL.jpg", useAsSource = true },
            new SynoThumb{ width = 800, height = 800, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_L.jpg", useAsSource = true },
            new SynoThumb{ width = 640, height = 640, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_B.jpg" },
            new SynoThumb{ width = 320, height = 320, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_M.jpg" },
            new SynoThumb{ width = 160, height = 120, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_PREVIEW.jpg" },
            new SynoThumb{ width = 120, height = 120, fileFormatString = "{0}/@eaDir/{1}/SYNOPHOTO_THUMB_S.jpg" }
        };

        private static string[] extensions = { ".jpg", ".jpeg" };
        private static bool s_verbose = false;
        private static bool s_alphaSort = false;
        private static bool s_useGraphicsMagick = false;

        private static void Usage()
        {
            Console.WriteLine("Usage: ThumbnailGen <folder> [options]");
            Console.WriteLine("Generates Synology thumbnails in the within the specified folder. Options:");
            Console.WriteLine(" -v      Enable verbose logging.");
            Console.WriteLine(" -r      Recurse into subdirectories.");
            Console.WriteLine(" -alpha  Process folders in alphabetic order (default is last-mod, most recent first).");
            Console.WriteLine(" -gm     Use GraphicsMagick instead of ImageMagick.");
        }

        public static void LogVerbose(string format, params object[] args)
        {
            if (s_verbose)
                Log(format, args);
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
                var filteredDirs = root.GetDirectories().Where(IncludeFolder);

                var ordered = s_alphaSort ? filteredDirs.OrderBy(x => x.Name) :
                                              filteredDirs.OrderByDescending(x => x.CreationTimeUtc);

                var folders = ordered.ToList();

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
            int converted = 0;

            foreach (var f in files)
            {
                current++;

                LogVerbose("Analysing ({0} of {1}) - {2} into {3} for {4} sizes.", current, total, f.FullName, f.FullName, thumbConfigs.Length);

                if (ConvertFile(f, thumbConfigs))
                {
                    Log("Converted ({0} of {1}) {2}.", current, total, f.FullName);
                    converted++;
                }

            }

            Log("Folder complete. {0} of {1} files had thumbnails generated.", converted, total);
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
        /// <returns>True if the file was converted</returns>
        private static bool ConvertFile(FileInfo source, SynoThumb[] sizes)
        {
            bool conversionPerformed = false;

            // Some useful unsharp and quality settings, plus by defining the max size of the JPEG, it 
            // makes imagemagic more efficient with its memory allocation, so significantly faster. 
            string args;

            if( s_useGraphicsMagick )
                args = string.Format(" convert -size 1280x1280 \"{0}\" -quality 90 -unsharp 0.5x0.5+1.25+0.0 ", source.FullName);
            else
                args = string.Format(" -define jpeg:size=1280x1280 \"{0}\" -quality 90 -unsharp 0.5x0.5+1.25+0.0 ", source.FullName);

            List<string> destinationFiles = new List<string>();
            FileInfo altSource = null;

            List<string> argsList = new List<string>();

            // First pre-check whether the thumbs exist - don't want to create them if they don't.
            foreach( var size in sizes )
            {
                string destFile = string.Format(string.Format(size.fileFormatString, source.DirectoryName, source.Name));

                string destDir = Path.GetDirectoryName(destFile);
                if (!Directory.Exists(destDir))
                {
                    LogVerbose("Creating directory: {0}", destDir);
                    Directory.CreateDirectory(destDir);
                }

                if( File.Exists(destFile) && File.GetLastWriteTime( destFile) == source.LastWriteTime )
                {
                    // If the creation time of both files is the same, we're done.
                    LogVerbose("File {0} already exists with matching creation time.", destFile);

                    if (altSource == null && size.useAsSource)
                        altSource = new FileInfo(destFile);

                    continue; // Don't re-gen it, we're done
                }

                // File didn't exist, so add it to the command-line. 
                if( s_useGraphicsMagick )
                    argsList.Add( string.Format(" -thumbnail {0}x{1}> -auto-orient -write \"{2}\" ", size.height, size.width, destFile) );
                else
                    argsList.Add( string.Format(" -resize {0}x{1}> -auto-orient -write \"{2}\" ", size.height, size.width, destFile) );

                destinationFiles.Add( destFile );
            }

            if( argsList.Any() )
            {
                var lastArg = argsList.Last();
                lastArg = lastArg.Replace(" -write ", " ");
                argsList[argsList.Count() - 1] = lastArg;

                args += string.Join(" ", argsList);

                if (altSource != null)
                {
                    source = altSource;
                    LogVerbose("File {0} exists - using it as source for smaller thumbs.", altSource.Name);
                }

                LogVerbose("Converting file {0}", source);

                Process process = new Process();

                if( s_useGraphicsMagick)
                    process.StartInfo.FileName = "gm";
                else
                    process.StartInfo.FileName = "convert";

                process.StartInfo.Arguments = args;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.UseShellExecute = false;
                process.OutputDataReceived += Process_OutputDataReceived;
                process.ErrorDataReceived += Process_OutputDataReceived;

                try
                {
                    LogVerbose("  Executing: {0} {1}", process.StartInfo.FileName, process.StartInfo.Arguments);

                    bool success = process.Start();

                    if (success)
                    {
                        process.BeginErrorReadLine();
                        process.BeginOutputReadLine();
                        process.WaitForExit();

                        LogVerbose("Execution complete.");
                        conversionPerformed = true;

                        // Touch the files so they match the source
                        foreach( string f in destinationFiles )
                        {
                            if( File.Exists( f ))
                                File.SetLastWriteTime(f, source.LastWriteTime);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log("Unable to start process: {0}", ex.Message);
                }
            }
            else
                LogVerbose("Thumbs already exist in all resolutions. Skipping...");

            return conversionPerformed;
        }

        static void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!s_verbose)
                return;

            if (!string.IsNullOrEmpty(e.Data))
                LogVerbose(e.Data);
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

            if (args.Any(x => x.ToLower() == "-alpha"))
            {
                Log("Alphabetic sort enabled.");
                s_alphaSort = true;
            }
            else
                Log("Most-recent-first sort enabled.");
                
            if (args.Any(x => x.ToLower() == "-v"))
            {
                Log("Verbose mode enabled.");
                s_verbose = true;
            }

            if (args.Any(x => x.ToLower() == "-gm"))
            {
                Log("GraphicsMagick enabled.");
                s_useGraphicsMagick = true;
            }
            var root = new DirectoryInfo(rootFolder);

            Log("Starting Synology thumbnail creation for folder {0}", root);

            ProcessFolderRecursive(root, recurse);

            Log("Completed Synology thumbnail creation.", root);
        }
    }
}
