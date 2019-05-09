using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using FakeApi.Server.AspNetCore.Models;
using FakeApi.Server.AspNetCore.Services;

namespace FakeApi.Server.AspNetCore.Exceptions
{
    [Serializable]
    public sealed class MultipleMatchException : Exception
    {
        [NonSerialized]
        public const string MessageText = "Multiple possible matches were found.";
        
        public MultipleMatchException(List<FakeEndpoint> bestMatches, int bestMatchCount,
            List<EndpointMatchingService.ScoredEndpoint> scoredEndpoints) : base(MessageText)
        {
            scoredEndpoints.Sort((a, b) => a.Score.CompareTo(b.Score));
                    
            Data.Add("BestMatches", bestMatches);
            Data.Add("BestMatchCount", bestMatchCount);
            Data.Add("AllMatches", scoredEndpoints);
        }

        private MultipleMatchException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}