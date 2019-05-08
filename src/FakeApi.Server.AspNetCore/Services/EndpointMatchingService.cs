using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Exceptions;
using FakeApi.Server.AspNetCore.Extensions;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public class EndpointMatchingService : IEndpointMatchingService
    {
        public async Task<FakeEndpoint> FindEndpointMatchingRequest(User user, HttpRequest request)
        {
            var partialHash = request.GetPartialEndpointHash();

            var possibleEndpoints = user.Endpoints
                .Where(pair => pair.Key.StartsWith(partialHash))
                .Select(pair => pair.Value)
                .ToList();

            var matchingEndpoints = new List<ScoredEndpoint>();
            var bestMatchEndpoints = new List<FakeEndpoint>();
            var bestMatchCount = 0;

            foreach (var endpoint in possibleEndpoints)
            {
                var scoredEndpoint = await CountEndpointMatches(request, endpoint);

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
                    bestMatchEndpoints = new List<FakeEndpoint> { endpoint };
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
        
        private async Task<ScoredEndpoint> CountEndpointMatches(HttpRequest request, FakeEndpoint endpoint)
        {
            var matches = 0;
                
            if (endpoint.Body != null)
            {
                if (endpoint.Body != await GetRequestBody(request))
                {
                    return null;
                }

                matches++;
            }

            var headerMatches = CountEndpointHeaderMatches(request, endpoint);
                
            if (headerMatches.HasValue == false)
            {
                return null;
            }

            matches += headerMatches.Value;

            var queryMatches = CountEndpointQueryMatches(request, endpoint);

            if (queryMatches.HasValue == false)
            {
                return null;
            }

            matches += queryMatches.Value;

            return new ScoredEndpoint(endpoint, matches);
        }

        private int? CountEndpointHeaderMatches(HttpRequest request, FakeEndpoint endpoint)
        {
            var normalizedHeaders = request.Headers
                .ToDictionary(k => k.Key.ToLower(), v => v.Value.ToString());

            return normalizedHeaders.CountMatches(endpoint.Headers);
        }

        private int? CountEndpointQueryMatches(HttpRequest request, FakeEndpoint endpoint)
        {
            var queryParams = request.Query
                .ToDictionary(k => k.Key, v => v.Value.ToString());

            return queryParams.CountMatches(endpoint.QueryParameters);
        }

        private async Task<string> GetRequestBody(HttpRequest request)
        {
            return await request.GetRawBodyStringAsync();
        }

        public class ScoredEndpoint
        {
            public FakeEndpoint Endpoint { get; }
            
            public int Score { get; }

            public ScoredEndpoint(FakeEndpoint endpoint, int score)
            {
                Endpoint = endpoint;
                Score = score;
            }
        }
    }
}