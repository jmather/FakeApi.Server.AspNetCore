namespace FakeApi.Server.AspNetCore.Logging
{
    public interface ILogRecordRedactor
    {
        RequestLogRecord Redact(RequestLogRecord record);
    }

    public sealed class IdentityLogRecordRedactor : ILogRecordRedactor
    {
        public RequestLogRecord Redact(RequestLogRecord record)
        {
            return record;
        }
    }

    public sealed class StampLogRecordRedactor : ILogRecordRedactor
    {
        public RequestLogRecord Redact(RequestLogRecord record)
        {
            record.Msg = (record.Msg ?? string.Empty) + " [stamped]";
            return record;
        }
    }

    public sealed class PrefixLogRedactor : ILogRedactor
    {
        public string Redact(string serialized, string format)
        {
            return "[" + format + "]" + serialized;
        }
    }
}