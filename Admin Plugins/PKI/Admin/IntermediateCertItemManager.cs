using System;
using PKI.Crypto;
namespace PKI.Admin
{
    public class IntermediateCertItemManager : PkiCertItemManager
    {
        public IntermediateCertItemManager(Guid kind) : base(kind) { }
        public override RolePreset RolePreset => RolePreset.IntermediateCA;
    }
}
