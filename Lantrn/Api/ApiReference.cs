using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Lantrn.Api;

// The OpenAPI document flattened for reading: references resolved, form parts merged and an example per operation.
// The reference page renders this, so it always describes exactly what /api/openapi/v1.json does.
public sealed record ApiReference(
    string Title,
    string Version,
    string? Description,
    string? AuthDescription,
    IReadOnlyList<ApiTagGroup> Groups,
    IReadOnlyList<ApiSchemaModel> Schemas)
{
    public static ApiReference From(OpenApiDocument document, string baseUrl) => new Builder(document, baseUrl).Build();

    private sealed class Builder(OpenApiDocument document, string baseUrl)
    {
        private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

        // Readable values for the examples, by property name; anything else gets one from its type.
        private static readonly Dictionary<string, Func<JsonNode>> Samples = new(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = () => "handbook",
            ["collection"] = () => "handbook",
            ["description"] = () => "The staff handbook",
            ["source"] = () => "leave-policy.md",
            ["markdown"] = () => "# Leave\n\nEveryone gets 25 days of paid leave a year.",
            ["url"] = () => "https://example.com/handbook/leave",
            ["tags"] = () => new JsonArray("hr", "policies"),
            ["documentCount"] = () => 12,
            ["chunkCount"] = () => 48,
            ["characters"] = () => 18234,
            ["documents"] = () => 3,
        };

        public ApiReference Build()
        {
            var operations = (document.Paths ?? [])
                .SelectMany(path => (path.Value.Operations ?? []).Select(op => (Path: path.Key, Method: op.Key.Method, Operation: op.Value)))
                .ToList();

            var tagOrder = (document.Tags ?? new HashSet<OpenApiTag>()).ToList();
            var groups = operations
                .GroupBy(o => o.Operation.Tags?.FirstOrDefault()?.Name ?? "Other")
                .OrderBy(g => tagOrder.FindIndex(t => t.Name == g.Key) is var i && i < 0 ? int.MaxValue : i)
                .Select(g => new ApiTagGroup(
                    g.Key,
                    tagOrder.FirstOrDefault(t => t.Name == g.Key)?.Description,
                    g.Select(o => Operation(o.Path, o.Method, o.Operation)).ToList()))
                .ToList();

            var schemas = (document.Components?.Schemas ?? new Dictionary<string, IOpenApiSchema>())
                .OrderBy(s => s.Key, StringComparer.Ordinal)
                .Select(s => new ApiSchemaModel(
                    s.Key,
                    s.Value.Description,
                    Fields(s.Value),
                    (s.Value.Enum ?? []).Select(v => v?.ToString() ?? "null").ToList()))
                .ToList();

            var auth = document.Components?.SecuritySchemes?.Values.FirstOrDefault()?.Description;

            return new ApiReference(
                document.Info?.Title ?? "API",
                document.Info?.Version ?? "",
                document.Info?.Description,
                auth,
                groups,
                schemas);
        }

        private ApiOperation Operation(string path, string method, OpenApiOperation operation)
        {
            var parameters = (operation.Parameters ?? [])
                .Select(p => new ApiField(
                    p.Name ?? "",
                    p.In?.ToString().ToLowerInvariant(),
                    p.Schema is { } schema ? TypeOf(schema) : new ApiType("string", null),
                    p.Required,
                    p.Description,
                    p.Schema is { } constrained ? Constraints(constrained) : null))
                .ToList();

            ApiBody? body = null;
            if (operation.RequestBody?.Content?.FirstOrDefault() is { Key: var contentType, Value.Schema: { } bodySchema })
            {
                body = new ApiBody(contentType, operation.RequestBody.Required, TypeOf(bodySchema), Fields(bodySchema));
            }

            var responses = (operation.Responses ?? new OpenApiResponses())
                .OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r =>
                {
                    // Responses without a body (204, 401, 403) have an empty content map, not a missing one.
                    var content = r.Value.Content?.FirstOrDefault(c => c.Value is not null);
                    var contentType = content?.Key;
                    var schema = content?.Value?.Schema;
                    var success = r.Key.StartsWith('2');
                    return new ApiResponseModel(
                        r.Key,
                        r.Value.Description,
                        contentType,
                        schema is null ? null : TypeOf(schema),
                        success && schema is not null && contentType?.Contains("json") == true
                            ? Sample(schema, includeOptional: true, depth: 0)?.ToJsonString(Indented)
                            : null);
                })
                .ToList();

            return new ApiOperation(
                "op-" + (operation.OperationId ?? $"{method}-{path}").ToLowerInvariant(),
                method.ToUpperInvariant(),
                path,
                operation.Summary,
                operation.Description,
                parameters,
                body,
                responses,
                Curl(path, method, operation, body));
        }

        private string Curl(string path, string method, OpenApiOperation operation, ApiBody? body)
        {
            var url = path;
            foreach (var parameter in operation.Parameters ?? [])
            {
                if (parameter.In == ParameterLocation.Path)
                {
                    var sample = parameter.Schema?.Format == "uuid" ? "0f8fad5b-d9cb-469f-a165-70867728950e" : "handbook";
                    url = url.Replace($"{{{parameter.Name}}}", sample);
                }
            }

            var curl = new StringBuilder("curl");
            if (method != "GET")
            {
                curl.Append(" -X ").Append(method.ToUpperInvariant());
            }
            curl.Append(" \"").Append(baseUrl).Append(url).Append('"');
            curl.Append(" \\\n  -H \"Authorization: Bearer $LANTRN_API_KEY\"");

            var schema = operation.RequestBody?.Content?.FirstOrDefault().Value?.Schema;
            if (body is null || schema is null)
            {
                return curl.ToString();
            }

            if (body.ContentType.StartsWith("multipart/", StringComparison.Ordinal))
            {
                foreach (var field in body.Fields.Where(f => f.Required || Samples.ContainsKey(f.Name)))
                {
                    var value = field.Type.Text == "file" ? "@leave-policy.pdf" : Sample(field.Name, null) is JsonArray list
                        ? string.Join(",", list.Select(n => n?.ToString()))
                        : "…";
                    curl.Append(" \\\n  -F \"").Append(field.Name).Append('=').Append(value).Append('"');
                }
                return curl.ToString();
            }

            var json = Sample(schema, includeOptional: false, depth: 0)?.ToJsonString(Indented) ?? "{}";
            curl.Append(" \\\n  -H \"Content-Type: ").Append(body.ContentType).Append('"');
            curl.Append(" \\\n  -d '").Append(json.Replace("'", "'\\''")).Append('\'');
            return curl.ToString();
        }

        // An example value for a schema. Examples in requests leave out optional nested objects, which only add noise.
        private JsonNode? Sample(IOpenApiSchema schema, bool includeOptional, int depth)
        {
            var target = Resolve(schema);
            if (depth > 4)
            {
                return null;
            }
            if (target.Enum is { Count: > 0 } values)
            {
                return values[0]?.DeepClone();
            }

            var type = TypeFlags(target);
            if (type.HasFlag(JsonSchemaType.Array))
            {
                return target.Items is { } items ? new JsonArray(Sample(items, includeOptional, depth + 1)) : new JsonArray();
            }

            if (type.HasFlag(JsonSchemaType.Object) || Properties(target).Count > 0)
            {
                var required = RequiredNames(target);
                var obj = new JsonObject();
                foreach (var (name, property) in Properties(target))
                {
                    var propertyTarget = Resolve(property);
                    var isObject = TypeFlags(propertyTarget).HasFlag(JsonSchemaType.Object) && propertyTarget.Enum is not { Count: > 0 };
                    if (!includeOptional && !required.Contains(name) && isObject)
                    {
                        continue;
                    }
                    obj[name] = Sample(name, property) ?? Sample(property, includeOptional, depth + 1);
                }
                return obj;
            }

            return Scalar(target);
        }

        private JsonNode? Sample(string name, IOpenApiSchema? schema) =>
            Samples.TryGetValue(name, out var sample) && (schema is null || Resolve(schema).Enum is not { Count: > 0 })
                ? sample()
                : null;

        private static JsonNode? Scalar(IOpenApiSchema schema)
        {
            var type = TypeFlags(schema);
            return schema.Format switch
            {
                "uuid" => "0f8fad5b-d9cb-469f-a165-70867728950e",
                "date-time" => "2026-09-25T09:30:00+00:00",
                "uri" => "https://example.com/page",
                _ when type.HasFlag(JsonSchemaType.Integer) => 0,
                _ when type.HasFlag(JsonSchemaType.Number) => 0.0,
                _ when type.HasFlag(JsonSchemaType.Boolean) => true,
                _ when type.HasFlag(JsonSchemaType.String) => "string",
                _ => null,
            };
        }

        private IReadOnlyList<ApiField> Fields(IOpenApiSchema schema)
        {
            var target = Resolve(schema);
            var required = RequiredNames(target);
            return Properties(target)
                .Select(p => new ApiField(p.Key, null, TypeOf(p.Value), required.Contains(p.Key), p.Value.Description, Constraints(p.Value)))
                .ToList();
        }

        // Form bodies arrive as an allOf of one object per form parameter; they read as one object.
        private List<KeyValuePair<string, IOpenApiSchema>> Properties(IOpenApiSchema schema)
        {
            var target = Resolve(schema);
            var properties = new List<KeyValuePair<string, IOpenApiSchema>>(target.Properties ?? new Dictionary<string, IOpenApiSchema>());
            foreach (var part in target.AllOf ?? [])
            {
                properties.AddRange(Properties(part).Where(p => properties.All(existing => existing.Key != p.Key)));
            }
            return properties;
        }

        private HashSet<string> RequiredNames(IOpenApiSchema schema)
        {
            var target = Resolve(schema);
            var required = new HashSet<string>(target.Required ?? new HashSet<string>());
            foreach (var part in target.AllOf ?? [])
            {
                required.UnionWith(RequiredNames(part));
            }
            return required;
        }

        private ApiType TypeOf(IOpenApiSchema schema)
        {
            if (schema is OpenApiSchemaReference { Reference.Id: { } id })
            {
                return new ApiType(id, id);
            }

            // Nullable references come as oneOf [null, $ref].
            var options = (schema.OneOf ?? []).Concat(schema.AnyOf ?? []).Where(o => TypeFlags(o) != JsonSchemaType.Null).ToList();
            if (options.Count == 1)
            {
                var inner = TypeOf(options[0]);
                return inner with { Text = inner.Text + "?" };
            }

            var type = TypeFlags(schema);
            var nullable = type.HasFlag(JsonSchemaType.Null) ? "?" : "";
            if (type.HasFlag(JsonSchemaType.Array))
            {
                var items = schema.Items is null ? new ApiType("any", null) : TypeOf(schema.Items);
                return items with { Text = items.Text + "[]" + nullable };
            }

            // Numbers are also accepted as strings, which the document says with a second type; the number is what's meant.
            var name = schema.Format switch
            {
                "binary" => "file",
                "uuid" or "date-time" or "uri" => schema.Format,
                _ when type.HasFlag(JsonSchemaType.Integer) => "integer",
                _ when type.HasFlag(JsonSchemaType.Number) => "number",
                _ when type.HasFlag(JsonSchemaType.Boolean) => "boolean",
                _ when type.HasFlag(JsonSchemaType.String) => "string",
                _ when type.HasFlag(JsonSchemaType.Object) || schema.AllOf is { Count: > 0 } => "object",
                _ => "any",
            };
            return new ApiType(name + nullable, null);
        }

        private static string? Constraints(IOpenApiSchema schema)
        {
            var parts = new List<string>();
            if (schema.Minimum is { } min && schema.Maximum is { } max)
            {
                parts.Add($"{min}–{max}");
            }
            if (schema.MaxLength is { } maxLength)
            {
                parts.Add($"at most {maxLength} characters");
            }
            if (schema.Default is { } value)
            {
                parts.Add($"default {value.ToJsonString()}");
            }
            if (schema.Pattern is { } pattern && !TypeFlags(schema).HasFlag(JsonSchemaType.Integer) && !TypeFlags(schema).HasFlag(JsonSchemaType.Number))
            {
                parts.Add($"matches {pattern}");
            }
            return parts.Count > 0 ? string.Join(" · ", parts) : null;
        }

        private IOpenApiSchema Resolve(IOpenApiSchema schema) =>
            schema is OpenApiSchemaReference { Reference.Id: { } id }
            && document.Components?.Schemas?.TryGetValue(id, out var target) == true
                ? target
                : schema;

        private static JsonSchemaType TypeFlags(IOpenApiSchema schema) => schema.Type ?? 0;
    }
}

public sealed record ApiTagGroup(string Name, string? Description, IReadOnlyList<ApiOperation> Operations);

public sealed record ApiOperation(
    string Anchor,
    string Method,
    string Path,
    string? Summary,
    string? Description,
    IReadOnlyList<ApiField> Parameters,
    ApiBody? RequestBody,
    IReadOnlyList<ApiResponseModel> Responses,
    string Example);

// SchemaName is set when the type is one of the document's schemas, so it can link there.
public sealed record ApiType(string Text, string? SchemaName);

public sealed record ApiField(string Name, string? Location, ApiType Type, bool Required, string? Description, string? Constraints);

public sealed record ApiBody(string ContentType, bool Required, ApiType Type, IReadOnlyList<ApiField> Fields);

public sealed record ApiResponseModel(string Status, string? Description, string? ContentType, ApiType? Type, string? Example);

public sealed record ApiSchemaModel(string Name, string? Description, IReadOnlyList<ApiField> Fields, IReadOnlyList<string> EnumValues);
