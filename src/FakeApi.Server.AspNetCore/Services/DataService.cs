using System;
using System.Security.Claims;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Repositories;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public class DataService : IDataService
    {
        private readonly IUserRepository _userRepository;
        private readonly IEndpointMatchingService _endpointMatcher;

        public DataService(IUserRepository userRepository, IEndpointMatchingService endpointMatcher)
        {
            _userRepository = userRepository;
            _endpointMatcher = endpointMatcher;
        }

        public User Register(UserInfo userInfo)
        {
            return _userRepository.Register(userInfo);
        }

        public bool RecordEndpoint(string username, FakeEndpoint endpoint)
        {
            var user = _userRepository.Get(username);

            return user != null && user.RecordEndpoint(endpoint);
        }
        
        public async Task<FakeEndpointResponse> GetEndpointResponse(string username, HttpRequest request)
        {
            var user = _userRepository.Get(username);

            if (user == null)
            {
                return null;
            }

            var endpoint = await _endpointMatcher.FindEndpointMatchingRequest(user, request);

            if (endpoint == null)
            {
                return null;
            }
            
            if (endpoint.ResponseMode == ResponseMode.Random && endpoint.Responses.Count > 1)
            {
                var random = new Random();
                endpoint.ResponseIndex = random.Next(0, endpoint.Responses.Count - 1);
            }

            var response = endpoint.Responses[endpoint.ResponseIndex];

            if (endpoint.ResponseMode == ResponseMode.Incremental)
            {
                endpoint.ResponseIndex++;
            }

            if (endpoint.ResponseIndex >= endpoint.Responses.Count)
            {
                endpoint.ResponseIndex = 0;
            }

            await Task.Delay(response.Delay);

            return response;
        }
    }
}