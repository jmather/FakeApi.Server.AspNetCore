using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FakeApi.Server.AspNetCore.Controllers
{
    [ApiController]
    public class ApplicationController : ControllerBase
    {
        private readonly IDataService _dataService;
        
        public ApplicationController(IDataService dataService)
        {
            _dataService = dataService;
        }
        
        [HttpGet("/{*path}")]
        [HttpPost("/{*path}")]
        [HttpPut("/{*path}")]
        [HttpPatch("/{*path}")]
        [HttpDelete("/{*path}")]
        [HttpHead("/{*path}")]
        [HttpOptions("/{*path}")]
        [Authorize]
        public async Task<IActionResult> HandleRequest()
        {
            try
            {
                var response = await _dataService.GetEndpointResponse(User.Identity?.Name, Request);

                return response == null
                    ? Text(StatusCodes.Status404NotFound, "Not Found")
                    : EndpointResponse(response);
            }
            catch (EndpointMatchingService.MatchingTieException)
            {
                return Text(StatusCodes.Status409Conflict, "Conflict");
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

        private IActionResult EndpointResponse(FakeEndpointResponse response)
        {
            if (response.Headers != null)
            {
                foreach (var header in response.Headers)
                {
                    if (IsHopHeader(header.Key))
                    {
                        continue;
                    }

                    HttpContext.Response.Headers[header.Key] = header.Value;
                }
            }

            return new ContentResult
            {
                StatusCode = response.Status,
                Content = response.Content,
                ContentType = response.ContentType,
            };
        }

        private static bool IsHopHeader(string name)
        {
            return string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "transfer-encoding", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase);
        }
    }
}