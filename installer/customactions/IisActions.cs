using System;
using System.IO;
using WixToolset.Dtf.WindowsInstaller;

namespace InstallerCustomActions
{
    // Custom actions that publish/unpublish the running MSI to a folder served
    // by IIS. Used by the optional "IISHosting" feature in the WiX installer.
    //
    // We can't ship the MSI as a <File> payload of itself (an installer can't
    // embed itself), so the install-time CA reads OriginalDatabase and copies
    // that file into the public folder. The uninstall-time CA deletes the
    // versioned MSI by name. Both CAs are deferred and run as LocalSystem.
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
    }
}
