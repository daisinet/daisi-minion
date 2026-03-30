using System.Text.Json.Nodes;

namespace Daisi.Minion.Coding;

/// <summary>
/// Validates tool call arguments against the tool's ParametersSchema.
/// Returns specific, actionable error messages the model can use to self-correct.
/// </summary>
public static class ToolCallValidator
{
    /// <summary>
    /// Validate arguments against a tool's schema. Returns null if valid,
    /// or a specific error message describing what's wrong.
    /// </summary>
    public static string? Validate(JsonObject arguments, JsonObject schema)
    {
        var errors = new List<string>();

        // Check required fields
        if (schema.TryGetPropertyValue("required", out var requiredNode) && requiredNode is JsonArray required)
        {
            foreach (var req in required)
            {
                var fieldName = req?.ToString();
                if (fieldName == null) continue;

                if (!arguments.ContainsKey(fieldName))
                    errors.Add($"Missing required field: \"{fieldName}\"");
                else if (arguments[fieldName] is JsonValue val && val.ToString() == "")
                    errors.Add($"Required field \"{fieldName}\" is empty");
            }
        }

        // Check property types
        if (schema.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject props)
        {
            foreach (var (key, value) in arguments)
            {
                if (value == null) continue;

                // Check if this property is defined in schema
                if (!props.ContainsKey(key)) continue; // allow extra properties

                var propSchema = props[key] as JsonObject;
                if (propSchema == null) continue;

                var expectedType = propSchema["type"]?.ToString();
                if (expectedType == null) continue;

                var typeError = ValidateType(key, value, expectedType);
                if (typeError != null)
                    errors.Add(typeError);
            }
        }

        return errors.Count > 0
            ? $"Invalid arguments:\n{string.Join("\n", errors.Select(e => $"  - {e}"))}"
            : null;
    }

    /// <summary>
    /// Validate a tool call: check the tool exists, arguments are valid.
    /// Returns (tool, error). If error is non-null, the call should not be executed.
    /// </summary>
    public static (IMinionTool? Tool, string? Error) ValidateCall(
        string toolName, JsonObject arguments, IReadOnlyDictionary<string, IMinionTool> tools)
    {
        if (!tools.TryGetValue(toolName, out var tool))
        {
            var available = string.Join(", ", tools.Keys.OrderBy(k => k));
            return (null, $"Unknown tool: \"{toolName}\". Available tools: {available}");
        }

        var schemaError = Validate(arguments, tool.ParametersSchema);
        if (schemaError != null)
            return (tool, $"Tool \"{toolName}\": {schemaError}");

        return (tool, null);
    }

    /// <summary>
    /// Coerce common type mismatches that the 9B model produces.
    /// Modifies the arguments in-place. Returns list of coercions applied.
    /// </summary>
    public static List<string> CoerceTypes(JsonObject arguments, JsonObject schema)
    {
        var coercions = new List<string>();

        if (!schema.TryGetPropertyValue("properties", out var propsNode) || propsNode is not JsonObject props)
            return coercions;

        foreach (var (key, propSchemaNode) in props)
        {
            if (!arguments.ContainsKey(key)) continue;
            var value = arguments[key];
            if (value == null) continue;

            var propSchema = propSchemaNode as JsonObject;
            var expectedType = propSchema?["type"]?.ToString();
            if (expectedType == null) continue;

            // Array → string: join with newlines
            if (expectedType == "string" && value is JsonArray arr)
            {
                var joined = string.Join("\n", arr.Select(n => n?.ToString() ?? ""));
                arguments[key] = joined;
                coercions.Add($"Coerced \"{key}\" from array to string");
            }

            // String → number
            if (expectedType is "number" or "integer" && value is JsonValue strVal)
            {
                try
                {
                    var str = strVal.GetValue<string>();
                    if (double.TryParse(str, out var num))
                    {
                        arguments[key] = num;
                        coercions.Add($"Coerced \"{key}\" from string to number");
                    }
                }
                catch { }
            }

            // String → boolean
            if (expectedType == "boolean" && value is JsonValue boolStr)
            {
                try
                {
                    var str = boolStr.GetValue<string>();
                    if (bool.TryParse(str, out var b))
                    {
                        arguments[key] = b;
                        coercions.Add($"Coerced \"{key}\" from string to boolean");
                    }
                }
                catch { }
            }
        }

        return coercions;
    }

    private static string? ValidateType(string key, JsonNode value, string expectedType)
    {
        return expectedType switch
        {
            "string" when value is not JsonValue && value is not JsonArray =>
                $"\"{key}\" must be a string, got {value.GetValueKind()}",
            "integer" or "number" when value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String =>
                $"\"{key}\" must be a {expectedType}, got string \"{v}\"",
            "boolean" when value is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String =>
                $"\"{key}\" must be a boolean, got string \"{v}\"",
            "array" when value is not JsonArray =>
                $"\"{key}\" must be an array, got {value.GetValueKind()}",
            "object" when value is not JsonObject =>
                $"\"{key}\" must be an object, got {value.GetValueKind()}",
            _ => null
        };
    }
}
