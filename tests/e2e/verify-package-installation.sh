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

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES_DIR="$SCRIPT_DIR/templates"

# Helper function to process templates by replacing {{VARIABLE}} placeholders
process_template() {
    local template_file="$1"
    local output_file="$2"
    
    if [ ! -f "$template_file" ]; then
        echo "Error: Template file not found: $template_file"
        exit 1
    fi
    
    # Escape backslashes for sed on Windows (Git Bash converts paths like D:\a to D:\\a)
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"
    local package_escaped="${PACKAGE_VERSION//\\/\\\\}"
    local runtime_escaped="${RUNTIME_PACKAGE//\\/\\\\}"
    
    # Use sed to replace {{VARIABLE}} with actual values
    # Use | as delimiter to avoid issues with forward slashes in paths
    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$package_escaped|g" \
        -e "s|{{RUNTIME_PACKAGE}}|$runtime_escaped|g" \
        "$template_file" > "$output_file"
}

# Create a temporary directory for testing
TEST_DIR="/tmp/nuget-verification-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

echo "✓ Created test directory: $TEST_DIR"

# Create nuget.config from template
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
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

# Create package.json from template
process_template "$TEMPLATES_DIR/package.json.template" "package.json"
echo "✓ Created package.json"

# Create build.mjs from template
process_template "$TEMPLATES_DIR/build.mjs.template" "build.mjs"
echo "✓ Created build.mjs test script"

# Update project file from template
process_template "$TEMPLATES_DIR/TestBunPackage.csproj.template" "TestBunPackage.csproj"
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
