using System;
using PKI.Crypto;
namespace PKI.Admin
{
    public class Dot1xCertItemManager : PkiCertItemManager
    {
        public Dot1xCertItemManager(Guid kind) : base(kind) { }
        public override RolePreset RolePreset => RolePreset.Dot1xClient;
    }
}
