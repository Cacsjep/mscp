using System;
using PKI.Crypto;
namespace PKI.Admin
{
    public class HttpsCertItemManager : PkiCertItemManager
    {
        public HttpsCertItemManager(Guid kind) : base(kind) { }
        public override RolePreset RolePreset => RolePreset.HttpsServer;
        protected override string HelpFileName => "HelpPage_Https.html";
    }
}
