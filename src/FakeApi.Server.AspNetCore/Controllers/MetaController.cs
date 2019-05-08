using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FakeApi.Server.AspNetCore.Controllers
{
    [ApiController]
    public class MetaController : ControllerBase
    {
        public const string FakeApiActionHeader = "X-FakeApi-Action";

        private readonly IDataService _dataService;

        public MetaController(IDataService dataService)
        {
            _dataService = dataService;
        }
        
        [HttpPost("/")]
        [RequiredHeader(FakeApiActionHeader, nameof(FakeApiAction.Register))]
        public IActionResult Register([FromBody] UserInfo userInfo)
        {
            if (userInfo == null)
            {
                return BadRequest("No User Information Provided");
            }

            var user = _dataService.Register(userInfo);
            
            return Created(Request.GetDisplayUrl(), user);
        }

        [HttpPut("/")]
        [RequiredHeader(FakeApiActionHeader, nameof(FakeApiAction.Record))]
        [Authorize]
        public IActionResult Record([FromBody] FakeEndpoint endpoint)
        {
            if (endpoint == null)
            {
                return BadRequest("No Endpoint Provided");
            }
            
            if (_dataService.RecordEndpoint(User, endpoint))
            {
                return Created(Request.GetDisplayUrl(), endpoint);
            }

            return Conflict("Matching endpoint already recorded.");
        }

        public enum FakeApiAction
        {
            Register,
            Record
        }
    }
}