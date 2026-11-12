using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Logging
{
    public class CursedSecretsTests
    {
        [SkippableFact]
        public async Task CorpusValuesDoNotAppearInDecodedLogs()
        {
            // Identity hooks on purpose. This fails until a real redactor is registered.
            Skip.IfNot(string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_CURSED"), "1", StringComparison.Ordinal));

            var strict = string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_CURSED_STRICT"), "1", StringComparison.Ordinal);
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cursed-secrets.json");
            using (var document = JsonDocument.Parse(File.ReadAllText(fixturePath)))
            {
                var entries = document.RootElement.GetProperty("entries").EnumerateArray().Select(Copy).ToList();
                var report = new StringBuilder();
                var hardMisses = 0;
                var softMisses = 0;
                foreach (var mode in new[] { "string", "object", "both" })
                {
                    report.AppendLine("mode=" + mode);
                    var outcome = await RunMode(entries, mode).ConfigureAwait(false);
                    foreach (var sensitivity in new[] { "definitely", "likely", "secret-ish" })
                    {
                        report.AppendLine(sensitivity);
                        foreach (var entry in entries.Where(item => item.Sensitivity == sensitivity))
                        {
                            if (string.IsNullOrEmpty(entry.Value))
                            {
                                report.AppendLine("  PASS " + entry.Id + " (empty)");
                                continue;
                            }

                            var where = outcome(entry.Value);
                            if (where == null)
                            {
                                report.AppendLine("  PASS " + entry.Id);
                                continue;
                            }

                            report.AppendLine("  FAIL " + entry.Id + " (" + where + ")");
                            if (sensitivity == "secret-ish")
                            {
                                softMisses++;
                            }
                            else
                            {
                                hardMisses++;
                            }
                        }
                    }
                }

                var text = report.ToString();
                Assert.True(hardMisses == 0, text);
                if (strict)
                {
                    Assert.True(softMisses == 0, text);
                }
            }
        }

        private static async Task<Func<string, string>> RunMode(List<Entry> entries, string mode)
        {
            var xmlPath = Path.Combine(Path.GetTempPath(), "fakeapi-cursed-" + Guid.NewGuid().ToString("N") + ".xml");
            var lines = new List<string>();
            try
            {
                using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string>
                        {
                            [RequestLogOptions.CaptureEnvironmentVariable] = "all",
                            [RequestLogOptions.XmlPathEnvironmentVariable] = xmlPath,
                        });
                    });
                    builder.ConfigureLogging(logging => logging.AddProvider(new ListLoggerProvider(lines)));
                    builder.ConfigureTestServices(services =>
                    {
                        services.RemoveAll<ILogRedactor>();
                        services.RemoveAll<ILogRecordRedactor>();
                        services.AddSingleton(StringHook(mode));
                        services.AddSingleton(ObjectHook(mode));
                    });
                }))
                using (var client = factory.CreateClient())
                {
                    await Register(client).ConfigureAwait(false);
                    foreach (var entry in entries)
                    {
                        await Exercise(client, entry).ConfigureAwait(false);
                    }
                }

                var xml = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : string.Empty;
                var capturedLines = lines.ToList();
                return secret =>
                {
                    var inJson = capturedLines.Any(line => JsonContains(line, secret));
                    var inXml = XmlContains(xml, secret);
                    if (inJson == false && inXml == false)
                    {
                        return null;
                    }

                    return inJson && inXml ? "json+xml" : inJson ? "json" : "xml";
                };
            }
            finally
            {
                if (File.Exists(xmlPath))
                {
                    File.Delete(xmlPath);
                }
            }
        }

        private static ILogRedactor StringHook(string mode)
        {
            if (mode == "object")
            {
                return new IdentityLogRedactor();
            }

            return string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_LOG_HOOK"), "prefix", StringComparison.OrdinalIgnoreCase)
                ? (ILogRedactor)new PrefixLogRedactor()
                : new IdentityLogRedactor();
        }

        private static ILogRecordRedactor ObjectHook(string mode)
        {
            if (mode == "string")
            {
                return new IdentityLogRecordRedactor();
            }

            return string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_LOG_RECORD_HOOK"), "stamp", StringComparison.OrdinalIgnoreCase)
                ? (ILogRecordRedactor)new StampLogRecordRedactor()
                : new IdentityLogRecordRedactor();
        }

        private static bool JsonContains(string line, string secret)
        {
            var payload = line.StartsWith("[json]", StringComparison.Ordinal) ? line.Substring(6) : line;
            try
            {
                using (var document = JsonDocument.Parse(payload))
                {
                    return WalkJson(document.RootElement, secret);
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool WalkJson(JsonElement element, string secret)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name.Contains(secret, StringComparison.Ordinal) || WalkJson(property.Value, secret))
                        {
                            return true;
                        }
                    }

                    return false;
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        if (WalkJson(item, secret))
                        {
                            return true;
                        }
                    }

                    return false;
                case JsonValueKind.String:
                    return (element.GetString() ?? string.Empty).Contains(secret, StringComparison.Ordinal);
                default:
                    return element.ToString().Contains(secret, StringComparison.Ordinal);
            }
        }

        private static bool XmlContains(string xml, string secret)
        {
            foreach (var line in xml.Split('\n'))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var payload = line.StartsWith("[xml]", StringComparison.Ordinal) ? line.Substring(5) : line;
                try
                {
                    var root = System.Xml.Linq.XDocument.Parse(payload).Root;
                    if (WalkXml(root, secret))
                    {
                        return true;
                    }
                }
                catch (System.Xml.XmlException)
                {
                }
            }

            return false;
        }

        private static bool WalkXml(System.Xml.Linq.XElement element, string secret)
        {
            if (element == null)
            {
                return false;
            }

            foreach (var attribute in element.Attributes())
            {
                if ((attribute.Value ?? string.Empty).Contains(secret, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            foreach (var node in element.Nodes())
            {
                if (node is System.Xml.Linq.XText text && (text.Value ?? string.Empty).Contains(secret, StringComparison.Ordinal))
                {
                    return true;
                }

                if (node is System.Xml.Linq.XElement child && WalkXml(child, secret))
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task Register(HttpClient client)
        {
            var register = new HttpRequestMessage(HttpMethod.Post, "/");
            register.Headers.TryAddWithoutValidation("X-FakeApi-Action", "register");
            register.Content = new StringContent("{\"external_id\":\"cursed\"}", Encoding.UTF8, "application/json");
            var response = await client.SendAsync(register);
            response.EnsureSuccessStatusCode();
            using (var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Authorization",
                    "Basic " + body.RootElement.GetProperty("auth_token").GetString());
            }
        }

        private static async Task Exercise(HttpClient client, Entry entry)
        {
            var path = "/cursed/" + entry.Id;
            var responseBody = entry.Value ?? string.Empty;
            var method = "get";
            string requestBody = null;
            string contentType = null;

            if (entry.Key.StartsWith("response:", StringComparison.Ordinal))
            {
                var field = entry.Key.Substring("response:".Length);
                responseBody = "{\"" + field + "\":\"" + (entry.Value ?? string.Empty) + "\"}";
            }
            else if (entry.Location == "json_body")
            {
                method = "post";
                requestBody = entry.Value != null && (entry.Value.StartsWith("{", StringComparison.Ordinal) || entry.Value.StartsWith("[", StringComparison.Ordinal))
                    ? "{\"" + entry.Key + "\":" + entry.Value + "}"
                    : "{\"" + entry.Key + "\":\"" + (entry.Value ?? string.Empty) + "\"}";
                contentType = "application/json";
            }
            else if (entry.Location == "xml_body")
            {
                method = "post";
                contentType = "application/xml";
                if (entry.Key.StartsWith("@", StringComparison.Ordinal))
                {
                    requestBody = "<root " + entry.Key.Substring(1) + "=\"" + entry.Value + "\"></root>";
                }
                else if (entry.Key == "<!--" || (entry.Value != null && entry.Value.Contains("<!--")))
                {
                    requestBody = entry.Value;
                }
                else
                {
                    requestBody = "<root><" + entry.Key + ">" + entry.Value + "</" + entry.Key + "></root>";
                }
            }
            else if (entry.Location == "yaml_body")
            {
                method = "post";
                contentType = "application/yaml";
                requestBody = entry.Key == "#" || (entry.Value != null && entry.Value.StartsWith("#", StringComparison.Ordinal))
                    ? entry.Value
                    : entry.Key + ": " + entry.Value;
            }
            else if (entry.Location == "text_body")
            {
                method = "post";
                contentType = "text/plain";
                requestBody = entry.Value;
            }
            else if (entry.Location == "form_body")
            {
                method = "post";
                contentType = "application/x-www-form-urlencoded";
                requestBody = entry.Key + "=" + entry.Value;
            }

            var record = new HttpRequestMessage(HttpMethod.Put, "/");
            record.Headers.TryAddWithoutValidation("X-FakeApi-Action", "record");
            record.Content = new StringContent(
                "{\"method\":\"" + method + "\",\"path\":\"" + path + "\",\"responses\":[{\"status\":200,\"content\":"
                + JsonSerializer.Serialize(responseBody) + ",\"content_type\":\"text/plain\"}]}",
                Encoding.UTF8,
                "application/json");
            (await client.SendAsync(record)).EnsureSuccessStatusCode();

            var uri = path;
            if (entry.Location == "query")
            {
                uri = path + "?" + Uri.EscapeDataString(entry.Key) + "=" + Uri.EscapeDataString(entry.Value ?? string.Empty);
            }

            var call = new HttpRequestMessage(new HttpMethod(method.ToUpperInvariant()), uri);
            if (entry.Location == "header")
            {
                var headerValue = entry.Value;
                if (string.Equals(entry.Key, "authorization", StringComparison.OrdinalIgnoreCase)
                    && headerValue.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase) == false
                    && headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == false)
                {
                    headerValue = "Basic " + headerValue;
                }

                call.Headers.TryAddWithoutValidation(entry.Key, headerValue);
            }

            if (requestBody != null)
            {
                call.Content = new StringContent(requestBody, Encoding.UTF8, contentType);
            }

            await client.SendAsync(call);
        }

        private static Entry Copy(JsonElement element)
        {
            return new Entry
            {
                Id = element.GetProperty("id").GetString(),
                Sensitivity = element.GetProperty("sensitivity").GetString(),
                Location = element.GetProperty("location").GetString(),
                Key = element.GetProperty("key").GetString(),
                Value = Resolve(element),
            };
        }

        private static string Resolve(JsonElement element)
        {
            if (element.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (element.TryGetProperty("parts", out var parts) == false)
            {
                return string.Empty;
            }

            if (parts.ValueKind == JsonValueKind.Array)
            {
                var builder = new StringBuilder();
                foreach (var part in parts.EnumerateArray())
                {
                    builder.Append(part.GetString());
                }

                return builder.ToString();
            }

            return ResolveNote(element, parts.GetString() ?? string.Empty);
        }

        private static string ResolveNote(JsonElement element, string note)
        {
            var text = note.StartsWith("note:", StringComparison.OrdinalIgnoreCase)
                ? note.Substring("note:".Length).Trim()
                : note;

            var base64Call = text.IndexOf("base64(", StringComparison.Ordinal);
            if (base64Call >= 0)
            {
                var start = base64Call + "base64(".Length;
                var end = text.IndexOf(')', start);
                var inner = text.Substring(start, end - start);
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(inner));
            }

            const string base64Of = "base64 of ";
            var base64OfAt = text.IndexOf(base64Of, StringComparison.Ordinal);
            if (base64OfAt >= 0)
            {
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(text.Substring(base64OfAt + base64Of.Length)));
            }

            if (text.IndexOf("empty", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return string.Empty;
            }

            if (text.IndexOf("collides with the key", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("Cyrillic", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return element.GetProperty("key").GetString();
            }

            if (text.IndexOf("unicode", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return char.ConvertFromUtf32(0x1F512) + element.GetProperty("id").GetString();
            }

            if (text.IndexOf("false-positive", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Guid.NewGuid().ToString();
            }

            return element.GetProperty("id").GetString();
        }

        private sealed class Entry
        {
            public string Id { get; set; }

            public string Sensitivity { get; set; }

            public string Location { get; set; }

            public string Key { get; set; }

            public string Value { get; set; }
        }

        private sealed class ListLoggerProvider : ILoggerProvider
        {
            private readonly List<string> _lines;

            public ListLoggerProvider(List<string> lines)
            {
                _lines = lines;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new ListLogger(categoryName, _lines);
            }

            public void Dispose()
            {
            }
        }

        private sealed class ListLogger : ILogger
        {
            private readonly string _category;
            private readonly List<string> _lines;

            public ListLogger(string category, List<string> lines)
            {
                _category = category;
                _lines = lines;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (_category != RequestLogSink.Category || formatter == null)
                {
                    return;
                }

                lock (_lines)
                {
                    _lines.Add(formatter(state, exception));
                }
            }
        }
    }
}
