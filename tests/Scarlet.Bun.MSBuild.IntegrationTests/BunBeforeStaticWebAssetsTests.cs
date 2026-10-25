using System.IO.Compression;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit.Abstractions;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

/// <summary>
/// Covers the <c>BunBeforeStaticWebAssets</c> item contract end to end, by running a real build and
/// asserting on what it produces. The point of the target is that files Bun writes into <c>wwwroot</c>
/// during the build - and so are absent when MSBuild evaluates the project - still reach the static web
/// assets pipeline, which is only observable from the package or publish output.
/// </summary>
public class BunBeforeStaticWebAssetsTests
{
    private const string GeneratedAsset = "css/generated.css";

    /// <summary>The version tests install from a private feed, to exercise the real packaged targets.</summary>
    private const string PackedVersion = "0.0.0-integration-test";

    /// <summary>The TFM the generated test projects target; also the intermediate output path segment.</summary>
    private const string TargetFramework = "net10.0";

    private readonly ITestOutputHelper _output;

    public BunBeforeStaticWebAssetsTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task MultiTargetRebuild_AfterDeletingOutputs_NoBuildPackRetainsAssets()
    {
        using var workspace = await CreatePackagedTargetsWorkspace("rebuild-static-web-assets");
        workspace.WriteFile("GeneratedAssetsRcl.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
                <BunRuntimeDirectory>{{SecurityElement.Escape(PackedRuntimeDirectory)}}</BunRuntimeDirectory>
              </PropertyGroup>
              <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
              <ItemGroup><PackageReference Include="Scarlet.Bun.MSBuild" Version="{{PackedVersion}}" PrivateAssets="all" /></ItemGroup>
              <ItemGroup>
                <BunBeforeStaticWebAssets Include="run"><Arguments>first.mjs</Arguments></BunBeforeStaticWebAssets>
                <BunBeforeStaticWebAssets Include="run">
                  <Arguments>build.mjs</Arguments>
                  <Inputs>build.mjs</Inputs>
                  <Outputs>wwwroot/js/generated.js;wwwroot/css/generated.css</Outputs>
                </BunBeforeStaticWebAssets>
              </ItemGroup>
              <Target Name="RecordContent" AfterTargets="ResolveProjectStaticWebAssets" Condition="'$(TargetFramework)' != ''">
                <WriteLinesToFile File="$(IntermediateOutputPath)content.txt" Lines="@(Content)" Overwrite="true" />
              </Target>
            </Project>
            """);
        workspace.WriteFile("first.mjs", "import fs from 'node:fs'; fs.appendFileSync('order.log', 'first,');");
        workspace.WriteFile("build.mjs", """
            import fs from 'node:fs';
            fs.appendFileSync('order.log', 'build,');
            fs.mkdirSync('wwwroot/js', { recursive: true });
            fs.mkdirSync('wwwroot/css', { recursive: true });
            fs.writeFileSync('wwwroot/js/generated.js', 'export const generated = true;');
            fs.writeFileSync('wwwroot/css/generated.css', '.generated { color: red; }');
            """);
        Assert.Equal(0, (await RunDotnet(workspace, "new sln -n RebuildAssets")).ExitCode);
        Assert.Equal(0, (await RunDotnet(workspace, "sln add GeneratedAssetsRcl.csproj")).ExitCode);
        string[] assets = ["js/generated.js", GeneratedAsset];
        string[] targetFrameworks = ["net8.0", "net9.0", "net10.0"];
        string ContentRecord(string tfm) => workspace.PathTo("obj", DotnetCli.Configuration, tfm, "content.txt");
        async Task AssertEachAssetIsContentOnce()
        {
            foreach (var tfm in targetFrameworks)
            {
                var content = await File.ReadAllLinesAsync(ContentRecord(tfm));
                foreach (var asset in assets)
                    Assert.Single(content, item => item.Replace('\\', '/') == $"wwwroot/{asset}");
            }
        }

        var build = $"build RebuildAssets.slnx -c {DotnetCli.Configuration}";
        Assert.Equal(0, (await RunDotnet(workspace, build)).ExitCode);
        Assert.Equal("first,build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));

        // On an incremental build the SDK glob already holds the files, so discovery must not add them again.
        Assert.Equal(0, (await RunDotnet(workspace, build + " --no-restore")).ExitCode);
        Assert.Equal("first,build,first,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
        await AssertEachAssetIsContentOnce();

        foreach (var asset in assets)
            File.Delete(workspace.PathTo("wwwroot", asset));
        File.Delete(workspace.PathTo("order.log"));
        // Clean leaves these behind, so delete them to prove the rebuild's inner builds wrote fresh ones.
        foreach (var tfm in targetFrameworks)
            File.Delete(ContentRecord(tfm));

        Assert.Equal(0, (await RunDotnet(workspace, build + " --no-restore --no-incremental")).ExitCode);
        Assert.Equal("first,build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
        foreach (var asset in assets)
            Assert.True(new FileInfo(workspace.PathTo("wwwroot", asset)).Length > 0);

        // Inspect immediately: another build or a building pack can repair the broken manifests.
        Assert.Equal(0, (await RunDotnet(workspace,
            $"pack GeneratedAssetsRcl.csproj -c {DotnetCli.Configuration} --no-build -o nupkg")).ExitCode);
        Assert.Equal("first,build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
        using var package = ZipFile.OpenRead(Assert.Single(Directory.GetFiles(workspace.PathTo("nupkg"), "*.nupkg")));
        foreach (var asset in assets)
        {
            var entry = Assert.Single(package.Entries, entry => entry.FullName == $"staticwebassets/{asset}");
            Assert.True(entry.Length > 0);
        }

        foreach (var tfm in targetFrameworks)
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(
                workspace.PathTo("obj", DotnetCli.Configuration, tfm, "staticwebassets.build.json")));
            foreach (var asset in assets)
            {
                var manifestAsset = Assert.Single(manifest.RootElement.GetProperty("Assets").EnumerateArray(),
                    item => item.GetProperty("OriginalItemSpec").GetString()?.Replace('\\', '/') == $"wwwroot/{asset}");
                Assert.True(manifestAsset.GetProperty("FileLength").GetInt64() > 0);
            }
        }
        await AssertEachAssetIsContentOnce();

        // Visual Studio's per-framework design-time builds reach static asset discovery through
        // CollectCopyToOutputDirectoryItemDesignTime -> _GetCopyToOutputDirectoryItemsFromThisProject ->
        // AssignTargetPaths, on project load. They must not start Bun, even with an output missing.
        File.Delete(workspace.PathTo("wwwroot", "js/generated.js"));
        Assert.Equal(0, (await RunDesignTimeBuild(workspace, "GeneratedAssetsRcl.csproj", "net8.0")).ExitCode);
        Assert.Equal("first,build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
        Assert.False(File.Exists(workspace.PathTo("wwwroot", "js/generated.js")));

        // dotnet build -f reaches one inner build without the outer one, which still has to run the steps.
        Assert.Equal(0, (await RunDotnet(workspace,
            $"build GeneratedAssetsRcl.csproj -c {DotnetCli.Configuration} --no-restore -f net8.0")).ExitCode);
        Assert.Equal("first,build,first,build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
        Assert.True(new FileInfo(workspace.PathTo("wwwroot", "js/generated.js")).Length > 0);
        await AssertManifestHasAsset(workspace.PathTo("obj", DotnetCli.Configuration, "net8.0", "staticwebassets.build.json"),
            "GeneratedAssetsRcl", "js/generated.js");
    }

    /// <summary>
    /// <c>dotnet run --project App</c> builds a referenced multi-targeted library only through the app's
    /// ProjectReference, which addresses one inner build directly and never the library's outer build.
    /// </summary>
    [Fact]
    public async Task MultiTargetLibrary_BuiltThroughProjectReferences_RunsStepsOnceAndAppsGetAssets()
    {
        using var workspace = await CreatePackagedTargetsWorkspace("project-reference-static-web-assets");
        workspace.WriteFile("Rcl/Rcl.csproj", $$"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
                <BunRuntimeDirectory>{{SecurityElement.Escape(PackedRuntimeDirectory)}}</BunRuntimeDirectory>
              </PropertyGroup>
              <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" /></ItemGroup>
              <ItemGroup><PackageReference Include="Scarlet.Bun.MSBuild" Version="{{PackedVersion}}" PrivateAssets="all" /></ItemGroup>
              <ItemGroup>
                <BunBeforeStaticWebAssets Include="run">
                  <Arguments>build.mjs</Arguments>
                  <Outputs>wwwroot/js/generated.js</Outputs>
                </BunBeforeStaticWebAssets>
              </ItemGroup>
            </Project>
            """);
        // start/end around a pause: two runs overlapping would log start,start,...
        workspace.WriteFile("Rcl/build.mjs", """
            import fs from 'node:fs';
            fs.appendFileSync('order.log', 'start,');
            await new Promise(resolve => setTimeout(resolve, 1000));
            fs.mkdirSync('wwwroot/js', { recursive: true });
            fs.writeFileSync('wwwroot/js/generated.js', 'export const generated = true;');
            fs.appendFileSync('order.log', 'end,');
            """);
        foreach (var (app, tfm) in new[] { ("App10", "net10.0"), ("App8", "net8.0") })
        {
            workspace.WriteFile($"{app}/{app}.csproj", $"""
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>{tfm}</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="..\Rcl\Rcl.csproj" /></ItemGroup>
                </Project>
                """);
            workspace.WriteFile($"{app}/Program.cs", "WebApplication.Create(args).Run();");
        }
        Assert.Equal(0, (await RunDotnet(workspace, "new sln -n Apps")).ExitCode);
        Assert.Equal(0, (await RunDotnet(workspace, "sln add Rcl/Rcl.csproj App10/App10.csproj App8/App8.csproj")).ExitCode);
        Assert.Equal(0, (await RunDotnet(workspace, "restore Apps.slnx")).ExitCode);

        string AppManifest(string app, string tfm) =>
            workspace.PathTo(app, "obj", DotnetCli.Configuration, tfm, "staticwebassets.build.json");
        void ResetGeneratedState()
        {
            if (Directory.Exists(workspace.PathTo("Rcl", "wwwroot")))
                Directory.Delete(workspace.PathTo("Rcl", "wwwroot"), recursive: true);
            File.Delete(workspace.PathTo("Rcl", "order.log"));
        }

        // Only the app is built, as dotnet run does: the library's outer build never runs.
        Assert.Equal(0, (await RunDotnet(workspace,
            $"build App10/App10.csproj -c {DotnetCli.Configuration} --no-restore")).ExitCode);
        Assert.Equal("start,end,", await File.ReadAllTextAsync(workspace.PathTo("Rcl", "order.log")));
        await AssertManifestHasAsset(AppManifest("App10", "net10.0"), "Rcl", "js/generated.js");

        // The whole solution: the library's outer build, three inner builds and two apps each reach the steps,
        // concurrently, and between them must run them exactly once.
        ResetGeneratedState();
        Assert.Equal(0, (await RunDotnet(workspace,
            $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore")).ExitCode);
        Assert.Equal("start,end,", await File.ReadAllTextAsync(workspace.PathTo("Rcl", "order.log")));
        await AssertManifestHasAsset(AppManifest("App10", "net10.0"), "Rcl", "js/generated.js");
        await AssertManifestHasAsset(AppManifest("App8", "net8.0"), "Rcl", "js/generated.js");
        foreach (var tfm in new[] { "net8.0", "net9.0", "net10.0" })
            await AssertManifestHasAsset(workspace.PathTo("Rcl", "obj", DotnetCli.Configuration, tfm, "staticwebassets.build.json"),
                "Rcl", "js/generated.js");

        // Rebuild cleans first, which evaluates and caches the inner builds before anything regenerates.
        ResetGeneratedState();
        Assert.Equal(0, (await RunDotnet(workspace,
            $"build Apps.slnx -c {DotnetCli.Configuration} --no-restore --no-incremental")).ExitCode);
        Assert.Equal("start,end,", await File.ReadAllTextAsync(workspace.PathTo("Rcl", "order.log")));
        await AssertManifestHasAsset(AppManifest("App8", "net8.0"), "Rcl", "js/generated.js");
    }

    [Fact]
    public async Task DesignTimeBuild_DoesNotRunTheSteps()
    {
        // Visual Studio runs a design-time build on project load and after many edits, and it reaches static
        // asset discovery through CollectCopyToOutputDirectoryItemDesignTime -> AssignTargetPaths. Without a
        // DesignTimeBuild guard every one of them runs every step - bun install included.
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            """);
        workspace.WriteFile("build.mjs", AppendToOrderLog("build"));
        Assert.Equal(0, (await RunDotnet(workspace, "restore")).ExitCode);

        var designTime = await RunDesignTimeBuild(workspace, "GeneratedAssetsRcl.csproj", TargetFramework);

        Assert.Equal(0, designTime.ExitCode);
        Assert.False(File.Exists(workspace.PathTo("order.log")), "A design-time build ran a Bun step.");

        // The guard must not cost the real build anything.
        Assert.Equal(0, (await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration} --no-restore")).ExitCode);
        Assert.Equal("build,", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
    }

    [Fact]
    public async Task SingleTargetFrameworkPack_IncludesGeneratedWwwrootFilesAsStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            """);

        await Pack(workspace);

        // staticwebassets/ is what makes the file reachable as _content/<PackageId>/css/generated.css in a
        // consuming app. An absolute Content glob still packs the file, but under content/ and
        // contentFiles/, where no consumer will ever serve it.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task RepeatedPack_DoesNotDuplicateGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            """);

        await Pack(workspace);

        // The second pack evaluates the project with wwwroot already populated, so the SDK's own glob has
        // already claimed the generated file and the target re-adds it on top. Nothing about that path is
        // exercised by a clean build, which is the only case the other tests cover.
        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task WebProjectPublish_FingerprintsGeneratedStaticWebAssets()
    {
        using var workspace = CreateWebApplication();

        var result = await RunDotnet(workspace, $"publish --configuration {DotnetCli.Configuration} --output publish");
        Assert.Equal(0, result.ExitCode);

        var endpoints = Assert.Single(
            Directory.GetFiles(workspace.PathTo("publish"), "*.staticwebassets.endpoints.json"));
        var manifest = await File.ReadAllTextAsync(endpoints);

        // A fingerprinted route proves the file went through the static web assets pipeline rather than
        // being copied to the publish directory as plain content, which is what the README promises.
        Assert.Matches(new Regex("""
            "Route"\s*:\s*"css/generated\.[a-z0-9]+\.css"
            """.Trim()), manifest);
    }

    [Fact]
    public async Task Steps_RunInDeclarationOrder()
    {
        // The whole feature rests on this: `bun install` has to finish before `bun run build.mjs` starts.
        // Ordering falls out of MSBuild's task batching following item declaration order, which is an
        // implementation detail worth pinning rather than assuming.
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>first.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>second.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("first.mjs", AppendToOrderLog("first"));
        workspace.WriteFile("second.mjs", AppendToOrderLog("second"));

        await Pack(workspace);

        // `dotnet pack` runs the steps in both its build and its pack pass, so assert the shape of the log
        // rather than a single pair: what matters is that "second" never precedes "first".
        Assert.Matches("^(first,second,)+$", await File.ReadAllTextAsync(workspace.PathTo("order.log")));
    }

    [Fact]
    public async Task WorkingDirectoryMetadata_RunsTheStepInThatDirectory()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>generate.mjs</Arguments>
              <WorkingDirectory>$(MSBuildProjectDirectory)/tools</WorkingDirectory>
            </BunBeforeStaticWebAssets>
            """);

        File.Delete(workspace.PathTo("build.mjs"));

        // Both the script path and the path it writes to are resolved from the working directory, so the
        // step only produces the asset in the right place if WorkingDirectory was honoured.
        workspace.WriteFile("tools/generate.mjs", WriteGeneratedAsset("../wwwroot/css"));

        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task ContinueOnErrorMetadata_KeepsTheBuildGreenWhenAStepFails()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>fail.mjs</Arguments>
              <ContinueOnError>true</ContinueOnError>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("fail.mjs", "process.exit(3);");

        await Pack(workspace);

        // The failing step is downgraded to a warning, and the step before it still contributed its asset.
        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    [Fact]
    public async Task FailingStep_FailsTheBuildByDefault()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>fail.mjs</Arguments>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("fail.mjs", "process.exit(3);");

        var result = await RunDotnet(workspace, $"pack --configuration {DotnetCli.Configuration} --output nupkg");

        Assert.NotEqual(0, result.ExitCode);
    }

    private static string AppendToOrderLog(string step) =>
        Script($"""fs.appendFileSync("order.log", "{step},");""", WriteGeneratedAssetTo("wwwroot/css"));

    private static string WriteGeneratedAsset(string directory) =>
        Script(WriteGeneratedAssetTo(directory));

    private static string WriteGeneratedAssetTo(string directory) =>
        // The existsSync guard is not defensive padding: bun's recursive mkdirSync throws EEXIST for a
        // relative path that walks up through "..", and the steps run more than once per `dotnet pack`.
        $$"""
        if (!fs.existsSync("{{directory}}")) {
            fs.mkdirSync("{{directory}}", { recursive: true });
        }

        fs.writeFileSync("{{directory}}/generated.css", "body{color:red}");
        """;

    /// <summary>
    /// Wraps script bodies in a single <c>node:fs</c> import; importing it twice is a syntax error.
    /// </summary>
    private static string Script(params string[] bodies) =>
        $"""
        import fs from "node:fs";

        {string.Join(Environment.NewLine, bodies)}
        """;

    /// <summary>
    /// Drives incremental skipping through MSBuild rather than by constructing the task directly, because the
    /// item metadata has to reach the task's parameters for any of it to happen.
    /// </summary>
    /// <remarks>
    /// Deleting the Inputs and Outputs attributes from both targets files leaves every task-level incremental
    /// test passing: the feature degrades to "always runs", which no assertion anywhere notices. This is the
    /// test that fails when that plumbing breaks.
    /// </remarks>
    [Fact]
    public async Task IncrementalMetadata_SkipsUnchangedStepsAndRerunsAfterASourceChange()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountBunRuns(workspace));
        Assert.Equal(0, await BuildAndCountBunRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountBunRuns(workspace));

        // The stamp belongs to the project's real intermediate output, and the exact directory is the
        // assertion: the task's own fallback is obj\Scarlet.Bun, which is still "somewhere under obj with
        // Scarlet.Bun in the path" - so anything looser passes when StampDirectory is not wired through at
        // all, and only the clean below would notice, under a message blaming the wrong thing.
        var stamp = Assert.Single(Directory.GetFiles(
            workspace.PathTo("obj"), "*.stamp", SearchOption.AllDirectories));

        Assert.Equal(
            workspace.PathTo("obj", DotnetCli.Configuration, TargetFramework, "Scarlet.Bun"),
            Path.GetDirectoryName(stamp));

        var clean = await RunDotnet(workspace, $"clean --configuration {DotnetCli.Configuration}");
        Assert.Equal(0, clean.ExitCode);
        Assert.False(File.Exists(stamp), "dotnet clean should remove the incremental stamp.");
    }

    /// <summary>
    /// The last of the documented metadata to reach the task. Unwired, the timeout falls back to
    /// <c>$(BunTimeoutMilliseconds)</c>, which defaults to 0 - no limit - so a step that should have been
    /// killed simply runs to completion and the build goes green.
    /// </summary>
    [Fact]
    public async Task TimeoutMillisecondsMetadata_ShouldKillAStepThatOverruns()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>slow.mjs</Arguments>
              <TimeoutMilliseconds>2000</TimeoutMilliseconds>
            </BunBeforeStaticWebAssets>
            """);

        // Far longer than the timeout, so the outcome cannot turn on scheduling noise.
        workspace.WriteFile("slow.mjs", "await Bun.sleep(120000);");

        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("timed out after 2000ms", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Relative <c>Inputs</c> and <c>Outputs</c> resolve against the project, not the working directory.
    /// </summary>
    /// <remarks>
    /// Needs both halves to mean anything: a step whose <c>WorkingDirectory</c> is somewhere other than the
    /// project, *and* relative paths. With them equal - which is the default, and what every other test here
    /// uses - the task's fallback produces the same answer as the wiring, so dropping
    /// <c>ProjectDirectory</c> from the task call changes nothing observable. Here it changes everything:
    /// the inputs resolve under tools\, are not found, and the step silently stops skipping.
    /// </remarks>
    [Fact]
    public async Task RelativeIncrementalPaths_ShouldResolveAgainstTheProjectNotTheWorkingDirectory()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>generate.mjs</Arguments>
              <WorkingDirectory>$(MSBuildProjectDirectory)/tools</WorkingDirectory>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </BunBeforeStaticWebAssets>
            """);

        File.Delete(workspace.PathTo("build.mjs"));
        workspace.WriteFile("tools/generate.mjs", WriteGeneratedAsset("../wwwroot/css"));
        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountBunRuns(workspace));

        // Skipping at all proves the inputs were found, which only happens from the project directory.
        Assert.Equal(0, await BuildAndCountBunRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountBunRuns(workspace));
    }

    /// <summary>
    /// <c>StampFile</c> is the third half of the incremental contract, and the only one whose absence is
    /// invisible: drop its attribute from the task call and the step still skips, just from the generated
    /// path instead of the requested one. Nothing else notices, so this asserts the location itself.
    /// </summary>
    [Fact]
    public async Task StampFileMetadata_ShouldPutTheStampWhereItAsks()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
              <StampFile>custom/bun-assets.stamp</StampFile>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountBunRuns(workspace));

        // Relative to the project, like every other path in a project file.
        var requested = workspace.PathTo("custom", "bun-assets.stamp");
        Assert.True(File.Exists(requested), $"Expected the stamp at {requested}.");

        // And nowhere else: a generated stamp under obj would mean StampFile was ignored and the step is
        // skipping on a path the project never asked for.
        Assert.Empty(Directory.Exists(workspace.PathTo("obj"))
            ? Directory.GetFiles(workspace.PathTo("obj"), "*.stamp", SearchOption.AllDirectories)
            : []);

        // Still a working stamp, not just a file in the right place.
        Assert.Equal(0, await BuildAndCountBunRuns(workspace));

        workspace.WriteFile("assets/app.js", "// v2");
        Assert.Equal(1, await BuildAndCountBunRuns(workspace));
    }

    /// <summary>
    /// A skipped step still has to leave a correct package behind: the generated files were produced by an
    /// earlier build, and it is the target's Content re-add - not the Bun run - that carries them into the
    /// static web assets pipeline.
    /// </summary>
    [Fact]
    public async Task IncrementalSkip_StillPacksTheGeneratedStaticWebAssets()
    {
        using var workspace = CreateRazorClassLibrary(
            """
            <BunBeforeStaticWebAssets Include="run">
              <Arguments>build.mjs</Arguments>
              <Inputs>assets/app.js</Inputs>
              <Outputs>wwwroot/css/generated.css</Outputs>
            </BunBeforeStaticWebAssets>
            """);

        workspace.WriteFile("assets/app.js", "// v1");

        Assert.Equal(1, await BuildAndCountBunRuns(workspace));
        Assert.Equal(0, await BuildAndCountBunRuns(workspace));

        await Pack(workspace);

        Assert.Equal(1, CountPackageEntries(workspace, $"staticwebassets/{GeneratedAsset}"));
    }

    private async Task<int> BuildAndCountBunRuns(TempWorkspace workspace)
    {
        var result = await RunDotnet(workspace, $"build --configuration {DotnetCli.Configuration} --verbosity normal");
        Assert.Equal(0, result.ExitCode);

        return result.Output.Split('\n').Count(line => line.Contains("Executing: bun ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A Razor Class Library is the strictest case: its wwwroot files have to be packed under
    /// staticwebassets/ for a consuming app to serve them.
    /// </summary>
    private static TempWorkspace CreateRazorClassLibrary(string steps)
    {
        var workspace = TempWorkspace.Create("static-web-assets");

        try
        {
            workspace.WriteFile("build.mjs", WriteGeneratedAsset("wwwroot/css"));
            workspace.WriteFile("GeneratedAssetsRcl.csproj", Project("Microsoft.NET.Sdk.Razor", steps));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static TempWorkspace CreateWebApplication()
    {
        var workspace = TempWorkspace.Create("static-web-assets-web");

        try
        {
            workspace.WriteFile("build.mjs", WriteGeneratedAsset("wwwroot/css"));
            workspace.WriteFile(
                "Program.cs",
                "WebApplication.CreateBuilder(args).Build().Run();");
            workspace.WriteFile(
                "GeneratedAssetsWeb.csproj",
                Project(
                    "Microsoft.NET.Sdk.Web",
                    """
                    <BunBeforeStaticWebAssets Include="run">
                      <Arguments>build.mjs</Arguments>
                    </BunBeforeStaticWebAssets>
                    """,
                    additionalProperties: "<ImplicitUsings>enable</ImplicitUsings>"));

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private static string Project(string sdk, string steps, string additionalProperties = "") =>
        $"""
        <Project Sdk="{sdk}">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <IsPackable>true</IsPackable>
            {additionalProperties}
          </PropertyGroup>

          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
          </ItemGroup>

          <Import Project="{SecurityElement.Escape(DevelopmentTargetsPath)}" />

          <ItemGroup>
        {steps}
          </ItemGroup>
        </Project>
        """;

    private static string DevelopmentTargetsPath => Path.Combine(
        RepositoryRoot.Path, "src", "Scarlet.Bun.MSBuild", "Scarlet.Bun.MSBuild.targets");

    private static string PackedRuntimeDirectory => Path.Combine(
        RepositoryRoot.Path, "src", "Scarlet.Bun.MSBuild", "bin", "runtimes");

    /// <summary>
    /// A workspace that installs the packaged targets rather than importing the development copy. Multi-targeted
    /// projects need this: the development copy depends on ResolveProjectReferences, which an outer build lacks.
    /// </summary>
    private async Task<TempWorkspace> CreatePackagedTargetsWorkspace(string name)
    {
        var workspace = TempWorkspace.Create(name);

        try
        {
            var taskProject = Path.Combine(RepositoryRoot.Path, "src", "Scarlet.Bun.MSBuild", "Scarlet.Bun.MSBuild.csproj");
            var packTask = await RunDotnet(workspace,
                $"pack \"{taskProject}\" --no-build -c {DotnetCli.Configuration} -p:PackageVersion={PackedVersion} -o feed");
            Assert.Equal(0, packTask.ExitCode);

            // A private package cache ensures each test installs the targets just packed, even on repeated runs.
            workspace.WriteFile("nuget.config", """
                <configuration>
                  <config><add key="globalPackagesFolder" value="packages" /></config>
                  <packageSources>
                    <clear />
                    <add key="local" value="feed" />
                    <add key="nuget" value="https://api.nuget.org/v3/index.json" />
                  </packageSources>
                </configuration>
                """);

            return workspace;
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private Task<DotnetResult> RunDesignTimeBuild(TempWorkspace workspace, string project, string targetFramework) =>
        RunDotnet(workspace,
            $"msbuild {project} -t:_GetCopyToOutputDirectoryItemsFromThisProject " +
            $"-p:Configuration={DotnetCli.Configuration} -p:TargetFramework={targetFramework} " +
            "-p:DesignTimeBuild=true -p:BuildingInsideVisualStudio=true");

    /// <summary>
    /// Asserts the static web assets manifest carries the asset once, as a non-empty primary asset of
    /// <paramref name="sourceId"/> - the project that owns it, also when the manifest is a referencing app's.
    /// </summary>
    private static async Task AssertManifestHasAsset(string manifestPath, string sourceId, string relativePath)
    {
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        var asset = Assert.Single(manifest.RootElement.GetProperty("Assets").EnumerateArray(), item =>
            item.GetProperty("SourceId").GetString() == sourceId &&
            item.GetProperty("AssetRole").GetString() == "Primary" &&
            item.GetProperty("OriginalItemSpec").GetString()?.Replace('\\', '/') == $"wwwroot/{relativePath}");
        Assert.True(asset.GetProperty("FileLength").GetInt64() > 0);
    }

    private async Task Pack(TempWorkspace workspace)
    {
        var result = await RunDotnet(workspace, $"pack --configuration {DotnetCli.Configuration} --output nupkg");

        Assert.Equal(0, result.ExitCode);
    }

    private async Task<DotnetResult> RunDotnet(TempWorkspace workspace, string arguments)
    {
        var result = await DotnetCli.Run(workspace.RootDirectory, arguments);
        _output.WriteLine(result.Output);

        return result;
    }

    private static int CountPackageEntries(TempWorkspace workspace, string entryName)
    {
        var packagePath = Assert.Single(Directory.GetFiles(workspace.PathTo("nupkg"), "*.nupkg"));

        using var package = ZipFile.OpenRead(packagePath);

        return package.Entries.Count(entry => entry.FullName == entryName);
    }
}
