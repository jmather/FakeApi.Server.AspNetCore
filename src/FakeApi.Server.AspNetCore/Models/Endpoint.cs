using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace FakeApi.Server.AspNetCore.Models
{
    public class Endpoint
    {
        [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
        public EndpointMethod Method { get; set; } = EndpointMethod.Get;
        
        public string Path { get; set; }
        
        public string Body { get; set; }
        
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        
        public Dictionary<string, string> QueryParameters { get; set; } = new Dictionary<string, string>();

        [JsonConverter(typeof(StringEnumConverter), typeof(SnakeCaseNamingStrategy))]
        public ResponseMode ResponseMode { get; set; } = ResponseMode.Incremental;
        
        public List<EndpointResponse> Responses { get; set; } = new List<EndpointResponse>();

        [JsonIgnore]
        public int ResponseIndex = 0;

        public string GetHash()
        {
            var pieces = new List<string>
            {
                Method.ToString().ToLower(),
                Path,
                Body,
                JsonConvert.SerializeObject(Headers),
                JsonConvert.SerializeObject(QueryParameters),
            };

            return string.Join("-", pieces);
        }
    }
}