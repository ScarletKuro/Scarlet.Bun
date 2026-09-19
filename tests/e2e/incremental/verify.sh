#!/bin/bash
set -e

# End-to-End test for incremental skipping (BunBeforeStaticWebAssets Inputs/Outputs).
#
# Multi-targeted on purpose. These steps run in the outer build, which has no CoreBuild and so never runs
# the incremental clean - the stamp outlives `dotnet clean`. The project deletes its generated wwwroot files
# on clean, the way MudBlazor and similar libraries do, so the only thing that can force a rebuild is the
# Outputs existence check. If that ever stops firing, a clean leaves the assets permanently missing.
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
echo "E2E Test: Incremental skipping (Inputs/Outputs)"
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
        linux-musl-arm64)
            echo "Scarlet.Bun.Runtime.linux-aarch64-musl"
            ;;
        linux-musl-x64)
            echo "Scarlet.Bun.Runtime.linux-x64-musl-baseline"
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
TEST_DIR="/tmp/incremental-verification-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

echo "✓ Created test directory: $TEST_DIR"

# Create nuget.config from template
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config with local package source"

# Create a Razor Class Library
dotnet new razorclasslib -n TestRclIncremental
cd TestRclIncremental

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

# Update project file from template (multi-TFM RCL, incremental)
process_template "$TEMPLATES_DIR/TestRclIncremental.csproj.template" "TestRclIncremental.csproj"
echo "✓ Updated project file with multi-target frameworks and incremental Inputs/Outputs"

# ==========================================================================
# 1. First build: nothing is stamped yet, so the asset build has to run.
# ==========================================================================
echo ""
echo "=========================================="
echo "Build 1: first build"
echo "=========================================="
dotnet build --verbosity normal 2>&1 | tee build1.log
BUILD_STATUS=${PIPESTATUS[0]}

if [ "$BUILD_STATUS" -ne 0 ]; then
    echo "✗ Build failed with exit code $BUILD_STATUS"
    exit 1
fi

FAILED=0

FIRST_RUNS=$(grep -c "Executing: bun run build.mjs" build1.log || true)

if [ "$FIRST_RUNS" -eq 1 ] && [ -f "wwwroot/js/bundle.min.js" ] && [ -f "wwwroot/css/style.min.css" ]; then
    echo "✓ First build ran the asset build once and produced both bundles"
else
    echo "✗ First build ran the asset build $FIRST_RUNS time(s); bundles:" \
         "js=$([ -f wwwroot/js/bundle.min.js ] && echo yes || echo no)" \
         "css=$([ -f wwwroot/css/style.min.css ] && echo yes || echo no)"
    FAILED=1
fi

# ==========================================================================
# 2. Nothing changed: the step must skip.
# ==========================================================================
echo ""
echo "=========================================="
echo "Build 2: nothing changed"
echo "=========================================="
dotnet build --verbosity normal > build2.log 2>&1
UNCHANGED_RUNS=$(grep -c "Executing: bun run build.mjs" build2.log || true)

if [ "$UNCHANGED_RUNS" -eq 0 ]; then
    echo "✓ Asset build was skipped when nothing changed"
else
    echo "✗ Expected the asset build to be skipped, but it ran $UNCHANGED_RUNS time(s)"
    FAILED=1
fi

# ==========================================================================
# 3. An input changed: the step must run again. Touch rather than edit, so this
#    tests the timestamp comparison rather than the build script's own behaviour.
# ==========================================================================
echo ""
echo "=========================================="
echo "Build 3: a source file changed"
echo "=========================================="
touch assets/scripts/hello.js
dotnet build --verbosity normal > build3.log 2>&1
CHANGED_RUNS=$(grep -c "Executing: bun run build.mjs" build3.log || true)

if [ "$CHANGED_RUNS" -eq 1 ]; then
    echo "✓ Touching an input re-ran the asset build"
else
    echo "✗ Expected a changed input to re-run the asset build, but it ran $CHANGED_RUNS time(s)"
    FAILED=1
fi

# ==========================================================================
# 4. The reason this scenario is multi-targeted: `dotnet clean` deletes the
#    generated bundles but cannot delete the stamp, because the outer build has
#    no CoreBuild and so never runs the incremental clean. Only the Outputs
#    existence check can force the rebuild.
# ==========================================================================
echo ""
echo "=========================================="
echo "Build 4: after a clean that deletes the generated assets"
echo "=========================================="
dotnet clean --verbosity minimal > clean.log 2>&1

if [ -f "wwwroot/js/bundle.min.js" ] || [ -f "wwwroot/css/style.min.css" ]; then
    echo "✗ Clean did not delete the generated bundles; the rest of this check would prove nothing"
    FAILED=1
fi

STAMPS_AFTER_CLEAN=$(find obj -name "*.stamp" 2>/dev/null | wc -l)

if [ "$STAMPS_AFTER_CLEAN" -ge 1 ]; then
    echo "✓ Stamp survived the clean ($STAMPS_AFTER_CLEAN), as expected for a multi-targeted project"
else
    echo "  Note: no stamp survived the clean, so this run does not exercise the stale-stamp path"
fi

dotnet build --verbosity normal > build4.log 2>&1
AFTER_CLEAN_RUNS=$(grep -c "Executing: bun run build.mjs" build4.log || true)

if [ "$AFTER_CLEAN_RUNS" -eq 1 ] && [ -f "wwwroot/js/bundle.min.js" ] && [ -f "wwwroot/css/style.min.css" ]; then
    echo "✓ Deleted outputs forced the asset build to re-run, and both bundles are back"
else
    echo "✗ Deleted outputs did not force a rebuild (ran $AFTER_CLEAN_RUNS time(s)); bundles:" \
         "js=$([ -f wwwroot/js/bundle.min.js ] && echo yes || echo no)" \
         "css=$([ -f wwwroot/css/style.min.css ] && echo yes || echo no)"
    FAILED=1
fi

echo ""
echo "=========================================="

cleanup() {
    if [ -n "$TEST_DIR" ] && [ -d "$TEST_DIR" ]; then
        echo "Cleaning up test directory: $TEST_DIR"
        rm -rf "$TEST_DIR"
    fi
}

if [ -z "$CI" ]; then
    cleanup
fi

if [ "$FAILED" -ne 0 ]; then
    echo "✗ Incremental E2E test FAILED"
    exit 1
fi

echo "✓ Incremental E2E test PASSED"
exit 0
