using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FakeApi.Server.AspNetCore.Schema
{
    public sealed class SchemaError
    {
        public SchemaError(string keyword, string path, string message)
        {
            Keyword = keyword;
            Path = path;
            Message = message;
        }

        public string Keyword { get; }

        public string Path { get; }

        public string Message { get; }
    }

    public sealed class PayloadValidation
    {
        public bool IsParseFailure { get; set; }

        public JsonNode Document { get; set; }

        public List<SchemaError> Errors { get; set; } = new List<SchemaError>();

        public string TopMessage
        {
            get { return IsParseFailure ? "Request payload is malformed." : "Request payload is invalid."; }
        }
    }

    public sealed class PayloadValidator
    {
        private readonly JsonObject _schema;
        private readonly List<SchemaError> _errors = new List<SchemaError>();

        public PayloadValidator(byte[] schemaBytes)
        {
            _schema = JsonNode.Parse(schemaBytes).AsObject();
        }

        public static PayloadValidator Shared { get; } = new PayloadValidator(SchemaAssets.SchemaJson);

        public PayloadValidation Validate(byte[] body, string definitionName)
        {
            var result = new PayloadValidation();
            if (body == null || body.Length == 0)
            {
                result.IsParseFailure = true;
                result.Errors.Add(new SchemaError("parse", "", "Request body is empty."));
                return result;
            }

            if (IsUtf8(body) == false)
            {
                result.IsParseFailure = true;
                result.Errors.Add(new SchemaError("parse", "", "Request body is not valid UTF-8."));
                return result;
            }

            JsonNode document;
            try
            {
                document = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                result.IsParseFailure = true;
                result.Errors.Add(new SchemaError("parse", "", "Request body is not valid JSON."));
                return result;
            }

            _errors.Clear();
            var definition = Resolve(JsonValue.Create("#/definitions/" + definitionName));
            Check(document, definition, "");
            result.Document = document;
            result.Errors = _errors
                .OrderBy(e => e.Path, StringComparer.Ordinal)
                .ThenBy(e => e.Keyword, StringComparer.Ordinal)
                .ThenBy(e => e.Message, StringComparer.Ordinal)
                .ToList();
            return result;
        }

        private void Check(JsonNode instance, JsonObject schema, string pointer)
        {
            if (schema == null)
            {
                return;
            }

            if (schema.TryGetPropertyValue("$ref", out var reference))
            {
                schema = Resolve(reference);
            }

            var typeName = schema.TryGetPropertyValue("type", out var typeNode) ? typeNode.GetValue<string>() : null;
            if (typeName != null && MatchesType(instance, typeName) == false)
            {
                Add("type", pointer, "must be " + typeName);
                return;
            }

            if (schema.TryGetPropertyValue("enum", out var enumNode) && enumNode is JsonArray allowed)
            {
                var matched = false;
                foreach (var candidate in allowed)
                {
                    if (JsonNode.DeepEquals(instance, candidate))
                    {
                        matched = true;
                        break;
                    }
                }

                if (matched == false)
                {
                    var encoded = string.Join(", ", allowed.Select(item => item.ToJsonString()));
                    Add("enum", pointer, "must be one of: " + encoded);
                }
            }

            if (instance is JsonValue scalar)
            {
                CheckScalar(scalar, schema, pointer);
            }

            if (instance is JsonArray array)
            {
                if (schema.TryGetPropertyValue("minItems", out var minItems))
                {
                    var min = minItems.GetValue<int>();
                    if (array.Count < min)
                    {
                        Add("minItems", pointer, "must have at least " + min.ToString(CultureInfo.InvariantCulture) + " items");
                    }
                }

                if (schema.TryGetPropertyValue("items", out var items) && items is JsonObject itemSchema)
                {
                    for (var i = 0; i < array.Count; i++)
                    {
                        Check(array[i], itemSchema, pointer + "/" + i.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }

            if (instance is JsonObject obj && (typeName == null || typeName == "object"))
            {
                ApplyDefaults(obj, schema);
                CheckObject(obj, schema, pointer);
            }
        }

        private void CheckScalar(JsonValue scalar, JsonObject schema, string pointer)
        {
            if (scalar.TryGetValue<string>(out var text))
            {
                if (schema.TryGetPropertyValue("pattern", out var patternNode))
                {
                    var pattern = patternNode.GetValue<string>();
                    if (Regex.IsMatch(text, pattern) == false)
                    {
                        Add("pattern", pointer, "must match pattern " + pattern);
                    }
                }

                if (schema.TryGetPropertyValue("format", out var formatNode))
                {
                    var format = formatNode.GetValue<string>();
                    if (format == "uri-reference" && Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out _) == false)
                    {
                        Add("format", pointer, "must be a valid " + format);
                    }
                }

                if (schema.TryGetPropertyValue("minLength", out var minLength) && text.Length < minLength.GetValue<int>())
                {
                    Add("minLength", pointer, "must be at least " + minLength.GetValue<int>().ToString(CultureInfo.InvariantCulture) + " characters");
                }

                if (schema.TryGetPropertyValue("maxLength", out var maxLength) && text.Length > maxLength.GetValue<int>())
                {
                    Add("maxLength", pointer, "must be at most " + maxLength.GetValue<int>().ToString(CultureInfo.InvariantCulture) + " characters");
                }
            }

            if (TryGetNumber(scalar, out var number))
            {
                if (schema.TryGetPropertyValue("minimum", out var minimum) && number < minimum.GetValue<double>())
                {
                    Add("minimum", pointer, "must be >= " + FormatNumber(minimum));
                }

                if (schema.TryGetPropertyValue("maximum", out var maximum) && number > maximum.GetValue<double>())
                {
                    Add("maximum", pointer, "must be <= " + FormatNumber(maximum));
                }
            }
        }

        private void CheckObject(JsonObject obj, JsonObject schema, string pointer)
        {
            var properties = schema.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propertySchema
                ? propertySchema
                : null;
            var additional = true;
            if (schema.TryGetPropertyValue("additionalProperties", out var additionalNode) && additionalNode is JsonValue)
            {
                additional = additionalNode.GetValue<bool>();
            }

            JsonObject patternProperties = null;
            if (schema.TryGetPropertyValue("patternProperties", out var patternNode) && patternNode is JsonObject patterns)
            {
                patternProperties = patterns;
            }

            if (schema.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray required)
            {
                foreach (var nameNode in required)
                {
                    var name = nameNode.GetValue<string>();
                    if (obj.ContainsKey(name) == false)
                    {
                        Add("required", pointer, "must have required property '" + name + "'");
                    }
                }
            }

            foreach (var pair in obj)
            {
                var childPointer = pointer + "/" + EscapePointer(pair.Key);
                if (schema.TryGetPropertyValue("propertyNames", out var propertyNames) && propertyNames is JsonObject nameSchema)
                {
                    if (nameSchema.TryGetPropertyValue("pattern", out var namePattern))
                    {
                        if (Regex.IsMatch(pair.Key, namePattern.GetValue<string>()) == false)
                        {
                            Add("propertyNames", childPointer, "property name '" + pair.Key + "' is not allowed");
                        }
                    }
                }

                JsonObject childSchema = null;
                var declared = false;
                if (properties != null && properties.TryGetPropertyValue(pair.Key, out var declaredNode))
                {
                    declared = true;
                    childSchema = declaredNode as JsonObject;
                }

                var patterned = false;
                if (declared == false && patternProperties != null)
                {
                    foreach (var pattern in patternProperties)
                    {
                        if (Regex.IsMatch(pair.Key, pattern.Key) && pattern.Value is JsonObject patternSchema)
                        {
                            childSchema = patternSchema;
                            patterned = true;
                            break;
                        }
                    }
                }

                if (declared == false && patterned == false && additional == false)
                {
                    Add("additionalProperties", pointer, "must not have additional property '" + pair.Key + "'");
                }

                if (childSchema != null)
                {
                    Check(pair.Value, childSchema, childPointer);
                }
            }
        }

        private static void ApplyDefaults(JsonObject obj, JsonObject schema)
        {
            if (schema.TryGetPropertyValue("properties", out var propsNode) == false || propsNode is JsonObject properties == false)
            {
                return;
            }

            foreach (var property in properties)
            {
                if (obj.ContainsKey(property.Key))
                {
                    continue;
                }

                if (property.Value is JsonObject propertySchema && propertySchema.TryGetPropertyValue("default", out var fallback))
                {
                    obj[property.Key] = fallback.DeepClone();
                }
            }
        }

        private JsonObject Resolve(JsonNode reference)
        {
            var pointer = reference.GetValue<string>();
            if (pointer.StartsWith("#/", StringComparison.Ordinal) == false)
            {
                throw new InvalidOperationException("Unsupported schema reference " + pointer);
            }

            JsonNode current = _schema;
            foreach (var raw in pointer.Substring(2).Split('/'))
            {
                var segment = raw.Replace("~1", "/").Replace("~0", "~");
                current = current[segment];
            }

            return current as JsonObject;
        }

        private static bool MatchesType(JsonNode instance, string typeName)
        {
            switch (typeName)
            {
                case "object":
                    return instance is JsonObject;
                case "array":
                    return instance is JsonArray;
                case "string":
                    return instance is JsonValue value && value.TryGetValue<string>(out _);
                case "boolean":
                    return instance is JsonValue flag && flag.TryGetValue<bool>(out _);
                case "integer":
                    return instance is JsonValue integer && TryGetNumber(integer, out var whole) && whole == Math.Truncate(whole);
                case "number":
                    return instance is JsonValue number && TryGetNumber(number, out _);
                case "null":
                    return instance == null;
                default:
                    return true;
            }
        }

        private static bool TryGetNumber(JsonValue value, out double number)
        {
            if (value.TryGetValue<double>(out number))
            {
                return true;
            }

            number = 0;
            return false;
        }

        private static string FormatNumber(JsonNode node)
        {
            if (node is JsonValue value && value.TryGetValue<long>(out var whole))
            {
                return whole.ToString(CultureInfo.InvariantCulture);
            }

            return node.ToJsonString();
        }

        private static bool IsUtf8(byte[] body)
        {
            try
            {
                var strict = new UTF8Encoding(false, true);
                strict.GetString(body);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static string EscapePointer(string segment)
        {
            return segment.Replace("~", "~0").Replace("/", "~1");
        }

        private void Add(string keyword, string path, string message)
        {
            _errors.Add(new SchemaError(keyword, path, message));
        }
    }
}
