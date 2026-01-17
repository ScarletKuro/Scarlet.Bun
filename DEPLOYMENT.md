# Deployment Guide

This document describes how to deploy new versions of the Scarlet.Bun.MSBuild NuGet packages.

## Overview

The project consists of 6 NuGet packages:
1. `Scarlet.Bun.MSBuild` - Main MSBuild task package
2. `Scarlet.Bun.Runtime.windows-x64-baseline` - Windows x64 runtime
3. `Scarlet.Bun.Runtime.linux-x64-baseline` - Linux x64 runtime
4. `Scarlet.Bun.Runtime.linux-aarch64` - Linux ARM64 runtime
5. `Scarlet.Bun.Runtime.darwin-x64-baseline` - macOS x64 runtime
6. `Scarlet.Bun.Runtime.darwin-aarch64` - macOS ARM64 runtime

## Prerequisites

Before deploying, ensure you have:
1. A NuGet.org API key with push permissions for the package
2. The API key configured as a GitHub secret named `NUGET_KEY`

### Setting up the NuGet API Key

1. Go to [NuGet.org](https://www.nuget.org) and sign in
2. Go to your account settings → API Keys
3. Create a new API key with "Push" permissions for the Scarlet.Bun.* packages
4. Copy the API key
5. In your GitHub repository:
   - Go to Settings → Secrets and variables → Actions
   - Click "New repository secret"
   - Name: `NUGET_KEY`
   - Value: Paste your API key
   - Click "Add secret"

## Deployment Process

The deployment is automated via GitHub Actions and triggered by pushing a SemVer 2.0 compliant tag.

### Supported Version Formats

The workflow supports SemVer 2.0 version formats, including:
- Release versions: `1.0.0`, `2.1.3`, `3.0.0`
- Pre-release versions: `1.0.0-preview.1`, `1.0.0-rc.1`, `2.0.0-beta.2`
- Build metadata: `1.0.0-preview.1+build.123`

### Steps to Deploy

1. **Ensure all changes are committed and pushed to the main development branch**
   ```bash
   # Switch to your main branch (master, main, etc.)
   git checkout master
   git pull origin master
   ```

2. **Create and push a version tag**
   ```bash
   # For a release version
   git tag 1.0.0
   git push origin 1.0.0

   # For a pre-release version
   git tag 1.0.0-preview.1
   git push origin 1.0.0-preview.1

   # For an RC version
   git tag 1.0.0-rc.1
   git push origin 1.0.0-rc.1
   ```

3. **Monitor the deployment**
   - Go to the "Actions" tab in your GitHub repository
   - Find the "Deploy to NuGet" workflow run
   - Monitor the progress through these stages:
     - Checkout code
     - Setup .NET
     - Extract version from tag
     - Restore dependencies
     - Build projects with version
     - Pack NuGet packages
     - Push to NuGet.org
     - Create and test local verification project

4. **Verify the deployment**
   - Check [NuGet.org](https://www.nuget.org/packages/Scarlet.Bun.MSBuild/) for the new package version
   - It may take a few minutes for the package to appear in search results
   - The workflow includes an automated verification step that creates a test project and confirms the packages work correctly

## What the Workflow Does

The deployment workflow (`.github/workflows/deploy.yml`) performs the following steps:

1. **Runs tests on all platforms** (Ubuntu, Windows, macOS):
   - Restores dependencies
   - Builds the solution
   - Runs all tests with code coverage
   - Only proceeds to deployment if all tests pass

2. **Extracts the version** from the Git tag (e.g., `1.0.0` from `refs/tags/1.0.0`)

3. **Builds all projects** with the specified version:
   ```bash
   dotnet build --configuration Release /p:Version=<version>
   ```

4. **Packs the main MSBuild package**:
   - Creates both `.nupkg` (main package) and `.snupkg` (symbols package)
   - Includes the MSBuild task DLL and build files

5. **Packs all runtime packages**:
   - Downloads Bun binaries for each platform if not already cached
   - Packages the runtime binaries in platform-specific packages
   - Each runtime package contains the Bun executable for its target platform
   - All packages are marked as `DevelopmentDependency=True` to prevent transitive dependencies

6. **Pushes to NuGet.org**:
   - Uploads all `.nupkg` files
   - Uploads all `.snupkg` symbol files
   - Uses `--skip-duplicate` to avoid errors if the version already exists

6. **Verifies the deployment**:
   - Creates a temporary test project
   - References the newly created packages from local build output
   - Executes a test Bun script to confirm everything works
   - Fails the workflow if verification doesn't pass

**Note:** The workflow will only proceed to packaging and deployment if all tests pass on all platforms (Ubuntu, Windows, macOS). This ensures that only tested and verified code is deployed to NuGet.org.

## Troubleshooting

### Tag push doesn't trigger the workflow

- Verify the tag follows SemVer format: `X.Y.Z` or `X.Y.Z-prerelease`
- Check the workflow file for correct tag pattern matching
- Ensure the workflow file is on the branch that receives the tag push

### Build fails during packaging

- Check that all runtime binaries were downloaded successfully
- Verify the `BunVersion` in `Directory.Build.props` is valid
- Check for network issues downloading Bun binaries from GitHub releases

### Push to NuGet fails

- Verify the `NUGET_KEY` secret is set and valid
- Check that the API key has "Push" permissions
- Ensure you're not trying to push a version that already exists (unless using --skip-duplicate)
- Check NuGet.org service status

### Verification fails

- Check the workflow logs for specific error messages
- The verification step tests that:
  - Packages can be installed from local source
  - BunRunTask can find and execute the Bun runtime
  - Bun can execute JavaScript files successfully

## Manual Deployment

If you need to deploy manually (not recommended), follow these steps:

```bash
# Set the version
VERSION="1.0.0"

# Restore and build
dotnet restore
dotnet build --configuration Release /p:Version=$VERSION

# Pack all packages
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Bun.Runtime.windows-x64-baseline/Scarlet.Bun.Runtime.windows-x64-baseline.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Bun.Runtime.linux-x64-baseline/Scarlet.Bun.Runtime.linux-x64-baseline.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Bun.Runtime.linux-aarch64/Scarlet.Bun.Runtime.linux-aarch64.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Bun.Runtime.darwin-x64-baseline/Scarlet.Bun.Runtime.darwin-x64-baseline.csproj --configuration Release --output ./packages /p:Version=$VERSION
dotnet pack src/Scarlet.Bun.Runtime.darwin-aarch64/Scarlet.Bun.Runtime.darwin-aarch64.csproj --configuration Release --output ./packages /p:Version=$VERSION

# Push to NuGet (requires API key)
dotnet nuget push "./packages/*.nupkg" --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push "./packages/*.snupkg" --api-key YOUR_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Version Strategy

Recommended version strategy:
- **Major version (X.0.0)**: Breaking changes, major new features
- **Minor version (0.X.0)**: New features, non-breaking changes
- **Patch version (0.0.X)**: Bug fixes, minor improvements
- **Pre-release (-preview.X)**: Early access, testing
- **Release candidate (-rc.X)**: Final testing before release

## Notes

- All packages must use the same version number
- Symbol packages (`.snupkg`) are uploaded for debugging support
- The workflow uses `--skip-duplicate` to allow re-running failed deployments
- Runtime binaries are downloaded on-demand during build if not present
- The verification step uses the RuntimeDirectory parameter to point to runtime packages
