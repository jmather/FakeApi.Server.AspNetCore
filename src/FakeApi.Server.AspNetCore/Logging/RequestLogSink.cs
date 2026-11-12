using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FakeApi.Server.AspNetCore.Logging
{
    public sealed class RequestLogSink : IRequestLogSink
    {
        public const string Category = "FakeApi.Server.AspNetCore.Logging.RequestLog";

        private readonly ILogger _logger;
        private readonly ILogRedactor _redactor;
        private readonly IOptions<RequestLogOptions> _options;
        private readonly object _xmlGate = new object();

        public RequestLogSink(ILoggerFactory loggerFactory, ILogRedactor redactor, IOptions<RequestLogOptions> options)
        {
            _logger = loggerFactory.CreateLogger(Category);
            _redactor = redactor;
            _options = options;
        }

        public void Write(RequestLogRecord record)
        {
            var json = RequestLogSerializer.ToJson(record);
            var redactedJson = _redactor.Redact(json, "json");
            _logger.Log(
                LogLevelFor(record.Status),
                new EventId(1, "RequestCompleted"),
                redactedJson,
                null,
                static (state, _) => state);

            var path = _options.Value.LogXmlPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var xml = RequestLogSerializer.ToXml(record);
            var redactedXml = _redactor.Redact(xml, "xml");
            lock (_xmlGate)
            {
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) == false)
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(path, redactedXml + "\n");
            }
        }

        private static LogLevel LogLevelFor(int status)
        {
            if (status >= 500)
            {
                return LogLevel.Error;
            }

            if (status >= 400)
            {
                return LogLevel.Warning;
            }

            return LogLevel.Information;
        }
    }
}
