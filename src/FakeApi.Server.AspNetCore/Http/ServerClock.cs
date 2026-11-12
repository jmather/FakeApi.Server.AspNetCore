using System;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace FakeApi.Server.AspNetCore.Http
{
    public sealed class ServerClock
    {
        public const string NowItem = "FakeApi.NowMs";

        private static readonly Regex NowPattern = new Regex("^-?(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

        public ServerClock(bool manual, long maxIdleMs, long purgeIntervalMs)
        {
            Manual = manual;
            MaxIdleMs = maxIdleMs < 0 ? 0 : maxIdleMs;
            PurgeIntervalMs = purgeIntervalMs < 1 ? 1 : purgeIntervalMs;
        }

        public bool Manual { get; }

        public long MaxIdleMs { get; }

        public long PurgeIntervalMs { get; }

        public static ServerClock FromEnvironment()
        {
            var manual = string.Equals(Environment.GetEnvironmentVariable("FAKEAPI_CLOCK"), "manual", StringComparison.OrdinalIgnoreCase);
            var maxIdle = ParseLong(Environment.GetEnvironmentVariable("FAKEAPI_MAX_IDLE_MS"), 7200000);
            var purge = ParseLong(Environment.GetEnvironmentVariable("FAKEAPI_PURGE_INTERVAL_MS"), 900000);
            return new ServerClock(manual, maxIdle, purge);
        }

        public bool TryGetNow(HttpRequest request, out long nowMs, out string error)
        {
            if (Manual == false || request.Headers.TryGetValue("X-FakeApi-Now", out var supplied) == false)
            {
                nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                error = null;
                return true;
            }

            var text = supplied.ToString().Trim();
            if (NowPattern.IsMatch(text) && long.TryParse(text, out nowMs))
            {
                error = null;
                return true;
            }

            nowMs = 0;
            error = "X-FakeApi-Now must be an integer number of milliseconds.";
            return false;
        }

        public static long Current(HttpContext context)
        {
            if (context.Items.TryGetValue(NowItem, out var value) && value is long now)
            {
                return now;
            }

            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        private static long ParseLong(string text, long fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            return long.TryParse(text.Trim(), out var parsed) ? parsed : fallback;
        }
    }
}
