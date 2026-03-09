using System;
using System.Collections.Generic;

namespace HttpRequests.Messaging
{
    internal static class HttpMessageIds
    {
        public const string ExecutionResult = "HttpRequests.ExecutionResult";
    }

    [Serializable]
    public class HttpExecutionResult
    {
        public Guid RequestItemId;
        public string RequestName;
        public string Url;
        public string Method;
        public int StatusCode;
        public bool Success;
        public string Error;
        public long ElapsedMs;
        public DateTime Timestamp;
    }
}
