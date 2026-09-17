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
│   ├── Scarlet.Bun.Core/              # Shared library (netstandard2.0, IsPackable=false)
│   │   ├── Platform.cs                    # Platform enum (Windows/Linux/macOS x64/ARM64)
│   │   ├── PlatformInfo.cs                # Per-platform RID / archive / executable names
│   │   ├── BunRuntimePack.cs              # The @(BunRuntimePack) item contract (MSBuild-free)
│   │   ├── BunRuntimePackSource.cs        # Item vs legacy-property provenance
│   │   ├── BunRuntimeResolver.cs          # Runtime detection, pack selection and path resolution
│   │   ├── BunDownloader.cs               # Runtime download functionality
│   │   ├── Chmod.cs / IBunLogger.cs       # Platform helpers
│   │   └── Providers/                     # chmod and zip abstractions
│   ├── Scarlet.Bun.Cli/               # `dotnet bun` .NET tool (net10.0)
│   │   ├── README.md                      # Package readme shown on nuget.org
│   │   ├── Program.cs                     # Composition root
│   │   ├── BunCliApplication.cs           # Orchestration, reserved flag, diagnostics
│   │   ├── BunCliResolver.cs              # explicit > embedded > cache > download
│   │   ├── BunCliOptions.cs               # SCARLET_BUN_* configuration, cache locations
│   │   ├── ProcessLauncher.cs             # Argument forwarding, stream inheritance, signals
│   │   └── DiagnosticsReport.cs           # --scarlet-info rendering
│   ├── Scarlet.Bun.MSBuild/           # Main MSBuild task library
│   │   ├── README.md                      # Package readme shown on nuget.org
│   │   ├── BunRunTask.cs                  # Main MSBuild task for executing Bun commands
│   │   ├── BunRuntimePackFactory.cs       # @(BunRuntimePack) item parsing (the MSBuild-coupled half)
│   │   ├── MsBuildBunLogger.cs            # IBunLogger over TaskLoggingHelper
│   │   └── build/
│   │       ├── Scarlet.Bun.MSBuild.props      # MSBuild properties
│   │       └── Scarlet.Bun.MSBuild.targets    # MSBuild targets
│   └── Scarlet.Bun.Runtime.{platform}/  # Platform-specific runtime packages (8 packages)
│       ├── build/                         # MSBuild integration for each runtime
│       │   └── Scarlet.Bun.Runtime.{platform}.props
│       └── {platform}.csproj              # Downloads/packages Bun binary for platform
├── samples/
│   ├── Scarlet.Bun.Sample/            # Sample project using embedded runtimes
│   └── Scarlet.Bun.Sample.Download/   # Sample project using runtime download
├── tests/
│   ├── Scarlet.Bun.MSBuild.Tests/         # Unit tests
│   │   ├── PlatformTests.cs                  # Platform detection tests
│   │   ├── BunRuntimePackTests.cs            # BunRuntimePack item parsing and de-duplication
│   │   ├── BunRuntimeResolverTests.cs        # Runtime resolver tests
│   │   └── BunDownloaderTests.cs             # Runtime downloader tests
│   └── Scarlet.Bun.MSBuild.IntegrationTests/  # Integration tests
│       ├── BunIntegrationTests.cs            # End-to-end Bun execution tests
│       ├── BunRuntimePackDiscoveryTests.cs    # Item + legacy property merging in BunRunTask
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
├── README.md                          # Repository landing page - routes to the per-package READMEs
└── Scarlet.Bun.MSBuild.slnx           # Solution file

```

## Key Components

### 0. Shared Library (`Scarlet.Bun.Core`)

Everything that is not MSBuild-specific lives here: platform detection, `BunRuntimeResolver`,
`BunDownloader`, `BunRuntimePack`, the chmod/zip providers. `netstandard2.0` so the MSBuild task can load
it; `IsPackable=false` because it is never published on its own.

Both shipping packages carry the assembly rather than depending on it: `Scarlet.Bun.MSBuild` packs it into
`tools/netstandard2.0/`, and `Scarlet.Bun.Cli` gets it through its publish output.

**The packing rule, which is easy to break and expensive to discover:** MSBuild resolves a task's
dependencies from the folder the task assembly lives in, so *every* assembly `Scarlet.Bun.MSBuild`
references must also appear as a `<None ... PackagePath="tools/netstandard2.0/" />` item. Miss one and the
task fails to load in every consumer build - while the in-process integration tests stay green, because
they resolve through ordinary project references. `TaskPackagingTests` encodes this rule and fails in
milliseconds; the `package-installation` e2e catches it for real.

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
- Eight separate NuGet packages, one for each supported platform
- Downloads platform-specific Bun binaries during build
- Packages binaries for distribution via NuGet (asset-only: no `lib/` assembly is shipped)
- Each package includes MSBuild integration via `.props` files

### 4a. Runtime Discovery Contract (`BunRuntimePack`)

**This is the extension point — read it before adding a platform.**

Bun runs on the *build host*, not on the project's target RID, so NuGet's RID-graph resolution does not
apply. Instead each runtime package's `build/*.props` contributes one `@(BunRuntimePack)` item
(`Rid`, `RuntimesPath`, `Variant`, `Priority`), the `Bun` target passes `@(BunRuntimePack)` to the task,
and `BunRuntimeResolver.SelectPacks` picks the best match for the host. Consequences:

- **Adding a runtime identifier needs no change to `Scarlet.Bun.MSBuild`.** A new package that emits the
  item is enough - no new task parameter, no new targets line, no version lockstep between packages.
- The same RID can be served by several packs; the highest `Priority` wins, ties break by pack id so the
  outcome never depends on NuGet import order.
- Anyone can point the build at their own Bun by declaring the item in their project file.
- `BunRuntimeDirectory` overrides packs entirely; `BunRuntimeDownload=true` bypasses them.

The older `BunRuntime_<rid>` properties are still set by the runtime packages and still read by the task.
That is deliberate: it keeps new runtime packages working with old task versions and vice versa. Do not
add new RIDs to that property set - it is frozen at the six original platforms. (The two musl runtime
packages added later do not set it at all: no released `Scarlet.Bun.MSBuild` predates them, so there is no
old task version for the property to protect.)

#### Retiring the legacy property contract

It is two deprecations with different lifetimes, and they come out at different times:

| Half | Lives in | Protects | Remove when |
|------|----------|----------|-------------|
| Packages *setting* the property | `<PropertyGroup>` in each runtime package's `build/*.props` | New runtime package + old `Scarlet.Bun.MSBuild` | You drop support for the pre-item major of `Scarlet.Bun.MSBuild`. Cheap enough to keep indefinitely. |
| Task *reading* the property | `BunRunTask.BunRuntime_*`, `CreateLegacyPacks()`, 12 attribute lines across the two `.targets` | Old runtime package + new `Scarlet.Bun.MSBuild` | A **major** version, once the oldest Bun version worth pinning is newer than **1.4.2**, the first runtime package version that emits the item. |

The second half is the long-lived one: runtime packages are versioned by Bun version, so people pin them on
purpose, not out of neglect. Breaking that on a task-package upgrade would be a nasty surprise.

Two things to watch:

- **`ReportDeprecatedPacks` in `BunRunTask` is stage one of the removal.** It logs at `Normal` importance
  when a pack was found only through the property. One release before removal, promote it to
  `Log.LogWarning`. Not sooner - a warning is noise for everyone legitimately pinned to an older Bun.
- **Shipping two packs for one RID forces the issue early.** A property can only hold one path per RID, so
  a baseline *and* a non-baseline `linux-x64` package would silently fight over `BunRuntime_linux_x64`,
  last import winning, with no diagnostic. The item contract handles it via `Priority`. If that day comes,
  drop the legacy `<PropertyGroup>` from at least those packages' props even if the task still reads it -
  a missing property gives a clear error, a wrong one gives a mystery.

Removal checklist: the six task parameters and `CreateLegacyPacks()`/`ReportDeprecatedPacks()`/
`GetLegacyPropertyName()`/`FirstItemAwareRuntimeVersion`, the `BunRuntimePackSource` enum and
`BunRuntimePack.Source`, the six `BunRuntime_*` attributes in both `.targets` files, the legacy
`<PropertyGroup>` in six `build/*.props`, the legacy cases in `BunRuntimePackDiscoveryTests` and
`RuntimePackagePropsTests`, and the notes in `README.md` and this file.

### 5. MSBuild Integration (`build/*.props` & `build/*.targets`)
- Automatically loaded when package is referenced
- Registers the BunRunTask for use in project files
- Provides default properties
- Integrates platform-specific runtime packages

### 6. Command Line Tool (`Scarlet.Bun.Cli`)

A .NET tool (`ToolCommandName=dotnet-bun`, invoked as `dotnet bun ...`) that forwards every argument to
Bun verbatim. Packaged with `RuntimeIdentifiers`, so one `dotnet pack` produces **ten** packages: eight
RID-specific ones with the Bun binary embedded, a portable `any` one that downloads Bun on first use, and
a top-level pointer package.

Things that will bite you if changed carelessly:

- **The version is `$(BunVersion).$(BunCliRevision)`**, both in `Directory.Build.props`. NuGet drops a
  trailing zero, so revision 0 publishes as plain `1.4.2`. Bump the revision to ship a CLI-only fix
  (`1.4.2.1`) and reset it to 0 when `BunVersion` moves - a re-release at an unchanged version is silently
  dropped by the deploy push, which skips duplicates. `deploy.yml` recomputes the same normalisation to
  find the pointer package's filename.
- **Push order is load-bearing.** Every RID package must reach the feed *before* the pointer package, or
  installs fail. A `*.nupkg` glob gets this backwards because `.` sorts before any letter, which is why
  `deploy.yml` pushes the pointer explicitly last.
- **Do not set `PublishTrimmed` / `PublishSingleFile` / `PublishAot`.** Each implies `SelfContained`, which
  would add ~70 MB of .NET runtime on top of a 61-94 MB Bun in every RID package, for no benefit - a
  dotnet tool already needs a .NET install to be invoked.
- **The embedded binary must be chmod'd at run time.** NuGet packages carry no Unix permission bits, so it
  is extracted `0644` and would fail with `EACCES` on first use on Linux and macOS.
- **Arguments are never parsed.** `--scarlet-info` is the single reserved token, honoured only as the first
  argument, with `SCARLET_BUN_PASSTHROUGH=1` as a permanent opt-out. Adding tool-level flags would break
  the promise that anything valid after `bun` is valid after `dotnet bun`.
- `tests/e2e/cli-tool/verify.sh` depends on `SCARLET_BUN_DIAGNOSTICS=1` printing
  `Scarlet.Bun: using Bun at <path>` to stderr, and on `--scarlet-info` reporting `Source ... embedded`.
  Reword either and update that script in the same commit.

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

### CLI Tests

Cover the `dotnet bun` tool. Everything except the literal `Process.Start` is unit-testable, through
`IFileSystem`, `IEnvironmentProvider`, `IProcessLauncher` and an injected base directory - which is how the
Windows, macOS and Linux cache layouts are all covered from a single CI leg:

```bash
dotnet test tests/Scarlet.Bun.Cli.Tests/Scarlet.Bun.Cli.Tests.csproj
```

The most important ones are in `ArgumentForwardingTests`: they assert that the argument list handed to Bun
is reference-equal to what the user typed, for spaces, embedded quotes, trailing backslashes, non-ASCII,
empty strings and `--`.

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
- [ ] **Runtime packages build correctly** - All 8 platform-specific runtime packages compile
- [ ] **Task dependencies are packed** - `tools/netstandard2.0/` in the packed `Scarlet.Bun.MSBuild` nupkg
      contains `Scarlet.Bun.Core.dll` alongside the task and the System.IO.Abstractions assemblies
- [ ] **The staged Bun is the pinned Bun** - `BunBinaryVersionTests` runs the host platform's binary and
      compares `--version` to `$(BunVersion)`
- [ ] **CLI packs to 10 packages** - `dotnet pack src/Scarlet.Bun.Cli` yields eight RID packages, an `any`
      package with no Bun in it, and a pointer package containing only `DotnetToolSettings.xml`

### Common Issues and Solutions

#### Integration Tests Fail
- **Issue:** Bun runtime not found
- **Solution:** Ensure runtime files are in `src/Scarlet.Bun.MSBuild/runtimes/` and are being copied to output

- **Issue:** Dependencies not installed
- **Solution:** Check that `bun install` command works in TestAssets directory

#### "Could not load file or assembly 'Scarlet.Bun.Core'" in a consumer build
- **Issue:** The task assembly was packed without one of its dependencies
- **Solution:** Add a `<None Include="$(OutputPath)\netstandard2.0\<name>.dll" Pack="true" PackagePath="tools/netstandard2.0/" />` item to `Scarlet.Bun.MSBuild.csproj`. `TaskPackagingTests` catches this in milliseconds; the in-process integration tests cannot, because they resolve through project references

#### Bun reports a different version than `$(BunVersion)`
- **Issue:** The staged binary is stale. This shipped once: the download scripts extracted into the project directory and then searched it, found the binary they were about to replace, skipped the move and wrote the version marker anyway
- **Solution:** Delete the `<exe>.version` markers and rebuild. `BunBinaryVersionTests` now fails when this happens. The scripts extract to a temp directory and only write the marker after a successful move

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
