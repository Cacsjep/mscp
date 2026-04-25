using System;
using PKI.Crypto;
namespace PKI.Admin
{
    public class RootCertItemManager : PkiCertItemManager
    {
        public RootCertItemManager(Guid kind) : base(kind) { }
        public override RolePreset RolePreset => RolePreset.RootCA;
    }
}
