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
# Normal verbosity so the runtime discovery messages are visible; they are logged at normal importance.
echo ""
echo "Building test project..."
echo "=========================================="
dotnet build --verbosity normal 2>&1 | tee build.log
BUILD_STATUS=${PIPESTATUS[0]}

if [ "$BUILD_STATUS" -ne 0 ]; then
    echo "✗ Build failed with exit code $BUILD_STATUS"
    exit 1
fi

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
echo "Verifying the runtime discovery contract..."
echo "=========================================="

# Proves the whole chain a unit test cannot reach: the packed build/*.props is imported, it contributes
# @(BunRuntimePack), and the Bun target binds it to the task's RuntimePacks parameter.
#
# The two checks below only carry that claim together. The resolver logs the same line whichever contract
# supplied the pack, so the first check proves resolution happened and the second proves the item - not the
# deprecated property - is what supplied it.
CONTRACT_OK=true

if grep -qE "(Using|Selected) Bun runtime pack $RUNTIME_PACKAGE" build.log; then
    echo "✓ Bun was resolved from runtime pack $RUNTIME_PACKAGE"
else
    echo "✗ Expected the build log to report resolving Bun from pack $RUNTIME_PACKAGE"
    echo "  Runtime-related log lines:"
    grep -E "Bun runtime pack|Using Bun at|Runtime packs" build.log || echo "  (none)"
    CONTRACT_OK=false
fi

if grep -q "deprecated BunRuntime_" build.log; then
    echo "✗ That pack came from the deprecated BunRuntime_<rid> property, not the @(BunRuntimePack) item"
    CONTRACT_OK=false
else
    echo "✓ That pack came from the @(BunRuntimePack) item, not the deprecated property"
fi

echo ""
echo "=========================================="
echo "Verifying a project-authored pack override..."
echo "=========================================="

# The README documents declaring your own BunRuntimePack to point the build at a Bun you supply. That path is
# evaluated differently from the package one - the package's props are imported before the project body - so
# only a real build proves a project-authored item merges with the package-provided pack and that Priority
# decides between them. The copy is what makes the two packs distinct: same RID and same directory would be
# de-duplicated, and the package pack (declared first) would win.
RESOLVED_BUN="$(grep -m1 'Using Bun at:' build.log | sed 's/.*Using Bun at: //' | tr -d '\r' | tr '\\' '/')"

if [ -z "$RESOLVED_BUN" ] || [ ! -f "$RESOLVED_BUN" ]; then
    echo "✗ Could not determine the Bun executable resolved by the first build"
    CONTRACT_OK=false
else
    BUN_EXE="$(basename "$RESOLVED_BUN")"
    BUN_RID="$(basename "$(dirname "$(dirname "$RESOLVED_BUN")")")"
    CUSTOM_RUNTIMES="$TEST_DIR/custom-bun/runtimes"

    mkdir -p "$CUSTOM_RUNTIMES/$BUN_RID/native"
    cp "$RESOLVED_BUN" "$CUSTOM_RUNTIMES/$BUN_RID/native/$BUN_EXE"
    chmod +x "$CUSTOM_RUNTIMES/$BUN_RID/native/$BUN_EXE" 2>/dev/null || true

    # MSBuild needs a native path; /tmp/... would resolve to C:\tmp\... on Windows
    if command -v cygpath >/dev/null 2>&1; then
        CUSTOM_RUNTIMES_MSBUILD="$(cygpath -m "$CUSTOM_RUNTIMES")"
    else
        CUSTOM_RUNTIMES_MSBUILD="$CUSTOM_RUNTIMES"
    fi

    echo "✓ Staged a custom Bun at $CUSTOM_RUNTIMES_MSBUILD"

    # Declare the override in the project file, exactly as the README shows. The pack id deliberately sorts
    # after "Scarlet.*" because at equal priority the alphabetically first id wins - so Priority is the only
    # thing that can explain this pack being chosen.
    {
        sed 's|</Project>||' TestBunPackage.csproj
        cat <<EOF
  <ItemGroup>
    <BunRuntimePack Include="Zephyr.Bun.Custom">
      <Rid>$BUN_RID</Rid>
      <RuntimesPath>$CUSTOM_RUNTIMES_MSBUILD</RuntimesPath>
      <Priority>100</Priority>
    </BunRuntimePack>
  </ItemGroup>
</Project>
EOF
    } > TestBunPackage.csproj.new && mv TestBunPackage.csproj.new TestBunPackage.csproj

    dotnet build --target:Rebuild --verbosity normal 2>&1 | tee override.log
    OVERRIDE_STATUS=${PIPESTATUS[0]}

    if [ "$OVERRIDE_STATUS" -ne 0 ]; then
        echo "✗ Build with a project-authored pack failed with exit code $OVERRIDE_STATUS"
        CONTRACT_OK=false
    elif ! grep -qE "Selected Bun runtime pack Zephyr\.Bun\.Custom .* out of 2 candidates" override.log; then
        echo "✗ The project-authored pack did not win over $RUNTIME_PACKAGE"
        grep -E "Bun runtime pack|Using Bun at" override.log || echo "  (no runtime-related log lines)"
        CONTRACT_OK=false
    elif ! grep -q "Using Bun at: .*custom-bun" override.log; then
        echo "✗ The winning pack was reported but a different Bun was executed"
        grep -E "Using Bun at" override.log || echo "  (none)"
        CONTRACT_OK=false
    else
        echo "✓ A project-authored BunRuntimePack with a higher Priority overrode the package-provided pack"
        echo "✓ The custom Bun is the one that actually ran"
    fi
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

if [ "$BUN_SUCCESS" = true ] && [ "$CONTRACT_OK" = true ]; then
    echo "✓ E2E test completed successfully - Bun executed via the BunRuntimePack contract"
    exit 0
else
    exit 1
fi
