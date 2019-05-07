using System.Collections.Generic;

namespace FakeApi.Server.AspNetCore.Models
{
    public class EndpointResponse
    {
        public int Status { get; set; } = 200;

        public string Content { get; set; } = "Hello World!";

        public string ContentType { get; set; } = "text/plain";
        
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();

        public int Delay { get; set; } = 0;
    }
}