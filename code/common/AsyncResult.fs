namespace common

open FSharpPlus
open System
open System.Threading.Tasks

[<RequireQualifiedAccess>]
module AsyncResult =
    let fromAsync x = Async.map Ok x

    let fromResult x = async.Return x

    let defaultWith f x = Async.map (Result.defaultWith f) x

    let defaultWithAsync f x =
        async {
            match! x with
            | Ok x' -> return x'
            | Error error -> return! f error
        }
    //let doAction action x =
    //    x
    //    |> Async.map (
    //        Result.map (fun x' ->
    //            action ()
    //            x')
    //    )

    //let doActionT action x =
    //    x
    //    |> Async.map (
    //        Result.map (fun x' ->
    //            action x
    //            x')
    //    )

    //let doTask (task: unit -> Task) x =
    //    async {
    //        match! x with
    //        | Ok x' ->
    //            do! task () |> Async.AwaitTask
    //            return Ok x'
    //        | Error error -> return Error error
    //    }

    //let doTaskT (task: 'a -> Task) x =
    //    async {
    //        match! x with
    //        | Ok x' ->
    //            do! task x' |> Async.AwaitTask
    //            return Ok x'
    //        | Error error -> return Error error
    //    }

    //let doAsync f x =
    //    async {
    //        match! x with
    //        | Ok x' ->
    //            do! f ()
    //            return Ok x'
    //        | Error error -> return Error error
    //    }

    //let doAsyncT f x =
    //    async {
    //        match! x with
    //        | Ok x' ->
    //            do! f x'
    //            return Ok x'
    //        | Error error -> return Error error
    //    }

    let map f x = Async.map (Result.map f) x

    let bind f x =
        async {
            match! x with
            | Ok x' -> return! f x'
            | Error error -> return Error error
        }

//let bindAsync f x =
//    async {
//        match! x with
//        | Ok x' ->
//            let! x'' = f x'
//            return Ok x''
//        | Error error -> return Error error
//    }

//let bindResult f x = Async.map (Result.bind f) x

[<AutoOpen>]
module AsyncResultBuilder =
    type AsyncResultBuilder() =
        member this.Return(x) : Async<Result<'a, 'b>> = async.Return(Ok x)

        member this.ReturnFrom(x) : Async<Result<'a, 'b>> = x

        //member this.ReturnFrom(x) : Async<Result<'a, 'b>> = Async.map Ok x

        member this.Zero() : Async<Result<unit, 'b>> = this.Return()

        member this.Bind(x, f) : Async<Result<'b, 'c>> = AsyncResult.bind f x

        //member this.Bind(x, f) : Async<Result<'b, 'c>> = Result.bind f x |> async.Return

        member this.BindReturn(x, f) : Async<Result<'b, 'c>> = AsyncResult.map f x

        //member this.BindReturn(x, f) : Async<Result<'b, 'c>> = Result.map f x |> async.Return

        member this.MergeSources(x, y) : Async<Result<('a * 'b), 'c>> =
            async {
                let! x' = x
                let! y' = y

                match x', y' with
                | Ok x'', Ok y'' -> return Ok(x'', y'')
                | Error x'', _ -> return Error x''
                | _, Error y'' -> return Error y''
            }

        member this.Delay f : Async<Result<'a, 'b>> = f ()

        member this.TryFinally(body, compensation) : Async<Result<'a, 'b>> =
            async {
                try
                    return! body
                finally
                    compensation ()
            }

        member this.TryWith(body, handler) : Async<Result<'a, 'b>> =
            async {
                try
                    return! body
                with
                | error -> return! handler error
            }

    let asyncResultCE = AsyncResultBuilder()
