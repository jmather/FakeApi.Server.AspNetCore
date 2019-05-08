using System.Security.Claims;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public interface IDataService
    {
        User Register(UserInfo userInfo);
        
        bool RecordEndpoint(ClaimsPrincipal principal, FakeEndpoint endpoint);

        Task<FakeEndpointResponse> GetEndpointResponse(ClaimsPrincipal principal, HttpRequest request);
    }
    
}