using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Controllers;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FakeApi.Server.AspNetCore.Test.Integration
{
    public class IntegratedTest : IClassFixture<WebApplicationFactory<Startup>>
    {
        private readonly WebApplicationFactory<Startup> _factory;

        public IntegratedTest(WebApplicationFactory<Startup> factory)
        {
            _factory = factory;
        }

        [Fact]
        public void TestThatWeGotTheEndpoints()
        {
            var collection = GetTestCollection();
            
            Assert.True(collection.Endpoints.ContainsKey("bad_hello_world"));
        }

        [Fact]
        public async Task TestRegister()
        {
            // Arrange
            var client = _factory.CreateClient();

            var msg = RegisterRequest();
                
            // Act
            var response = await client.SendAsync(msg);
            
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var payload = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());
            
            Assert.True(payload.ContainsKey("username"));
            Assert.True(payload.ContainsKey("password"));
            Assert.True(payload.ContainsKey("auth_token"));
        }
        
        [Theory]
        [MemberData(nameof(IncrementalEndpoints))]
        public async Task TestIncrementalEndpointsIndividually(string name, FakeEndpoint definition)
        {
            // Arrange
            var client = await GetAuthToken();

            Assert.True(await RegisterEndpoint(client, definition), name);

            var msg = GetRequestMessage(definition);
            
            // Act
            var response = await client.SendAsync(msg);

            var resp = definition.Responses.First();
            
            Assert.True(await DoesMatchResponse(resp, response));
        }

        [Theory]
        [MemberData(nameof(RandomEndpoints))]
        public async Task TestRandomEndpointsIndividually(string name, FakeEndpoint definition)
        {
            // Arrange
            var client = await GetAuthToken();

            Assert.True(await RegisterEndpoint(client, definition), name);
            
            String lastContent = null;
            var unmatchCount = 0;

            foreach (var expected in definition.Responses)
            {
                var msg = GetRequestMessage(definition);
                var response = await client.SendAsync(msg);
                
                if (await DoesMatchResponse(expected, response))
                {
                    unmatchCount++;
                }
            }

            Assert.True(unmatchCount < definition.Responses.Count);
        }

        [Fact]
        public async Task TestEndpointsTogether()
        {
            var endpoints = IncrementalEndpoints().Select(i =>
            {
                var l = i.ToList();
                return KeyValuePair.Create((string) l[0], (FakeEndpoint) l[1]);
            }).ToList();
            
            // Arrange
            var client = await GetAuthToken();

            foreach (var (name, endpoint) in endpoints)
            {
                Debug.WriteLine($"Adding {name}");
                Assert.True(await RegisterEndpoint(client, endpoint), name);
            }

            foreach (var (name, endpoint) in endpoints)
            {
                var msg = GetRequestMessage(endpoint);
            
                // Act
                var response = await client.SendAsync(msg);

                var resp = endpoint.Responses.First();
            
                Assert.Equal(resp.Status, (int) response.StatusCode);
                Assert.Equal(resp.Content, await response.Content.ReadAsStringAsync());
            }
        }

        private HttpRequestMessage GetRequestMessage(FakeEndpoint endpoint)
        {
            var method = new HttpMethod(endpoint.Method.ToString());

            var queryVars = endpoint.QueryParameters
                .Select((kv) => $"{kv.Key}={kv.Value}")
                .ToList();

            var query = string.Join("&", queryVars);

            var path = string.IsNullOrWhiteSpace(query) ? endpoint.Path : endpoint.Path + '?' + query; 
                
            var msg = new HttpRequestMessage(method, path);

            if (endpoint.Body != null)
            {
                msg.Content = new StringContent(endpoint.Body, Encoding.UTF8, endpoint.ContentType);
            }
            
            foreach (var (hName, hVal) in endpoint.Headers)
            {
                msg.Headers.Add(hName, hVal);
            }

            return msg;
        }

        private async Task<bool> DoesMatchResponse(FakeEndpointResponse expected, HttpResponseMessage response)
        {
            if (expected.Status != (int) response.StatusCode)
            {
                return false;
            }
            
            var responseContent = await response.Content.ReadAsStringAsync();
            if (expected.Content != responseContent)
            {
                return false;
            }

            return expected.ContentType == response.Content.Headers.ContentType.MediaType;
        }

        private async Task<bool> RegisterEndpoint(HttpClient client, FakeEndpoint endpoint)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };

            var content = JsonConvert.SerializeObject(endpoint, settings);
            
            var msg = new HttpRequestMessage(HttpMethod.Put, "/")
            {
                Content = new StringContent(content, Encoding.UTF8,"application/json"),
            };
            
            msg.Headers.Add(MetaController.FakeApiActionHeader, nameof(MetaController.FakeApiAction.Record));

            var response = await client.SendAsync(msg);

            return response.StatusCode == HttpStatusCode.Created;
        }

        private async Task<HttpClient> GetAuthToken()
        {
            var client = _factory.CreateClient();

            var msg = RegisterRequest();
                
            // Act
            var response = await client.SendAsync(msg);
            
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var payload = JsonConvert.DeserializeObject<Dictionary<string, string>>(await response.Content.ReadAsStringAsync());

            payload.TryGetValue("auth_token", out var authToken);

            client.DefaultRequestHeaders.Add("Authorization", "Basic " + authToken);

            return client;
        }
        
        private static HttpRequestMessage RegisterRequest()
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/");

            msg.Headers.Add(MetaController.FakeApiActionHeader, nameof(MetaController.FakeApiAction.Register));

            var userInfo = new UserInfo
            {
                ExternalId = "test"
            };

            var payload = JsonConvert.SerializeObject(userInfo);
            
            msg.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            return msg;
        }

        public static IEnumerable<object[]> IncrementalEndpoints()
        {
            var collection = GetTestCollection();

            var resp = collection.Endpoints
                .Where(kv => kv.Key.StartsWith("bad_") == false)
                .Where(kv => kv.Value.ResponseMode == ResponseMode.Incremental)
                .Select(kv => new object[] {kv.Key, kv.Value})
                .ToList();

            return resp;
        }
        
        public static IEnumerable<object[]> RandomEndpoints()
        {
            var collection = GetTestCollection();

            var resp = collection.Endpoints
                .Where(kv => kv.Key.StartsWith("bad_") == false)
                .Where(kv => kv.Value.ResponseMode == ResponseMode.Random)
                .Select(kv => new object[] {kv.Key, kv.Value})
                .ToList();

            return resp;
        }
        
        private static FakeEndpointCollection GetTestCollection()
        {
            var content = File.ReadAllText("./Integration/test-endpoints.collection.yaml");
            var input = new StringReader(content);
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(new UnderscoredNamingConvention())
                .Build();

            return deserializer.Deserialize<FakeEndpointCollection>(input);
        }
    }
}