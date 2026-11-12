using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var repo = FindRepo(AppContext.BaseDirectory) ?? FindRepo(Directory.GetCurrentDirectory())
    ?? throw new InvalidOperationException("Could not find the conformance directory.");
var suite = Path.Combine(repo, "conformance");
var checksumError = VerifySuite(suite);
if (checksumError != null)
{
    Console.Error.WriteLine(checksumError);
    return 1;
}

var dll = ResolveServer(repo);
if (File.Exists(dll) == false)
{
    Console.Error.WriteLine("Server assembly was not found at " + dll);
    return 1;
}

var failed = 0;
foreach (var file in Directory.GetFiles(Path.Combine(suite, "fixtures"), "*.json").OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
{
    using var document = JsonDocument.Parse(File.ReadAllText(file));
    var scenario = document.RootElement;
    var id = scenario.GetProperty("id").GetString();
    try
    {
        await RunScenario(scenario, suite, dll);
        Console.WriteLine("PASS " + id);
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine("FAIL " + id);
        Console.Error.WriteLine(ex.Message);
    }
}

return failed == 0 ? 0 : 1;

static string FindRepo(string start)
{
    var current = new DirectoryInfo(start);
    while (current != null)
    {
        if (File.Exists(Path.Combine(current.FullName, "conformance", "SHA256SUMS"))
            && Directory.Exists(Path.Combine(current.FullName, "src")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return null;
}

static string ResolveServer(string repo)
{
    var release = Path.Combine(repo, "src", "FakeApi.Server.AspNetCore", "bin", "Release", "net10.0", "FakeApi.Server.AspNetCore.dll");
    if (File.Exists(release))
    {
        return release;
    }

    return Path.Combine(repo, "src", "FakeApi.Server.AspNetCore", "bin", "Debug", "net10.0", "FakeApi.Server.AspNetCore.dll");
}

static string VerifySuite(string suite)
{
    var versionPath = Path.Combine(suite, "VERSION");
    var version = File.ReadAllBytes(versionPath);
    if (version.Length != 2 || version[0] != (byte)'1' || version[1] != (byte)'\n')
    {
        return "VERSION must be the bytes 1\\n";
    }

    var listed = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var line in File.ReadAllLines(Path.Combine(suite, "SHA256SUMS")))
    {
        if (line.Length < 66)
        {
            return "Malformed SHA256SUMS line: " + line;
        }

        listed[line.Substring(66)] = line.Substring(0, 64);
    }

    var actual = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var file in Directory.GetFiles(suite, "*", SearchOption.AllDirectories))
    {
        var relative = Path.GetRelativePath(suite, file).Replace('\\', '/');
        if (relative == "SHA256SUMS")
        {
            continue;
        }

        actual[relative] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
    }

    foreach (var pair in listed)
    {
        if (actual.TryGetValue(pair.Key, out var digest) == false)
        {
            return "Missing conformance file " + pair.Key;
        }

        if (digest != pair.Value)
        {
            return "Checksum mismatch for " + pair.Key;
        }
    }

    foreach (var pair in actual)
    {
        if (listed.ContainsKey(pair.Key) == false)
        {
            return "Unlisted conformance file " + pair.Key;
        }
    }

    return null;
}

static async Task RunScenario(JsonElement scenario, string suite, string dll)
{
    var id = scenario.GetProperty("id").GetString();
    var port = FreePort();
    var xmlPath = Path.Combine(Path.GetTempPath(), "fakeapi-conformance-" + Guid.NewGuid().ToString("N") + ".xml");
    var psi = new ProcessStartInfo("dotnet", "exec \"" + dll + "\" --urls http://127.0.0.1:" + port)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = Path.GetDirectoryName(dll),
    };

    foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        var key = (string)entry.Key;
        if (key.StartsWith("FAKEAPI_", StringComparison.Ordinal))
        {
            psi.Environment.Remove(key);
        }
    }

    if (scenario.TryGetProperty("env", out var env))
    {
        foreach (var pair in env.EnumerateObject())
        {
            psi.Environment[pair.Name] = pair.Value.GetString();
        }
    }

    var xmlLog = scenario.TryGetProperty("xml_log", out var xmlFlag) && xmlFlag.GetBoolean();
    if (xmlLog)
    {
        psi.Environment["FAKEAPI_LOG_XML_PATH"] = xmlPath;
    }

    var stringHook = "identity";
    var objectHook = "identity";
    if (scenario.TryGetProperty("hooks", out var hooks))
    {
        if (hooks.TryGetProperty("string", out var stringName))
        {
            stringHook = stringName.GetString();
        }

        if (hooks.TryGetProperty("object", out var objectName))
        {
            objectHook = objectName.GetString();
        }
    }

    if (stringHook == "prefix")
    {
        psi.Environment["FAKEAPI_LOG_HOOK"] = "prefix";
    }

    if (objectHook == "stamp")
    {
        psi.Environment["FAKEAPI_LOG_RECORD_HOOK"] = "stamp";
    }

    using var process = Process.Start(psi);
    var records = new Queue<string>();
    var stderr = new StringBuilder();
    var stdoutDone = Task.Run(async () =>
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            if (line == null)
            {
                break;
            }

            if (IsRecord(line))
            {
                lock (records)
                {
                    records.Enqueue(line);
                    Monitor.Pulse(records);
                }
            }
        }
    });
    var stderrDone = Task.Run(async () =>
    {
        stderr.Append(await process.StandardError.ReadToEndAsync().ConfigureAwait(false));
    });

    try
    {
        await WaitForPort(port).ConfigureAwait(false);
        var captures = new Dictionary<string, string>(StringComparer.Ordinal) { ["server"] = "dotnet" };
        var httpIndex = 0;
        foreach (var step in scenario.GetProperty("steps").EnumerateArray())
        {
            var stepId = step.TryGetProperty("id", out var stepName) ? stepName.GetString() : "sleep";
            var kind = step.TryGetProperty("kind", out var kindNode) ? kindNode.GetString() : "http";
            try
            {
                if (kind == "sleep" || step.TryGetProperty("request", out _) == false && step.TryGetProperty("ms", out _))
                {
                    await Task.Delay(step.GetProperty("ms").GetInt32()).ConfigureAwait(false);
                    continue;
                }

                var request = step.GetProperty("request");
                using var response = await Send(request, port, captures).ConfigureAwait(false);
                var rawLine = await NextRecord(records).ConfigureAwait(false);
                if (stringHook == "prefix" && rawLine.StartsWith("{", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("JSON record did not start with [json]");
                }

                if (step.TryGetProperty("expect", out var expect))
                {
                    await AssertResponse(expect, response, request, port, captures).ConfigureAwait(false);
                }

                if (step.TryGetProperty("log", out var log))
                {
                    AssertLog(log, rawLine, xmlLog ? xmlPath : null, httpIndex, captures, response);
                }

                httpIndex++;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(id + " " + stepId + ": " + ex.Message, ex);
            }
        }
    }
    finally
    {
        if (process.HasExited == false)
        {
            process.Kill(entireProcessTree: true);
        }

        await Task.WhenAll(stdoutDone, stderrDone).ConfigureAwait(false);
        if (File.Exists(xmlPath))
        {
            File.Delete(xmlPath);
        }
    }
}

static bool IsRecord(string line)
{
    var payload = line.StartsWith("[json]", StringComparison.Ordinal) ? line.Substring(6) : line;

    try
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.TryGetProperty("msg", out var msg)
            && msg.ValueKind == JsonValueKind.String
            && msg.GetString().StartsWith("request completed", StringComparison.Ordinal)
            && document.RootElement.TryGetProperty("request_id", out _);
    }
    catch (JsonException)
    {
        return false;
    }
}

static async Task<string> NextRecord(Queue<string> records)
{
    var deadline = DateTime.UtcNow.AddSeconds(5);
    while (DateTime.UtcNow < deadline)
    {
        lock (records)
        {
            if (records.Count > 0)
            {
                return records.Dequeue();
            }

            Monitor.Wait(records, 50);
        }

        await Task.Yield();
    }

    throw new TimeoutException("Timed out waiting for the request log line.");
}

static async Task WaitForPort(int port)
{
    var deadline = DateTime.UtcNow.AddSeconds(30);
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).ConfigureAwait(false);
            return;
        }
        catch (SocketException)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    throw new TimeoutException("Server did not accept TCP on 127.0.0.1:" + port);
}

static int FreePort()
{
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task<HttpResponseMessage> Send(JsonElement request, int port, Dictionary<string, string> captures)
{
    var method = request.GetProperty("method").GetString();
    var path = Interpolate(request.GetProperty("path").GetString(), captures);
    string query = null;
    if (request.TryGetProperty("query_raw", out var raw))
    {
        query = Interpolate(raw.GetString(), captures);
    }
    else if (request.TryGetProperty("query", out var queryNode))
    {
        query = EncodeQuery(queryNode, captures);
    }

    var target = "http://127.0.0.1:" + port + path + (query == null ? string.Empty : "?" + query);
    var message = new HttpRequestMessage(new HttpMethod(method), target);
    var contentHeaders = new List<(string Name, string Value)>();
    if (request.TryGetProperty("headers", out var headers))
    {
        foreach (var header in headers.EnumerateObject())
        {
            var value = Interpolate(header.Value.GetString(), captures);
            if (message.Headers.TryAddWithoutValidation(header.Name, value) == false)
            {
                contentHeaders.Add((header.Name, value));
            }
        }
    }

    if (request.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null || contentHeaders.Count > 0)
    {
        var bytes = request.TryGetProperty("body", out body) && body.ValueKind != JsonValueKind.Null
            ? BodyBytes(body, captures)
            : Array.Empty<byte>();
        var content = new ByteArrayContent(bytes);
        foreach (var header in contentHeaders)
        {
            content.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        message.Content = content;
    }

    var watch = Stopwatch.StartNew();
    var client = new HttpClient();
    var response = await client.SendAsync(message).ConfigureAwait(false);
    watch.Stop();
    response.RequestMessage.Options.Set(new HttpRequestOptionsKey<long>("elapsed"), watch.ElapsedMilliseconds);
    response.RequestMessage.Options.Set(new HttpRequestOptionsKey<string>("url"), target);
    return response;
}

static byte[] BodyBytes(JsonElement body, Dictionary<string, string> captures)
{
    if (body.ValueKind == JsonValueKind.String)
    {
        return Encoding.UTF8.GetBytes(Interpolate(body.GetString(), captures));
    }

    if (body.TryGetProperty("base64", out var encoded))
    {
        return Convert.FromBase64String(encoded.GetString());
    }

    var unit = body.GetProperty("repeat").GetString();
    var count = body.GetProperty("count").GetInt32();
    var builder = new StringBuilder(unit.Length * count);
    for (var i = 0; i < count; i++)
    {
        builder.Append(unit);
    }

    return Encoding.UTF8.GetBytes(builder.ToString());
}

static string EncodeQuery(JsonElement query, Dictionary<string, string> captures)
{
    var parts = new List<string>();
    foreach (var pair in query.EnumerateObject())
    {
        if (pair.Value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pair.Value.EnumerateArray())
            {
                parts.Add(Form(pair.Name) + "=" + Form(Interpolate(item.GetString(), captures)));
            }
        }
        else
        {
            parts.Add(Form(pair.Name) + "=" + Form(Interpolate(pair.Value.GetString(), captures)));
        }
    }

    return string.Join("&", parts);
}

static string Form(string value)
{
    return Uri.EscapeDataString(value).Replace("%20", "+");
}

static async Task AssertResponse(JsonElement expect, HttpResponseMessage response, JsonElement request, int port, Dictionary<string, string> captures)
{
    var status = expect.GetProperty("status").GetInt32();
    if ((int)response.StatusCode != status)
    {
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        throw new InvalidOperationException("status " + (int)response.StatusCode + " != " + status + " body " + text);
    }

    if (expect.TryGetProperty("elapsed_ms_gte", out var elapsed))
    {
        var actual = Option<long>(response, "elapsed");
        if (actual < elapsed.GetInt32())
        {
            throw new InvalidOperationException("elapsed " + actual + " < " + elapsed.GetInt32());
        }
    }

    if (expect.TryGetProperty("headers", out var headers))
    {
        foreach (var check in headers.EnumerateArray())
        {
            AssertHeader(check, response, captures);
        }
    }

    if (expect.TryGetProperty("body", out var body))
    {
        await AssertBody(body, response, captures).ConfigureAwait(false);
    }
}

static void AssertHeader(JsonElement check, HttpResponseMessage response, Dictionary<string, string> captures)
{
    var name = check.GetProperty("name").GetString();
    var match = check.GetProperty("match").GetString();
    var found = TryHeader(response, name, out var actual);
    if (match == "absent")
    {
        if (found)
        {
            throw new InvalidOperationException("header " + name + " was present");
        }

        return;
    }

    if (found == false)
    {
        throw new InvalidOperationException("header " + name + " was absent");
    }

    if (match == "present")
    {
        return;
    }

    if (match == "request_url")
    {
        var url = Option<string>(response, "url");
        if (actual != url)
        {
            throw new InvalidOperationException("header " + name + " " + actual + " != " + url);
        }

        return;
    }

    if (match == "uuid")
    {
        if (Patterns.Uuid.IsMatch(actual) == false)
        {
            throw new InvalidOperationException("header " + name + " is not a uuid: " + actual);
        }

        return;
    }

    var expected = check.TryGetProperty("value", out var value) ? Interpolate(value.GetString(), captures) : null;
    var ok = match switch
    {
        "exact" => actual == expected,
        "prefix" => actual.StartsWith(expected, StringComparison.Ordinal),
        "contains" => actual.Contains(expected, StringComparison.Ordinal),
        "regex" => Regex.IsMatch(actual, "\\A(?:" + expected + ")\\z"),
        _ => throw new InvalidOperationException("unknown header match " + match),
    };
    if (ok == false)
    {
        throw new InvalidOperationException("header " + name + " " + actual + " did not " + match + " " + expected);
    }
}

static T Option<T>(HttpResponseMessage response, string name)
{
    response.RequestMessage.Options.TryGetValue(new HttpRequestOptionsKey<T>(name), out var value);
    return value;
}

static bool TryHeader(HttpResponseMessage response, string name, out string value)
{
    if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
    {
        value = string.Join(", ", values);
        return true;
    }

    value = null;
    return false;
}

static async Task AssertBody(JsonElement check, HttpResponseMessage response, Dictionary<string, string> captures)
{
    var match = check.GetProperty("match").GetString();
    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    if (match == "empty")
    {
        if (bytes.Length != 0)
        {
            throw new InvalidOperationException("body was not empty");
        }

        return;
    }

    if (match == "file")
    {
        var expected = File.ReadAllBytes(Path.Combine(FindRepo(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory(), "conformance", check.GetProperty("path").GetString()));
        if (bytes.AsSpan().SequenceEqual(expected) == false)
        {
            throw new InvalidOperationException("body bytes did not match " + check.GetProperty("path").GetString());
        }

        return;
    }

    var text = Encoding.UTF8.GetString(bytes);
    if (match == "exact")
    {
        var expected = Interpolate(check.GetProperty("value").GetString(), captures);
        if (text != expected)
        {
            throw new InvalidOperationException("body " + text + " != " + expected);
        }

        return;
    }

    if (match == "regex")
    {
        var pattern = check.GetProperty("pattern").GetString();
        if (Regex.IsMatch(text, "\\A(?:" + pattern + ")\\z") == false)
        {
            throw new InvalidOperationException("body did not match " + pattern);
        }

        return;
    }

    if (match == "json")
    {
        using var actual = JsonDocument.Parse(text);
        MatchJson(check.GetProperty("value"), actual.RootElement, "$", captures, response);
    }
}

static void MatchJson(JsonElement expected, JsonElement actual, string path, Dictionary<string, string> captures, HttpResponseMessage response)
{
    if (expected.ValueKind == JsonValueKind.Object && expected.TryGetProperty("match", out var matchNode))
    {
        var match = matchNode.GetString();
        string captured = null;
        switch (match)
        {
            case "uuid":
                captured = actual.GetString();
                if (actual.ValueKind != JsonValueKind.String || Patterns.Uuid.IsMatch(captured) == false)
                {
                    throw new InvalidOperationException(path + " is not a uuid");
                }

                if (expected.TryGetProperty("equals_header", out var header))
                {
                    if (TryHeader(response, header.GetString(), out var headerValue) == false || headerValue != captured)
                    {
                        throw new InvalidOperationException(path + " did not equal header " + header.GetString());
                    }
                }

                break;
            case "non_empty":
                captured = actual.GetString();
                if (actual.ValueKind != JsonValueKind.String || captured.Length == 0)
                {
                    throw new InvalidOperationException(path + " is empty");
                }

                break;
            case "timestamp":
                captured = actual.GetString();
                if (actual.ValueKind != JsonValueKind.String || Patterns.Timestamp.IsMatch(captured) == false)
                {
                    throw new InvalidOperationException(path + " is not a timestamp: " + captured);
                }

                break;
            case "number":
                if (actual.ValueKind != JsonValueKind.Number)
                {
                    throw new InvalidOperationException(path + " is not a number");
                }

                var number = actual.GetDouble();
                if (expected.TryGetProperty("minimum", out var minimum) && number < minimum.GetDouble())
                {
                    throw new InvalidOperationException(path + " " + number + " < " + minimum.GetDouble());
                }

                if (expected.TryGetProperty("maximum", out var maximum) && number > maximum.GetDouble())
                {
                    throw new InvalidOperationException(path + " " + number + " > " + maximum.GetDouble());
                }

                captured = actual.GetRawText();
                break;
            case "basic_token":
                captured = actual.GetString();
                var username = captures[expected.GetProperty("username").GetString()];
                var password = captures[expected.GetProperty("password").GetString()];
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
                if (captured != token)
                {
                    throw new InvalidOperationException(path + " token mismatch");
                }

                break;
            case "var":
                captured = actual.GetString();
                if (captured != captures[expected.GetProperty("name").GetString()])
                {
                    throw new InvalidOperationException(path + " did not equal var");
                }

                break;
            case "regex":
                captured = actual.GetString();
                if (Regex.IsMatch(captured ?? string.Empty, "\\A(?:" + expected.GetProperty("pattern").GetString() + ")\\z") == false)
                {
                    throw new InvalidOperationException(path + " did not match regex");
                }

                break;
            case "object":
                if (actual.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidOperationException(path + " is not an object");
                }

                if (expected.TryGetProperty("absent", out var absent))
                {
                    foreach (var name in absent.EnumerateArray())
                    {
                        if (actual.TryGetProperty(name.GetString(), out _))
                        {
                            throw new InvalidOperationException(path + " has forbidden key " + name.GetString());
                        }
                    }
                }

                foreach (var key in expected.GetProperty("keys").EnumerateObject())
                {
                    if (actual.TryGetProperty(key.Name, out var child) == false)
                    {
                        throw new InvalidOperationException(path + " missing " + key.Name);
                    }

                    MatchJson(key.Value, child, path + "." + key.Name, captures, response);
                }

                return;
            default:
                throw new InvalidOperationException("unknown json match " + match);
        }

        if (expected.TryGetProperty("capture", out var capture))
        {
            captures[capture.GetString()] = captured;
        }

        return;
    }

    if (expected.ValueKind == JsonValueKind.Object)
    {
        if (actual.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(path + " is not an object");
        }

        var expectedNames = expected.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var actualNames = actual.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        if (expectedNames.SetEquals(actualNames) == false)
        {
            throw new InvalidOperationException(path + " keys " + string.Join(",", actualNames.OrderBy(name => name)) + " != " + string.Join(",", expectedNames.OrderBy(name => name)));
        }

        foreach (var property in expected.EnumerateObject())
        {
            MatchJson(property.Value, actual.GetProperty(property.Name), path + "." + property.Name, captures, response);
        }

        return;
    }

    if (expected.ValueKind == JsonValueKind.Array)
    {
        if (actual.ValueKind != JsonValueKind.Array || actual.GetArrayLength() != expected.GetArrayLength())
        {
            throw new InvalidOperationException(path + " array length mismatch");
        }

        var index = 0;
        foreach (var item in expected.EnumerateArray())
        {
            MatchJson(item, actual[index], path + "[" + index + "]", captures, response);
            index++;
        }

        return;
    }

    if (expected.ValueKind == JsonValueKind.String)
    {
        var wanted = Interpolate(expected.GetString(), captures);
        if (actual.ValueKind != JsonValueKind.String || actual.GetString() != wanted)
        {
            throw new InvalidOperationException(path + " " + actual + " != " + wanted);
        }

        return;
    }

    if (JsonElement.DeepEquals(expected, actual) == false)
    {
        throw new InvalidOperationException(path + " " + actual + " != " + expected);
    }
}

static void AssertLog(JsonElement log, string rawLine, string xmlPath, int httpIndex, Dictionary<string, string> captures, HttpResponseMessage response)
{
    var line = rawLine + "\n";
    if (log.TryGetProperty("raw_prefix", out var prefix) && line.StartsWith(prefix.GetString(), StringComparison.Ordinal) == false)
    {
        throw new InvalidOperationException("log line did not start with " + prefix.GetString());
    }

    if (log.TryGetProperty("raw_excludes", out var excludes))
    {
        foreach (var item in excludes.EnumerateArray())
        {
            var banned = Interpolate(item.GetString(), captures);
            if (banned.Length > 0 && line.Contains(banned, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("log line contained excluded text");
            }
        }
    }

    var payload = rawLine.StartsWith("[json]", StringComparison.Ordinal) ? rawLine.Substring(6) : rawLine;
    using var document = JsonDocument.Parse(payload);
    if (log.TryGetProperty("json", out var json))
    {
        MatchJson(json.GetProperty("value"), document.RootElement, "log", captures, response);
    }

    if (log.TryGetProperty("xml", out var xml))
    {
        var records = File.ReadAllLines(xmlPath).Where(entry => entry.Length > 0).ToList();
        var record = records[httpIndex];
        if (record.StartsWith("[xml]", StringComparison.Ordinal))
        {
            record = record.Substring(5);
        }

        XElement root;
        try
        {
            root = XDocument.Parse(record).Root;
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException(ex.Message + " XML=" + record);
        }
        foreach (var entry in xml.GetProperty("text").EnumerateObject())
        {
            var actual = XmlText(root, entry.Name);
            var wanted = Interpolate(entry.Value.GetString(), captures);
            if (actual != wanted)
            {
                throw new InvalidOperationException("xml " + entry.Name + " " + actual + " != " + wanted);
            }
        }
    }
}

static string XmlText(XElement root, string path)
{
    IEnumerable<XElement> current = new[] { root };
    foreach (var segment in path.Split('/'))
    {
        var name = segment;
        var index = 0;
        var hash = name.LastIndexOf('#');
        if (hash >= 0 && int.TryParse(name.Substring(hash + 1), out var parsed))
        {
            index = parsed;
            name = name.Substring(0, hash);
        }

        string attr = null;
        string attrValue = null;
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            var eq = name.IndexOf('=', at);
            attr = name.Substring(at + 1, eq - at - 1);
            attrValue = name.Substring(eq + 1);
            name = name.Substring(0, at);
        }

        var next = new List<XElement>();
        foreach (var node in current)
        {
            next.AddRange(node.Elements(name).Where(element =>
                attr == null || (string)element.Attribute(attr) == attrValue));
        }

        if (index >= next.Count)
        {
            throw new InvalidOperationException("xml path " + path + " missed " + segment);
        }

        current = new[] { next[index] };
    }

    return current.Single().Value ?? string.Empty;
}

static string Interpolate(string value, Dictionary<string, string> captures)
{
    if (value == null || value.Contains("{{", StringComparison.Ordinal) == false)
    {
        return value;
    }

    return Regex.Replace(value, "\\{\\{([^{}]+)\\}\\}", match =>
    {
        var name = match.Groups[1].Value;
        if (captures.TryGetValue(name, out var captured) == false)
        {
            throw new InvalidOperationException("Unknown capture {{" + name + "}}");
        }

        return captured;
    });
}

static class Patterns
{
    public static readonly Regex Uuid = new Regex("\\A[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant);

    public static readonly Regex Timestamp = new Regex("\\A\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}:\\d{2}\\.\\d{3}Z\\z", RegexOptions.CultureInvariant);
}
