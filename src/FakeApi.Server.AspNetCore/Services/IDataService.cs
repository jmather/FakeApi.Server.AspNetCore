using System.Security.Claims;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public interface IDataService
    {
        User Register(UserInfo userInfo);
        
        bool RecordEndpoint(string username, FakeEndpoint endpoint);

        Task<FakeEndpointResponse> GetEndpointResponse(string username, HttpRequest request);
    }
    
}