#!/bin/bash
set -e

# End-to-End test for the Scarlet.Bun.Cli .NET tool.
#
# Proves the claim that justifies the package existing: the platform-specific tool package carries its own
# Bun binary, so `dotnet tool restore` followed by `dotnet bun ...` works with no network access at all.
#
# Usage: ./verify.sh <workspace-path> <package-version> <runtime-version>
#
# Arguments:
#   workspace-path:  Path to the repository root (contains packages folder)
#   package-version: Version of the Scarlet.Bun.MSBuild package (unused here, kept for a uniform signature)
#   runtime-version: Version of the CLI/runtime packages, which is also the Bun version (e.g. "1.4.2")
#
# Exit codes:
#   0: Success
#   1: Failure

if [ $# -ne 3 ]; then
    echo "Usage: $0 <workspace-path> <package-version> <runtime-version>"
    echo "Example: $0 /path/to/repo 0.0.1-ci.26 1.4.2"
    exit 1
fi

WORKSPACE_PATH="$1"
PACKAGE_VERSION="$2"
RUNTIME_VERSION="$3"

echo "=========================================="
echo "E2E Test: Scarlet.Bun.Cli (dotnet tool)"
echo "=========================================="
echo "Workspace: $WORKSPACE_PATH"
echo "Expected Bun Version: $RUNTIME_VERSION"
echo "=========================================="

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEMPLATES_DIR="$SCRIPT_DIR/templates"

detect_dotnet_rid() {
    local dotnet_info
    local rid

    dotnet_info="$(dotnet --info 2>/dev/null || true)"
    rid="$(printf '%s\n' "$dotnet_info" | sed -n 's/^[[:space:]]*RID:[[:space:]]*//p' | head -n 1)"
    printf '%s' "$rid"
}

process_template() {
    local template_file="$1"
    local output_file="$2"

    if [ ! -f "$template_file" ]; then
        echo "Error: Template file not found: $template_file"
        exit 1
    fi

    # Escape backslashes for sed on Windows (Git Bash converts paths like D:\a to D:\\a)
    local workspace_escaped="${WORKSPACE_PATH//\\/\\\\}"

    sed -e "s|{{WORKSPACE_PATH}}|$workspace_escaped|g" \
        -e "s|{{PACKAGE_VERSION}}|$PACKAGE_VERSION|g" \
        -e "s|{{RUNTIME_VERSION}}|$RUNTIME_VERSION|g" \
        "$template_file" > "$output_file"
}

TEST_DIR="/tmp/cli-tool-verification-$$"
mkdir -p "$TEST_DIR"
cd "$TEST_DIR"

echo "✓ Created test directory: $TEST_DIR"

# A private package cache is what lets this script say where the Bun that ran came from, and guarantees
# nothing is served out of a warm machine-wide cache from an earlier run.
export NUGET_PACKAGES="$TEST_DIR/nuget-packages"

process_template "$TEMPLATES_DIR/nuget.config.template" "nuget.config"
process_template "$TEMPLATES_DIR/hello.js.template" "hello.js"
process_template "$TEMPLATES_DIR/fail.js.template" "fail.js"
echo "✓ Created nuget.config and test scripts"

DOTNET_RID="$(detect_dotnet_rid)"
echo "✓ Runtime detection: dotnet_rid=${DOTNET_RID:-<not detected>}"

FAILED=0

# Deliberately no --version: the local feed holds exactly the package that was just built, and the CLI
# version is $(BunVersion).$(BunCliRevision) rather than the runtime version this script is handed. Asking
# for "whatever we just packed" and then asserting the Bun it produces is both simpler and stricter than
# guessing the version string here.
dotnet new tool-manifest > /dev/null
dotnet tool install Scarlet.Bun.Cli
echo "✓ Installed Scarlet.Bun.Cli into a local tool manifest"

# --- 1. The RID-specific package was selected, not just the pointer ----------------------------------
RID_PACKAGE_DIR="$NUGET_PACKAGES/scarlet.bun.cli.$DOTNET_RID"
if [ -d "$RID_PACKAGE_DIR" ]; then
    echo "✓ The $DOTNET_RID tool package was restored"
else
    echo "✗ Expected the $DOTNET_RID tool package at $RID_PACKAGE_DIR"
    ls -1 "$NUGET_PACKAGES" 2>/dev/null || true
    FAILED=1
fi

# --- 2. That package actually carries a Bun binary ---------------------------------------------------
EMBEDDED_BUN="$(find "$RID_PACKAGE_DIR" -type f \( -name 'bun' -o -name 'bun.exe' \) 2>/dev/null | head -n 1)"
if [ -n "$EMBEDDED_BUN" ]; then
    echo "✓ The tool package ships a Bun binary"
else
    echo "✗ No Bun binary inside $RID_PACKAGE_DIR - the tool would have to download one"
    FAILED=1
fi

# --- 3. It runs, and the Bun it runs is the pinned version -------------------------------------------
# A private cache root that must stay empty is how this proves nothing was downloaded.
export SCARLET_BUN_CACHE="$TEST_DIR/bun-cache"

set +e
REPORTED_BUN_VERSION="$(dotnet bun --version 2>bun.err)"
BUN_STATUS=$?
set -e

if [ "$BUN_STATUS" -ne 0 ]; then
    echo "✗ 'dotnet bun --version' failed with exit code $BUN_STATUS"
    cat bun.err
    FAILED=1
elif [ "$REPORTED_BUN_VERSION" = "$RUNTIME_VERSION" ]; then
    echo "✓ 'dotnet bun --version' printed Bun's version ($REPORTED_BUN_VERSION), proving argument passthrough"
else
    echo "✗ Expected Bun $RUNTIME_VERSION, got '$REPORTED_BUN_VERSION'"
    FAILED=1
fi

# --- 4. The Bun that ran is the embedded one ---------------------------------------------------------
if dotnet bun --scarlet-info | grep -q "^Source .*embedded"; then
    echo "✓ The tool reports the embedded binary as its source"
else
    echo "✗ The tool did not resolve the embedded binary"
    dotnet bun --scarlet-info || true
    FAILED=1
fi

# --- 5. Nothing was downloaded -----------------------------------------------------------------------
# Checks 2, 4 and 5 only carry the "no network" claim together: the package contains a Bun, the tool says
# it used that one, and the only directory it could have downloaded into was never created.
if [ ! -d "$SCARLET_BUN_CACHE" ]; then
    echo "✓ The download cache was never created - nothing was fetched"
else
    echo "✗ A download cache appeared at $SCARLET_BUN_CACHE"
    find "$SCARLET_BUN_CACHE" -type f | head -5
    FAILED=1
fi

# --- 6. Running a real script ------------------------------------------------------------------------
set +e
dotnet bun run hello.js > hello.log 2>&1
HELLO_STATUS=$?
set -e

if [ "$HELLO_STATUS" -eq 0 ] && [ -f "output.txt" ]; then
    echo "✓ 'dotnet bun run hello.js' executed the script"
    cat output.txt
else
    echo "✗ 'dotnet bun run hello.js' failed with exit code $HELLO_STATUS"
    cat hello.log
    FAILED=1
fi

# --- 7. Exit codes propagate -------------------------------------------------------------------------
set +e
dotnet bun run fail.js > /dev/null 2>&1
FAIL_STATUS=$?
set -e

if [ "$FAIL_STATUS" -eq 42 ]; then
    echo "✓ Bun's exit code (42) propagated through the tool"
else
    echo "✗ Expected exit code 42 from the failing script, got $FAIL_STATUS"
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

# Only cleanup if not running in CI (to allow inspection if needed)
if [ -z "$CI" ]; then
    cleanup
else
    echo "Running in CI - skipping cleanup to allow inspection"
    echo "Test directory: $TEST_DIR"
fi

if [ "$FAILED" -eq 0 ]; then
    echo "✓ E2E CLI tool test completed successfully - Bun ran offline from the embedded binary"
    exit 0
else
    exit 1
fi
