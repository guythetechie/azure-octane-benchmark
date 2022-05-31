namespace common

[<RequireQualifiedAccess>]
module Result =
    let rec traverseSeq f seq =
        let folder head tail =
            f head
            |> Result.bind (fun h -> tail |> Result.bind (fun t -> Ok(h :: t)))

        List.foldBack folder (List.ofSeq seq) (Ok [])


[<AutoOpen>]
module ResultBuilder =
    type ResultBuilder() =
        member this.Return(x) : Result<'a, 'b> = Ok x

        member this.ReturnFrom(x) : Result<'a, 'b> = x

        member this.Zero() : Result<unit, 'b> = this.Return()

        member this.Bind(x, f) : Result<'b, 'c> = Result.bind f x

        member this.BindReturn(x, f) : Result<'b, 'c> = Result.map f x

        member this.MergeSources(x, y) : Result<('a * 'b), 'c> =
            match x, y with
            | Ok x'', Ok y'' -> Ok(x'', y'')
            | Error x'', _ -> Error x''
            | _, Error y'' -> Error y''

        member this.Delay f : Result<'a, 'b> = f ()

        member this.Combine(x1, x2) : Result<'a, 'b> = this.BindReturn(x1, (fun () -> x2))

        member this.TryFinally(body, compensation) : Result<'a, 'b> =
            try
                body
            finally
                compensation ()

        member this.TryWith(body, handler) : Result<'a, 'b> =
            try
                body
            with
            | error -> handler error

    let resultCE = ResultBuilder()
