using Xunit.Abstractions;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

public class BunIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public BunIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BunRunTask_ShouldExecuteBuildScript()
    {
        using var workspace = TestAssetWorkspace.Create(_output);
        var testAssetsDir = workspace.RootDirectory;
        var buildScriptPath = workspace.BuildScriptPath;
        var outputDir = workspace.OutputDirectory;
        var jsOutputFile = Path.Combine(outputDir, "bundle.min.js");
        var cssOutputFile = Path.Combine(outputDir, "style.min.css");

        // Clean up any previous output
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        _output.WriteLine($"Test assets directory: {testAssetsDir}");
        _output.WriteLine($"Build script: {buildScriptPath}");
        
        Assert.True(Directory.Exists(testAssetsDir), $"Test assets directory not found: {testAssetsDir}");
        Assert.True(File.Exists(buildScriptPath), $"Build script not found: {buildScriptPath}");

        // First, install dependencies using Bun
        _output.WriteLine("Installing dependencies with Bun...");
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        _output.WriteLine($"Runtime directory: {runtimesDirectory}");
        
        var installTask = new BunRunTask
        {
            Command = "ci",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        var installResult = installTask.Execute();
        _output.WriteLine($"Install result: {installResult}");
        _output.WriteLine($"Install exit code: {installTask.ExitCode}");
        if (!string.IsNullOrEmpty(installTask.StandardOutput))
        {
            _output.WriteLine($"Install output: {installTask.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(installTask.StandardError))
        {
            _output.WriteLine($"Install error: {installTask.StandardError}");
        }

        Assert.True(installResult, "Bun ci failed");
        Assert.Equal(0, installTask.ExitCode);

        // Act - Execute the build script
        _output.WriteLine("Running build script with Bun...");
        var task = new BunRunTask
        {
            Command = "run",
            Arguments = "build.mjs",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();
        _output.WriteLine($"Build result: {result}");
        _output.WriteLine($"Build exit code: {task.ExitCode}");
        if (!string.IsNullOrEmpty(task.StandardOutput))
        {
            _output.WriteLine($"Build output: {task.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(task.StandardError))
        {
            _output.WriteLine($"Build error: {task.StandardError}");
        }

        // Assert
        Assert.True(result, "Bun run command failed");
        Assert.Equal(0, task.ExitCode);

        // Verify output files were created
        Assert.True(File.Exists(jsOutputFile), $"JS output file not created: {jsOutputFile}");
        Assert.True(File.Exists(cssOutputFile), $"CSS output file not created: {cssOutputFile}");

        // Verify JS bundle content
        var jsContent = File.ReadAllText(jsOutputFile);
        _output.WriteLine($"JS bundle size: {jsContent.Length} bytes");
        Assert.NotEmpty(jsContent);
        Assert.Contains("hello", jsContent); // Should contain minified version of our functions
        Assert.Contains("world", jsContent);

        // Verify CSS bundle content
        var cssContent = File.ReadAllText(cssOutputFile);
        _output.WriteLine($"CSS bundle size: {cssContent.Length} bytes");
        Assert.NotEmpty(cssContent);
        Assert.Contains("body", cssContent);
        Assert.Contains(".button", cssContent);
    }

    [Fact]
    public void BunRunTask_CanBeInvokedAndResolvesRuntime()
    {
        // This test verifies that:
        // 1. The BunRunTask can be instantiated
        // 2. It can resolve the Bun runtime path
        // 3. It attempts to execute Bun (even if Bun crashes, our task works)
        
        using var workspace = TestAssetWorkspace.Create(_output);
        var testAssetsDir = workspace.RootDirectory;
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        
        _output.WriteLine($"Test assets directory: {testAssetsDir}");
        _output.WriteLine($"Runtime directory: {runtimesDirectory}");
        _output.WriteLine($"Current platform: {BunRuntimeResolver.GetCurrentPlatform()}");
        
        // Create a simple task that will try to get Bun version
        var task = new BunRunTask
        {
            Command = "run",
            WorkingDirectory = testAssetsDir,
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output),
            ContinueOnError = true // Don't fail if Bun has issues
        };

        // Execute - this tests that our task can find and attempt to execute Bun
        var result = task.Execute();
        
        _output.WriteLine($"Task execution completed: {result}");
        _output.WriteLine($"Exit code: {task.ExitCode}");
        if (!string.IsNullOrEmpty(task.StandardOutput))
        {
            _output.WriteLine($"Output: {task.StandardOutput}");
        }
        if (!string.IsNullOrEmpty(task.StandardError))
        {
            _output.WriteLine($"Error: {task.StandardError}");
        }
        
        // ContinueOnError=true should keep the task successful even if Bun exits non-zero.
        Assert.True(result, "Task should return true when ContinueOnError is enabled.");
        Assert.NotEqual(-1, task.ExitCode);
        Assert.False(
            string.IsNullOrWhiteSpace(task.StandardOutput) && string.IsNullOrWhiteSpace(task.StandardError),
            "Expected Bun invocation to produce stdout or stderr.");
    }

    [Fact]
    public void BunRunTask_WithInvalidCommand_ShouldFail()
    {
        // Arrange
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new BunRunTask
        {
            Command = "invalid-command-that-does-not-exist-xyz123",
            RuntimeDirectory = runtimesDirectory,
            BuildEngine = new MockBuildEngine(_output)
        };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result);
        Assert.NotEqual(0, task.ExitCode);
    }

    [Fact]
    public void BunRunTask_WithUpToDateInputsAndOutputs_ShouldSkipBeforeResolvingRuntime()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-bun-incremental-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "bundle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "console.log('output');");

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(input, now.AddMinutes(-10));
            File.SetLastWriteTimeUtc(output, now);

            var firstRun = new BunRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(firstRun.Execute());
            Assert.Equal(0, firstRun.ExitCode);

            var buildEngine = new MockBuildEngine(_output);
            var task = new BunRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Equal(0, task.ExitCode);
            Assert.Empty(buildEngine.Errors);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);

            var changedCommandEngine = new MockBuildEngine(_output);
            var changedCommand = new BunRunTask
            {
                Command = "--help",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = input,
                Outputs = output,
                BuildEngine = changedCommandEngine
            };

            Assert.False(changedCommand.Execute());
            Assert.DoesNotContain(changedCommandEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void BunRunTask_WithCaptureOutputDisabled_ShouldNotRetainStdoutOrStderr()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new BunRunTask
        {
            Command = "--version",
            RuntimeDirectory = runtimesDirectory,
            CaptureOutput = false,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(0, task.ExitCode);
        Assert.Null(task.StandardOutput);
        Assert.Null(task.StandardError);
    }

    [Fact]
    public void BunRunTask_WithFailedIncrementalRun_ShouldNotCreateSuccessStamp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-bun-failed-incremental-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "bundle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "partial output");

            var failedRun = new BunRunTask
            {
                Command = "invalid-command-that-does-not-exist-xyz123",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                ContinueOnError = true,
                Inputs = input,
                Outputs = output,
                BuildEngine = new MockBuildEngine(_output)
            };

            Assert.True(failedRun.Execute());
            Assert.NotEqual(0, failedRun.ExitCode);

            var buildEngine = new MockBuildEngine(_output);
            var nextRun = new BunRunTask
            {
                Command = "invalid-command-that-does-not-exist-xyz123",
                WorkingDirectory = tempDir,
                RuntimeDirectory = "invalid-runtime-directory",
                Inputs = input,
                Outputs = output,
                BuildEngine = buildEngine
            };

            Assert.False(nextRun.Execute());
            Assert.DoesNotContain(buildEngine.Messages, message => message.Message?.Contains("outputs are up-to-date", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void BunRunTask_WhenIncrementalStampCannotBeWritten_ShouldKeepSuccessfulBunResult()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"scarlet-bun-stamp-write-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
            var input = Path.Combine(tempDir, "input.js");
            var output = Path.Combine(tempDir, "bundle.js");
            File.WriteAllText(input, "console.log('input');");
            File.WriteAllText(output, "existing output");

            var buildEngine = new MockBuildEngine(_output);
            var task = new BunRunTask
            {
                Command = "--version",
                WorkingDirectory = tempDir,
                RuntimeDirectory = runtimesDirectory,
                Inputs = input,
                Outputs = output,
                StampFile = "invalid\0stamp",
                BuildEngine = buildEngine
            };

            var result = task.Execute();

            Assert.True(result);
            Assert.Equal(0, task.ExitCode);
            Assert.Contains(buildEngine.Messages, message => message.Message?.Contains("Could not write Bun incremental stamp", StringComparison.Ordinal) == true);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void BunRunTask_WithMissingCommand_ShouldFail()
    {
        // Arrange
        var task = new BunRunTask
        {
            Command = null,
            BuildEngine = new MockBuildEngine(_output)
        };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void BunRunTask_WithRuntimeDownloadAndInvalidRuntimePath_ShouldHitDownloadCatch()
    {
        // Forces BunDownloader to throw before any network call (invalid path chars),
        // which is handled by BunRunTask's runtime-download catch block.
        var task = new BunRunTask
        {
            Command = "run",
            BunRuntimeDownload = true,
            RuntimeDirectory = "invalid\0path",
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void BunRunTask_WithoutRuntimeDirectory_UsesPlatformRuntimeProperty()
    {
        // The legacy BunRuntime_<rid> contract is frozen at the six original platforms (see
        // BunRuntimeResolver.PlatformMap and AGENTS.md) - musl was added afterwards and deliberately never
        // got one, so this legacy-path test has nothing to exercise when it runs on a musl host.
        var currentRid = BunRuntimeResolver.GetRuntimeIdentifier(BunRuntimeResolver.GetCurrentPlatform());
        if (currentRid is "linux-musl-x64" or "linux-musl-arm64")
        {
            return;
        }

        var runtimePackagePath = Directory.GetCurrentDirectory();
        var task = new BunRunTask
        {
            Command = "run",
            RuntimeDirectory = null,
            ContinueOnError = true,
            BuildEngine = new MockBuildEngine(_output)
        };

        SetCurrentPlatformRuntimePath(task, runtimePackagePath);

        var result = task.Execute();

        // ContinueOnError keeps task green even if Bun process exits non-zero.
        Assert.True(result);
        Assert.NotEqual(-1, task.ExitCode);
    }

    [Fact]
    public void BunRunTask_WithTimeout_ShouldKillProcessAndFail()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new BunRunTask
        {
            Command = "run",
            Arguments = "build.mjs",
            RuntimeDirectory = runtimesDirectory,
            TimeoutMilliseconds = 50,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.False(result);
    }

    [Fact]
    public void BunRunTask_WithInvalidWorkingDirectory_ShouldHitOuterCatch()
    {
        var runtimesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "runtimes");
        var task = new BunRunTask
        {
            Command = "install",
            RuntimeDirectory = runtimesDirectory,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ContinueOnError = true,
            BuildEngine = new MockBuildEngine(_output)
        };

        var result = task.Execute();

        Assert.True(result);
        Assert.Equal(-1, task.ExitCode);
    }

    private static void SetCurrentPlatformRuntimePath(BunRunTask task, string runtimePackagePath)
    {
        var platform = BunRuntimeResolver.GetCurrentPlatform();
        var rid = BunRuntimeResolver.GetRuntimeIdentifier(platform);

        switch (rid)
        {
            case "win-x64":
                task.BunRuntime_win_x64 = runtimePackagePath;
                break;
            case "win-arm64":
                task.BunRuntime_win_arm64 = runtimePackagePath;
                break;
            case "linux-x64":
                task.BunRuntime_linux_x64 = runtimePackagePath;
                break;
            case "linux-arm64":
                task.BunRuntime_linux_arm64 = runtimePackagePath;
                break;
            case "osx-x64":
                task.BunRuntime_osx_x64 = runtimePackagePath;
                break;
            case "osx-arm64":
                task.BunRuntime_osx_arm64 = runtimePackagePath;
                break;
            default:
                throw new InvalidOperationException($"Unsupported runtime identifier: {rid}");
        }
    }
}
