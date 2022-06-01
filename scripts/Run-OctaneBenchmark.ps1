[CmdletBinding()]
param (
    [Parameter(Mandatory = $true)]
    [string]
    $BenchmarkDownloadUri,
    [Parameter(Mandatory = $true)]
    [string]
    $DiagnosticId,
    [Parameter(Mandatory = $true)]
    [string]
    $VirtualMachineSku,
    [Parameter(Mandatory = $true)]
    [string]
    $ApplicationInsightsConnectionString
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$VerbosePreference = "Continue"
$InformationPreference = "Continue"

$downloadFilePath = "$([System.IO.Path]::GetTempFileName()).zip"
Invoke-WebRequest -Uri $BenchmarkDownloadUri -OutFile $downloadFilePath

$scriptDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ([System.IO.Path]::GetRandomFileName())
Expand-Archive -LiteralPath $downloadFilePath -DestinationPath $scriptDirectory

$scriptExecutablePath = Join-Path $scriptDirectory "benchmark.exe"
. $scriptExecutablePath --DIAGNOSTIC_ID $DiagnosticId --VIRTUAL_MACHINE_SKU $VirtualMachineSku --APPLICATION_INSIGHTS_CONNECTION_STRING $ApplicationInsightsConnectionString