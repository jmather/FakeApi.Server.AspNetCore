using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FakeApi.Server.AspNetCore.Models
{
    public class FakeEndpoint
    {
        [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
        public FakeEndpointMethod Method { get; set; } = FakeEndpointMethod.Get;
        
        public string Path { get; set; }
        
        public string Body { get; set; }
        
        [JsonIgnore]
        public string ContentType { get; set; }
        
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        
        public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();

        [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
        public ResponseMode ResponseMode { get; set; } = ResponseMode.Incremental;
        
        public List<FakeEndpointResponse> Responses { get; set; } = new List<FakeEndpointResponse>();

        [JsonIgnore]
        public int ResponseIndex { get; set; }

        [JsonIgnore]
        public bool HasBody { get; set; }

        public string IdentityKey()
        {
            var headers = string.Join(
                "\n",
                Headers
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Key.ToLowerInvariant() + "\u001f" + pair.Value));
            var query = string.Join(
                "\n",
                QueryParameters
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => pair.Key + "\u001f" + pair.Value));
            var body = HasBody ? "b:" + (Body ?? string.Empty) : "null";
            return string.Join(
                "\u001e",
                Method.ToString().ToLowerInvariant(),
                Path ?? string.Empty,
                body,
                headers,
                query);
        }
    }
}