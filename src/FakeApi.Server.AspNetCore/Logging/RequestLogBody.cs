using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FakeApi.Server.AspNetCore.Logging
{
    public static class RequestLogBody
    {
        public const int MaxBytes = 64 * 1024;

        public const string TruncationSuffix = "…[truncated]";

        public static async Task<string> ReadCappedAsync(Stream stream)
        {
            var buffer = new byte[8192];
            using (var collected = new MemoryStream())
            {
                var truncated = false;
                while (collected.Length < MaxBytes)
                {
                    var want = (int)System.Math.Min(buffer.Length, MaxBytes - collected.Length);
                    var read = await stream.ReadAsync(buffer, 0, want).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    collected.Write(buffer, 0, read);
                    if (collected.Length >= MaxBytes)
                    {
                        var extra = await stream.ReadAsync(buffer, 0, 1).ConfigureAwait(false);
                        truncated = extra > 0;
                        break;
                    }
                }

                var text = Encoding.UTF8.GetString(collected.ToArray());
                if (truncated)
                {
                    text += TruncationSuffix;
                }

                return text;
            }
        }
    }
}
