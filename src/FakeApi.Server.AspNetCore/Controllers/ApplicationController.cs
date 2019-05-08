using System;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Exceptions;
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
                var response = await _dataService.GetEndpointResponse(User.Identity.Name, Request);

                return response == null ? NotFound() : EndpointResponse(response);
            }
            catch (MultipleMatchException e)
            {
                return Error(e);
            }
        }

        private static IActionResult Error(Exception e)
        {
            var details = new ErrorDetails
            {
                Message = e.Message,
                Data = e.Data,
            };

            return new ObjectResult(details)
            {
                StatusCode = 500,
            };
        }

        private IActionResult EndpointResponse(FakeEndpointResponse response)
        {
            foreach (var (key, value) in response.Headers)
            {
                HttpContext.Response.Headers.Add(key, value);
            }

            return new ContentResult
            {
                StatusCode = response.Status,
                Content = response.Content,
                ContentType = response.ContentType,
            };
        }
    }
}