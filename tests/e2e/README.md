# End-to-End (E2E) Tests

This directory contains end-to-end tests that validate the complete package installation and execution flow.

## Directory Structure

```
tests/e2e/
├── package-installation/
│   ├── verify.sh                    # Package installation E2E test script
│   └── templates/                   # Template files for the package install test
├── monorepo-download/
│   ├── verify.sh                    # Shared runtime download E2E test script
│   └── templates/                   # Template files for the monorepo download test
└── README.md                        # This file
```

## Overview

The E2E tests verify that:
1. NuGet packages can be created and packed correctly
2. Packages can be installed from a local source
3. The MSBuild task executes correctly when referenced as a package
4. Bun runtime executes successfully through the MSBuild integration

## Test Scripts

### package-installation/verify.sh

**Purpose**: Validates that the Scarlet.Bun.MSBuild package works correctly when installed from a local NuGet source.

**Usage**:
```bash
./tests/e2e/package-installation/verify.sh <workspace-path> <package-version> <runtime-version>
```

**Arguments**:
- `workspace-path`: The root directory containing the `packages` folder with NuGet packages
- `package-version`: The version of the packages to test (e.g., "0.0.1-ci.26")
- `runtime-version`: The version of the runtime packages to test (e.g., "1.3.6")

**What it does**:
1. Creates a temporary test directory (`/tmp/nuget-verification-<pid>`)
2. Sets up a local NuGet source pointing to the workspace packages
3. Creates a simple .NET console application
4. Adds the Scarlet.Bun.MSBuild package and appropriate runtime package
5. Uses template files to create test project files:
   - `nuget.config` - NuGet configuration with local package source
   - `package.json` - Simple package.json for Bun
   - `build.mjs` - Test script that creates output.txt
   - `TestBunPackage.csproj` - Project file with MSBuild Bun targets
6. Builds the test project (triggers Bun execution via MSBuild)
7. Verifies that Bun executed successfully by checking for output.txt

The runtime package is selected from the `dotnet --info` RID first, which keeps the script aligned with the same `dotnet` host that later runs MSBuild:
- Windows ARM64 uses `Scarlet.Bun.Runtime.windows-aarch64`
- Windows x64 uses `Scarlet.Bun.Runtime.windows-x64-baseline`
- Linux ARM64 uses `Scarlet.Bun.Runtime.linux-aarch64`
- Linux x64 uses `Scarlet.Bun.Runtime.linux-x64-baseline`
- macOS ARM64 uses `Scarlet.Bun.Runtime.darwin-aarch64`
- macOS x64 uses `Scarlet.Bun.Runtime.darwin-x64-baseline`

If the RID cannot be determined, the script falls back to shell-based OS and architecture detection. The script logs the detected `dotnet` RID, shell platform, shell architecture, and selected runtime package before installing packages. Unsupported combinations fail fast with a clear error.

### monorepo-download/verify.sh

**Purpose**: Validates the shared runtime-download path used by `BunRuntimeDownload=true` when multiple projects build in parallel.

This script does not choose a runtime package in bash. Instead, it logs the current `dotnet --info` RID for observability and then relies on `BunRunTask` and `BunRuntimeResolver.GetCurrentPlatform()` to choose the correct runtime for the current host. The scenario specifically exercises the shared download mutex plus atomic executable publication so parallel projects do not observe a partially written Bun binary. After the build, it prints the downloaded runtime path(s) under the shared runtime directory so Windows ARM runs can be inspected directly.

**Template System**:

The script uses template files located in `tests/e2e/package-installation/templates/` with `{{VARIABLE}}` placeholders:
- `{{WORKSPACE_PATH}}` - Repository root path (automatically handles Windows backslashes)
- `{{PACKAGE_VERSION}}` - Package version being tested
- `{{RUNTIME_PACKAGE}}` - Platform-specific runtime package (auto-detected)
- `{{RUNTIME_VERSION}}` - Runtime package version being tested

Template files are processed during execution using `sed` to replace placeholders with actual values. The script automatically escapes backslashes in paths for Windows compatibility, preventing issues with Windows path separators (e.g., `D:\a\path` won't be misinterpreted as escape sequences).

**Exit codes**:
- `0`: Success - package installation and execution worked, or build succeeded with Bun execution warning

**Example**:
```bash
# Run from CI
./tests/e2e/package-installation/verify.sh "$GITHUB_WORKSPACE" "0.0.1-ci.26" "1.3.6"

# Run locally
./tests/e2e/package-installation/verify.sh /path/to/repo "1.0.0-local" "1.3.6"
```

## Running in CI

The E2E tests are integrated into the CI workflow (`.github/workflows/ci.yml`):

1. **Pack NuGet packages** - Creates versioned packages in `./packages`
2. **List generated packages** - Shows what was created
3. **E2E Test - Package installation and verification** - Runs `tests/e2e/package-installation/verify.sh` which:
   - Creates test project
   - Installs packages
   - Detects the correct platform-specific runtime package from the `dotnet` RID, including Windows ARM64
   - Builds project (triggers Bun execution)
   - Verifies Bun execution (warns if it fails)
4. **E2E Test - Monorepo download** - Runs `tests/e2e/monorepo-download/verify.sh` which:
   - Configures `BunRuntimeDownload=true`
   - Relies on task-side platform detection instead of selecting a runtime package in bash
   - Logs the `dotnet` RID and downloaded runtime path(s) for inspection

The test runs on all platforms (Linux, Windows, macOS) to ensure cross-platform compatibility.

**Note**: The script does not clean up test directories when running in CI (`$CI` environment variable is set), allowing for inspection if needed. Test directories are created in `/tmp/nuget-verification-*`.

## Running Locally

To run the E2E tests locally:

```bash
# 1. Build and pack the packages
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj -o ./packages -p:Version=1.0.0-local
dotnet pack src/Scarlet.Bun.Runtime.linux-x64-baseline/Scarlet.Bun.Runtime.linux-x64-baseline.csproj -o ./packages -p:Version=1.0.0-local
# ... pack other runtime packages as needed

# 2. Run the E2E test
./tests/e2e/package-installation/verify.sh "$(pwd)" "1.0.0-local" "1.3.6"
```

## Test Structure

The E2E test creates the following structure in `/tmp/nuget-verification`:

```
/tmp/nuget-verification/
├── nuget.config              # NuGet configuration with local source
└── TestBunPackage/
    ├── TestBunPackage.csproj # Test project with MSBuild targets
    ├── package.json          # Simple package.json for Bun
    ├── build.mjs             # Test script that creates output.txt
    └── output.txt            # Created by build.mjs if successful
```

## Troubleshooting

**Package not found**:
- Ensure packages were created in the `./packages` directory
- Check that the version number matches what was specified

**Bun execution fails**:
- This may be expected in some CI environments
- The test will show a warning but not fail the build
- Check that the correct runtime package for your platform is installed

**Windows-specific issues**:
- If you see "invalid character" errors in NuGet.Config, this is due to path escaping
- The script automatically handles Windows backslashes in paths
- Ensure you're using Git Bash or a compatible shell on Windows
- `package-installation/verify.sh` uses the `dotnet` RID as the source of truth, so Windows ARM64 requires the `windows-aarch64` package to be present in `./packages`
- `monorepo-download/verify.sh` does not install a runtime package directly; it relies on `BunRuntimeDownload=true` and logs the resolved runtime path after build

**Build errors**:
- Check that the package structure is correct
- Verify that all dependencies are included in the packages
- Review the MSBuild diagnostic output

## Future Enhancements

Potential improvements to E2E tests:
- Add more complex scenarios (TypeScript, SCSS, multiple files)
- Test different .NET target frameworks
- Add performance benchmarks
- Test package updates and version conflicts
- Add snapshot testing for output files
