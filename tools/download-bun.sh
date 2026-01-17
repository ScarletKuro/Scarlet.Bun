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
# Use unique temp file name to avoid conflicts when multiple projects build in parallel
TMP_ZIP="/tmp/bun-$(uuidgen | cut -d'-' -f1).zip"
EXTRACT_DIR=$(dirname "$EXECUTABLE_PATH")

echo "Downloading Bun from $DOWNLOAD_URL"
curl -L "$DOWNLOAD_URL" -o "$TMP_ZIP"

echo "Extracting to $EXTRACT_DIR"
unzip -o "$TMP_ZIP" -d "$EXTRACT_DIR"

# Bun zip files contain a directory structure - find and move the executable
BUN_EXE=$(find "$EXTRACT_DIR" -type f -name "bun" -not -path "*/.*" | head -n 1)
if [ -n "$BUN_EXE" ] && [ "$BUN_EXE" != "$EXECUTABLE_PATH" ]; then
  echo "Moving $BUN_EXE to $EXECUTABLE_PATH"
  mv "$BUN_EXE" "$EXECUTABLE_PATH"
  
  # Clean up extracted directory structures (both bun-* and __MACOSX)
  find "$EXTRACT_DIR" -mindepth 1 -maxdepth 1 -type d -exec rm -rf {} + 2>/dev/null || true
fi

# Clean up temp file with error handling
if ! rm "$TMP_ZIP" 2>/dev/null; then
  echo "Warning: Could not remove temp file $TMP_ZIP"
fi

# Write version marker file
echo -n "$BUN_VERSION" > "$VERSION_FILE"
echo "Version marker created: $VERSION_FILE"

# Ensure the binary is executable
chmod +x "$EXECUTABLE_PATH"
echo "Bun setup complete at $EXECUTABLE_PATH"
