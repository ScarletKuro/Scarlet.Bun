param (
  [string]$ExecutablePath,
  [string]$DownloadFilename,
  [string]$BunVersion
)

# Version marker file to track which version is downloaded
$versionFile = "$ExecutablePath.version"

# Check if Bun exists and version matches
$needsDownload = $true
if (Test-Path $ExecutablePath) {
  Write-Host "Bun binary found at $ExecutablePath, checking version..."

  # Check version from marker file instead of executing the binary
  # This avoids trying to execute binaries for other platforms (e.g., Linux binary on Windows)
  if (Test-Path $versionFile) {
    $storedVersion = Get-Content $versionFile -Raw
    $storedVersion = $storedVersion.Trim()
    Write-Host "Stored version: $storedVersion, Required: $BunVersion"

    if ($storedVersion -eq $BunVersion) {
      Write-Host "Version matches! No download needed."
      $needsDownload = $false
    } else {
      Write-Host "Version mismatch! Will download correct version."
    }
  } else {
    Write-Host "No version marker found. Will download to ensure correct version."
  }
}

if (-not $needsDownload) {
  Write-Host "Bun setup complete at $ExecutablePath"
  exit 0
}

$downloadUrl = "https://github.com/oven-sh/bun/releases/download/bun-v$($BunVersion)/$($DownloadFilename)"
# Use unique temp file name to avoid conflicts when multiple projects build in parallel
$tempZip = "$env:TEMP\bun-$([System.Guid]::NewGuid().ToString('N').Substring(0,8)).zip"
$extractDir = Split-Path $ExecutablePath

Write-Host "Downloading Bun from $downloadUrl"
Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing

Write-Host "Extracting to $extractDir"
Expand-Archive -Path $tempZip -DestinationPath $extractDir -Force

# Bun zip files contain a directory with the bun executable inside
# Find the executable and move it to the correct location
$extractedFiles = Get-ChildItem -Path $extractDir -Recurse -File | Where-Object { $_.Name -match "^bun(\.exe)?$" -and $_.DirectoryName -notlike "*__MACOSX*" }
if ($extractedFiles) {
  $bunExe = $extractedFiles | Select-Object -First 1
  if ($bunExe.FullName -ne $ExecutablePath) {
    Write-Host "Moving $($bunExe.FullName) to $ExecutablePath"
    Move-Item -Path $bunExe.FullName -Destination $ExecutablePath -Force
    
    # Clean up ALL extracted directories (including bun-* and __MACOSX)
    Get-ChildItem -Path $extractDir -Directory | ForEach-Object {
      Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
}

# Clean up temp file with retry logic
try {
  Remove-Item $tempZip -ErrorAction Stop
} catch {
  Write-Warning "Could not remove temp file $tempZip : $($_.Exception.Message)"
  # Try to remove it again after a short delay
  Start-Sleep -Milliseconds 100
  try {
    Remove-Item $tempZip -ErrorAction Stop
  } catch {
    Write-Warning "Second attempt to remove temp file failed. Continuing anyway."
  }
}

# Write version marker file
Set-Content -Path $versionFile -Value $BunVersion -NoNewline
Write-Host "Version marker created: $versionFile"

Write-Host "Bun setup complete at $ExecutablePath"
