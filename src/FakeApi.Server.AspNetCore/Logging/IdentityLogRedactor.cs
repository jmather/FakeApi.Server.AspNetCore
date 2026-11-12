namespace FakeApi.Server.AspNetCore.Logging
{
    public sealed class IdentityLogRedactor : ILogRedactor
    {
        public string Redact(string serialized, string format)
        {
            return serialized;
        }
    }
}
