using LanguageExt;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace benchmark;

public class Worker : BackgroundService
{
    private readonly ILogger logger;
    private readonly TelemetryClient telemetryClient;
    private readonly DiagnosticId diagnosticId;
    private readonly EdgeDriverFactory edgeDriverFactory;
    private readonly IHostApplicationLifetime hostApplicationLifetime;

    public Worker(ILogger<Worker> logger, TelemetryClient telemetryClient, DiagnosticId diagnosticId, EdgeDriverFactory edgeDriverFactory, IHostApplicationLifetime hostApplicationLifetime)
    {
        this.logger = logger;
        this.telemetryClient = telemetryClient;
        this.diagnosticId = diagnosticId;
        this.edgeDriverFactory = edgeDriverFactory;
        this.hostApplicationLifetime = hostApplicationLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var activity = new Activity("Octane.Benchmark");
            activity.SetParentId(diagnosticId);

            using var operation = telemetryClient.StartOperation<RequestTelemetry>(activity);

            var score = await Run(stoppingToken);
            logger.LogInformation("Octane score: {OctaneScore}", score);
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "");
            throw;
        }
        finally
        {
            await telemetryClient.FlushAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            hostApplicationLifetime.StopApplication();
        }
    }

    private async ValueTask<uint> Run(CancellationToken cancellationToken)
    {
        using var driver = await edgeDriverFactory.CreateDriver(cancellationToken);

        var octaneUri = new Uri("http://chromium.github.io/octane/");
        driver.Navigate().GoToUrl(octaneUri);

        var runOctaneHref = new WebDriverWait(driver, TimeSpan.FromSeconds(30))
                                    .Until(driver => driver.FindElement(By.Id("run-octane")), cancellationToken);
        runOctaneHref.Click();

        var octaneScoreElementWait = new WebDriverWait(driver, TimeSpan.FromMinutes(1));
        octaneScoreElementWait.IgnoreExceptionTypes(typeof(InvalidElementStateException), typeof(NoSuchElementException));
        var octaneScoreElement = octaneScoreElementWait
                                .Until(driver =>
                                {
                                    var element = driver.FindElement(By.Id("main-banner"));
                                    return element.Text.Contains("Octane score", StringComparison.OrdinalIgnoreCase)
                                            ? element
                                            : throw new InvalidElementStateException("Octane score isn't visible.");
                                });
        var scoreString = octaneScoreElement.Text.Split(":").Last().Trim();

        return uint.TryParse(scoreString, out var score)
            ? score > 0
                ? score
                : throw new InvalidOperationException($"Score must be greater than 0.")
            : throw new InvalidOperationException($"Could not parse score '{scoreString}'.");
    }
}
