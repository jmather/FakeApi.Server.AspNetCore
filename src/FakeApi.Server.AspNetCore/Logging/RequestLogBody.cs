using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FakeApi.Server.AspNetCore.Logging
{
    public static class RequestLogBody
    {
        public const int MaxBytes = 64 * 1024;

        public const string TruncationSuffix = "…[truncated]";

        public static async Task<byte[]> ReadAllAsync(Stream stream)
        {
            using (var collected = new MemoryStream())
            {
                await stream.CopyToAsync(collected).ConfigureAwait(false);
                return collected.ToArray();
            }
        }

        public static string Format(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return string.Empty;
            }

            if (data.Length <= MaxBytes)
            {
                return Encoding.UTF8.GetString(data);
            }

            var prefix = new byte[MaxBytes];
            System.Buffer.BlockCopy(data, 0, prefix, 0, MaxBytes);
            var encoding = Encoding.GetEncoding(
                "utf-8",
                new EncoderReplacementFallback(string.Empty),
                new DecoderReplacementFallback(string.Empty));
            return encoding.GetString(prefix) + TruncationSuffix;
        }

        public static async Task<string> ReadCappedAsync(Stream stream)
        {
            return Format(await ReadAllAsync(stream).ConfigureAwait(false));
        }
    }
}
