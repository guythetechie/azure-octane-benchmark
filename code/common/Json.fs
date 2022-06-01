namespace common

open FSharpPlus
open System
open System.IO
open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes

[<RequireQualifiedAccess>]
type JsonError =
    private
    | JsonError of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "JSON error cannot be empty."
        else
            value |> NonEmptyString.fromString |> JsonError

    static member toString(JsonError (NonEmptyString value)) = value

[<RequireQualifiedAccess>]
module JsonObject =
    let private serializerOptions =
        let options = new JsonSerializerOptions()
        options.PropertyNameCaseInsensitive <- false
        options.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        options

    let tryFromStream (stream: Stream) =
        async {
            try
                let! cancellationToken = Async.CancellationToken

                let! jsonObject =
                    JsonSerializer.DeserializeAsync<JsonObject>(stream, serializerOptions, cancellationToken)
                    |> Async.fromValueTaskOf

                return
                    if isNull jsonObject then
                        "Stream has a null JSON object"
                        |> JsonError.fromString
                        |> Error
                    else
                        Ok jsonObject
            with
            | :? JsonException as jsonException ->
                return
                    jsonException.Message
                    |> JsonError.fromString
                    |> Error
            | error -> return raise error
        }

    let tryFromBytes (bytes: byte array) =
        try
            let jsonObject =
                JsonSerializer.Deserialize<JsonObject>(bytes, serializerOptions)

            if isNull jsonObject then
                "Stream has a null JSON object"
                |> JsonError.fromString
                |> Error
            else
                Ok jsonObject
        with
        | :? JsonException as jsonException ->
            jsonException.Message
            |> JsonError.fromString
            |> Error
        | error -> raise error

    let toBytes (jsonObject: JsonObject) =
        JsonSerializer.SerializeToUtf8Bytes(jsonObject, serializerOptions)

    let toString (jsonObject: JsonObject) =
        jsonObject.ToJsonString(serializerOptions)

    let getOptionalNullableProperty propertyName (jsonObject: JsonObject) =
        match jsonObject.TryGetPropertyValue(propertyName) with
        | true, value ->
            if isNull value then
                Some None
            else
                Some(Some value)
        | _ -> None

    let tryGetNullableProperty propertyName jsonObject =
        jsonObject
        |> getOptionalNullableProperty propertyName
        |> Option.toResultWith (JsonError.fromString $"Property '{propertyName}' does not exist in JSON object.")

    let tryGetProperty propertyName jsonObject =
        jsonObject
        |> tryGetNullableProperty propertyName
        |> Result.bind (Option.toResultWith (JsonError.fromString $"Property '{propertyName}' has a null value."))

    let tryGetJsonObjectProperty propertyName jsonObject =
        jsonObject
        |> tryGetProperty propertyName
        |> Result.bind (function
            | :? JsonObject as jsonObject -> Ok jsonObject
            | _ ->
                $"Property '{propertyName}' is not a JSON object."
                |> JsonError.fromString
                |> Error)

    let tryGetJsonArrayProperty propertyName jsonObject =
        jsonObject
        |> tryGetProperty propertyName
        |> Result.bind (function
            | :? JsonArray as jsonArray -> Ok jsonArray
            | _ ->
                $"Property '{propertyName}' is not a JSON array."
                |> JsonError.fromString
                |> Error)

    let tryGetJsonObjectArrayProperty propertyName jsonObject =
        jsonObject
        |> tryGetJsonArrayProperty propertyName
        |> Result.bind (
            Result.traverseSeq (function
                | null -> "Node is null." |> JsonError.fromString |> Error
                | :? JsonObject as jsonObject -> Ok jsonObject
                | _ ->
                    "Node is not a JSON object."
                    |> JsonError.fromString
                    |> Error)
        )
        |> Result.mapError (fun _ ->
            JsonError.fromString
                $"Property '{propertyName}' has an element that is either null or not a valid JSON object.")

    let tryGetJsonValueProperty propertyName jsonObject =
        jsonObject
        |> tryGetProperty propertyName
        |> Result.bind (function
            | :? JsonValue as jsonValue -> Ok jsonValue
            | _ ->
                $"Property '{propertyName}' is not a JSON value."
                |> JsonError.fromString
                |> Error)

    let tryGetJsonValuePropertyOf<'a> propertyName jsonObject =
        jsonObject
        |> tryGetJsonValueProperty propertyName
        |> Result.bind (fun jsonValue ->
            match jsonValue.TryGetValue<'a>() with
            | true, value -> Ok value
            | _ ->
                $"Property '{propertyName}' is not a JSON value of type '{typeof<'a>.Name}'."
                |> JsonError.fromString
                |> Error)

    let tryGetStringProperty propertyName jsonObject =
        jsonObject
        |> tryGetJsonValuePropertyOf<string> propertyName

    let tryGetNonEmptyStringProperty propertyName jsonObject =
        jsonObject
        |> tryGetStringProperty propertyName
        |> Result.bind (fun value ->
            if String.IsNullOrWhiteSpace(value) then
                $"Property '{propertyName}' is an empty or whtespace string."
                |> JsonError.fromString
                |> Error
            else
                value |> NonEmptyString.fromString |> Ok)

    let tryGetUIntProperty propertyName jsonObject =
        jsonObject
        |> tryGetJsonValuePropertyOf<uint> propertyName

    let addProperty propertyName node (jsonObject: JsonObject) =
        jsonObject.Add(propertyName, node)
        jsonObject

    let addStringProperty propertyName (value: string) jsonObject =
        jsonObject
        |> addProperty propertyName (JsonNode.op_Implicit (value))

[<RequireQualifiedAccess>]
module JsonArray =
    let addProperty node (jsonArray: JsonArray) =
        jsonArray.Add(node)
        jsonArray

    let addStringProperty (value: string) jsonArray =
        jsonArray
        |> addProperty (JsonNode.op_Implicit (value))
