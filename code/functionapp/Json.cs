using common;
using LanguageExt;
using LanguageExt.Common;
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

using static LanguageExt.Prelude;

namespace functionapp;

public static class JsonModule
{
    public static Error ToError(this JsonException jsonException)
    {
        return GetJsonError(jsonException.Message);
    }

    public static Error GetJsonError(string errorMessage)
    {
        return Error.New(CommonErrorCode.InvalidJson, errorMessage);
    }
}

public static class JsonObjectModule
{
    public static uint GetUIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetUIntProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<uint> TryGetUIntProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonValueOfTProperty<uint>(propertyName);
    }

    public static NonEmptyString GetNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetNonEmptyStringProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<NonEmptyString> TryGetNonEmptyStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetStringProperty(propertyName)
                         .Bind(value => string.IsNullOrWhiteSpace(value)
                                        ? FailEff<NonEmptyString>(JsonModule.GetJsonError($"Property '{propertyName}' is a null, empty, or whitespace string."))
                                        : SuccessEff(new NonEmptyString(value)));

    }

    public static string GetStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetStringProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<string> TryGetStringProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonValueOfTProperty<string>(propertyName);
    }

    public static Eff<T> TryGetJsonValueOfTProperty<T>(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonValueProperty(propertyName)
                         .Bind(jsonValue => jsonValue.TryGetValue<T>(out var value)
                                            ? SuccessEff(value)
                                            : FailEff<T>(JsonModule.GetJsonError($"Property '{propertyName}' is not a JSON value of type '{typeof(T).Name}'.")));
    }

    public static Eff<JsonValue> TryGetJsonValueProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(node => node is JsonValue jsonValue
                                        ? SuccessEff(jsonValue)
                                        : FailEff<JsonValue>(JsonModule.GetJsonError($"Property '{propertyName}' is not a JSON value.")));
    }

    public static Eff<Seq<JsonObject>> TryGetJsonObjectArray(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonArrayProperty(propertyName)
                         .Bind(jsonArray => jsonArray.Sequence(node => node is JsonObject jsonObject
                                                                        ? SuccessEff(jsonObject)
                                                                        : FailEff<JsonObject>(JsonModule.GetJsonError("Node is not a JSON object.")))
                                                     .MapFail(error => JsonModule.GetJsonError($"Property '{propertyName}' has at least one element that is not a JSON object.")))
                         .Map(enumerable => enumerable.ToSeq());
    }

    public static JsonArray GetJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonArrayProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<JsonArray> TryGetJsonArrayProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(node => node is JsonArray jsonArray
                                        ? SuccessEff(jsonArray)
                                        : FailEff<JsonArray>(JsonModule.GetJsonError($"Property '{propertyName}' is not a JSON array.")));
    }

    public static JsonObject GetJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetJsonObjectProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<JsonObject> TryGetJsonObjectProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Bind(node => node is JsonObject jsonObject
                                        ? SuccessEff(jsonObject)
                                        : FailEff<JsonObject>(JsonModule.GetJsonError($"Property '{propertyName}' is not a JSON object.")));
    }

    public static JsonNode GetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<JsonNode> TryGetProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalProperty(propertyName)
                         .Bind(option => option.ToEff(JsonModule.GetJsonError($"Property '{propertyName}' does not exist in JSON object.")));
    }

    public static Option<JsonNode> GetOptionalProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetOptionalProperty(propertyName)
                         .Run()
                         .ThrowIfFail();
    }

    public static Eff<Option<JsonNode>> TryGetOptionalProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.GetOptionalNullableProperty(propertyName)
                         .Map(option => option.ToEff(JsonModule.GetJsonError($"Property '{propertyName}' has a null value.")))
                         .Sequence();
    }

    public static Option<Option<JsonNode>> GetOptionalNullableProperty(this JsonObject jsonObject, string propertyName)
    {
        return jsonObject.TryGetPropertyValue(propertyName, out var property)
            ? Some(property is null ? None : Some(property))
            : None;
    }

    public static Aff<JsonObject> FromStream(Stream stream, CancellationToken cancellationToken)
    {
        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        return Aff(() => JsonSerializer.DeserializeAsync<JsonObject>(stream, serializerOptions, cancellationToken))
                .Bind(jsonObject => jsonObject is null
                                    ? FailAff<JsonObject>(JsonModule.GetJsonError("Stream has a null JSON object."))
                                    : SuccessAff(jsonObject));
    }

    public static Eff<JsonObject> FromBinaryData(BinaryData binaryData)
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        return Eff(() => JsonSerializer.Deserialize<JsonObject>(binaryData, serializerOptions))
                .Bind(jsonObject => jsonObject is null
                                    ? FailEff<JsonObject>(JsonModule.GetJsonError("JSON object is null."))
                                    : SuccessEff(jsonObject));
    }

    public static string SerializeToString(this JsonObject jsonObject)
    {
        var serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        return jsonObject.ToJsonString(serializerOptions);
    }
}