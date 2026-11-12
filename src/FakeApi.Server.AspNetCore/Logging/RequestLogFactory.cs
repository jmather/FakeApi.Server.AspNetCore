using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Logging
{
    public static class RequestLogFactory
    {
        public const string Message = "request completed";

        public const string ServerName = "dotnet";

        public static RequestLogRecord Create(
            HttpContext context,
            string requestId,
            TimeSpan elapsed,
            LogCaptureMode capture,
            string requestBody,
            string responseBody)
        {
            var status = context.Response.StatusCode;
            var record = new RequestLogRecord
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
                Level = LevelFor(status),
                Msg = Message,
                Server = ServerName,
                RequestId = requestId,
                Method = context.Request.Method ?? string.Empty,
                Path = context.Request.Path.HasValue ? context.Request.Path.Value : "/",
                Query = QueryOf(context.Request),
                Status = status,
                DurationMs = Math.Round(elapsed.TotalMilliseconds, 3, MidpointRounding.AwayFromZero),
                Action = HeaderOrNull(context.Request.Headers, "X-FakeApi-Action"),
                User = UsernameFromBasic(context.Request),
            };

            if (capture != LogCaptureMode.None)
            {
                record.Request = Payload(context.Request.Headers, context.Request.ContentType, requestBody, capture);
                record.Response = Payload(context.Response.Headers, ResponseContentType(context.Response), responseBody, capture);
            }

            return record;
        }

        public static string LevelFor(int status)
        {
            if (status >= 500)
            {
                return "error";
            }

            if (status >= 400)
            {
                return "warn";
            }

            return "info";
        }

        private static RequestLogPayload Payload(
            IHeaderDictionary headers,
            string contentType,
            string body,
            LogCaptureMode capture)
        {
            return new RequestLogPayload
            {
                Headers = HeadersOf(headers),
                ContentType = contentType ?? string.Empty,
                Body = body,
                IncludeBody = capture == LogCaptureMode.All,
            };
        }

        private static string ResponseContentType(HttpResponse response)
        {
            if (response.Headers.TryGetValue("Content-Type", out var values) && values.Count > 0)
            {
                return values.ToString();
            }

            return response.ContentType ?? string.Empty;
        }

        private static Dictionary<string, string> HeadersOf(IHeaderDictionary headers)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var header in headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                dict[header.Key.ToLowerInvariant()] = header.Value.ToString();
            }

            return dict;
        }

        private static Dictionary<string, object> QueryOf(HttpRequest request)
        {
            var dict = new Dictionary<string, object>(StringComparer.Ordinal);
            var raw = request.QueryString.HasValue ? request.QueryString.Value : string.Empty;
            if (raw.StartsWith("?", StringComparison.Ordinal))
            {
                raw = raw.Substring(1);
            }

            if (raw.Length == 0)
            {
                return dict;
            }

            var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var part in raw.Split('&'))
            {
                if (part.Length == 0)
                {
                    continue;
                }

                var split = part.IndexOf('=');
                var rawKey = split >= 0 ? part.Substring(0, split) : part;
                var rawValue = split >= 0 ? part.Substring(split + 1) : string.Empty;
                var key = Uri.UnescapeDataString(rawKey.Replace("+", " "));
                if (grouped.TryGetValue(key, out var values) == false)
                {
                    values = new List<string>();
                    grouped[key] = values;
                    order.Add(key);
                }

                values.Add(rawValue);
            }

            foreach (var key in order)
            {
                var values = grouped[key];
                dict[key] = values.Count == 1 ? (object)values[0] : values.ToArray();
            }

            return dict;
        }

        private static string HeaderOrNull(IHeaderDictionary headers, string name)
        {
            if (headers.TryGetValue(name, out var values) == false || values.Count == 0)
            {
                return null;
            }

            var text = values.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        private static string UsernameFromBasic(HttpRequest request)
        {
            if (request.Headers.TryGetValue("Authorization", out var values) == false)
            {
                return null;
            }

            var header = values.ToString();
            const string prefix = "Basic ";
            if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false)
            {
                return null;
            }

            var token = header.Substring(prefix.Length).Trim();
            if (token.Length == 0)
            {
                return null;
            }

            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                var split = decoded.IndexOf(':');
                if (split < 0)
                {
                    return null;
                }

                return decoded.Substring(0, split);
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
