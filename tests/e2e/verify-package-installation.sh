#!/bin/bash
set -e

# End-to-End test script for Scarlet.Bun.MSBuild package installation
# 
# Usage: ./verify-package-installation.sh <workspace-path> <package-version>
#
# Arguments:
#   workspace-path: Path to the repository root (contains packages folder)
#   package-version: Version of the packages to test (e.g., "0.0.1-ci.26")
#
# Exit codes:
#   0: Success
#   1: Failure

if [ $# -ne 2 ]; then
    echo "Usage: $0 <workspace-path> <package-version>"
    echo "Example: $0 /path/to/repo 0.0.1-ci.26"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"

echo "=========================================="
echo "E2E Test: Package Installation"
echo "=========================================="
echo "Workspace: $WORKSPACE_PATH"
echo "Version: $PACKAGE_VERSION"
echo "=========================================="

# Create a temporary directory for testing
TEST_DIR="/tmp/nuget-verification-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

echo "✓ Created test directory: $TEST_DIR"

# Create nuget.config to use local packages
cat > nuget.config << EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="LocalPackages" value="$WORKSPACE_PATH/packages" />
  </packageSources>
</configuration>
EOF

echo "✓ Created nuget.config with local package source"

# Create a simple console app that will use the packages
dotnet new console -n TestBunPackage
cd TestBunPackage

echo "✓ Created test console application"

# Determine the platform-specific runtime package name
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
  RUNTIME_PACKAGE="Scarlet.Bun.Runtime.linux-x64-baseline"
elif [[ "$OSTYPE" == "darwin"* ]]; then
  if [[ $(uname -m) == "arm64" ]]; then
    RUNTIME_PACKAGE="Scarlet.Bun.Runtime.darwin-aarch64"
  else
    RUNTIME_PACKAGE="Scarlet.Bun.Runtime.darwin-x64-baseline"
  fi
else
  RUNTIME_PACKAGE="Scarlet.Bun.Runtime.windows-x64-baseline"
fi

echo "✓ Detected platform runtime package: $RUNTIME_PACKAGE"

# Add the packages
echo "Adding Scarlet.Bun.MSBuild package..."
dotnet add package Scarlet.Bun.MSBuild --version "$PACKAGE_VERSION"

echo "Adding $RUNTIME_PACKAGE package..."
dotnet add package "$RUNTIME_PACKAGE" --version "$PACKAGE_VERSION"

echo "✓ Packages added successfully"

# Create a simple package.json
cat > package.json << 'EOF'
{
  "name": "test-bun-package",
  "version": "1.0.0",
  "dependencies": {}
}
EOF

echo "✓ Created package.json"

# Create a simple build.mjs script
cat > build.mjs << 'EOF'
console.log("Bun is working!");
const fs = require('fs');
fs.writeFileSync('output.txt', 'Build succeeded!');
EOF

echo "✓ Created build.mjs test script"

# Update project file to use Bun target (testing real-world scenario)
cat > TestBunPackage.csproj << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="Scarlet.Bun.MSBuild" Version="$PACKAGE_VERSION">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="$RUNTIME_PACKAGE" Version="$PACKAGE_VERSION">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  
  <Target Name="BunInstall" AfterTargets="Build">
    <MSBuild Projects="\$(MSBuildProjectFullPath)"
             Targets="Bun"
             Properties="BunCommand=install;BunWorkingDirectory=\$(MSBuildProjectDirectory)" />
  </Target>
  
  <!-- Using MSBuild task to pass per-call properties -->
  <Target Name="BunBuildTest" AfterTargets="Build">
    <MSBuild Projects="\$(MSBuildProjectFullPath)"
             Targets="Bun"
             Properties="BunCommand=run;BunArguments=build.mjs;BunWorkingDirectory=\$(MSBuildProjectDirectory)" />
  </Target>
</Project>
EOF

echo "✓ Updated project file with MSBuild Bun targets"

# Build the test project (this should trigger BunRunTask)
echo ""
echo "Building test project..."
echo "=========================================="
dotnet build --verbosity minimal

echo ""
echo "=========================================="
echo "Build completed"
echo "=========================================="

# Verify that the build.mjs created the output file
echo ""
echo "=========================================="
echo "Verifying Bun execution..."
echo "=========================================="

if [ -f "output.txt" ]; then
    echo "✓ Bun executed successfully via NuGet package!"
    echo "Output content:"
    cat output.txt
    BUN_SUCCESS=true
else
    echo "⚠ Bun execution failed - output.txt not found"
    echo "This may be expected in some CI environments where Bun doesn't work"
    BUN_SUCCESS=false
fi

echo ""
echo "=========================================="

# Cleanup function
cleanup() {
    if [ -n "$TEST_DIR" ] && [ -d "$TEST_DIR" ]; then
        echo "Cleaning up test directory: $TEST_DIR"
        rm -rf "$TEST_DIR"
    fi
}

# Only cleanup if not running in CI (to allow inspection if needed)
if [ -z "$CI" ]; then
    cleanup
else
    echo "Running in CI - skipping cleanup to allow inspection"
    echo "Test directory: $TEST_DIR"
fi

if [ "$BUN_SUCCESS" = true ]; then
    echo "✓ E2E test completed successfully - Bun executed"
    exit 0
else
    exit 1
fi
