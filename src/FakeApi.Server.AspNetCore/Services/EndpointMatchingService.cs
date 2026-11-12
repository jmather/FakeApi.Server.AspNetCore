using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Http;
using FakeApi.Server.AspNetCore.Models;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Services
{
    public class EndpointMatchingService : IEndpointMatchingService
    {
        public async Task<FakeEndpoint> FindEndpointMatchingRequest(User user, HttpRequest request)
        {
            var outcome = await Find(user, request).ConfigureAwait(false);
            if (outcome.Tie)
            {
                throw new MatchingTieException();
            }

            return outcome.Endpoint;
        }

        public async Task<MatchOutcome> Find(User user, HttpRequest request)
        {
            var method = request.Method ?? string.Empty;
            var path = request.Path.HasValue ? request.Path.Value : "/";
            var body = RequestBodyText(request);
            var possible = user.Endpoints.Values
                .Where(endpoint => string.Equals(endpoint.Method.ToString(), method, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(endpoint.Path, path, StringComparison.Ordinal))
                .ToList();

            var best = new List<FakeEndpoint>();
            var bestScore = 0;
            var any = false;

            foreach (var endpoint in possible)
            {
                var scored = Score(request, endpoint, body);
                if (scored.HasValue == false)
                {
                    continue;
                }

                any = true;
                if (best.Count == 0 || scored.Value > bestScore)
                {
                    best = new List<FakeEndpoint> { endpoint };
                    bestScore = scored.Value;
                }
                else if (scored.Value == bestScore)
                {
                    best.Add(endpoint);
                }
            }

            if (any == false || best.Count == 0)
            {
                return new MatchOutcome();
            }

            if (best.Count > 1)
            {
                return new MatchOutcome { Tie = true };
            }

            return new MatchOutcome { Endpoint = best[0] };
        }

        private static int? Score(HttpRequest request, FakeEndpoint endpoint, string body)
        {
            var matches = 0;
            if (endpoint.HasBody)
            {
                if (endpoint.Body != body)
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

            return matches;
        }

        private static int? CountEndpointHeaderMatches(HttpRequest request, FakeEndpoint endpoint)
        {
            if (endpoint.Headers == null || endpoint.Headers.Count == 0)
            {
                return 0;
            }

            var matches = 0;
            foreach (var required in endpoint.Headers)
            {
                var found = false;
                foreach (var header in request.Headers)
                {
                    if (string.Equals(header.Key, required.Key, StringComparison.OrdinalIgnoreCase) == false)
                    {
                        continue;
                    }

                    if (string.Equals(header.Value.ToString(), required.Value, StringComparison.Ordinal) == false)
                    {
                        return null;
                    }

                    found = true;
                    break;
                }

                if (found == false)
                {
                    return null;
                }

                matches++;
            }

            return matches;
        }

        private static int? CountEndpointQueryMatches(HttpRequest request, FakeEndpoint endpoint)
        {
            if (endpoint.QueryParameters == null || endpoint.QueryParameters.Count == 0)
            {
                return 0;
            }

            var matches = 0;
            foreach (var required in endpoint.QueryParameters)
            {
                if (request.Query.TryGetValue(required.Key, out var actual) == false)
                {
                    return null;
                }

                if (string.Equals(actual.ToString(), required.Value, StringComparison.Ordinal) == false)
                {
                    return null;
                }

                matches++;
            }

            return matches;
        }

        private static string RequestBodyText(HttpRequest request)
        {
            if (request.HttpContext.Items.TryGetValue(RequestBodyItem.Key, out var value) && value is byte[] bytes)
            {
                return Encoding.UTF8.GetString(bytes);
            }

            return string.Empty;
        }

        public sealed class MatchOutcome
        {
            public FakeEndpoint Endpoint { get; set; }

            public bool Tie { get; set; }
        }

        public sealed class MatchingTieException : Exception
        {
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