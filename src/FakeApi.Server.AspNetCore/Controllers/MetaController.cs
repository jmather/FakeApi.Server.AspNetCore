using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Schema;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FakeApi.Server.AspNetCore.Controllers
{
    public class MetaController : ControllerBase
    {
        public const string FakeApiActionHeader = "X-FakeApi-Action";

        private static readonly JsonSerializerOptions EchoOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private readonly IDataService _dataService;

        public MetaController(IDataService dataService)
        {
            _dataService = dataService;
        }
        
        [HttpPost("/")]
        [RequiredHeader(FakeApiActionHeader, nameof(FakeApiAction.Register))]
        public async Task<IActionResult> Register()
        {
            var validation = PayloadValidator.Shared.Validate(ReadBody(), "UserInfo");
            if (validation.Errors.Count > 0)
            {
                await PlainResponses.WriteValidation(HttpContext, validation).ConfigureAwait(false);
                return new EmptyResult();
            }

            var externalId = validation.Document["external_id"]?.GetValue<string>() ?? string.Empty;
            var user = _dataService.Register(new UserInfo { ExternalId = externalId }, ServerClock.Current(HttpContext));
            Response.Headers.Location = Request.GetDisplayUrl();
            await PlainResponses.WriteJson(HttpContext, StatusCodes.Status201Created, CredentialJson(user)).ConfigureAwait(false);
            return new EmptyResult();
        }

        [HttpPut("/")]
        [RequiredHeader(FakeApiActionHeader, nameof(FakeApiAction.Record))]
        [Authorize]
        public async Task<IActionResult> Record()
        {
            var validation = PayloadValidator.Shared.Validate(ReadBody(), "Endpoint");
            if (validation.Errors.Count > 0)
            {
                await PlainResponses.WriteValidation(HttpContext, validation).ConfigureAwait(false);
                return new EmptyResult();
            }

            var endpoint = EndpointMapper.FromDocument(validation.Document.AsObject());
            if (_dataService.RecordEndpoint(User.Identity.Name, endpoint) == false)
            {
                return Text(StatusCodes.Status409Conflict, "Conflict");
            }

            Response.Headers.Location = Request.GetDisplayUrl();
            var echo = validation.Document.ToJsonString(EchoOptions);
            await PlainResponses.WriteJson(HttpContext, StatusCodes.Status201Created, Encoding.UTF8.GetBytes(echo)).ConfigureAwait(false);
            return new EmptyResult();
        }

        private byte[] ReadBody()
        {
            if (HttpContext.Items.TryGetValue(RequestBodyItem.Key, out var value) && value is byte[] bytes)
            {
                return bytes;
            }

            return Array.Empty<byte>();
        }

        private static byte[] CredentialJson(User user)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("username", user.Username);
                    writer.WriteString("password", user.Password);
                    writer.WriteString("auth_token", user.AuthToken);
                    writer.WriteEndObject();
                }

                return stream.ToArray();
            }
        }

        private static ContentResult Text(int status, string body)
        {
            return new ContentResult
            {
                StatusCode = status,
                Content = body,
                ContentType = PlainResponses.PlainContentType,
            };
        }

        public enum FakeApiAction
        {
            Register,
            Record
        }
    }

    public static class EndpointMapper
    {
        public static FakeEndpoint FromDocument(JsonObject document)
        {
            var endpoint = new FakeEndpoint
            {
                Method = Enum.Parse<FakeEndpointMethod>(document["method"].GetValue<string>(), true),
                Path = document["path"].GetValue<string>(),
                ResponseMode = Enum.Parse<ResponseMode>(document["response_mode"].GetValue<string>(), true),
                Headers = ReadMap(document["headers"] as JsonObject),
                QueryParameters = ReadMap(document["query_parameters"] as JsonObject),
                Responses = new List<FakeEndpointResponse>(),
            };

            if (document.ContainsKey("body"))
            {
                endpoint.HasBody = true;
                endpoint.Body = document["body"] == null ? null : document["body"].GetValue<string>();
            }

            foreach (var item in document["responses"].AsArray())
            {
                var response = item.AsObject();
                var mapped = new FakeEndpointResponse
                {
                    Status = response["status"].GetValue<int>(),
                    Content = response["content"].GetValue<string>(),
                    ContentType = response["content_type"].GetValue<string>(),
                    Delay = response["delay"].GetValue<int>(),
                    Headers = ReadMap(response["headers"] as JsonObject),
                };
                endpoint.Responses.Add(mapped);
            }

            return endpoint;
        }

        private static Dictionary<string, string> ReadMap(JsonObject source)
        {
            var map = new Dictionary<string, string>();
            if (source == null)
            {
                return map;
            }

            foreach (var pair in source)
            {
                map[pair.Key] = pair.Value == null ? null : pair.Value.GetValue<string>();
            }

            return map;
        }
    }
}
