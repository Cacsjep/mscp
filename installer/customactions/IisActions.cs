using System;
using System.IO;
using System.IO.Compression;
using WixToolset.Dtf.WindowsInstaller;

namespace InstallerCustomActions
{
    // Custom actions for the ZIP-only MSI payload.
    //
    // The MSI ships every plugin as a single ZIP file installed under
    // [CommonAppDataFolder]Milestone\MSCPPluginPayload\<Name>.zip. There is no
    // loose-file install tree any more — plugin DLLs only land on disk after
    // ExtractZipToFolder runs at install time, which is why each plugin's
    // Feature_<Name> sequences a paired (extract, cleanup) CA.
    //
    // The optional IISHosting feature pulls in the MscpAllPluginZips component
    // group, so when it's selected every plugin ZIP is installed regardless of
    // which plugin features the user picked. CopyPluginZipsToPublic then sweeps
    // those ZIPs into MSCP_PUBLIC\plugins\ as <Name>-v<Version>.zip so the
    // download page can serve them. The mirror remove-CAs reverse it on
    // uninstall.
    public static class IisActions
    {
        // CustomActionData keys: SourceMsi, DestinationFolder, FileName
        [CustomAction]
        public static ActionResult CopyMsiToPublicFolder(Session session)
        {
            try
            {
                var src = session.CustomActionData["SourceMsi"];
                var folder = session.CustomActionData["DestinationFolder"];
                var name = session.CustomActionData["FileName"];

                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                {
                    session.Log($"[IisActions] Source MSI not found: '{src}'. Skipping publish.");
                    return ActionResult.Success;
                }

                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                {
                    session.Log($"[IisActions] Missing DestinationFolder or FileName, skipping publish.");
                    return ActionResult.Success;
                }

                Directory.CreateDirectory(folder);
                var dest = Path.Combine(folder, name);
                File.Copy(src, dest, overwrite: true);
                session.Log($"[IisActions] Published MSI '{src}' -> '{dest}'");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                // Never fail the whole install over an IIS publishing failure;
                // the user's plugin install still succeeded, the local download
                // page is just a convenience.
                session.Log($"[IisActions] CopyMsiToPublicFolder failed (non-fatal): {ex}");
                return ActionResult.Success;
            }
        }

        // CustomActionData keys: DestinationFolder, FileName
        [CustomAction]
        public static ActionResult RemoveMsiFromPublicFolder(Session session)
        {
            try
            {
                var folder = session.CustomActionData["DestinationFolder"];
                var name = session.CustomActionData["FileName"];
                if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name))
                {
                    session.Log("[IisActions] Missing DestinationFolder or FileName, skipping unpublish.");
                    return ActionResult.Success;
                }

                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    session.Log($"[IisActions] Unpublished MSI '{path}'");
                }
                else
                {
                    session.Log($"[IisActions] MSI '{path}' was already absent.");
                }
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[IisActions] RemoveMsiFromPublicFolder failed (non-fatal): {ex}");
                return ActionResult.Success;
            }
        }

        // Per-plugin extract: unpack a single plugin ZIP into the destination
        // folder (typically MIPPLUGINS\<Name>\ or MIPDRIVERS\<Name>\). Wipes
        // the destination first so reinstalls / upgrades don't leave stale
        // files behind. Failure is fatal — a missing payload means the plugin
        // is broken on disk and should fail the install rather than silently
        // skip.
        //
        // CustomActionData keys: Source, Dest
        [CustomAction]
        public static ActionResult ExtractZipToFolder(Session session)
        {
            try
            {
                var source = session.CustomActionData["Source"];
                var dest = session.CustomActionData["Dest"];

                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                {
                    session.Log($"[IisActions] Plugin ZIP not found: '{source}'. Failing install.");
                    return ActionResult.Failure;
                }
                if (string.IsNullOrWhiteSpace(dest))
                {
                    session.Log("[IisActions] Empty Dest in ExtractZipToFolder.");
                    return ActionResult.Failure;
                }

                if (Directory.Exists(dest))
                {
                    DeleteDirectoryContents(dest, session);
                }
                Directory.CreateDirectory(dest);

                using (var archive = ZipFile.OpenRead(source))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            // Pure directory entry — ExtractToFile would throw.
                            continue;
                        }

                        // The per-plugin ZIPs Compress-Archive produces start
                        // with a top-level <Name>/ folder; strip it so files
                        // land directly under <dest>\.
                        var rel = entry.FullName;
                        var idx = rel.IndexOfAny(new[] { '/', '\\' });
                        if (idx >= 0) rel = rel.Substring(idx + 1);
                        if (string.IsNullOrWhiteSpace(rel)) continue;

                        var outPath = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
                        var outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                        entry.ExtractToFile(outPath, overwrite: true);
                    }
                }

                session.Log($"[IisActions] Extracted '{source}' -> '{dest}'");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[IisActions] ExtractZipToFolder failed: {ex}");
                return ActionResult.Failure;
            }
        }

        // Per-plugin cleanup: remove the extracted folder. Mirror of extract.
        // CustomActionData keys: Dest
        [CustomAction]
        public static ActionResult RemoveExtractedFolder(Session session)
        {
            try
            {
                var dest = session.CustomActionData["Dest"];
                if (string.IsNullOrWhiteSpace(dest))
                {
                    session.Log("[IisActions] Empty Dest in RemoveExtractedFolder.");
                    return ActionResult.Success;
                }

                if (Directory.Exists(dest))
                {
                    Directory.Delete(dest, recursive: true);
                    session.Log($"[IisActions] Removed extracted folder '{dest}'");
                }
                else
                {
                    session.Log($"[IisActions] Folder '{dest}' was already absent.");
                }
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[IisActions] RemoveExtractedFolder failed (non-fatal): {ex}");
                return ActionResult.Success;
            }
        }

        // Sweep CA for the IIS feature: copies every plugin ZIP currently
        // installed under [CommonAppDataFolder]Milestone\MSCPPluginPayload\
        // into MSCP_PUBLIC\plugins\ as <Name>-v<Version>.zip. Runs once per
        // install when the IISHosting feature is selected; the per-plugin
        // ZIP components are dual-referenced from each Feature_<Name> AND from
        // IISHosting, so when IIS is on the source folder always contains all
        // 18 ZIPs even on a "page only" install.
        //
        // CustomActionData keys: Source, Dest, Version
        [CustomAction]
        public static ActionResult CopyPluginZipsToPublic(Session session)
        {
            try
            {
                var source = session.CustomActionData["Source"];
                var dest = session.CustomActionData["Dest"];
                var version = session.CustomActionData["Version"];

                if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
                {
                    session.Log($"[IisActions] Plugin payload folder '{source}' not found. Nothing to publish.");
                    return ActionResult.Success;
                }
                if (string.IsNullOrWhiteSpace(dest) || string.IsNullOrWhiteSpace(version))
                {
                    session.Log("[IisActions] Missing Dest or Version in CopyPluginZipsToPublic.");
                    return ActionResult.Success;
                }

                Directory.CreateDirectory(dest);

                var count = 0;
                foreach (var zip in Directory.GetFiles(source, "*.zip"))
                {
                    var pluginName = Path.GetFileNameWithoutExtension(zip);
                    var outName = $"{pluginName}-v{version}.zip";
                    var outPath = Path.Combine(dest, outName);
                    File.Copy(zip, outPath, overwrite: true);
                    session.Log($"[IisActions] Published '{zip}' -> '{outPath}'");
                    count++;
                }

                session.Log($"[IisActions] Published {count} plugin ZIP(s) to '{dest}'");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                // Non-fatal: page won't list every plugin but the install
                // itself succeeded.
                session.Log($"[IisActions] CopyPluginZipsToPublic failed (non-fatal): {ex}");
                return ActionResult.Success;
            }
        }

        // Cleanup the public plugins folder on uninstall / IISHosting removal.
        // CustomActionData keys: DestinationFolder
        [CustomAction]
        public static ActionResult RemovePluginZips(Session session)
        {
            try
            {
                var destFolder = session.CustomActionData["DestinationFolder"];
                if (string.IsNullOrWhiteSpace(destFolder) || !Directory.Exists(destFolder))
                {
                    session.Log($"[IisActions] DestinationFolder '{destFolder}' missing, nothing to remove.");
                    return ActionResult.Success;
                }

                foreach (var zip in Directory.GetFiles(destFolder, "*.zip"))
                {
                    try
                    {
                        File.Delete(zip);
                        session.Log($"[IisActions] Removed plugin ZIP '{zip}'");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"[IisActions] Failed to delete '{zip}' (non-fatal): {ex.Message}");
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[IisActions] RemovePluginZips failed (non-fatal): {ex}");
                return ActionResult.Success;
            }
        }

        // Wipe the contents of a directory without removing the directory
        // itself. Used by ExtractZipToFolder to clear stale files before
        // re-extracting on reinstall / Major Upgrade.
        private static void DeleteDirectoryContents(string path, Session session)
        {
            foreach (var file in Directory.GetFiles(path))
            {
                try { File.Delete(file); }
                catch (Exception ex) { session.Log($"[IisActions] Could not delete '{file}': {ex.Message}"); }
            }
            foreach (var sub in Directory.GetDirectories(path))
            {
                try { Directory.Delete(sub, recursive: true); }
                catch (Exception ex) { session.Log($"[IisActions] Could not delete '{sub}': {ex.Message}"); }
            }
        }
    }
}
