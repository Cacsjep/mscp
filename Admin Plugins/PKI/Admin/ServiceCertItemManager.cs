using System;
using PKI.Crypto;
namespace PKI.Admin
{
    public class ServiceCertItemManager : PkiCertItemManager
    {
        public ServiceCertItemManager(Guid kind) : base(kind) { }
        public override RolePreset RolePreset => RolePreset.Service;
    }
}
