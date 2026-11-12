using System;
using System.Collections.Generic;
using FakeApi.Server.AspNetCore;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Xunit;

namespace FakeApi.Server.AspNetCore.Test.Integration
{
    public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly WebApplicationFactory<Program> _factory;

        public SmokeTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task RegisterRecordAndFetchReturnsTheCannedResponse()
        {
            var client = await RegisterClient();

            var record = RecordRequest(
                "{\"method\":\"get\",\"path\":\"/smoke/hello\",\"response_mode\":\"incremental\",\"responses\":[{\"status\":200,\"content\":\"Hello smoke\",\"content_type\":\"text/plain\",\"delay\":0,\"headers\":{\"x-smoke\":\"yes\"}}]}");

            var recorded = await client.SendAsync(record);
            Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);

            var duplicate = await client.SendAsync(RecordRequest(
                "{\"method\":\"get\",\"path\":\"/smoke/hello\",\"response_mode\":\"incremental\",\"responses\":[{\"status\":200,\"content\":\"Hello smoke\",\"content_type\":\"text/plain\",\"delay\":0,\"headers\":{\"x-smoke\":\"yes\"}}]}"));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var fetched = await client.GetAsync("/smoke/hello");
            Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
            Assert.Equal("Hello smoke", await fetched.Content.ReadAsStringAsync());
            Assert.Equal("text/plain", fetched.Content.Headers.ContentType.MediaType);
            Assert.Equal("yes", fetched.Headers.GetValues("x-smoke").Single());
        }

        [Fact]
        public async Task IncrementalResponsesCycle()
        {
            var client = await RegisterClient();

            var recorded = await client.SendAsync(RecordRequest(
                "{\"method\":\"get\",\"path\":\"/smoke/cycle\",\"responses\":[{\"status\":200,\"content\":\"one\",\"content_type\":\"text/plain\"},{\"status\":200,\"content\":\"two\",\"content_type\":\"text/plain\"}]}"));
            Assert.Equal(HttpStatusCode.Created, recorded.StatusCode);

            Assert.Equal("one", await (await client.GetAsync("/smoke/cycle")).Content.ReadAsStringAsync());
            Assert.Equal("two", await (await client.GetAsync("/smoke/cycle")).Content.ReadAsStringAsync());
            Assert.Equal("one", await (await client.GetAsync("/smoke/cycle")).Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task UnauthenticatedCallsAreRejectedAndUnknownEndpointsAreMissing()
        {
            var anonymous = _factory.CreateClient();
            var rejected = await anonymous.GetAsync("/smoke/private");
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

            var client = await RegisterClient();
            var missing = await client.GetAsync("/smoke/not-recorded");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        [Fact]
        public async Task CorsReflectsTheRequestOriginAndAllowsCredentials()
        {
            var client = _factory.CreateClient();
            var preflight = new HttpRequestMessage(HttpMethod.Options, "/smoke/hello");
            preflight.Headers.Add("Origin", "https://example.com");
            preflight.Headers.Add("Access-Control-Request-Method", "GET");

            var response = await client.SendAsync(preflight);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal("https://example.com", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
        }

        private async Task<HttpClient> RegisterClient()
        {
            var client = _factory.CreateClient();
            var register = new HttpRequestMessage(HttpMethod.Post, "/");
            register.Headers.Add(MetaController.FakeApiActionHeader, nameof(MetaController.FakeApiAction.Register));
            register.Content = new StringContent("{\"external_id\":\"smoke\"}", Encoding.UTF8, "application/json");

            var response = await client.SendAsync(register);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var payload = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
            var expectedToken = Convert.ToBase64String(
                Encoding.ASCII.GetBytes(payload["username"] + ":" + payload["password"]));
            Assert.Equal(expectedToken, payload["auth_token"]);

            client.DefaultRequestHeaders.Add("Authorization", "Basic " + payload["auth_token"]);
            return client;
        }

        private static HttpRequestMessage RecordRequest(string json)
        {
            var record = new HttpRequestMessage(HttpMethod.Put, "/");
            record.Headers.Add(MetaController.FakeApiActionHeader, nameof(MetaController.FakeApiAction.Record));
            record.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return record;
        }
    }
}
