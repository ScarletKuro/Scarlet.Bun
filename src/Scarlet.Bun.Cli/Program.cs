using System.Diagnostics.CodeAnalysis;
using System.IO.Abstractions;
using Scarlet.Bun.Core;
using Scarlet.Bun.Core.Providers;

namespace Scarlet.Bun.Cli;

/// <summary>
/// Composition root. Everything interesting lives in <see cref="BunCliApplication"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class Program
{
    // Returning the code from Main rather than assigning Environment.ExitCode means it is set after
    // finalizers have run, so a slow finalizer can never race the process exit.
    private static int Main(string[] args)
    {
        var fileSystem = new FileSystem();
        var environment = new SystemEnvironmentProvider();
        var chmodProvider = Chmod.CreateProvider();

        Platform platform;
        try
        {
            platform = BunRuntimeResolver.GetCurrentPlatform();
        }
        catch (PlatformNotSupportedException exception)
        {
            Console.Error.WriteLine($"Scarlet.Bun: {exception.Message}");

            return ExitCodes.BunNotFound;
        }

        var options = BunCliOptions.FromEnvironment(environment, BunBuildInfo.PinnedBunVersion);

        var resolver = new BunCliResolver(
            fileSystem,
            chmodProvider,
            platform,
            AppContext.BaseDirectory,
            (targetPlatform, log) => new BunDownloader(
                BunDownloader.CreateHttpClient(),
                fileSystem,
                ZipArchiveProvider.Instance,
                chmodProvider,
                targetPlatform,
                log));

        var application = new BunCliApplication(
            resolver,
            new ProcessLauncher(),
            options,
            Console.Out,
            Console.Error);

        return application.Run(args);
    }
}
