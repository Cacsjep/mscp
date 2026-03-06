using VideoOS.Platform;

namespace Auditor
{
    public class PluginLog
    {
        internal string _where;
        public PluginLog(string where)
        {
            _where = where;
        }
        public void Info(string message)
        {
            EnvironmentManager.Instance.Log(false, _where, message);
        }
        public void Error(string message)
        {
            EnvironmentManager.Instance.Log(true, _where, message);
        }
    }
}
