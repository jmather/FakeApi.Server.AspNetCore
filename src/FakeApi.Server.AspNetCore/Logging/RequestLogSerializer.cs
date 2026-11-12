using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;

namespace FakeApi.Server.AspNetCore.Logging
{
    public static class RequestLogSerializer
    {
        private static readonly JsonWriterOptions JsonOptions = new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        };

        public static string ToJson(RequestLogRecord record)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, JsonOptions))
                {
                    writer.WriteStartObject();
                    writer.WriteString("timestamp", record.Timestamp);
                    writer.WriteString("level", record.Level);
                    writer.WriteString("msg", record.Msg);
                    writer.WriteString("server", record.Server);
                    writer.WriteString("request_id", record.RequestId);
                    writer.WriteString("method", record.Method);
                    writer.WriteString("path", record.Path);
                    writer.WritePropertyName("query");
                    WriteQuery(writer, record.Query);
                    writer.WriteNumber("status", record.Status);
                    writer.WriteNumber("duration_ms", record.DurationMs);
                    WriteNullableString(writer, "action", record.Action);
                    WriteNullableString(writer, "user", record.User);
                    if (record.Request != null)
                    {
                        WritePayload(writer, "request", record.Request);
                    }

                    if (record.Response != null)
                    {
                        WritePayload(writer, "response", record.Response);
                    }

                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static string ToXml(RequestLogRecord record)
        {
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                ConformanceLevel = ConformanceLevel.Fragment,
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                CloseOutput = false,
            };

            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartElement("request-log");
                    WriteTextElement(writer, "timestamp", record.Timestamp);
                    WriteTextElement(writer, "level", record.Level);
                    WriteTextElement(writer, "msg", record.Msg);
                    WriteTextElement(writer, "server", record.Server);
                    WriteTextElement(writer, "request_id", record.RequestId);
                    WriteTextElement(writer, "method", record.Method);
                    WriteTextElement(writer, "path", record.Path);
                    writer.WriteStartElement("query");
                    if (record.Query != null)
                    {
                        foreach (var pair in record.Query)
                        {
                            if (pair.Value is string[] many)
                            {
                                foreach (var value in many)
                                {
                                    WriteParam(writer, pair.Key, value);
                                }
                            }
                            else
                            {
                                WriteParam(writer, pair.Key, pair.Value as string ?? string.Empty);
                            }
                        }
                    }

                    writer.WriteEndElement();
                    WriteTextElement(writer, "status", record.Status.ToString());
                    WriteTextElement(writer, "duration_ms", FormatDuration(record.DurationMs));
                    WriteTextElement(writer, "action", record.Action);
                    WriteTextElement(writer, "user", record.User);
                    if (record.Request != null)
                    {
                        WritePayloadXml(writer, "request", record.Request);
                    }

                    if (record.Response != null)
                    {
                        WritePayloadXml(writer, "response", record.Response);
                    }

                    writer.WriteEndElement();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteQuery(Utf8JsonWriter writer, Dictionary<string, object> query)
        {
            writer.WriteStartObject();
            if (query != null)
            {
                foreach (var pair in query)
                {
                    writer.WritePropertyName(pair.Key);
                    if (pair.Value is string[] many)
                    {
                        writer.WriteStartArray();
                        foreach (var value in many)
                        {
                            writer.WriteStringValue(value);
                        }

                        writer.WriteEndArray();
                    }
                    else
                    {
                        writer.WriteStringValue(pair.Value as string ?? string.Empty);
                    }
                }
            }

            writer.WriteEndObject();
        }

        private static void WriteNullableString(Utf8JsonWriter writer, string name, string value)
        {
            if (value == null)
            {
                writer.WriteNull(name);
            }
            else
            {
                writer.WriteString(name, value);
            }
        }

        private static void WritePayload(Utf8JsonWriter writer, string name, RequestLogPayload payload)
        {
            writer.WriteStartObject(name);
            writer.WritePropertyName("headers");
            writer.WriteStartObject();
            if (payload.Headers != null)
            {
                foreach (var header in payload.Headers)
                {
                    writer.WriteString(header.Key, header.Value);
                }
            }

            writer.WriteEndObject();
            writer.WriteString("content_type", payload.ContentType ?? string.Empty);
            if (payload.IncludeBody)
            {
                writer.WriteString("body", payload.Body ?? string.Empty);
            }

            writer.WriteEndObject();
        }

        private static void WritePayloadXml(XmlWriter writer, string name, RequestLogPayload payload)
        {
            writer.WriteStartElement(name);
            writer.WriteStartElement("headers");
            if (payload.Headers != null)
            {
                foreach (var header in payload.Headers)
                {
                    writer.WriteStartElement("header");
                    writer.WriteAttributeString("name", header.Key);
                    writer.WriteRaw(XmlEscape(header.Value));
                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement();
            WriteTextElement(writer, "content_type", payload.ContentType);
            if (payload.IncludeBody)
            {
                WriteTextElement(writer, "body", payload.Body);
            }

            writer.WriteEndElement();
        }

        private static void WriteParam(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement("param");
            writer.WriteAttributeString("name", name ?? string.Empty);
            writer.WriteRaw(XmlEscape(value));
            writer.WriteEndElement();
        }

        private static void WriteTextElement(XmlWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteRaw(XmlEscape(value));
            writer.WriteEndElement();
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                switch (character)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    case '"':
                        builder.Append("&quot;");
                        break;
                    case '\'':
                        builder.Append("&apos;");
                        break;
                    case '\r':
                        builder.Append("&#xD;");
                        break;
                    case '\n':
                        builder.Append("&#xA;");
                        break;
                    default:
                        builder.Append(character);
                        break;
                }
            }

            return builder.ToString();
        }

        private static string FormatDuration(double durationMs)
        {
            return durationMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
