namespace FakeApi.Server.AspNetCore.Logging
{
    public interface IRequestLogSink
    {
        void Write(RequestLogRecord record);
    }
}
