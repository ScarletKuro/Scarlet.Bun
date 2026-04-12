#!/bin/bash
set -e

# End-to-End test for monorepo parallel download scenario.
# Two projects share the same BunRuntimeDirectory and build in parallel.
# The named mutex in BunDownloader.DownloadRuntime prevents "process is busy" errors.
#
# Usage: ./verify.sh <workspace-path> <package-version> <bun-version>
#
# Arguments:
#   workspace-path:  Path to the repository root (contains packages folder)
#   package-version: Version of the Scarlet.Bun.MSBuild package to test (e.g., "0.0.1-ci.26")
#   bun-version:     Bun version to download (e.g., "1.3.6")
#
# Exit codes:
#   0: Success
#   1: Failure

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <bun-version>"
    echo "Example: $0 /path/to/repo 0.0.1-ci.26 1.3.6"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
BUN_VERSION="$3"

echo "=========================================="
echo "E2E Test: Monorepo Download"
echo "=========================================="
echo "Workspace: $WORKSPACE_PATH"
echo "Scarlet.Bun.MSBuild Version: $PACKAGE_VERSION"
echo "Bun Version: $BUN_VERSION"
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
    local bun_ver_escaped="${BUN_VERSION//\\/\\\\}"
    local shared_runtime_escaped="${SHARED_RUNTIME_DIR//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$package_escaped|g" \
        -e "s|{{BUN_VERSION}}|$bun_ver_escaped|g" \
        -e "s|{{SHARED_RUNTIME_DIR}}|$shared_runtime_escaped|g" \
        "$template_file" > "$output_file"
}

# Create a temporary directory for testing
TEST_DIR="/tmp/monorepo-download-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

# Shared runtime directory — both projects download to the same location
SHARED_RUNTIME_DIR="$TEST_DIR/tools"
mkdir -p "$SHARED_RUNTIME_DIR"

echo "✓ Created test directory: $TEST_DIR"
echo "✓ Shared runtime directory: $SHARED_RUNTIME_DIR"

# Create nuget.config at solution root
process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
echo "✓ Created nuget.config"

# Create Directory.Build.props at solution root (shared BunRuntimeDownload config)
process_template "$TEMPLATES_DIR/Directory.Build.props.template" "Directory.Build.props"
echo "✓ Created Directory.Build.props with shared download config"

# Create App1
mkdir -p App1
dotnet new console -n App1 -o App1 --force
process_template "$TEMPLATES_DIR/App.csproj.template" "App1/App1.csproj"
process_template "$TEMPLATES_DIR/build.mjs.template" "App1/build.mjs"
process_template "$TEMPLATES_DIR/package.json.template" "App1/package.json"
echo "✓ Created App1"

# Create App2
mkdir -p App2
dotnet new console -n App2 -o App2 --force
process_template "$TEMPLATES_DIR/App.csproj.template" "App2/App2.csproj"
process_template "$TEMPLATES_DIR/build.mjs.template" "App2/build.mjs"
process_template "$TEMPLATES_DIR/package.json.template" "App2/package.json"
echo "✓ Created App2"

# Create solution and add both projects
dotnet new sln -n MonorepoTest
dotnet sln add App1/App1.csproj
dotnet sln add App2/App2.csproj
echo "✓ Created solution with App1 and App2"

# Build the solution — MSBuild will build both projects in parallel.
# Without the mutex, this would fail with "process is busy" errors
# when both try to download the same Bun runtime simultaneously.
echo ""
echo "Building solution (parallel)..."
echo "=========================================="
dotnet build --verbosity minimal
echo ""
echo "=========================================="
echo "Build completed"
echo "=========================================="

# Verify that both projects produced output
echo ""
echo "=========================================="
echo "Verifying Bun execution..."
echo "=========================================="

FAILED=0

if [ -f "App1/output.txt" ]; then
    echo "✓ App1: Bun executed successfully"
    cat App1/output.txt
else
    echo "✗ App1: output.txt not found"
    FAILED=1
fi

if [ -f "App2/output.txt" ]; then
    echo "✓ App2: Bun executed successfully"
    cat App2/output.txt
else
    echo "✗ App2: output.txt not found"
    FAILED=1
fi

# Verify that the shared runtime directory was used (only one copy downloaded)
RUNTIME_COUNT=$(find "$SHARED_RUNTIME_DIR" -type f \( -name "bun" -o -name "bun.exe" \) | wc -l)
echo ""
echo "Runtime binaries in shared directory: $RUNTIME_COUNT"
if [ "$RUNTIME_COUNT" -ge 1 ]; then
    echo "✓ Shared runtime directory used correctly"
    find "$SHARED_RUNTIME_DIR" -type f \( -name "bun" -o -name "bun.exe" \) -ls
else
    echo "✗ No runtime found in shared directory"
    FAILED=1
fi

echo ""
echo "=========================================="

# Cleanup
cleanup() {
    if [ -n "$TEST_DIR" ] && [ -d "$TEST_DIR" ]; then
        echo "Cleaning up test directory: $TEST_DIR"
        rm -rf "$TEST_DIR"
    fi
}

if [ -z "$CI" ]; then
    cleanup
else
    echo "Running in CI - skipping cleanup to allow inspection"
    echo "Test directory: $TEST_DIR"
fi

if [ "$FAILED" -eq 0 ]; then
    echo "✓ E2E monorepo download test completed successfully"
    exit 0
else
    exit 1
fi
