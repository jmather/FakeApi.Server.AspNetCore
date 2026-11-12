using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FakeApi.Server.AspNetCore.Logging
{
    public sealed class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IRequestLogSink _sink;
        private readonly IOptions<RequestLogOptions> _options;

        public RequestLoggingMiddleware(RequestDelegate next, IRequestLogSink sink, IOptions<RequestLogOptions> options)
        {
            _next = next;
            _sink = sink;
            _options = options;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var requestId = System.Guid.NewGuid().ToString("D");
            context.Response.Headers["X-Request-Id"] = requestId;

            var capture = RequestLogOptions.ParseCapture(_options.Value.LogCapture);
            var captureBodies = capture == LogCaptureMode.All;
            var started = Stopwatch.StartNew();

            string requestBody = null;
            if (captureBodies)
            {
                context.Request.EnableBuffering();
                requestBody = await RequestLogBody.ReadCappedAsync(context.Request.Body).ConfigureAwait(false);
                context.Request.Body.Position = 0;
            }

            var originalResponseBody = context.Response.Body;
            MemoryStream responseBuffer = null;
            if (captureBodies)
            {
                responseBuffer = new MemoryStream();
                context.Response.Body = responseBuffer;
            }

            try
            {
                await _next(context).ConfigureAwait(false);
            }
            finally
            {
                string responseBody = null;
                if (responseBuffer != null)
                {
                    try
                    {
                        context.Response.Body = originalResponseBody;
                        responseBuffer.Position = 0;
                        responseBody = await RequestLogBody.ReadCappedAsync(responseBuffer).ConfigureAwait(false);
                        responseBuffer.Position = 0;
                        await responseBuffer.CopyToAsync(originalResponseBody).ConfigureAwait(false);
                    }
                    finally
                    {
                        await responseBuffer.DisposeAsync().ConfigureAwait(false);
                    }
                }

                started.Stop();
                var record = RequestLogFactory.Create(
                    context,
                    requestId,
                    started.Elapsed,
                    capture,
                    requestBody,
                    responseBody);
                _sink.Write(record);
            }
        }
    }
}
