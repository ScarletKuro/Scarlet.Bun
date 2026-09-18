param (
  [string]$ExecutablePath,
  [string]$DownloadFilename,
  [string]$BunVersion
)

# Any failure must stop the script before the version marker is written. Without this, a failed download or
# move would still stamp the marker, and every later build would skip the download and keep a stale binary.
$ErrorActionPreference = 'Stop'

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

$releaseUrl = "https://github.com/oven-sh/bun/releases/download/bun-v$($BunVersion)"
$downloadUrl = "$releaseUrl/$($DownloadFilename)"
$checksumsUrl = "$releaseUrl/SHASUMS256.txt"
# Unique temp paths so parallel project builds never collide.
$unique = [System.Guid]::NewGuid().ToString('N').Substring(0, 8)
$tempZip = Join-Path $env:TEMP "bun-$unique.zip"
$tempSums = Join-Path $env:TEMP "bun-shasums-$unique.txt"

# Extract into a temp directory, NEVER next to $ExecutablePath. Extracting into the project directory made
# the search below find the binary it was about to replace, so the move was skipped as a no-op and a stale
# binary kept its place while the marker advertised the new version.
$tempExtract = Join-Path $env:TEMP "bun-extract-$unique"

try {
  Write-Host "Downloading Bun from $downloadUrl"
  Invoke-WebRequest -Uri $downloadUrl -OutFile $tempZip -UseBasicParsing

  # Verify the download against upstream's published SHA-256 sums before touching the archive. Bun publishes
  # SHASUMS256.txt alongside every release; without this check a compromised release asset or a MITM'd
  # download would be extracted and shipped, unverified, into every consumer's build.
  Write-Host "Downloading checksums from $checksumsUrl"
  Invoke-WebRequest -Uri $checksumsUrl -OutFile $tempSums -UseBasicParsing

  $escapedFilename = [regex]::Escape($DownloadFilename)
  $sumLine = Select-String -Path $tempSums -Pattern "^\S+\s+$escapedFilename$" | Select-Object -First 1
  if (-not $sumLine) {
    throw "No checksum entry for '$DownloadFilename' in $checksumsUrl"
  }
  $expectedSha256 = ($sumLine.Line -split '\s+')[0]

  $actualSha256 = (Get-FileHash -Path $tempZip -Algorithm SHA256).Hash
  if ($actualSha256 -ne $expectedSha256) {
    throw "Checksum mismatch for '$DownloadFilename'. Expected $expectedSha256, got $actualSha256."
  }
  Write-Host "Checksum verified: $actualSha256"

  Write-Host "Extracting to $tempExtract"
  New-Item -ItemType Directory -Path $tempExtract -Force | Out-Null
  Expand-Archive -Path $tempZip -DestinationPath $tempExtract -Force

  # Bun zip files contain a directory with the bun executable inside.
  $bunExe = Get-ChildItem -Path $tempExtract -Recurse -File |
    Where-Object { $_.Name -match "^bun(\.exe)?$" -and $_.DirectoryName -notlike "*__MACOSX*" } |
    Select-Object -First 1

  if (-not $bunExe) {
    throw "The archive '$DownloadFilename' did not contain a bun executable."
  }

  Write-Host "Moving $($bunExe.FullName) to $ExecutablePath"
  New-Item -ItemType Directory -Path (Split-Path $ExecutablePath) -Force | Out-Null
  Move-Item -Path $bunExe.FullName -Destination $ExecutablePath -Force

  if (-not (Test-Path $ExecutablePath)) {
    throw "Bun was not present at $ExecutablePath after extraction."
  }

  # Only now is the marker true.
  Set-Content -Path $versionFile -Value $BunVersion -NoNewline
  Write-Host "Version marker created: $versionFile"
  Write-Host "Bun setup complete at $ExecutablePath"
}
finally {
  Remove-Item -Path $tempZip -Force -ErrorAction SilentlyContinue
  Remove-Item -Path $tempSums -Force -ErrorAction SilentlyContinue
  Remove-Item -Path $tempExtract -Recurse -Force -ErrorAction SilentlyContinue
}
