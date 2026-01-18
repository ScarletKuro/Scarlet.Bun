# End-to-End (E2E) Tests

This directory contains end-to-end tests that validate the complete package installation and execution flow.

## Directory Structure

```
tests/e2e/
├── verify-package-installation.sh   # Main E2E test script
├── templates/                        # Template files for test projects
│   ├── nuget.config.template        # NuGet configuration template
│   ├── package.json.template        # NPM package.json template
│   ├── build.mjs.template           # Bun test script template
│   └── TestBunPackage.csproj.template # Test project file template
└── README.md                         # This file
```

## Overview

The E2E tests verify that:
1. NuGet packages can be created and packed correctly
2. Packages can be installed from a local source
3. The MSBuild task executes correctly when referenced as a package
4. Bun runtime executes successfully through the MSBuild integration

## Test Scripts

### verify-package-installation.sh

**Purpose**: Validates that the Scarlet.Bun.MSBuild package works correctly when installed from a local NuGet source.

**Usage**:
```bash
./verify-package-installation.sh <workspace-path> <package-version>
```

**Arguments**:
- `workspace-path`: The root directory containing the `packages` folder with NuGet packages
- `package-version`: The version of the packages to test (e.g., "0.0.1-ci.26")

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

**Template System**:

The script uses template files located in `tests/e2e/templates/` with `{{VARIABLE}}` placeholders:
- `{{WORKSPACE_PATH}}` - Repository root path
- `{{PACKAGE_VERSION}}` - Package version being tested
- `{{RUNTIME_PACKAGE}}` - Platform-specific runtime package (auto-detected)

Template files are processed during execution to create actual project files with correct values.

**Exit codes**:
- `0`: Success - package installation and execution worked, or build succeeded with Bun execution warning

**Example**:
```bash
# Run from CI
./tests/e2e/verify-package-installation.sh $GITHUB_WORKSPACE "0.0.1-ci.26"

# Run locally
./tests/e2e/verify-package-installation.sh /path/to/repo "1.0.0-local"
```

## Running in CI

The E2E tests are integrated into the CI workflow (`.github/workflows/ci.yml`):

1. **Pack NuGet packages** - Creates versioned packages in `./packages`
2. **List generated packages** - Shows what was created
3. **E2E Test - Package installation and verification** - Runs `verify-package-installation.sh` which:
   - Creates test project
   - Installs packages
   - Builds project (triggers Bun execution)
   - Verifies Bun execution (warns if it fails)

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
./tests/e2e/verify-package-installation.sh $(pwd) "1.0.0-local"
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
