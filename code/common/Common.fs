namespace common

open System

[<RequireQualifiedAccess>]
type CommonError = Exception of exn

type NonEmptyString =
    private
    | NonEmptyString of string

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "String cannot be empty."
        else
            NonEmptyString value

    static member toString(NonEmptyString value) = value

type VirtualMachineName =
    private
    | VirtualMachineName of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Virtual machine name cannot be empty."
        else
            value
            |> NonEmptyString.fromString
            |> VirtualMachineName

    static member fromNonEmptyString value = VirtualMachineName value

    static member toString(VirtualMachineName (NonEmptyString value)) = value

type VirtualMachineSku =
    private
    | VirtualMachineSku of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Virtual machine SKU cannot be empty."
        else
            value
            |> NonEmptyString.fromString
            |> VirtualMachineSku

    static member fromNonEmptyString value = VirtualMachineSku value

    static member toString(VirtualMachineSku (NonEmptyString value)) = value

type VirtualMachine =
    { Name: VirtualMachineName
      Sku: VirtualMachineSku }

type DiagnosticId =
    private
    | DiagnosticId of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Diagnostic ID cannot be empty."
        else
            value |> NonEmptyString.fromString |> DiagnosticId

    static member toString(DiagnosticId (NonEmptyString value)) = value

type ApplicationInsightsConnectionString =
    private
    | ApplicationInsightsConnectionString of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Appliation insights connection string cannot be empty."
        else
            value
            |> NonEmptyString.fromString
            |> ApplicationInsightsConnectionString

    static member toString(ApplicationInsightsConnectionString (NonEmptyString value)) = value
