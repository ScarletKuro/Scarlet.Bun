using System.Diagnostics;
using System.IO.Compression;
using System.Security;
using Xunit.Abstractions;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

public class BunBeforeStaticWebAssetsTests
{
    private readonly ITestOutputHelper _output;

    public BunBeforeStaticWebAssetsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SingleTargetFrameworkPack_IncludesGeneratedWwwrootFilesAsStaticWebAssets()
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            $"scarlet-bun-static-web-assets-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(workspace);
            var targetsPath = Path.Combine(LocateRepositoryRoot(), "src", "Scarlet.Bun.MSBuild", "Scarlet.Bun.MSBuild.targets");
            var projectPath = Path.Combine(workspace, "GeneratedAssetsRcl.csproj");
            var packageDirectory = Path.Combine(workspace, "nupkg");

            File.WriteAllText(
                Path.Combine(workspace, "build.mjs"),
                """
                import fs from "node:fs";

                fs.mkdirSync("wwwroot/css", { recursive: true });
                fs.writeFileSync("wwwroot/css/generated.css", "body{color:red}");
                """);

            File.WriteAllText(
                projectPath,
                $"""
                <Project Sdk="Microsoft.NET.Sdk.Razor">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsPackable>true</IsPackable>
                  </PropertyGroup>

                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                  </ItemGroup>

                  <Import Project="{SecurityElement.Escape(targetsPath)}" />

                  <ItemGroup>
                    <BunBeforeStaticWebAssets Include="run">
                      <Arguments>build.mjs</Arguments>
                    </BunBeforeStaticWebAssets>
                  </ItemGroup>
                </Project>
                """);

            var result = await RunDotnet(workspace, "pack GeneratedAssetsRcl.csproj --configuration Debug --output nupkg --verbosity minimal /nodeReuse:false");
            _output.WriteLine(result.StandardOutput);
            _output.WriteLine(result.StandardError);

            Assert.Equal(0, result.ExitCode);

            var packagePath = Assert.Single(Directory.GetFiles(packageDirectory, "*.nupkg"));
            using var package = ZipFile.OpenRead(packagePath);
            Assert.Contains(package.Entries, entry => entry.FullName == "staticwebassets/css/generated.css");
        }
        finally
        {
            try
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup for temp test workspaces.
            }
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunDotnet(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start dotnet.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {arguments} did not finish within two minutes.");
        }

        return (process.ExitCode, await standardOutput, await standardError);
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repository root above '{AppContext.BaseDirectory}'. Expected a *.slnx file in an ancestor directory.");
    }
}
