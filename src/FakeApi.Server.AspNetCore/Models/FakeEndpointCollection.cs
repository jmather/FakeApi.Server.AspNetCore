using System.Collections.Generic;

namespace FakeApi.Server.AspNetCore.Models
{
    public class FakeEndpointCollection
    {
        public Dictionary<string, FakeEndpoint> Endpoints { get; set; }
    }
}