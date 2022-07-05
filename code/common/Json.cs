using LanguageExt;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace common;

public record JsonError
{
    public JsonError(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("JSON error cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public static class JsonErrorModule
{
    public static T IfLeftThrow<T>(this Either<JsonError, T> either)
    {
        return either.IfLeft(jsonError => throw new JsonException(jsonError.Value));
    }
}

public static class JsonNodeModule
{
    public static async Task<Stream> SerializeToStream(this JsonNode? jsonNode, CancellationToken cancellationToken)
    {
        var stream = new MemoryStream();
        await JsonSerializer.SerializeAsync(stream, jsonNode, cancellationToken: cancellationToken);
        stream.Position = 0;
        return stream;
    }

    public static string SerializeToString(this JsonNode? jsonNode)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(jsonNode, SerializationModule.Options);

        return Encoding.UTF8.GetString(bytes);
    }

    internal static Either<JsonError, JsonObject> TryAsJsonObject(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonObject jsonObject
            ? jsonObject
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, JsonArray> TryAsJsonArray(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonArray jsonArray
            ? jsonArray
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, JsonValue> TryAsJsonValue(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, string> TryAsString(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsString(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, string> TryAsNonEmptyString(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsNonEmptyString(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, DateTimeOffset> TryAsDateTimeOffset(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsDateTimeOffset(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, DateOnly> TryAsDateOnly(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsDateOnly(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, bool> TryAsBool(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsBool(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, Guid> TryAsGuid(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsGuid(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, decimal> TryAsDecimal(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsDecimal(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, int> TryAsInt(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsInt(errorMessage)
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, uint> TryAsUInt(this JsonNode? jsonNode, string errorMessage)
    {
        return jsonNode is JsonValue jsonValue
            ? jsonValue.TryAsUInt(errorMessage)
            : new JsonError(errorMessage);
    }
}

public static class JsonValueModule
{
    internal static Either<JsonError, string> TryAsString(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<string>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, string> TryAsNonEmptyString(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<string>(out var value)
            ? string.IsNullOrWhiteSpace(value)
                ? new JsonError(errorMessage)
                : value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, bool> TryAsBool(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<bool>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, DateTimeOffset> TryAsDateTimeOffset(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<DateTimeOffset>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, DateOnly> TryAsDateOnly(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<DateOnly>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, Guid> TryAsGuid(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<Guid>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, decimal> TryAsDecimal(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<decimal>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, int> TryAsInt(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<int>(out var value)
            ? value
            : new JsonError(errorMessage);
    }

    internal static Either<JsonError, uint> TryAsUInt(this JsonValue jsonValue, string errorMessage)
    {
        return jsonValue.TryGetValue<uint>(out var value)
            ? value
            : new JsonError(errorMessage);
    }
}

public static class JsonArrayModule
{
    public static async ValueTask<JsonArray> ToJsonArray(this IAsyncEnumerable<JsonNode> jsonNodes, CancellationToken cancellationToken)
    {
        var jsonNodesArray = await jsonNodes.ToArrayAsync(cancellationToken);

        return new JsonArray(jsonNodesArray);
    }

    public static JsonArray ToJsonArray(this IEnumerable<JsonNode> jsonNodes)
    {
        return new JsonArray(jsonNodes.ToArray());
    }
}

public static class JsonObjectModule
{
    public static bool GetBoolProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetBoolProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, bool> TryGetBoolProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsBool($"Property '{propertyName}' is not a bool value."));
    }

    public static Guid GetGuidProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetGuidProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, Guid> TryGetGuidProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsGuid($"Property '{propertyName}' is not a GUID value."));
    }

    public static decimal GetDecimalProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetDecimalProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, decimal> TryGetDecimalProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsDecimal($"Property '{propertyName}' is not a decimal value."));
    }

    public static int GetIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetIntProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, int> TryGetIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsInt($"Property '{propertyName}' is not a integer value."));
    }

    public static uint GetUIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetUIntProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, uint> TryGetUIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsUInt($"Property '{propertyName}' is not a uint value."));
    }

    public static string GetNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetNonEmptyStringProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, string> TryGetNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsNonEmptyString($"Property '{propertyName}' has a null, empty, or whitespace string value."));
    }

    public static Option<string> GetOptionalNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalNonEmptyStringProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, Option<string>> TryGetOptionalNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .Map(node => node.TryAsNonEmptyString($"Property '{propertyName}' has a null, empty, or whitespace string value."))
                         .Sequence();
    }

    public static string GetStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetStringProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, string> TryGetStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsString($"Property '{propertyName}' is not a string value."));
    }

    public static Either<JsonError, Option<string>> TryGetOptionalStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .Map(node => node.TryAsString($"Property '{propertyName}' is not a string value."))
                         .Sequence();
    }

    public static DateOnly GetDateOnlyProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetDateOnlyProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, DateOnly> TryGetDateOnlyProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsDateOnly($"Property '{propertyName}' is not a DateOnly value."));
    }

    public static DateTimeOffset GetDateTimeOffsetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetDateTimeOffsetProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, DateTimeOffset> TryGetDateTimeOffsetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(value => value.TryAsDateTimeOffset($"Property '{propertyName}' is not a DateTimeOffset value."));
    }

    public static Option<DateTimeOffset> GetOptionalDateTimeOffsetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalDateTimeOffsetProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, Option<DateTimeOffset>> TryGetOptionalDateTimeOffsetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .Map(node => node.TryAsDateTimeOffset($"Property '{propertyName}' is not a DateTimeOffset value."))
                         .Sequence();
    }

    public static JsonArray GetJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonArrayProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, JsonArray> TryGetJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(node => node.TryAsJsonArray($"Property '{propertyName}' is not a JSON array."));
    }

    public static Option<JsonArray> GetOptionalJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalJsonArrayProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, Option<JsonArray>> TryGetOptionalJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .Map(node => node.TryAsJsonArray($"Property '{propertyName}' is not a JSON array."))
                         .Sequence();
    }

    public static JsonObject GetJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonObjectProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, JsonObject> TryGetJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(node => node.TryAsJsonObject($"Property '{propertyName}' is not a JSON object."));
    }

    public static Option<JsonObject> GetOptionalJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalJsonObjectProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, Option<JsonObject>> TryGetOptionalJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .Map(node => node.TryAsJsonObject($"Property '{propertyName}' is not a JSON object."))
                         .Sequence();
    }

    public static JsonNode GetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName).IfLeftThrow();
    }

    public static Either<JsonError, JsonNode> TryGetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalProperty(propertyName)
                         .ToEither(new JsonError($"Property '{propertyName}' is null or does not exist in JSON object."));
    }

    public static Option<JsonNode> GetOptionalProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetPropertyValue(propertyName, out var property)
            ? property is null
                ? None
                : property
            : None;
    }

    public static async ValueTask<JsonObject> FromStream(Stream stream, CancellationToken cancellationToken)
    {
        var either = await TryFromStream(stream, cancellationToken);

        return either.IfLeftThrow();
    }

    public static async ValueTask<Either<JsonError, JsonObject>> TryFromStream(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            var jsonObject = await JsonSerializer.DeserializeAsync<JsonObject>(stream, SerializationModule.Options, cancellationToken);

            return jsonObject is null
                ? new JsonError("Stream has a null JSON value.")
                : jsonObject;

        }
        catch (JsonException jsonException)
        {
            return new JsonError(jsonException.Message);
        }
    }

    public static JsonObject FromBinaryData(BinaryData binaryData)
    {
        return TryFromBinaryData(binaryData).IfLeftThrow();
    }

    public static Either<JsonError, JsonObject> TryFromBinaryData(BinaryData binaryData)
    {
        try
        {
            var jsonObject = JsonSerializer.Deserialize<JsonObject>(binaryData, SerializationModule.Options);

            return jsonObject is null
                ? new JsonError("Stream has a null JSON value.")
                : jsonObject;
        }
        catch (JsonException jsonException)
        {
            return new JsonError(jsonException.Message);
        }
    }

    public static Either<JsonError, JsonObject> TryFromString(string source)
    {
        try
        {
            var jsonObject = JsonSerializer.Deserialize<JsonObject>(source, SerializationModule.Options);

            return jsonObject is null
                ? new JsonError("String has a null JSON value.")
                : jsonObject;
        }
        catch (JsonException jsonException)
        {
            return new JsonError(jsonException.Message);
        }
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, JsonNode property)
    {
        jsonObject.Add(propertyName, property);

        return jsonObject;
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, Option<JsonNode> option)
    {
        return option.Match(value => jsonObject.AddProperty(propertyName, value),
                            () => jsonObject);
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, JsonObject property)
    {
        return jsonObject.AddProperty(propertyName, (JsonNode)property!);
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, Option<JsonObject> option)
    {
        return jsonObject.AddProperty(propertyName, option.Map(value => (JsonNode)value!));
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, string property)
    {
        return jsonObject.AddProperty(propertyName, (JsonNode)property!);
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, Option<string> option)
    {
        return jsonObject.AddProperty(propertyName, option.Map(value => (JsonNode)value!));
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, DateTimeOffset property)
    {
        return jsonObject.AddProperty(propertyName, (JsonNode)property!);
    }

    public static JsonObject AddProperty(this JsonObject jsonObject, string propertyName, Option<DateTimeOffset> option)
    {
        return jsonObject.AddProperty(propertyName, option.Map(value => (JsonNode)value!));
    }

    public static JsonObject SetProperty(this JsonObject jsonObject, string propertyName, string property)
    {
        jsonObject[propertyName] = property;

        return jsonObject;
    }
}

public static class SerializationModule
{
    public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public static JsonNodeOptions NodeOptions { get; } = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
}