#!/bin/bash
set -e

# End-to-End test for multi-target framework support using a Razor Class Library.
# Proves that the netstandard2.0 MSBuild task loads and resolves runtimes correctly
# when a Blazor/Razor class library multi-targets net8.0, net9.0, and net10.0.
# Also verifies that the packed NuGet package contains static web assets (JS/CSS).
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
echo "E2E Test: Multi-Target Framework (RCL)"
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

detect_dotnet_rid() {
    local dotnet_info
    local rid

    dotnet_info="$(dotnet --info 2>/dev/null || true)"
    rid="$(printf '%s\n' "$dotnet_info" | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n 1)"
    printf '%s' "$rid"
}

detect_host_architecture() {
    local uname_arch
    local processor_arch
    local wow64_arch

    uname_arch="$(uname -m 2>/dev/null || true)"
    uname_arch="$(normalize_architecture "$uname_arch")"
    processor_arch="$(normalize_architecture "${PROCESSOR_ARCHITECTURE:-}")"
    wow64_arch="$(normalize_architecture "${PROCESSOR_ARCHITEW6432:-}")"

    # On Windows, an emulated shell may report x64 even on an ARM64 host.
    # Prefer ARM64 if either native Windows architecture variable reports it.
    if [ "$processor_arch" = "arm64" ] || [ "$wow64_arch" = "arm64" ]; then
        echo "arm64"
        return
    fi

    if [ -n "$uname_arch" ] && [ "$uname_arch" != "unknown" ]; then
        echo "$uname_arch"
        return
    fi

    if [ -n "$wow64_arch" ]; then
        echo "$wow64_arch"
        return
    fi

    if [ -n "$processor_arch" ]; then
        echo "$processor_arch"
        return
    fi

    echo "unknown"
}

select_runtime_package_from_rid() {
    local rid="$1"

    case "$rid" in
        win-arm64)
            echo "Scarlet.Bun.Runtime.windows-aarch64"
            ;;
        win-x64)
            echo "Scarlet.Bun.Runtime.windows-x64-baseline"
            ;;
        linux-arm64)
            echo "Scarlet.Bun.Runtime.linux-aarch64"
            ;;
        linux-x64)
            echo "Scarlet.Bun.Runtime.linux-x64-baseline"
            ;;
        osx-arm64)
            echo "Scarlet.Bun.Runtime.darwin-aarch64"
            ;;
        osx-x64)
            echo "Scarlet.Bun.Runtime.darwin-x64-baseline"
            ;;
        *)
            echo ""
            ;;
    esac
}

select_runtime_package_from_shell() {
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

# Copy a template file without variable substitution
copy_template() {
    local template_file="$1"
    local output_file="$2"

    if [ ! -f "$template_file" ]; then
        echo "Error: Template file not found: $template_file"
        exit 1
    fi

    cp "$template_file" "$output_file"
}

# Create a temporary directory for testing
TEST_DIR="/tmp/multi-tfm-verification-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

echo "✓ Created test directory: $TEST_DIR"

# Create nuget.config from template
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

# Create a Razor Class Library
dotnet new razorclasslib -n TestRclMultiTfm
cd TestRclMultiTfm

echo "✓ Created Razor Class Library"

# Determine the platform-specific runtime package name
PLATFORM_FAMILY="$(detect_platform_family)"
HOST_ARCHITECTURE="$(detect_host_architecture)"
DOTNET_RID="$(detect_dotnet_rid)"
RUNTIME_PACKAGE=""
FALLBACK_RUNTIME_PACKAGE=""

if [ -n "$DOTNET_RID" ]; then
    RUNTIME_PACKAGE="$(select_runtime_package_from_rid "$DOTNET_RID")"
fi

if [ -z "$RUNTIME_PACKAGE" ]; then
    FALLBACK_RUNTIME_PACKAGE="$(select_runtime_package_from_shell "$PLATFORM_FAMILY" "$HOST_ARCHITECTURE")"
    RUNTIME_PACKAGE="$FALLBACK_RUNTIME_PACKAGE"
fi

if [ -z "$RUNTIME_PACKAGE" ] || { [ -n "$DOTNET_RID" ] && [ -z "$(select_runtime_package_from_rid "$DOTNET_RID")" ]; }; then
    echo "Error: Unsupported platform/runtime combination detected."
    echo "dotnet RID: ${DOTNET_RID:-<not detected>}"
    echo "OSTYPE: $OSTYPE"
    echo "Platform family: $PLATFORM_FAMILY"
    echo "Architecture: $HOST_ARCHITECTURE"
    exit 1
fi

if [ -n "$DOTNET_RID" ]; then
    FALLBACK_RUNTIME_PACKAGE="$(select_runtime_package_from_shell "$PLATFORM_FAMILY" "$HOST_ARCHITECTURE")"

    if [ -n "$FALLBACK_RUNTIME_PACKAGE" ] && [ "$FALLBACK_RUNTIME_PACKAGE" != "$RUNTIME_PACKAGE" ]; then
        if [ "$PLATFORM_FAMILY" = "windows" ] && [ "$DOTNET_RID" = "win-arm64" ] && [ "$HOST_ARCHITECTURE" = "x64" ]; then
            echo "Info: shell reports x64, but dotnet RID is win-arm64; using dotnet RID as the source of truth."
        else
            echo "Error: dotnet RID and shell fallback detection disagree."
            echo "dotnet RID: $DOTNET_RID"
            echo "RID package: $RUNTIME_PACKAGE"
            echo "Shell platform: $PLATFORM_FAMILY"
            echo "Shell architecture: $HOST_ARCHITECTURE"
            echo "Shell fallback package: $FALLBACK_RUNTIME_PACKAGE"
            exit 1
        fi
    fi
fi

echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>} shell_platform=$PLATFORM_FAMILY shell_architecture=$HOST_ARCHITECTURE package=$RUNTIME_PACKAGE"

# Add the packages
echo "Adding Scarlet.Bun.MSBuild package..."
dotnet add package Scarlet.Bun.MSBuild --version "$PACKAGE_VERSION"

echo "Adding $RUNTIME_PACKAGE package..."
dotnet add package "$RUNTIME_PACKAGE" --version "$RUNTIME_VERSION"

echo "✓ Packages added successfully"

# Create asset directories
mkdir -p assets/scripts
mkdir -p assets/styles

# Copy asset templates
copy_template "$TEMPLATES_DIR/hello.js.template" "assets/scripts/hello.js"
copy_template "$TEMPLATES_DIR/utils.js.template" "assets/scripts/utils.js"
copy_template "$TEMPLATES_DIR/style.scss.template" "assets/styles/style.scss"
copy_template "$TEMPLATES_DIR/_variables.scss.template" "assets/styles/_variables.scss"
echo "✓ Created source assets (JS + SCSS)"

# Create package.json, bun.lock, and build.mjs from templates
copy_template "$TEMPLATES_DIR/package.json.template" "package.json"
copy_template "$TEMPLATES_DIR/bun.lock.template" "bun.lock"
copy_template "$TEMPLATES_DIR/build.mjs.template" "build.mjs"
echo "✓ Created package.json, bun.lock, and build.mjs"

# Update project file from template (multi-TFM RCL)
process_template "$TEMPLATES_DIR/TestRclMultiTfm.csproj.template" "TestRclMultiTfm.csproj"
echo "✓ Updated project file with multi-target frameworks (net8.0, net9.0, net10.0)"

# Build the project (triggers Bun install + asset build per TFM)
echo ""
echo "Building Razor Class Library (multi-TFM)..."
echo "=========================================="
dotnet build --verbosity minimal
echo ""
echo "=========================================="
echo "Build completed"
echo "=========================================="

# Verification phase
echo ""
echo "=========================================="
echo "Verifying multi-TFM RCL build..."
echo "=========================================="

FAILED=0

# Check that Bun created the bundled assets
if [ -f "wwwroot/js/bundle.min.js" ]; then
    echo "✓ JavaScript bundle created (wwwroot/js/bundle.min.js)"
else
    echo "✗ JavaScript bundle not found"
    FAILED=1
fi

if [ -f "wwwroot/css/style.min.css" ]; then
    echo "✓ CSS bundle created (wwwroot/css/style.min.css)"
else
    echo "✗ CSS bundle not found"
    FAILED=1
fi

# Check that each TFM produced build output
for tfm in net8.0 net9.0 net10.0; do
    if [ -d "bin/Debug/$tfm" ]; then
        echo "✓ $tfm build output directory exists"
    else
        echo "✗ $tfm build output directory not found"
        FAILED=1
    fi
done

# Pack the RCL and verify static web assets in nupkg
echo ""
echo "=========================================="
echo "Packing NuGet package..."
echo "=========================================="
dotnet pack --configuration Debug --output ./nupkg --verbosity minimal

echo ""
echo "=========================================="
echo "Verifying static web assets in NuGet package..."
echo "=========================================="

NUPKG_FILE=$(find ./nupkg -name "*.nupkg" -not -name "*.symbols.nupkg" | head -n 1)

if [ -z "$NUPKG_FILE" ]; then
    echo "✗ No .nupkg file found"
    FAILED=1
else
    echo "Package: $NUPKG_FILE"

    # List nupkg contents and check for static web assets
    NUPKG_CONTENTS=$(unzip -l "$NUPKG_FILE" 2>/dev/null || true)

    if echo "$NUPKG_CONTENTS" | grep -q "bundle\.min\.js"; then
        echo "✓ JavaScript bundle found in NuGet package"
    else
        echo "✗ JavaScript bundle NOT found in NuGet package"
        FAILED=1
    fi

    if echo "$NUPKG_CONTENTS" | grep -q "style\.min\.css"; then
        echo "✓ CSS bundle found in NuGet package"
    else
        echo "✗ CSS bundle NOT found in NuGet package"
        FAILED=1
    fi

    # Show the static web asset paths for inspection
    echo ""
    echo "Static web asset entries in package:"
    echo "$NUPKG_CONTENTS" | grep -E "bundle\.min\.js|style\.min\.css" || echo "(none found)"
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

if [ "$FAILED" -eq 0 ]; then
    echo "✓ E2E multi-TFM RCL test completed successfully"
    exit 0
else
    exit 1
fi
