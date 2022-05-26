using LanguageExt.Common;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using static LanguageExt.Prelude;

namespace functionapp;

public static class CommonErrorCode
{
    public static int InvalidJson { get; } = 1;
}

public static class ErrorModule
{
    public static IActionResult ToActionResult(Error error)
    {
        var actionableError = error.Exception.Match(exception => exception switch
                                                    {
                                                        JsonException jsonException => jsonException.ToError(),
                                                        AggregateException aggregateException => aggregateException.Flatten().InnerExceptions
                                                                                                                   .Choose(innerException => innerException is JsonException jsonException ? Some(jsonException) : None)
                                                                                                                   .HeadOrNone()
                                                                                                                   .Map(JsonModule.ToError)
                                                                                                                   .IfNone(() => throw aggregateException),
                                                        var otherException => throw otherException
                                                    },
                                                    error);
        var errorJson = new JsonObject
        {
            ["code"] = actionableError.Code switch
            {
                var _ when actionableError.Code == CommonErrorCode.InvalidJson => nameof(CommonErrorCode.InvalidJson),
                _ => throw new NotImplementedException(),
            },
            ["message"] = actionableError.Message
        };

        var errorJsonString = errorJson.SerializeToString();

        return actionableError switch
        {
            var _ when actionableError.Code == CommonErrorCode.InvalidJson => new BadRequestObjectResult(errorJsonString),
            _ => throw new NotImplementedException()
        };
    }
}