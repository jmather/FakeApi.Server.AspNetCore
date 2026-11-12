using System.Collections.Generic;

namespace FakeApi.Server.AspNetCore.Logging
{
    public sealed class RequestLogRecord
    {
        public string Timestamp { get; set; }

        public string Level { get; set; }

        public string Msg { get; set; }

        public string Server { get; set; }

        public string RequestId { get; set; }

        public string Method { get; set; }

        public string Path { get; set; }

        public Dictionary<string, object> Query { get; set; }

        public int Status { get; set; }

        public double DurationMs { get; set; }

        public string Action { get; set; }

        public string User { get; set; }

        public RequestLogPayload Request { get; set; }

        public RequestLogPayload Response { get; set; }
    }

    public sealed class RequestLogPayload
    {
        public Dictionary<string, string> Headers { get; set; }

        public string ContentType { get; set; }

        public string Body { get; set; }

        public bool IncludeBody { get; set; }
    }
}
