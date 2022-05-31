namespace common

open Microsoft.Extensions.Configuration

[<RequireQualifiedAccess>]
module Configuration =
    let getOptionalSection key (configuration: IConfiguration) =
        let section = configuration.GetSection(key)

        if section.Exists() then
            Some section
        else
            None

    let getSection key configuration =
        configuration
        |> getOptionalSection key
        |> Option.defaultWith (fun () -> invalidOp $"Could not find section with key '{key}' in configuration.")

    let getOptionalValue key configuration =
        configuration
        |> getOptionalSection key
        |> Option.map (fun section -> section.Value)

    let getValue key configuration =
        configuration
        |> getSection key
        |> fun section -> section.Value
