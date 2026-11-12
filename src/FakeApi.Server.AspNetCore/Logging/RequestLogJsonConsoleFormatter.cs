using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace FakeApi.Server.AspNetCore.Logging
{
    /// <summary>
    /// Console formatter named "json". Request logs are already one JSON object per line
    /// after the redaction hook, so those lines are written unchanged. Every other category
    /// is passed to the framework JSON console formatter.
    /// </summary>
    public sealed class RequestLogJsonConsoleFormatter : ConsoleFormatter
    {
        private readonly ConsoleFormatter _inner;

        public RequestLogJsonConsoleFormatter(IOptionsMonitor<JsonConsoleFormatterOptions> options)
            : base(ConsoleFormatterNames.Json)
        {
            var formatterType = Type.GetType(
                "Microsoft.Extensions.Logging.Console.JsonConsoleFormatter, Microsoft.Extensions.Logging.Console",
                throwOnError: true);
            _inner = (ConsoleFormatter)Activator.CreateInstance(formatterType, options);
        }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider scopeProvider, TextWriter textWriter)
        {
            if (logEntry.Category == RequestLogSink.Category)
            {
                var message = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
                if (string.IsNullOrEmpty(message) == false)
                {
                    textWriter.WriteLine(message);
                }

                return;
            }

            _inner.Write(logEntry, scopeProvider, textWriter);
        }
    }
}
