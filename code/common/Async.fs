namespace common

open FSharpPlus
open System
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module Async =
    let bind f a =
        async {
            let! x = a
            return! f x
        }

    let fromValueTask (valueTask: ValueTask) = valueTask.AsTask() |> Async.AwaitTask

    let fromValueTaskOf<'a> (valueTask: ValueTask<'a>) = valueTask.AsTask() |> Async.AwaitTask

    let tapAsync f a =
        async {
            let! x = a
            do! f x
            return x
        }

    let withTimeout (duration: TimeSpan) a =
        Async.StartChild(a, int duration.TotalMilliseconds)
        |> Async.join

    let toTask cancellationToken a =
        Async.StartAsTask(a, TaskCreationOptions.None, cancellationToken)

    let catch a =
        a |> Async.Catch |> Async.map Result.ofChoice
