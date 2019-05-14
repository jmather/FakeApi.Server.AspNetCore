# Fake API Server

[![Build Status](https://dev.azure.com/jmather0115/FakeAPI.Server.AspNetCore/_apis/build/status/GitHub%20FakeAPI.Server.AspNetCore?branchName=master)](https://dev.azure.com/jmather0115/FakeAPI.Server.AspNetCore/_build/latest?definitionId=2&branchName=master)
[![Sonarcloud Status](https://sonarcloud.io/api/project_badges/measure?project=jmather_FakeApi.Server.AspNetCore&metric=alert_status)](https://sonarcloud.io/dashboard?id=jmather_FakeApi.Server.AspNetCore)

This tool allows you to register collections of arbitrary endpoints to mock responses for, allowing you to easily test your code end-to-end.

Pair with the [Fake API Client](https://github.com/jmather/node-fake-api-client) for easy collection registration.

View the [API Documentation](https://documenter.getpostman.com/view/4858910/S1LpZrgg#intro) to get a better idea of how to use the Fake API.

A server instance has been set up at [https://node-fake-api-server.herokuapp.com/](https://node-fake-api-server.herokuapp.com/).

## Schema

We have defined an [Open API Specification](/public/fake-api.openapi.yaml) as well as a detailed [JSON Schema](/public/fake-api-schema.json) of the request payloads.

## Usage

### Run Locally (Private)

```bash
npm install -g node-fake-api-server
fake-api-server
```

### Run Locally (Public)

```bash
npm install -g node-fake-api-server
fake-api-server --public
```

### Run In Code

```javascript
const http = require('http');
const app = require('node-fake-api-server');

const port = Math.floor(Math.random() * 2000) + 3000;

app.set('port', port);

const server = http.createServer(app);
server.listen(port);

server.on('listening', () => {
    // do stuff...
});
```
