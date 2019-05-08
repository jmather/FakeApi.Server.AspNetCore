using System;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace FakeApi.Server.AspNetCore.Models
{
    public class User
    {
        public string Id => Username;
        
        public string Username { get; set; }
        
        public string Password { get; set; }
        
        [JsonIgnore]
        public string ExternalId { get; set; }

        public string AuthToken
        {
            get
            {
                var raw = $"{Username}:{Password}";
                var bytes = System.Text.Encoding.ASCII.GetBytes(raw);
                return Convert.ToBase64String(bytes);
            }
        }

        [JsonIgnore]
        public DateTime LastActivityAt = DateTime.Now;

        [JsonIgnore]
        public ConcurrentDictionary<string, FakeEndpoint> Endpoints { get; set; } = new ConcurrentDictionary<string, FakeEndpoint>();

        public bool RecordEndpoint(FakeEndpoint fakeEndpoint)
        {
            var hash = fakeEndpoint.GetHash();

            return Endpoints.TryAdd(hash, fakeEndpoint);
        }
    }
}