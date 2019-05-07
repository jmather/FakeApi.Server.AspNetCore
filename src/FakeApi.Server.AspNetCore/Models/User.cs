using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace FakeApi.Server.AspNetCore.Models
{
    public class User
    {
        public string Username { get; set; }
        
        public string Password { get; set; }
        
        [JsonIgnore]
        public string ExternalId { get; set; }

        public string ApiToken
        {
            get
            {
                var raw = $"{Username}:{Password}";
                var bytes = System.Text.Encoding.ASCII.GetBytes(raw);
                return System.Convert.ToBase64String(bytes);
            }
        }

        [JsonIgnore]
        public DateTime LastActivityAt = DateTime.Now;

        [JsonIgnore]
        public ConcurrentDictionary<string, Endpoint> Endpoints { get; set; } = new ConcurrentDictionary<string, Endpoint>();

        public bool RecordEndpoint(Endpoint endpoint)
        {
            var hash = endpoint.GetHash();

            return Endpoints.TryAdd(hash, endpoint);
        }
    }
}