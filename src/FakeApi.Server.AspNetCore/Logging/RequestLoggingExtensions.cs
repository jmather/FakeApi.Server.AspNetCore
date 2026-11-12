using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace FakeApi.Server.AspNetCore.Logging
{
    public static class RequestLoggingExtensions
    {
        public static void AddFakeApiRequestLogging(this WebApplicationBuilder builder)
        {
            builder.Services.Configure<RequestLogOptions>(builder.Configuration.GetSection(RequestLogOptions.SectionName));
            builder.Services.PostConfigure<RequestLogOptions>(options =>
            {
                RequestLogOptions.ApplyOverrides(options, builder.Configuration);
            });
            if (string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_LOG_HOOK"), "prefix", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddSingleton<ILogRedactor, PrefixLogRedactor>();
            }
            else
            {
                builder.Services.AddSingleton<ILogRedactor, IdentityLogRedactor>();
            }

            if (string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_LOG_RECORD_HOOK"), "stamp", StringComparison.OrdinalIgnoreCase))
            {
                builder.Services.AddSingleton<ILogRecordRedactor, StampLogRecordRedactor>();
            }
            else
            {
                builder.Services.AddSingleton<ILogRecordRedactor, IdentityLogRecordRedactor>();
            }

            builder.Services.AddSingleton<IRequestLogSink, RequestLogSink>();
            builder.Logging.AddFilter(RequestLogSink.Category, LogLevel.Information);
            // The default console logger already registers its own "json" formatter.
            // Drop that registration so request lines are the record itself.
            builder.Services.RemoveAll<ConsoleFormatter>();
            builder.Logging.AddConsoleFormatter<RequestLogJsonConsoleFormatter, JsonConsoleFormatterOptions>();
            builder.Services.Configure<ConsoleLoggerOptions>(options =>
            {
                options.FormatterName = ConsoleFormatterNames.Json;
            });
        }

        public static void UseFakeApiRequestLogging(this WebApplication app)
        {
            app.UseMiddleware<RequestLoggingMiddleware>();
        }
    }
}
