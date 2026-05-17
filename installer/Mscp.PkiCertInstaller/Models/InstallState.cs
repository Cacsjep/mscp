namespace Mscp.PkiCertInstaller.Models;

public enum InstallState
{
    Unknown,
    NotInstalled,
    Installed,
    InstalledNoKey,
    Expired,
}
