using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using JMather.RoutingHelpers.AspNetCore.Annotations;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace FakeApi.Server.AspNetCore.Controllers
{
    [ApiController]
    public class MetaController : FakeApiControllerBase
    {
        public MetaController(UserManager userManager) : base(userManager)
        {
        }
        
        [HttpPost("/")]
        [RequiredHeader(name: "X-FakeApi-Action", allowedValue: nameof(FakeApiAction.Register))]
        public IActionResult Register([FromBody] UserInfo userInfo)
        {
            if (userInfo == null)
            {
                return BadRequest("No User Information Provided");
            }

            var user = UserManager.Register(userInfo);
            
            return Created(HttpContext.Request.GetDisplayUrl(), user);
        }

        [HttpPut("/")]
        [RequiredHeader(name: "X-FakeApi-Action", allowedValue: nameof(FakeApiAction.Record))]
        public IActionResult Record([FromBody] Endpoint endpoint)
        {
            if (endpoint == null)
            {
                return BadRequest("No Endpoint Provided");
            }
            
            var user = GetRequestUser();

            if (user == null)
            {
                return Unauthorized();
            }

            if (user.RecordEndpoint(endpoint))
            {
                return Created(HttpContext.Request.GetDisplayUrl(), endpoint);
            }

            return Conflict("Matching endpoint already recorded.");
        }
    }
}