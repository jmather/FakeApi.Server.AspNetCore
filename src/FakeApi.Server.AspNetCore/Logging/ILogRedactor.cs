namespace FakeApi.Server.AspNetCore.Logging
{
    /// <summary>
    /// Runs after a request log is serialized and before that string is written.
    /// <paramref name="format"/> is <c>json</c> or <c>xml</c>. The default returns the input unchanged.
    /// </summary>
    public interface ILogRedactor
    {
        string Redact(string serialized, string format);
    }
}
