module benchmark.Program

open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.DataContracts
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open OpenQA.Selenium
open OpenQA.Selenium.Support.UI
open System
open System.Diagnostics
open OpenQA.Selenium.Edge

open common

let private getServiceProvider arguments =
    let configureServices (services: IServiceCollection) =
        services.AddApplicationInsightsTelemetryWorkerService()
        |> ignore

    let configureConfiguration (builder: IConfigurationBuilder) =
        builder.AddUserSecrets("dotnet-benchmark-9580E170-D49A-4EF5-9299-A61DB8FA6449")
        |> ignore

    let configureLogging (builder: ILoggingBuilder) =
        builder.AddFile("log.txt", append = true)
        |> ignore

    let hostBuilder = Host.CreateDefaultBuilder(arguments)

    let hostBuilder =
        hostBuilder.ConfigureServices(configureServices)

    let hostBuilder =
        hostBuilder.ConfigureAppConfiguration(configureConfiguration)

    let hostBuilder =
        hostBuilder.ConfigureLogging(configureLogging)

    hostBuilder.Build().Services

let private getOctaneScore (driver: IWebDriver) =
    let navigateToOctaneUri () =
        let octaneUri =
            new Uri("http://chromium.github.io/octane/")

        driver.Navigate().GoToUrl(octaneUri)

    let clickOnRunOctaneLink () =
        async {
            let! cancellationToken = Async.CancellationToken

            WebDriverWait(driver, TimeSpan.FromSeconds(30))
                .Until((fun driver -> driver.FindElement(By.Id("run-octane"))), cancellationToken)
                .Click()
        }

    let getOctaneScore () =
        let wait =
            WebDriverWait(driver, TimeSpan.FromMinutes(1))

        wait.IgnoreExceptionTypes(typeof<InvalidElementStateException>, typeof<NoSuchElementException>)

        let octaneScoreElement =
            wait.Until (fun driver ->
                let element = driver.FindElement(By.Id("main-banner"))

                if element.Text.Contains("Octane score", StringComparison.OrdinalIgnoreCase) then
                    element
                else
                    InvalidElementStateException("Octane score is not visible.")
                    |> raise)

        let scoreString =
            octaneScoreElement.Text.Split(":")
            |> Array.last
            |> fun value -> value.Trim()

        match UInt32.TryParse(scoreString) with
        | true, value ->
            if value > 0u then
                value
            else
                invalidOp "Octane score must be greater than zero."
        | _ -> invalidOp $"Could not parse score '{scoreString}'."

    async {
        navigateToOctaneUri ()
        do! clickOnRunOctaneLink ()
        return getOctaneScore ()
    }

let private getActivity (serviceProvider: IServiceProvider) =
    let configuration =
        serviceProvider.GetRequiredService<IConfiguration>()

    let diagnosticId =
        Configuration.getValue "DIAGNOSTIC_ID" configuration

    let virtualMachineSku =
        Configuration.getValue "VIRTUAL_MACHINE_SKU" configuration

    let activity = new Activity("Octane.Benchmark")

    activity
        .SetParentId(diagnosticId)
        .AddBaggage("VirtualMachineSku", virtualMachineSku)

let private getTelemetryClient (serviceProvider: IServiceProvider) =
    serviceProvider.GetRequiredService<TelemetryClient>()


let private startTelemetry (telemetryClient: TelemetryClient) (activity: Activity) =
    telemetryClient.StartOperation<RequestTelemetry>(activity)
    |> ignore

[<EntryPoint>]
let main arguments =
    async {
        let serviceProvider = getServiceProvider arguments
        let telemetryClient = getTelemetryClient serviceProvider
        use activity = getActivity serviceProvider
        do startTelemetry telemetryClient activity

        let logger =
            serviceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Program")

        try
            try
                use edgeService = EdgeDriverService.CreateDefaultService()

                use edgeDriver =
                    let options = new EdgeOptions()
                    options.AddArgument("headless")
                    new EdgeDriver(edgeService, options)

                let! score = getOctaneScore edgeDriver
                logger.LogInformation("Octane score: {OctaneScore}", score)
            with
            | error -> logger.LogCritical(error, "")
        finally
            telemetryClient.Flush()

            TimeSpan.FromSeconds(5.0)
            |> Async.Sleep
            |> Async.RunSynchronously
    }
    |> Async.RunSynchronously

    0
