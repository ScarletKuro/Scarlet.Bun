#!/bin/bash

set -e

EXECUTABLE_PATH="$1"
DOWNLOAD_FILENAME="$2"
BUN_VERSION="$3"

# Version marker file to track which version is downloaded
VERSION_FILE="${EXECUTABLE_PATH}.version"

if [ -f "$EXECUTABLE_PATH" ]; then
  echo "Bun binary found at $EXECUTABLE_PATH, checking version..."

  # Check version from marker file instead of executing the binary
  # This avoids trying to execute binaries for other platforms (e.g., Windows binary on Linux)
  if [ -f "$VERSION_FILE" ]; then
    STORED_VERSION=$(cat "$VERSION_FILE")
    echo "Stored version: $STORED_VERSION"
    echo "Required version: $BUN_VERSION"

    if [ "$STORED_VERSION" = "$BUN_VERSION" ]; then
      echo "Version matches! No download needed."
      echo "Bun setup complete at $EXECUTABLE_PATH"
      exit 0
    else
      echo "Version mismatch! Will download correct version."
    fi
  else
    echo "No version marker found. Will download to ensure correct version."
  fi
fi

DOWNLOAD_URL="https://github.com/oven-sh/bun/releases/download/bun-v$BUN_VERSION/$DOWNLOAD_FILENAME"

# Unique temp paths so parallel project builds never collide.
# No suffix after the X's: BusyBox's mktemp (Alpine) requires the X's to be the very last characters of
# the template and rejects a trailing suffix like ".zip" with "Invalid argument", unlike GNU mktemp. curl
# and unzip do not care that the file has no .zip extension.
TMP_ZIP="$(mktemp -t bun-download-XXXXXXXX)"

# Extract into a temp directory, NEVER next to $EXECUTABLE_PATH. Extracting into the project directory made
# the search below find the binary it was about to replace, so the move was skipped as a no-op and a stale
# binary kept its place while the marker advertised the new version.
TMP_EXTRACT="$(mktemp -d -t bun-extract-XXXXXXXX)"

cleanup() {
  rm -f "$TMP_ZIP" 2>/dev/null || true
  rm -rf "$TMP_EXTRACT" 2>/dev/null || true
}
trap cleanup EXIT

echo "Downloading Bun from $DOWNLOAD_URL"
curl -fL "$DOWNLOAD_URL" -o "$TMP_ZIP"

echo "Extracting to $TMP_EXTRACT"
unzip -o -q "$TMP_ZIP" -d "$TMP_EXTRACT"

# Bun zip files contain a directory structure - find and move the executable
EXPECTED_FILENAME=$(basename "$EXECUTABLE_PATH")
echo "Looking for executable named: $EXPECTED_FILENAME"

BUN_EXE=$(find "$TMP_EXTRACT" -type f -name "$EXPECTED_FILENAME" -not -path "*__MACOSX*" | head -n 1)

if [ -z "$BUN_EXE" ]; then
  echo "Error: the archive '$DOWNLOAD_FILENAME' did not contain an executable named '$EXPECTED_FILENAME'." >&2
  exit 1
fi

echo "Moving $BUN_EXE to $EXECUTABLE_PATH"
mkdir -p "$(dirname "$EXECUTABLE_PATH")"
mv -f "$BUN_EXE" "$EXECUTABLE_PATH"

if [ ! -f "$EXECUTABLE_PATH" ]; then
  echo "Error: Bun was not present at $EXECUTABLE_PATH after extraction." >&2
  exit 1
fi

chmod +x "$EXECUTABLE_PATH"

# Only now is the marker true.
echo -n "$BUN_VERSION" > "$VERSION_FILE"
echo "Version marker created: $VERSION_FILE"
echo "Bun setup complete at $EXECUTABLE_PATH"
