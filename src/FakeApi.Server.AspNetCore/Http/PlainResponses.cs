using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Schema;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Http
{
    public static class PlainResponses
    {
        public const string PlainContentType = "text/plain; charset=utf-8";

        public const string JsonContentType = "application/json; charset=utf-8";

        public static async Task WriteText(HttpContext context, int status, string body)
        {
            context.Response.StatusCode = status;
            context.Response.Headers["Content-Type"] = PlainContentType;
            await context.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(body)).ConfigureAwait(false);
        }

        public static async Task WriteJson(HttpContext context, int status, byte[] json)
        {
            context.Response.StatusCode = status;
            context.Response.Headers["Content-Type"] = JsonContentType;
            await context.Response.Body.WriteAsync(json).ConfigureAwait(false);
        }

        public static async Task WriteValidation(HttpContext context, PayloadValidation validation)
        {
            using (var stream = new System.IO.MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("message", validation.TopMessage);
                    writer.WritePropertyName("errors");
                    writer.WriteStartArray();
                    foreach (var error in validation.Errors)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("keyword", error.Keyword);
                        writer.WriteString("path", error.Path);
                        writer.WriteString("message", error.Message);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                await WriteJson(context, StatusCodes.Status400BadRequest, stream.ToArray()).ConfigureAwait(false);
            }
        }
    }
}
