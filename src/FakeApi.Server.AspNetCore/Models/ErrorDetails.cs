using System.Collections;

namespace FakeApi.Server.AspNetCore.Models
{
    public class ErrorDetails
    {
        public string Message { get; set; }
        
        public IDictionary Data { get; set; }
    }
}