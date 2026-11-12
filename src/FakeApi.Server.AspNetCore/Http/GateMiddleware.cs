using System.Linq;
using System.Threading.Tasks;
using FakeApi.Server.AspNetCore.Repositories;
using FakeApi.Server.AspNetCore.Schema;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Http
{
    public sealed class GateMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IUserRepository _users;
        private readonly ServerClock _clock;

        public GateMiddleware(RequestDelegate next, IUserRepository users, ServerClock clock)
        {
            _next = next;
            _users = users;
            _clock = clock;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            ApplyCors(context);

            if (_clock.TryGetNow(context.Request, out var now, out var clockError) == false)
            {
                var validation = new PayloadValidation();
                validation.Errors.Add(new SchemaError("type", "", clockError));
                await PlainResponses.WriteValidation(context, validation).ConfigureAwait(false);
                return;
            }

            context.Items[ServerClock.NowItem] = now;
            if (_clock.Manual)
            {
                _users.Purge(now);
            }

            if (IsPreflight(context.Request))
            {
                var method = context.Request.Headers["Access-Control-Request-Method"].ToString();
                context.Response.Headers["Access-Control-Allow-Methods"] = method;
                if (context.Request.Headers.TryGetValue("Access-Control-Request-Headers", out var requested)
                    && requested.ToString().Length > 0)
                {
                    context.Response.Headers["Access-Control-Allow-Headers"] = requested.ToString();
                }

                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await _next(context).ConfigureAwait(false);

            if (context.Response.StatusCode == StatusCodes.Status404NotFound
                && context.GetEndpoint() == null
                && context.Response.HasStarted == false)
            {
                await PlainResponses.WriteText(context, StatusCodes.Status404NotFound, "Not Found").ConfigureAwait(false);
            }
        }

        private static bool IsPreflight(HttpRequest request)
        {
            return HttpMethods.IsOptions(request.Method)
                && request.Headers.ContainsKey("Origin")
                && request.Headers.ContainsKey("Access-Control-Request-Method");
        }

        private static void ApplyCors(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("Origin", out var origin) == false)
            {
                return;
            }

            context.Response.Headers["Access-Control-Allow-Origin"] = origin.ToString();
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
            var vary = context.Response.Headers.Vary.ToString();
            if (string.IsNullOrEmpty(vary))
            {
                context.Response.Headers["Vary"] = "Origin";
                return;
            }

            var listed = vary.Split(',').Select(part => part.Trim());
            if (listed.Contains("Origin") == false)
            {
                context.Response.Headers["Vary"] = vary + ", Origin";
            }
        }
    }
}
