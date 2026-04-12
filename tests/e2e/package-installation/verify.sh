#!/bin/bash
set -e

# End-to-End test script for Scarlet.Bun.MSBuild package installation
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>
#
# Arguments:
#   workspace-path:  Path to the repository root (contains packages folder)
#   package-version: Version of the Scarlet.Bun.MSBuild package to test (e.g., "0.0.1-ci.26")
#   runtime-version: Version of the runtime packages to test (e.g., "1.3.6")
#
# Exit codes:
#   0: Success
#   1: Failure

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    echo "Example: $0 /path/to/repo 0.0.1-ci.26 1.3.6"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

echo "=========================================="
echo "E2E Test: Package Installation"
echo "=========================================="
echo "Workspace: $WORKSPACE_PATH"
echo "Scarlet.Bun.MSBuild Version: $PACKAGE_VERSION"
echo "Runtime Version: $RUNTIME_VERSION"
echo "=========================================="

# Get the directory where this script is located
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES_DIR="$SCRIPT_DIR/templates"

normalize_architecture() {
    local arch="$1"
    arch="$(printf '%s' "$arch" | tr '[:upper:]' '[:lower:]')"

    case "$arch" in
        arm64|aarch64)
            echo "arm64"
            ;;
        x86_64|amd64|x64)
            echo "x64"
            ;;
        *)
            echo "$arch"
            ;;
    esac
}

detect_platform_family() {
    case "$OSTYPE" in
        linux-gnu*)
            echo "linux"
            ;;
        darwin*)
            echo "darwin"
            ;;
        msys*|cygwin*|win32*)
            echo "windows"
            ;;
        *)
            echo "unknown"
            ;;
    esac
}

detect_host_architecture() {
    local detected_arch
    detected_arch="$(uname -m 2>/dev/null || true)"
    detected_arch="$(normalize_architecture "$detected_arch")"

    if [ -n "$detected_arch" ] && [ "$detected_arch" != "unknown" ]; then
        echo "$detected_arch"
        return
    fi

    detected_arch="$(normalize_architecture "${PROCESSOR_ARCHITECTURE:-}")"
    if [ -n "$detected_arch" ]; then
        echo "$detected_arch"
        return
    fi

    detected_arch="$(normalize_architecture "${PROCESSOR_ARCHITEW6432:-}")"
    if [ -n "$detected_arch" ]; then
        echo "$detected_arch"
        return
    fi

    echo "unknown"
}

select_runtime_package() {
    local platform="$1"
    local arch="$2"

    case "$platform:$arch" in
        windows:arm64)
            echo "Scarlet.Bun.Runtime.windows-aarch64"
            ;;
        windows:x64)
            echo "Scarlet.Bun.Runtime.windows-x64-baseline"
            ;;
        linux:arm64)
            echo "Scarlet.Bun.Runtime.linux-aarch64"
            ;;
        linux:x64)
            echo "Scarlet.Bun.Runtime.linux-x64-baseline"
            ;;
        darwin:arm64)
            echo "Scarlet.Bun.Runtime.darwin-aarch64"
            ;;
        darwin:x64)
            echo "Scarlet.Bun.Runtime.darwin-x64-baseline"
            ;;
        *)
            echo ""
            ;;
    esac
}

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
    local runtime_ver_escaped="${RUNTIME_VERSION//\\/\\\\}"

    # Use sed to replace {{VARIABLE}} with actual values
    # Use | as delimiter to avoid issues with forward slashes in paths
    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$package_escaped|g" \
        -e "s|{{RUNTIME_PACKAGE}}|$runtime_escaped|g" \
        -e "s|{{RUNTIME_VERSION}}|$runtime_ver_escaped|g" \
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
PLATFORM_FAMILY="$(detect_platform_family)"
HOST_ARCHITECTURE="$(detect_host_architecture)"
RUNTIME_PACKAGE="$(select_runtime_package "$PLATFORM_FAMILY" "$HOST_ARCHITECTURE")"

if [ -z "$RUNTIME_PACKAGE" ]; then
    echo "Error: Unsupported platform/runtime combination detected."
    echo "OSTYPE: $OSTYPE"
    echo "Platform family: $PLATFORM_FAMILY"
    echo "Architecture: $HOST_ARCHITECTURE"
    exit 1
fi

echo "✓ Runtime detection: platform=$PLATFORM_FAMILY architecture=$HOST_ARCHITECTURE package=$RUNTIME_PACKAGE"

# Add the packages
echo "Adding Scarlet.Bun.MSBuild package..."
dotnet add package Scarlet.Bun.MSBuild --version "$PACKAGE_VERSION"

echo "Adding $RUNTIME_PACKAGE package..."
dotnet add package "$RUNTIME_PACKAGE" --version "$RUNTIME_VERSION"

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
