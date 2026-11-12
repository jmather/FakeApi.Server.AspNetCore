using System;
using Microsoft.Extensions.Configuration;

namespace FakeApi.Server.AspNetCore.Logging
{
    public sealed class RequestLogOptions
    {
        public const string SectionName = "FakeApi";

        public const string CaptureEnvironmentVariable = "FAKEAPI_LOG_CAPTURE";

        public const string XmlPathEnvironmentVariable = "FAKEAPI_LOG_XML_PATH";

        public string LogCapture { get; set; } = "none";

        public string LogXmlPath { get; set; }

        public static LogCaptureMode ParseCapture(string value)
        {
            if (string.Equals(value, "headers", StringComparison.OrdinalIgnoreCase))
            {
                return LogCaptureMode.Headers;
            }

            if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
            {
                return LogCaptureMode.All;
            }

            return LogCaptureMode.None;
        }

        public static void ApplyOverrides(RequestLogOptions options, IConfiguration configuration)
        {
            var capture = Environment.GetEnvironmentVariable(CaptureEnvironmentVariable)
                ?? configuration[CaptureEnvironmentVariable];
            if (string.IsNullOrWhiteSpace(capture) == false)
            {
                options.LogCapture = capture;
            }

            var envXml = Environment.GetEnvironmentVariable(XmlPathEnvironmentVariable);
            if (envXml != null)
            {
                options.LogXmlPath = envXml;
                return;
            }

            var flatXml = configuration[XmlPathEnvironmentVariable];
            if (flatXml != null)
            {
                options.LogXmlPath = flatXml;
            }
        }
    }
}
