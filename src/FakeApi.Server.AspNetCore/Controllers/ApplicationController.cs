using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Extensions;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;
using Microsoft.AspNetCore.Mvc;
using Endpoint = FakeApi.Server.AspNetCore.Models.Endpoint;

namespace FakeApi.Server.AspNetCore.Controllers
{
    [ApiController]
    public class ApplicationController : FakeApiControllerBase
    {
        private string _requestBody = null;
        
        public ApplicationController(UserManager userManager) : base(userManager)
        {
        }
        
        [HttpGet("/{*path}")]
        [HttpPost("/{*path}")]
        [HttpPut("/{*path}")]
        [HttpPatch("/{*path}")]
        [HttpDelete("/{*path}")]
        [HttpHead("/{*path}")]
        [HttpOptions("/{*path}")]
        public async Task<IActionResult> HandleRequest()
        {
            var user = GetRequestUser();

            if (user == null)
            {
                return Unauthorized();
            }

            try
            {
                var endpoint = await FindEndpointMatchingRequest(user);

                if (endpoint == null)
                {
                    return NotFound();
                }
                
                var response = await GetNextResponse(endpoint);

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
            catch (MultipleMatchException e)
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
        }
        
        private async Task<EndpointResponse> GetNextResponse(Endpoint endpoint)
        {
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

        private async Task<Endpoint> FindEndpointMatchingRequest(User user)
        {
            var partialHash = HttpContext.Request.GetPartialEndpointHash();

            var possibleEndpoints = user.Endpoints
                .Where(pair => pair.Key.StartsWith(partialHash))
                .Select(pair => pair.Value)
                .ToList();

            var matchingEndpoints = new List<ScoredEndpoint>();
            var bestMatchEndpoints = new List<Endpoint>();
            var bestMatchCount = 0;

            foreach (var endpoint in possibleEndpoints)
            {
                var scoredEndpoint = await CountEndpointMatches(endpoint);

                if (scoredEndpoint == null)
                {
                    continue;
                }

                matchingEndpoints.Add(scoredEndpoint);

                if (bestMatchCount == scoredEndpoint.Score)
                {
                    bestMatchEndpoints.Add(endpoint);
                }
                else if (bestMatchCount < scoredEndpoint.Score)
                {
                    bestMatchEndpoints = new List<Endpoint> { endpoint };
                    bestMatchCount = scoredEndpoint.Score;
                }
                else if (bestMatchEndpoints.Count == 0)
                {
                    bestMatchEndpoints.Add(endpoint);
                    bestMatchCount = scoredEndpoint.Score;
                }
            }

            if (bestMatchEndpoints.Count == 1)
            {
                return bestMatchEndpoints.First();
            }
            
            if (bestMatchEndpoints.Count > 1)
            {
                throw new MultipleMatchException(bestMatchEndpoints, bestMatchCount, matchingEndpoints);
            }

            return null;
        }

        private async Task<ScoredEndpoint> CountEndpointMatches(Endpoint endpoint)
        {
            var matches = 0;
                
            if (endpoint.Body != null)
            {
                if (endpoint.Body != await GetRequestBody())
                {
                    return null;
                }

                matches++;
            }

            var headerMatches = CountEndpointHeaderMatches(endpoint);
                
            if (headerMatches.HasValue == false)
            {
                return null;
            }

            matches += headerMatches.Value;

            var queryMatches = CountEndpointQueryMatches(endpoint);

            if (queryMatches.HasValue == false)
            {
                return null;
            }

            matches += queryMatches.Value;

            return new ScoredEndpoint(endpoint, matches);
        }

        private int? CountEndpointHeaderMatches(Endpoint endpoint)
        {
            var normalizedHeaders = HttpContext.Request.Headers
                .ToDictionary(k => k.Key.ToLower(), v => v.Value.ToString());

            return normalizedHeaders.CountMatches(endpoint.Headers);
        }

        private int? CountEndpointQueryMatches(Endpoint endpoint)
        {
            var queryParams = HttpContext.Request.Query
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            return queryParams.CountMatches(endpoint.QueryParameters);
        }

        private async Task<string> GetRequestBody()
        {
            if (_requestBody != null)
            {
                return _requestBody;
            }

            return _requestBody = await HttpContext.Request.GetRawBodyStringAsync();
        }

        public class ScoredEndpoint
        {
            public Endpoint Endpoint { get; }
            
            public int Score { get; }

            public ScoredEndpoint(Endpoint endpoint, int score)
            {
                Endpoint = endpoint;
                Score = score;
            }
        }

        public sealed class MultipleMatchException : Exception
        {
            public MultipleMatchException(List<Endpoint> bestMatches, int bestMatchCount,
                List<ScoredEndpoint> scoredEndpoints) : base("Multiple possible matches were found.")
            {
                scoredEndpoints.Sort((a, b) => a.Score.CompareTo(b.Score));
                    
                Data.Add("BestMatches", bestMatches);
                Data.Add("BestMatchCount", bestMatchCount);
                Data.Add("AllMatches", scoredEndpoints);
            }
        }
    }
}