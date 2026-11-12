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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Logging
{
    public class CursedSecretsTests
    {
        [SkippableFact]
        public async Task CorpusValuesDoNotAppearVerbatimWhenCaptureIsOn()
        {
            // Identity hook on purpose. This fails until a real redactor is registered.
            Skip.IfNot(string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_CURSED"), "1", StringComparison.Ordinal));

            var strictSecretIsh = string.Equals(
                Environment.GetEnvironmentVariable("FAKEAPI_CURSED_SECRETISH"),
                "strict",
                StringComparison.OrdinalIgnoreCase);
            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "cursed-secrets.json");
            using (var document = JsonDocument.Parse(File.ReadAllText(fixturePath)))
            {
                var entries = document.RootElement.GetProperty("entries").EnumerateArray().Select(Copy).ToList();
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
                    }))
                    using (var client = factory.CreateClient())
                    {
                        await Register(client);
                        foreach (var entry in entries)
                        {
                            await Exercise(client, entry);
                        }
                    }

                    var json = string.Join("\n", lines);
                    var xml = File.Exists(xmlPath) ? File.ReadAllText(xmlPath) : string.Empty;
                    var grouped = new StringBuilder();
                    var hardMisses = 0;
                    var softMisses = 0;
                    foreach (var sensitivity in new[] { "definitely", "likely", "secret-ish" })
                    {
                        grouped.AppendLine(sensitivity);
                        foreach (var entry in entries.Where(e => e.Sensitivity == sensitivity))
                        {
                            if (string.IsNullOrEmpty(entry.Value))
                            {
                                grouped.AppendLine("  PASS " + entry.Id + " (empty)");
                                continue;
                            }

                            var inJson = json.Contains(entry.Value);
                            var inXml = xml.Contains(entry.Value);
                            if (inJson == false && inXml == false)
                            {
                                grouped.AppendLine("  PASS " + entry.Id);
                                continue;
                            }

                            var where = inJson && inXml ? "json+xml" : inJson ? "json" : "xml";
                            grouped.AppendLine("  FAIL " + entry.Id + " (" + where + ")");
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

                    var report = grouped.ToString();
                    Assert.True(hardMisses == 0, report);
                    if (strictSecretIsh)
                    {
                        Assert.True(softMisses == 0, report);
                    }
                }
                finally
                {
                    if (File.Exists(xmlPath))
                    {
                        File.Delete(xmlPath);
                    }
                }
            }
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
