using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public interface IEndpointMatchingService
    {
        Task<FakeEndpoint> FindEndpointMatchingRequest(User user, HttpRequest request);
    }
}