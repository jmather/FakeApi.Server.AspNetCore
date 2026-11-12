using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using FakeApi.Server.AspNetCore.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Logging
{
    public class RequestLoggingTests
    {
        private static readonly string[] RecordFields =
        {
            "timestamp", "level", "msg", "server", "request_id", "method", "path",
            "query", "status", "duration_ms", "action", "user",
        };

        [Fact]
        public async Task CaptureNoneOmitsRequestAndResponseAndKeepsTheRecordShape()
        {
            using (var host = new LogHost("none", xml: false))
            {
                var response = await host.Client.SendAsync(RequestWithSecret("/shape?x=1&x=2"));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                Assert.True(response.Headers.TryGetValues("X-Request-Id", out var requestIds));

                Assert.Contains(host.Formatters, formatter => formatter is RequestLogJsonConsoleFormatter);

                var line = host.Lines.Single();
                Assert.DoesNotContain("secret-password", line);
                Assert.DoesNotContain("raw-body-secret", line);

                using (var doc = JsonDocument.Parse(line))
                {
                    var root = doc.RootElement;
                    Assert.Equal(RecordFields, root.EnumerateObject().Select(p => p.Name).ToArray());
                    Assert.Matches(TimestampPattern(), root.GetProperty("timestamp").GetString());
                    Assert.Equal("warn", root.GetProperty("level").GetString());
                    Assert.Equal("request completed", root.GetProperty("msg").GetString());
                    Assert.Equal("dotnet", root.GetProperty("server").GetString());
                    Assert.Equal(requestIds.Single(), root.GetProperty("request_id").GetString());
                    Assert.Equal("POST", root.GetProperty("method").GetString());
                    Assert.Equal("/shape", root.GetProperty("path").GetString());
                    Assert.Equal(JsonValueKind.Array, root.GetProperty("query").GetProperty("x").ValueKind);
                    Assert.Equal("1", root.GetProperty("query").GetProperty("x")[0].GetString());
                    Assert.Equal("2", root.GetProperty("query").GetProperty("x")[1].GetString());
                    Assert.Equal(401, root.GetProperty("status").GetInt32());
                    Assert.Equal(JsonValueKind.Number, root.GetProperty("duration_ms").ValueKind);
                    Assert.True(root.GetProperty("duration_ms").GetDouble() >= 0);
                    Assert.Equal("register", root.GetProperty("action").GetString());
                    Assert.Equal("alice", root.GetProperty("user").GetString());
                }

                Assert.False(File.Exists(host.XmlPath));
            }
        }

        [Fact]
        public async Task CaptureHeadersIncludesHeadersButNotBodies()
        {
            using (var host = new LogHost("headers", xml: true))
            {
                var response = await host.Client.SendAsync(RequestWithSecret("/headers"));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

                var line = host.Lines.Single();
                Assert.DoesNotContain("raw-body-secret", line);
                using (var doc = JsonDocument.Parse(line))
                {
                    var request = doc.RootElement.GetProperty("request");
                    Assert.Equal(new[] { "headers", "content_type" }, request.EnumerateObject().Select(p => p.Name).ToArray());
                    Assert.Equal("secret-password-header", request.GetProperty("headers").GetProperty("x-secret").GetString());
                    Assert.Contains("application/json", request.GetProperty("content_type").GetString());
                    Assert.False(request.TryGetProperty("body", out _));
                    Assert.False(doc.RootElement.GetProperty("response").TryGetProperty("body", out _));
                }

                var xml = XDocument.Parse(File.ReadAllLines(host.XmlPath).Single(l => l.Length > 0));
                Assert.Equal("request-log", xml.Root.Name.LocalName);
                Assert.Null(xml.Root.Element("request").Element("body"));
                Assert.Equal("secret-password-header", HeaderValue(xml.Root.Element("request"), "x-secret"));
            }
        }

        [Fact]
        public async Task CaptureAllKeepsTheRawBodyAndLevelsFollowStatus()
        {
            using (var host = new LogHost("all", xml: true))
            {
                var client = await Register(host);
                var recorded = await client.SendAsync(Record("/boom", 500, "  <raw>not-parsed</raw>  "));
                Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);

                var played = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/boom"));
                Assert.Equal(HttpStatusCode.InternalServerError, played.StatusCode);
                Assert.Equal("  <raw>not-parsed</raw>  ", await played.Content.ReadAsStringAsync());

                var boom = FindPath(host.Lines, "/boom");
                using (var doc = JsonDocument.Parse(boom))
                {
                    Assert.Equal("error", doc.RootElement.GetProperty("level").GetString());
                    Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
                    Assert.Equal("  <raw>not-parsed</raw>  ", doc.RootElement.GetProperty("response").GetProperty("body").GetString());
                    Assert.Equal("text/plain", doc.RootElement.GetProperty("response").GetProperty("content_type").GetString());
                }

                var register = Find(host.Lines, root =>
                    root.GetProperty("path").GetString() == "/" && root.GetProperty("method").GetString() == "POST");
                using (var doc = JsonDocument.Parse(register))
                {
                    Assert.Equal("info", doc.RootElement.GetProperty("level").GetString());
                    Assert.Equal(201, doc.RootElement.GetProperty("status").GetInt32());
                    Assert.Contains("external_id", doc.RootElement.GetProperty("request").GetProperty("body").GetString());
                }

                foreach (var line in File.ReadAllLines(host.XmlPath).Where(l => l.Length > 0))
                {
                    var xml = XDocument.Parse(line);
                    Assert.Equal("request-log", xml.Root.Name.LocalName);
                    Assert.NotNull(xml.Root.Element("timestamp"));
                    Assert.NotNull(xml.Root.Element("query"));
                }
            }
        }

        [Fact]
        public async Task HookReturnValueIsWhatGetsWritten()
        {
            var redactor = new RecordingRedactor();
            using (var host = new LogHost("all", xml: true, redactor))
            {
                var response = await host.Client.SendAsync(RequestWithSecret("/hook"));
                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

                Assert.Equal(2, redactor.Calls.Count);
                Assert.Equal("json", redactor.Calls[0].Format);
                Assert.Equal("xml", redactor.Calls[1].Format);
                Assert.Contains("\"server\":\"dotnet\"", redactor.Calls[0].Serialized);
                Assert.StartsWith("<request-log>", redactor.Calls[1].Serialized);

                Assert.Equal(redactor.Calls[0].Returned, host.Lines.Single());
                Assert.Equal(redactor.Calls[1].Returned, File.ReadAllText(host.XmlPath).TrimEnd('\n', '\r'));
                Assert.StartsWith("HOOK[json]", host.Lines.Single());
                Assert.StartsWith("HOOK[xml]", File.ReadAllText(host.XmlPath));
            }
        }

        [Fact]
        public void JsonFormatterWritesTheRequestLineUnchanged()
        {
            var formatter = new RequestLogJsonConsoleFormatter(new FixedOptions());
            var writer = new StringWriter();
            var entry = new LogEntry<string>(
                LogLevel.Information,
                RequestLogSink.Category,
                new EventId(1),
                "{\"server\":\"dotnet\"}",
                exception: null,
                static (state, _) => state);
            formatter.Write(entry, scopeProvider: null, writer);

            Assert.Equal("{\"server\":\"dotnet\"}" + Environment.NewLine, writer.ToString());
        }

        [Fact]
        public async Task BodiesLongerThan64KiBAreMarkedTruncated()
        {
            var payload = new byte[RequestLogBody.MaxBytes + 25];
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = (byte)'a';
            }

            var text = await RequestLogBody.ReadCappedAsync(new MemoryStream(payload));
            Assert.EndsWith(RequestLogBody.TruncationSuffix, text);
            Assert.Equal(RequestLogBody.MaxBytes, text.Length - RequestLogBody.TruncationSuffix.Length);

            var exact = await RequestLogBody.ReadCappedAsync(new MemoryStream(new byte[RequestLogBody.MaxBytes]));
            Assert.DoesNotContain(RequestLogBody.TruncationSuffix, exact);
            Assert.Equal(RequestLogBody.MaxBytes, exact.Length);
        }

        private static string FindPath(IEnumerable<string> lines, string path)
        {
            return Find(lines, root => root.GetProperty("path").GetString() == path);
        }

        private static string Find(IEnumerable<string> lines, Func<JsonElement, bool> match)
        {
            foreach (var line in lines.Reverse())
            {
                using (var doc = JsonDocument.Parse(line))
                {
                    if (match(doc.RootElement))
                    {
                        return line;
                    }
                }
            }

            throw new InvalidOperationException("No matching request log.");
        }

        private static string HeaderValue(XElement payload, string name)
        {
            return payload.Element("headers").Elements("header")
                .Single(h => h.Attribute("name").Value == name)
                .Value;
        }

        private static HttpRequestMessage RequestWithSecret(string path)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, path);
            message.Headers.TryAddWithoutValidation("X-FakeApi-Action", "register");
            message.Headers.TryAddWithoutValidation("X-Secret", "secret-password-header");
            message.Headers.TryAddWithoutValidation(
                "Authorization",
                "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:secret-password")));
            message.Content = new StringContent("{\"password\":\"raw-body-secret\"}", Encoding.UTF8, "application/json");
            return message;
        }

        private static async Task<HttpClient> Register(LogHost host)
        {
            var register = new HttpRequestMessage(HttpMethod.Post, "/");
            register.Headers.TryAddWithoutValidation("X-FakeApi-Action", "register");
            register.Content = new StringContent("{\"external_id\":\"log\"}", Encoding.UTF8, "application/json");
            var response = await host.Client.SendAsync(register);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using (var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            {
                host.Client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "Authorization",
                    "Basic " + doc.RootElement.GetProperty("auth_token").GetString());
            }

            return host.Client;
        }

        private static HttpRequestMessage Record(string path, int status, string content)
        {
            var message = new HttpRequestMessage(HttpMethod.Put, "/");
            message.Headers.TryAddWithoutValidation("X-FakeApi-Action", "record");
            var body = "{\"method\":\"get\",\"path\":\"" + path
                + "\",\"responses\":[{\"status\":" + status
                + ",\"content\":" + JsonSerializer.Serialize(content)
                + ",\"content_type\":\"text/plain\"}]}";
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return message;
        }

        private static Regex TimestampPattern()
        {
            return new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$");
        }

        private sealed class RecordingRedactor : ILogRedactor
        {
            public List<Call> Calls { get; } = new List<Call>();

            public string Redact(string serialized, string format)
            {
                var returned = "HOOK[" + format + "]" + serialized;
                Calls.Add(new Call(format, serialized, returned));
                return returned;
            }

            public sealed class Call
            {
                public Call(string format, string serialized, string returned)
                {
                    Format = format;
                    Serialized = serialized;
                    Returned = returned;
                }

                public string Format { get; }

                public string Serialized { get; }

                public string Returned { get; }
            }
        }

        private sealed class FixedOptions : IOptionsMonitor<JsonConsoleFormatterOptions>
        {
            public JsonConsoleFormatterOptions CurrentValue { get; } = new JsonConsoleFormatterOptions();

            public JsonConsoleFormatterOptions Get(string name)
            {
                return CurrentValue;
            }

            public IDisposable OnChange(Action<JsonConsoleFormatterOptions, string> listener)
            {
                return null;
            }
        }

        private sealed class LogHost : IDisposable
        {
            private readonly WebApplicationFactory<Program> _factory;

            public LogHost(string capture, bool xml, ILogRedactor redactor = null)
            {
                Lines = new ConcurrentQueue<string>();
                XmlPath = xml
                    ? Path.Combine(Path.GetTempPath(), "fakeapi-log-" + Guid.NewGuid().ToString("N") + ".xml")
                    : string.Empty;
                var lines = Lines;
                _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string>
                        {
                            [RequestLogOptions.CaptureEnvironmentVariable] = capture,
                            [RequestLogOptions.XmlPathEnvironmentVariable] = XmlPath,
                        });
                    });
                    builder.ConfigureLogging(logging =>
                    {
                        logging.AddProvider(new LineLoggerProvider(lines));
                    });
                    if (redactor != null)
                    {
                        builder.ConfigureTestServices(services =>
                        {
                            services.RemoveAll<ILogRedactor>();
                            services.AddSingleton(redactor);
                        });
                    }
                });
                Client = _factory.CreateClient();
            }

            public HttpClient Client { get; }

            public IEnumerable<ConsoleFormatter> Formatters
            {
                get { return _factory.Services.GetServices<ConsoleFormatter>(); }
            }

            public ConcurrentQueue<string> Lines { get; }

            public string XmlPath { get; }

            public void Dispose()
            {
                Client.Dispose();
                _factory.Dispose();
                if (string.IsNullOrEmpty(XmlPath) == false && File.Exists(XmlPath))
                {
                    File.Delete(XmlPath);
                }
            }
        }

        private sealed class LineLoggerProvider : ILoggerProvider
        {
            private readonly ConcurrentQueue<string> _lines;

            public LineLoggerProvider(ConcurrentQueue<string> lines)
            {
                _lines = lines;
            }

            public ILogger CreateLogger(string categoryName)
            {
                return new LineLogger(categoryName, _lines);
            }

            public void Dispose()
            {
            }
        }

        private sealed class LineLogger : ILogger
        {
            private readonly string _category;
            private readonly ConcurrentQueue<string> _lines;

            public LineLogger(string category, ConcurrentQueue<string> lines)
            {
                _category = category;
                _lines = lines;
            }

            public IDisposable BeginScope<TState>(TState state)
            {
                return NullScope.Instance;
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

                _lines.Enqueue(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
