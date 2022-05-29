using common;
using DotNext.Threading;
using LanguageExt;
using LanguageExt.Common;
using Microsoft.Win32;
using OpenQA.Selenium.Edge;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace benchmark;

public delegate void CleanupAfterEdgeDriverService();

public class EdgeDriverFactory : IAsyncDisposable
{
    private readonly DirectoryInfo driverFolder;
    private readonly AsyncLazy<EdgeDriverService> lazyEdgeDriverService;

    public EdgeDriverFactory(IHttpClientFactory httpClientFactory)
    {
        this.driverFolder = GetRandomDirectory();
        this.lazyEdgeDriverService = new(async (cancellationToken) =>
        {
            using var client = httpClientFactory.CreateClient();
            return await GetService(client, driverFolder, cancellationToken);
        });
    }

    public Aff<EdgeDriver> CreateDriver(CancellationToken cancellationToken)
    {
        return Aff(async () =>
        {
            var service = await lazyEdgeDriverService.WithCancellation(cancellationToken);

            var options = new EdgeOptions();
            options.AddArgument("headless");

            return new EdgeDriver(service, options);
        });
    }

    private static async ValueTask<EdgeDriverService> GetService(HttpClient client, DirectoryInfo driverFolder, CancellationToken cancellationToken)
    {
        var driverDownloadFile = new FileInfo(Path.GetTempFileName());
        await DownloadDriver(client, driverDownloadFile, cancellationToken);
        ZipFile.ExtractToDirectory(driverDownloadFile.FullName, driverFolder.FullName);
        File.Delete(driverDownloadFile.FullName);

        return EdgeDriverService.CreateDefaultService(driverFolder.FullName);
    }

    private static DirectoryInfo GetRandomDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        return new DirectoryInfo(path);
    }

    private static async ValueTask<Unit> DownloadDriver(HttpClient client, FileInfo downloadFile, CancellationToken cancellationToken)
    {
        var downloadUri = GetDownloadUri();
        using var response = await client.GetAsync(downloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = downloadFile.Create();
        await responseStream.CopyToAsync(fileStream, cancellationToken);

        return Unit.Default;
    }

    private static Uri GetDownloadUri()
    {
        return Edge.TryGetDriverDownloadUri()
                   .Run()
                   .ThrowIfFail();
    }

    public async ValueTask DisposeAsync()
    {
        if (lazyEdgeDriverService.IsValueCreated)
        {
            var service = await lazyEdgeDriverService;
            service.Dispose();
        }

        if (driverFolder.Exists)
        {
            driverFolder.Delete(recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}

internal static class Edge
{
    public static Eff<Uri> TryGetDriverDownloadUri()
    {
        return TryGetInstalledVersion().Map(version =>
        {
            string architecture = Environment.Is64BitOperatingSystem ? "64" : "32";

            return new Uri($"https://msedgedriver.azureedge.net/{version}/edgedriver_win{architecture}.zip");
        });
    }

    private static Eff<NonEmptyString> TryGetInstalledVersion()
    {
        return SuccessEff(Unit.Default).Bind(_ => OperatingSystem.IsWindows()
                                                    ? SuccessEff(Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Edge\BLBeacon")
                                                                                    ?.GetValue("version")
                                                                                    ?.ToString())
                                                    : FailEff<string?>($"Getting Edge is only supported on Windows. Current operating system is {Environment.OSVersion.VersionString}."))
                                       .Bind(value => string.IsNullOrWhiteSpace(value)
                                                        ? FailEff<NonEmptyString>(Error.New("Edge version cannot be null or whitespace."))
                                                        : SuccessEff(new NonEmptyString(value)));
    }
}
