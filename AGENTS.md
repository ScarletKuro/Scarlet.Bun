# AGENTS.md

This document provides information for AI agents and developers about the Scarlet.Bun.MSBuild project structure, how to run tests, and verify that nothing is broken.

## Project Overview

This is an MSBuild task package that integrates Bun (a fast JavaScript runtime) into .NET build processes. It allows developers to execute Bun commands as part of their .NET project builds.

## Understanding Bun

**IMPORTANT: Before making any changes related to Bun functionality, capabilities, or commands, you MUST first consult the comprehensive Bun documentation.**

The complete Bun documentation for LLMs is available at `.github/agents/bun-llms-full.txt`. This file contains:
- Complete API reference for all Bun commands and features
- Usage examples and best practices
- Performance characteristics and optimization techniques
- Platform-specific behavior and compatibility notes
- Build, bundler, test runner, and runtime capabilities

**When to consult the Bun documentation:**
- Before implementing or modifying any Bun command execution
- When adding new Bun features or capabilities to the MSBuild task
- When troubleshooting Bun-related issues or errors
- When optimizing Bun command parameters or flags
- When updating integration tests that use Bun commands

This ensures that any Bun-related implementations leverage the full capabilities of the tool and follow recommended practices.

## Understanding MSBuild

**⚠️ CRITICAL: Before making ANY changes related to MSBuild tasks, targets, properties, or build logic, you MUST first read the comprehensive MSBuild documentation. This is an absolute requirement.**

The complete MSBuild documentation for LLMs is available at `.github/agents/msbuild-llms-full.txt`. This file contains:
- MSBuild architecture, properties, items, targets, and tasks
- Custom task development and best practices
- Dependency management (CopyLocalLockFileAssemblies, PrivateAssets)
- Build process and lifecycle understanding
- Inline tasks with RoslynCodeTaskFactory
- Common patterns for multi-platform support and file system abstraction
- Troubleshooting and debugging techniques
- Testing strategies with MockBuildEngine

**This documentation MUST be read before:**
- Creating or modifying MSBuild tasks (BunRunTask, custom tasks)
- Adding package dependencies to MSBuild tasks
- Working with .targets or .props files
- Debugging "Could not load assembly" or other build errors
- Implementing custom build logic
- Packaging MSBuild tasks for NuGet
- Understanding why CopyLocalLockFileAssemblies is used

**Critical MSBuild concepts for this project:**
- How MSBuild loads and executes custom tasks in a separate context
- Why task dependencies must be explicitly copied with CopyLocalLockFileAssemblies
- Proper error handling and logging in custom tasks
- Testing custom tasks with MockBuildEngine
- Using IFileSystem abstraction for testable file operations
- InternalsVisibleTo for exposing internal methods to tests

## Project Structure

```
/
├── .github/
│   └── agents/                        # AI agent resources
│       ├── bun-llms-full.txt              # Comprehensive Bun documentation for LLMs
│       ├── msbuild-llms-full.txt          # Comprehensive MSBuild documentation for LLMs
│       └── README.md                      # Documentation about agent resources
├── build/
│   └── BunRuntime.targets             # Shared MSBuild targets for runtime packages
├── src/
│   ├── Scarlet.Bun.MSBuild/           # Main MSBuild task library
│   │   ├── Platform.cs                    # Platform enum (Windows/Linux/macOS x64/ARM64)
│   │   ├── BunRuntimeResolver.cs          # Runtime detection and path resolution
│   │   ├── BunRunTask.cs                  # Main MSBuild task for executing Bun commands
│   │   ├── BunDownloader.cs               # Runtime download functionality
│   │   └── build/
│   │       ├── Scarlet.Bun.MSBuild.props      # MSBuild properties
│   │       └── Scarlet.Bun.MSBuild.targets    # MSBuild targets
│   └── Scarlet.Bun.Runtime.{platform}/  # Platform-specific runtime packages (5 packages)
│       ├── build/                         # MSBuild integration for each runtime
│       │   └── Scarlet.Bun.Runtime.{platform}.props
│       └── {platform}.csproj              # Downloads/packages Bun binary for platform
├── samples/
│   ├── Scarlet.Bun.Sample/            # Sample project using embedded runtimes
│   └── Scarlet.Bun.Sample.Download/   # Sample project using runtime download
├── tests/
│   ├── Scarlet.Bun.MSBuild.Tests/         # Unit tests
│   │   ├── PlatformTests.cs                  # Platform detection tests
│   │   ├── BunRuntimeResolverTests.cs        # Runtime resolver tests
│   │   └── BunDownloaderTests.cs             # Runtime downloader tests
│   └── Scarlet.Bun.MSBuild.IntegrationTests/  # Integration tests
│       ├── BunIntegrationTests.cs            # End-to-end Bun execution tests
│       ├── BunDownloadIntegrationTests.cs    # Runtime download integration tests
│       ├── MockBuildEngine.cs                # Mock MSBuild engine for testing
│       └── TestAssets/                       # Test files for integration tests
│           ├── build.mjs                     # Sample Bun build script
│           ├── package.json                  # Node dependencies
│           ├── scripts/                      # Sample JS files
│           └── styles/                       # Sample SCSS files
├── tools/
│   ├── download-bun.sh                # Bash script to download Bun runtime
│   └── download-bun.ps1               # PowerShell script to download Bun runtime
├── AGENTS.md                          # This file - guide for AI agents
├── README.md                          # User-facing documentation
└── Scarlet.Bun.MSBuild.slnx           # Solution file

```

## Key Components

### 1. Platform Detection (`Platform.cs` & `BunRuntimeResolver.cs`)
- Detects the current OS and architecture (Windows/Linux/macOS, x64/ARM64)
- Maps platforms to their corresponding Bun runtime directories
- Resolves the path to the appropriate Bun executable
- Sets execute permissions on Unix systems

### 2. MSBuild Task (`BunRunTask.cs`)
- Inherits from `Microsoft.Build.Utilities.Task`
- Executes Bun commands with configurable parameters
- Captures stdout/stderr
- Supports timeout and error handling
- Works with both embedded runtimes and downloaded runtimes

### 3. Runtime Downloader (`BunDownloader.cs`)
- Downloads Bun runtime from GitHub releases
- Caches downloaded runtimes to avoid re-downloading
- Supports version-specific downloads
- Validates runtime availability before download

### 4. Platform-Specific Runtime Packages (`Scarlet.Bun.Runtime.{platform}`)
- Five separate NuGet packages, one for each supported platform
- Downloads platform-specific Bun binaries during build
- Packages binaries for distribution via NuGet
- Each package includes MSBuild integration via `.props` files

### 5. MSBuild Integration (`build/*.props` & `build/*.targets`)
- Automatically loaded when package is referenced
- Registers the BunRunTask for use in project files
- Provides default properties
- Integrates platform-specific runtime packages

## How to Build

```bash
# Build the entire solution
dotnet build

# Build specific project
dotnet build src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj

# Build with specific configuration
dotnet build --configuration Release
```

## How to Run Tests

### Unit Tests

Unit tests verify the core functionality without executing actual Bun commands:

```bash
# Run all unit tests
dotnet test tests/Scarlet.Bun.MSBuild.Tests/Scarlet.Bun.MSBuild.Tests.csproj

# Run with detailed output
dotnet test tests/Scarlet.Bun.MSBuild.Tests/Scarlet.Bun.MSBuild.Tests.csproj --verbosity normal

# Run specific test
dotnet test tests/Scarlet.Bun.MSBuild.Tests/Scarlet.Bun.MSBuild.Tests.csproj --filter "FullyQualifiedName~PlatformTests"
```

**Expected Result:** All unit tests should pass. They test:
- Platform detection for all supported platforms
- Runtime directory name mapping
- Executable name resolution
- Path resolution logic

### Integration Tests

Integration tests execute actual Bun commands and verify real-world scenarios:

```bash
# Run all integration tests
dotnet test tests/Scarlet.Bun.MSBuild.IntegrationTests/Scarlet.Bun.MSBuild.IntegrationTests.csproj

# Run with detailed output
dotnet test tests/Scarlet.Bun.MSBuild.IntegrationTests/Scarlet.Bun.MSBuild.IntegrationTests.csproj --verbosity normal
```

**What integration tests do:**
1. **BunRunTask_ShouldExecuteBuildScript:**
   - Installs npm dependencies using `bun install`
   - Executes the `build.mjs` script using `bun run`
   - Verifies bundled JavaScript output file is created
   - Verifies compiled CSS output file is created
   - Checks that output contains expected content

2. **BunRunTask_WithInvalidCommand:**
   - Tests error handling with invalid commands
   - Verifies task fails gracefully

3. **BunRunTask_WithMissingCommand:**
   - Tests parameter validation
   - Verifies task fails when required parameters are missing

**Expected Result:** All integration tests should pass, demonstrating that:
- Bun executable is found and can be executed
- Dependencies can be installed
- Build scripts execute successfully
- Output files are created correctly

### Run All Tests

```bash
# Run all tests in the solution
dotnet test

# Run with code coverage (if configured)
dotnet test --collect:"XPlat Code Coverage"
```

## How to Verify Nothing Is Broken

### Quick Verification

```bash
# 1. Clean build
dotnet clean
dotnet build

# 2. Run all tests
dotnet test

# 3. Create package
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj
```

If all three commands succeed, the project is in good shape.

### Detailed Verification Checklist

- [ ] **Build succeeds** - `dotnet build` completes without errors
- [ ] **All unit tests pass** - Run `dotnet test tests/Scarlet.Bun.MSBuild.Tests/`
- [ ] **All integration tests pass** - Run `dotnet test tests/Scarlet.Bun.MSBuild.IntegrationTests/`
  - Dependencies are installed
  - Build script executes
  - Output files are created
  - Content is minified/bundled correctly
- [ ] **Package creation succeeds** - `dotnet pack` creates .nupkg file
- [ ] **No unexpected files in source control** - Check `git status`
- [ ] **Runtime packages build correctly** - All 5 platform-specific runtime packages compile

### Common Issues and Solutions

#### Integration Tests Fail
- **Issue:** Bun runtime not found
- **Solution:** Ensure runtime files are in `src/Scarlet.Bun.MSBuild/runtimes/` and are being copied to output

- **Issue:** Dependencies not installed
- **Solution:** Check that `bun install` command works in TestAssets directory

#### Build Warnings
- **Issue:** NU1903 warnings about Microsoft.Build packages
- **Solution:** These are expected. MSBuild packages have known vulnerabilities but are used with `PrivateAssets=all` so they won't affect consumers

#### Platform-Specific Issues
- **Issue:** Tests fail on specific platform
- **Solution:** Verify the correct runtime binary exists for that platform in runtimes folder

## Making Changes

### Before Making Changes
1. Run tests to establish baseline: `dotnet test`
2. Note any existing warnings or failures

### After Making Changes
1. Build: `dotnet build`
2. Run affected tests
3. Run full test suite: `dotnet test`
4. Verify no new warnings introduced
5. Test package creation: `dotnet pack`

### Testing Changes Locally

To test the package in another project:

```bash
# 1. Create package
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj -o ./packages

# 2. In your test project, add local source
dotnet nuget add source /path/to/Scarlet.Bun.MSBuild/packages -n LocalBun

# 3. Reference the package
dotnet add package Scarlet.Bun.MSBuild
```

## CI/CD Considerations

When setting up CI/CD:
1. Ensure all tests run on target platforms (Windows, Linux, macOS)
2. Archive test results and logs
3. Create and publish packages on successful builds
4. Test package installation in a clean environment

## Dependencies

### Runtime Dependencies
- Microsoft.Build (17.12.6) - MSBuild framework
- Microsoft.Build.Tasks.Core (17.12.6) - MSBuild task infrastructure

### Test Dependencies
- xUnit - Test framework
- Microsoft.NET.Test.Sdk - Test runner

### Integration Test Dependencies (via Bun/npm)
- terser - JavaScript minification
- sass - SCSS compilation

## Notes for AI Agents

- **CRITICAL:** Before working on Bun-related functionality, read `.github/agents/bun-llms-full.txt` to understand Bun's full capabilities and proper usage
- This project uses **netstandard2.0** for maximum compatibility
- Runtime binaries are large (~100MB each) and should not be modified
- Integration tests require actual Bun execution, so they're slower than unit tests
- The project uses xUnit for testing
- MSBuild packages have security warnings - this is expected and acceptable for build-time tools
- Always run both unit and integration tests before claiming success
- The package is designed as a development dependency (`DevelopmentDependency=true`)
- When implementing new Bun features, verify against the official Bun documentation in `.github/agents/bun-llms-full.txt` to ensure correctness
