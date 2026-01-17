using Xunit.Abstractions;

namespace Scarlet.Bun.MSBuild.IntegrationTests;

public class BunDownloadIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public BunDownloadIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void BunRunTask_WithRuntimeDownload_ShouldDownloadAndExecuteCommand()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-integration-test-{Guid.NewGuid()}");
        var testAssetsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            _output.WriteLine($"Temp directory: {tempDir}");
            _output.WriteLine($"Test assets directory: {testAssetsDir}");

            var task = new BunRunTask
            {
                Command = "install",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                BunVersionDownload = "1.3.6", // Use a known stable version
                BuildEngine = new MockBuildEngine(_output)
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result, "Task should succeed");
            Assert.Equal(0, task.ExitCode);
            
            // Verify runtime was downloaded
            var platform = BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
            var executableName = BunRuntimeResolver.GetExecutableName(platform);
            var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
            Assert.True(File.Exists(expectedPath), $"Expected runtime to be downloaded at {expectedPath}");
            
            _output.WriteLine($"Runtime downloaded to: {expectedPath}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public void BunRunTask_WithRuntimeDownloadLatestVersion_ShouldDownloadAndExecute()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-integration-test-{Guid.NewGuid()}");
        var testAssetsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            _output.WriteLine($"Temp directory: {tempDir}");

            var task = new BunRunTask
            {
                Command = "install",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                // BunVersionDownload not specified - should download latest
                BuildEngine = new MockBuildEngine(_output)
            };

            // Act
            var result = task.Execute();

            // Assert
            Assert.True(result, "Task should succeed");
            Assert.Equal(0, task.ExitCode);
            
            // Verify runtime was downloaded
            var platform = BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
            var executableName = BunRuntimeResolver.GetExecutableName(platform);
            var expectedPath = Path.Combine(tempDir, runtimeId, "native", executableName);
            Assert.True(File.Exists(expectedPath), $"Expected runtime to be downloaded at {expectedPath}");
            
            _output.WriteLine($"Runtime downloaded to: {expectedPath}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public void BunRunTask_WithRuntimeDownloadAndBuildScript_ShouldExecuteSuccessfully()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-integration-test-{Guid.NewGuid()}");
        var testAssetsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        var buildScriptPath = Path.Combine(testAssetsDir, "build.mjs");
        var outputDir = Path.Combine(testAssetsDir, "output");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            
            // Clean up any previous output
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
            
            _output.WriteLine($"Temp directory: {tempDir}");
            _output.WriteLine($"Test assets directory: {testAssetsDir}");
            _output.WriteLine($"Build script: {buildScriptPath}");

            // First, install dependencies
            _output.WriteLine("Installing dependencies...");
            var installTask = new BunRunTask
            {
                Command = "install",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                BunVersionDownload = "1.3.6",
                BuildEngine = new MockBuildEngine(_output)
            };

            var installResult = installTask.Execute();
            Assert.True(installResult, "Install should succeed");
            Assert.Equal(0, installTask.ExitCode);

            // Then, run the build script
            _output.WriteLine("Running build script...");
            var buildTask = new BunRunTask
            {
                Command = "run",
                Arguments = "build.mjs",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                BunVersionDownload = "1.3.6",
                BuildEngine = new MockBuildEngine(_output)
            };

            // Act
            var buildResult = buildTask.Execute();

            // Assert
            Assert.True(buildResult, "Build should succeed");
            Assert.Equal(0, buildTask.ExitCode);
            
            // Verify output files were created
            var jsOutputFile = Path.Combine(outputDir, "bundle.min.js");
            var cssOutputFile = Path.Combine(outputDir, "style.min.css");
            
            Assert.True(File.Exists(jsOutputFile), $"Expected JavaScript output at {jsOutputFile}");
            Assert.True(File.Exists(cssOutputFile), $"Expected CSS output at {cssOutputFile}");
            
            _output.WriteLine($"JavaScript output created: {jsOutputFile}");
            _output.WriteLine($"CSS output created: {cssOutputFile}");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
            
            if (Directory.Exists(outputDir))
            {
                try
                {
                    Directory.Delete(outputDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public void BunRunTask_WithRuntimeDownloadWithoutRuntimeDirectory_ShouldFail()
    {
        // Arrange
        var testAssetsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        
        var task = new BunRunTask
        {
            Command = "install",
            WorkingDirectory = testAssetsDir,
            BunRuntimeDownload = true,
            // RuntimeDirectory not specified - should fail
            BuildEngine = new MockBuildEngine(_output)
        };

        // Act
        var result = task.Execute();

        // Assert
        Assert.False(result, "Task should fail when RuntimeDirectory is not specified with BunRuntimeDownload=true");
    }

    [Fact(Skip = "Downloads from GitHub - may fail in CI due to network restrictions. Verify manually.")]
    public void BunRunTask_WithRuntimeDownloadSecondCall_ShouldReuseDownloadedRuntime()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"bun-integration-test-{Guid.NewGuid()}");
        var testAssetsDir = Path.Combine(Directory.GetCurrentDirectory(), "TestAssets");
        
        try
        {
            Directory.CreateDirectory(tempDir);
            _output.WriteLine($"Temp directory: {tempDir}");

            // First call - downloads runtime
            var task1 = new BunRunTask
            {
                Command = "install",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                BunVersionDownload = "1.3.6",
                BuildEngine = new MockBuildEngine(_output)
            };

            var result1 = task1.Execute();
            Assert.True(result1, "First task should succeed");
            
            var platform = BunRuntimeResolver.GetCurrentPlatform();
            var runtimeId = BunRuntimeResolver.GetRuntimeIdentifier(platform);
            var executableName = BunRuntimeResolver.GetExecutableName(platform);
            var runtimePath = Path.Combine(tempDir, runtimeId, "native", executableName);
            var fileInfo1 = new FileInfo(runtimePath);
            var firstWriteTime = fileInfo1.LastWriteTimeUtc;
            
            _output.WriteLine($"First call completed, runtime at: {runtimePath}");
            _output.WriteLine($"First write time: {firstWriteTime}");

            // Wait a bit to ensure timestamps would be different if file was rewritten
            System.Threading.Thread.Sleep(100);

            // Second call - should reuse runtime
            var task2 = new BunRunTask
            {
                Command = "install",
                WorkingDirectory = testAssetsDir,
                RuntimeDirectory = tempDir,
                BunRuntimeDownload = true,
                BunVersionDownload = "1.3.6",
                BuildEngine = new MockBuildEngine(_output)
            };

            // Act
            var result2 = task2.Execute();

            // Assert
            Assert.True(result2, "Second task should succeed");
            var fileInfo2 = new FileInfo(runtimePath);
            var secondWriteTime = fileInfo2.LastWriteTimeUtc;
            
            _output.WriteLine($"Second call completed");
            _output.WriteLine($"Second write time: {secondWriteTime}");
            
            // File should not have been rewritten
            Assert.Equal(firstWriteTime, secondWriteTime);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"Cleanup failed: {ex.Message}");
                }
            }
        }
    }
}
