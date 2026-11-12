# Fake API Server

[![CI](https://github.com/jmather/FakeApi.Server.AspNetCore/actions/workflows/ci.yml/badge.svg)](https://github.com/jmather/FakeApi.Server.AspNetCore/actions/workflows/ci.yml)
[![Sonarcloud Status](https://sonarcloud.io/api/project_badges/measure?project=jmather_FakeApi.Server.AspNetCore&metric=alert_status)](https://sonarcloud.io/dashboard?id=jmather_FakeApi.Server.AspNetCore)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=jmather_FakeApi.Server.AspNetCore&metric=coverage)](https://sonarcloud.io/dashboard?id=jmather_FakeApi.Server.AspNetCore)
[![Maintainability Rating](https://sonarcloud.io/api/project_badges/measure?project=jmather_FakeApi.Server.AspNetCore&metric=sqale_rating)](https://sonarcloud.io/dashboard?id=jmather_FakeApi.Server.AspNetCore)

ASP.NET Core server that records arbitrary endpoints and replays canned responses, so callers can exercise their code end to end.

The HTTP contract matches the sibling [node-fake-api-server](https://github.com/jmather/node-fake-api-server): the same meta actions, snake_case JSON payloads, and Basic authentication on playback. Pair it with the [Fake API Client](https://github.com/jmather/node-fake-api-client) to register a YAML collection.

## Requirements

- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0), the current long-term support release (supported through November 14, 2028).

.NET 11 is a standard-term release. This project stays on the LTS line. `global.json` asks for the .NET 10 SDK and will not roll forward to 11.

## Build and test

```bash
dotnet build
dotnet test
```

## Run

```bash
dotnet run --project src/FakeApi.Server.AspNetCore
```

The launch profile listens on `http://localhost:3000` and `https://localhost:3001`. To skip the launch profile:

```bash
dotnet run --project src/FakeApi.Server.AspNetCore --no-launch-profile --urls http://localhost:3000
```

### Register a caller

```bash
curl -sS -D - -X POST http://localhost:3000/ \
  -H 'Content-Type: application/json' \
  -H 'X-FakeApi-Action: register' \
  -d '{"external_id":"sample"}'
```

The `201` body includes `username`, `password`, and `auth_token`. `auth_token` is `base64(username:password)` and is sent back as a Basic credential.

### Record an endpoint

```bash
curl -sS -D - -X PUT http://localhost:3000/ \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Basic <auth_token>' \
  -H 'X-FakeApi-Action: record' \
  -d '{
    "method": "get",
    "path": "/hello",
    "response_mode": "incremental",
    "responses": [
      {
        "status": 200,
        "content": "Hello",
        "content_type": "text/plain",
        "delay": 0,
        "headers": { "x-smoke": "yes" }
      }
    ]
  }'
```

Recording the same method, path, body, headers, and query again returns `409`.

### Call it

```bash
curl -sS -D - http://localhost:3000/hello \
  -H 'Authorization: Basic <auth_token>'
```

`response_mode` is `incremental` (the default) or `random`. Incremental playback walks the response list and wraps. `delay` is milliseconds before the body is returned, capped by the client that recorded it. Header names on a recorded endpoint are compared with the lowercased request headers, so record them in lowercase.

Calls without a valid Basic credential receive `401`. A credential with no matching endpoint receives `404`.

## Schema

The request shape is described by the [OpenAPI document](src/FakeApi.Server.AspNetCore/wwwroot/fake-api.openapi.yaml) and the [JSON Schema](src/FakeApi.Server.AspNetCore/wwwroot/fake-api-schema.json). Those files live in `wwwroot` for reference. The server does not serve them as static files.

JSON property names are snake_case (`external_id`, `content_type`, `response_mode`, `auth_token`).

## Configuration

`appsettings.json` holds logging and `AllowedHosts`. Recorded users stay in memory. A successful authentication refreshes that user's activity clock. Users with no successful authentication for two hours are dropped, and the cleanup pass runs every fifteen minutes. Those intervals are the defaults on `UserRepository.Config`, not appsettings keys.

Application Insights is optional and stays disabled when it is not configured. The 3.x SDK reads `ApplicationInsights:ConnectionString` or `APPLICATIONINSIGHTS_CONNECTION_STRING`. An instrumentation key (`ApplicationInsights:InstrumentationKey` or `APPINSIGHTS_INSTRUMENTATIONKEY`) is still accepted and turned into `InstrumentationKey=<key>`.

## Logging and redaction hook

Every completed request writes one JSON object through `ILogger`. The console provider uses the JSON formatter. Request lines are the record itself (one object per line). Other categories still go through the framework JSON console formatter.

The record uses these fields: `timestamp`, `level`, `msg`, `server`, `request_id`, `method`, `path`, `query`, `status`, `duration_ms`, `action`, `user`. `server` is `dotnet`. `level` is `info`, `warn` for 4xx, and `error` for 5xx. `action` is the `X-FakeApi-Action` header or null. `user` is the username from a Basic `Authorization` header, never the password. The response includes `X-Request-Id`.

`request` and `response` are omitted unless capture is on. Each of those objects has `headers` (lowercased names), `content_type`, and, when bodies are captured, `body` (the raw string, not re-parsed, cut at 64 KiB with a `…[truncated]` suffix).

| | |
| --- | --- |
| `FAKEAPI_LOG_CAPTURE` | `none` (default), `headers`, or `all` |
| `FakeApi:LogCapture` | same values in `appsettings.json` |
| `FAKEAPI_LOG_XML_PATH` | file path for the XML sink; unset or empty disables it |
| `FakeApi:LogXmlPath` | same path in `appsettings.json` |

Environment variables win over `appsettings`. With nothing set, bodies and headers are not written.

The XML sink appends one `<request-log>` element per request, newline-delimited, written with `XmlWriter`. It stays off unless a path is set.

```bash
FAKEAPI_LOG_CAPTURE=all FAKEAPI_LOG_XML_PATH=/tmp/fake-api.xml \
  dotnet run --project src/FakeApi.Server.AspNetCore --no-launch-profile --urls http://127.0.0.1:5088
```

Before either sink writes, it calls `ILogRedactor.Redact(serialized, format)`. `format` is `json` or `xml`. The string returned by the hook is what gets written. The default registration is `IdentityLogRedactor`, which returns the input unchanged. Replace it in DI to plug in a redaction library that works on serialized JSON, XML, YAML, or text:

```csharp
builder.Services.AddSingleton<ILogRedactor, MyRedactor>();
```

Register the replacement after `AddFakeApiRequestLogging`, or remove the default descriptor first, so the container injects one implementation.

`test/FakeApi.Server.AspNetCore.Test/Fixtures/cursed-secrets.json` is the shared corpus. `CursedSecretsTests` is skipped unless `FAKEAPI_CURSED=1`. It uses the identity hook, so it is expected to fail until a real redactor is installed. Entries whose `parts` are an array are concatenated at runtime. `FAKEAPI_CURSED_SECRETISH=strict` also fails the run when a `secret-ish` value is still present; otherwise those misses are reported and do not fail the test by themselves.
