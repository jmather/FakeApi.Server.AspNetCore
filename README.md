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

`appsettings.json` holds logging and `AllowedHosts`. Recorded users stay in memory. Inactive users are dropped after two hours, and the cleanup pass runs every fifteen minutes. Those intervals are the defaults on `UserRepository.Config`, not appsettings keys.

Application Insights is optional and stays disabled when it is not configured. The 3.x SDK reads `ApplicationInsights:ConnectionString` or `APPLICATIONINSIGHTS_CONNECTION_STRING`. An instrumentation key (`ApplicationInsights:InstrumentationKey` or `APPINSIGHTS_INSTRUMENTATIONKEY`) is still accepted and turned into `InstrumentationKey=<key>`.
