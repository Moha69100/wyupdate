﻿using System;
using System.IO;
using Ionic.Zip;
using wyUpdate.Common;
using wyUpdate.Compression.Vcdiff;

namespace wyUpdate
{
    partial class InstallUpdate
    {
        void ExtractUpdateFile()
        {
            using (ZipFile zip = ZipFile.Read(Filename))
            {
                int totalFiles = zip.Entries.Count;
                int filesDone = 0;

                foreach (ZipEntry e in zip)
                {
                    if (canceled)
                        break; //stop outputting new files

                    if (!SkipProgressReporting)
                    {
                        int unweightedPercent = totalFiles > 0 ? (filesDone*100)/totalFiles : 0;

                        ThreadHelper.ReportProgress(Sender, SenderDelegate,
                            "Extracting " + Path.GetFileName(e.FileName),
                            GetRelativeProgess(1, unweightedPercent), unweightedPercent);

                        filesDone++;
                    }

                    e.Extract(OutputDirectory, ExtractExistingFileAction.OverwriteSilently);  // overwrite == true
                }
            }
        }

        // unzip the update to the temp folder
        public void RunUnzipProcess()
        {
            Exception except = null;

            string updtDetailsFilename = Path.Combine(TempDirectory, "updtdetails.udt");

            try
            {
                ExtractUpdateFile();

                try
                {
                    // remove update file (it's no longer needed)
                    File.Delete(Filename);
                }
                catch { }


                // Try to load the update details file
                if (File.Exists(updtDetailsFilename))
                {
                    UpdtDetails = UpdateDetails.Load(updtDetailsFilename);
                }
                else
                    throw new Exception("The update details file \"updtdetails.udt\" is missing.");


                if (Directory.Exists(Path.Combine(TempDirectory, "patches")))
                {
                    // patch the files
                    foreach (UpdateFile file in UpdtDetails.UpdateFiles)
                    {
                        if (file.DeltaPatchRelativePath != null)
                        {
                            string tempFilename = Path.Combine(TempDirectory, file.RelativePath);

                            // create the directory to store the patched file
                            if (!Directory.Exists(Path.GetDirectoryName(tempFilename)))
                                Directory.CreateDirectory(Path.GetDirectoryName(tempFilename));

                            try
                            {
                                using (FileStream original = File.OpenRead(FixUpdateDetailsPaths(file.RelativePath)))
                                using (FileStream patch = File.OpenRead(Path.Combine(TempDirectory, file.DeltaPatchRelativePath)))
                                using (FileStream target = File.Open(tempFilename, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                                {
                                    VcdiffDecoder.Decode(original, patch, target, file.NewFileAdler32);
                                }
                            }
                            catch (Exception ex)
                            {
                                throw new PatchApplicationException("Patch failed to apply to " + FixUpdateDetailsPaths(file.RelativePath) + "\r\n\r\n" + ex.Message);
                            }


                            // the 'last write time' of the patch file is really the 'lwt' of the dest. file
                            File.SetLastWriteTime(tempFilename, File.GetLastWriteTime(Path.Combine(TempDirectory, file.DeltaPatchRelativePath)));
                        }
                    }


                    try
                    {
                        // remove the patches directory (frees up a bit of space)
                        Directory.Delete(Path.Combine(TempDirectory, "patches"), true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                except = ex;
            }


            if (canceled || except != null)
            {
                //report cancellation
                ThreadHelper.ReportProgress(Sender, SenderDelegate, "Cancelling update...", -1, -1);

                //Delete temporary files

                if (except != null && except.GetType() != typeof(PatchApplicationException))
                {
                    // remove the entire temp directory
                    try
                    {
                        Directory.Delete(OutputDirectory, true);
                    }
                    catch { }
                }
                else
                {
                    //only 'gut' the folder leaving the server file

                    string[] dirs = Directory.GetDirectories(TempDirectory);

                    foreach (string dir in dirs)
                    {
                        // delete everything but the self-update folder (AutoUpdate specific)
                        if (Path.GetFileName(dir) == "selfupdate")
                            continue;

                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch { }
                    }

                    // remove the update details
                    if (File.Exists(updtDetailsFilename))
                    {
                        File.Delete(updtDetailsFilename);
                    }
                }

                ThreadHelper.ReportError(Sender, SenderDelegate, string.Empty, except);
            }
            else
            {
                ThreadHelper.ReportSuccess(Sender, SenderDelegate, "Extraction complete");
            }
        }
    }
}