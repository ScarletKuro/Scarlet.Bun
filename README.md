# Scarlet.Bun.MSBuild

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/ScarletKuro/Scarlet.Bun/.github/workflows/ci.yml?branch=master&logo=github&style=flat-square)
[![codecov](https://codecov.io/gh/ScarletKuro/Scarlet.Bun/graph/badge.svg?token=A7MOQE06ZQ)](https://codecov.io/gh/ScarletKuro/Scarlet.Bun)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Bun?color=594ae2&logo=github&style=flat-square)](https://github.com/ScarletKuro/Scarlet.Bun/blob/master/LICENSE)
[![NuGet version](https://img.shields.io/nuget/v/Scarlet.Bun.MSBuild?color=ff4081&label=nuget%20version&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.MSBuild/)

An MSBuild task package that integrates [Bun](https://bun.sh/) - a fast all-in-one JavaScript runtime - into your .NET build process. This package allows you to run Bun commands as part of your .NET project build, enabling JavaScript/TypeScript bundling, minification, and other Bun-powered operations.

## Features

- ✅ Cross-platform support (Windows, Linux, macOS - x64 and ARM64)
- ✅ Embedded Bun runtimes - no separate installation required
- ✅ Easy MSBuild integration
- ✅ Supports modern .NET / .NET Core (no .NET Framework support)
- ✅ Execute any Bun command during build

## Supported Platforms

| Platform     | Runtime                    | Package Name                                      | Version |
|--------------|----------------------------|--------------------------------------------------|---------|
| Windows x64  | bun-windows-x64-baseline   | Scarlet.Bun.Runtime.windows-x64-baseline         | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.windows-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.windows-x64-baseline/) |
| Linux x64    | bun-linux-x64-baseline     | Scarlet.Bun.Runtime.linux-x64-baseline           | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-x64-baseline/) |
| Linux ARM64  | bun-linux-aarch64          | Scarlet.Bun.Runtime.linux-aarch64                | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-aarch64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-aarch64/) |
| macOS x64    | bun-darwin-x64-baseline    | Scarlet.Bun.Runtime.darwin-x64-baseline          | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.darwin-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-x64-baseline/) |
| macOS ARM64  | bun-darwin-aarch64         | Scarlet.Bun.Runtime.darwin-aarch64               | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.darwin-aarch64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-aarch64/) |


## Installation

Install the NuGet package:

```bash
dotnet add package Scarlet.Bun.MSBuild
```

Or via Package Manager:

```powershell
Install-Package Scarlet.Bun.MSBuild
```

## Usage

### Basic Example

Add the following to your `.csproj` file to run a Bun script during build:

```xml
<!-- Install dependencies -->
<Target Name="BunInstall" BeforeTargets="Build">
  <MSBuild Projects="$(MSBuildProjectFullPath)"
           Targets="Bun"
           Properties="BunCommand=install;BunWorkingDirectory=$(MSBuildProjectDirectory)" />
</Target>

<!-- Build with different command -->
<Target Name="BunBuild" AfterTargets="BunInstall">
  <MSBuild Projects="$(MSBuildProjectFullPath)"
           Targets="Bun"
           Properties="BunCommand=run;BunArguments=build.mjs;BunWorkingDirectory=$(MSBuildProjectDirectory)" />
</Target>
```

### Using Runtime Download

If you prefer to download the Bun runtime dynamically instead of using embedded runtimes, you can enable the `BunRuntimeDownload` option as a global property:

```xml
<PropertyGroup>
  <BunRuntimeDownload>true</BunRuntimeDownload>
  <BunVersionDownload>1.3.6</BunVersionDownload>
  <BunRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</BunRuntimeDirectory>
</PropertyGroup>

<!-- Install dependencies using downloaded runtime -->
<Target Name="BunInstall" BeforeTargets="Build">
  <MSBuild Projects="$(MSBuildProjectFullPath)"
           Targets="Bun"
           Properties="BunCommand=install;BunWorkingDirectory=$(MSBuildProjectDirectory)" />
</Target>
```

When using `BunRuntimeDownload=true`:
- The `BunRuntimeDirectory` property is **required** and specifies where to download the runtime
- The `BunVersionDownload` property is optional (defaults to latest version if not specified)
- Only the runtime for the current platform will be downloaded
- The runtime is cached in the specified directory and reused on subsequent builds

### Task Parameters

The `BunRunTask` supports the following parameters:

| Parameter | Required | Description | Default |
|-----------|----------|-------------|---------|
| `Command` | Yes | The Bun command to execute (e.g., "run", "install", "build") | - |
| `Arguments` | No | Arguments to pass to the Bun command | "" |
| `WorkingDirectory` | No | Working directory for command execution | Current directory |
| `RuntimeDirectory` | No | Path to the runtime directory containing Bun executables. If not specified, uses the default NuGet package structure. Required when using `BunRuntimeDownload`. | null |
| `TimeoutMilliseconds` | No | Timeout in milliseconds (0 = no timeout) | 0 |
| `ContinueOnError` | No | Whether to continue build if command fails | false |
| `BunRuntimeDownload` | No | When true, downloads the Bun runtime from GitHub releases instead of using embedded runtimes | false |
| `BunVersionDownload` | No | Specific Bun version to download (e.g., "1.3.6"). If not specified, downloads latest version. Only used when `BunRuntimeDownload=true`. | latest |

### Output Parameters

| Parameter | Description |
|-----------|-------------|
| `ExitCode` | The exit code of the executed command |
| `StandardOutput` | Standard output from the command |
| `StandardError` | Standard error from the command |

## Example: JavaScript/SCSS Build Script

Here's an example `build.mjs` script that bundles JavaScript and compiles SCSS:

```javascript
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { minify } from "terser";
import * as sass from "sass";

const scriptFilename = fileURLToPath(import.meta.url);
const scriptDirectory = path.dirname(scriptFilename);
const jsInputDir = path.join(scriptDirectory, "scripts");
const jsOutputFile = path.join(scriptDirectory, "wwwroot/js/bundle.min.js");
const scssInput = path.join(scriptDirectory, "styles/main.scss");
const scssOutput = path.join(scriptDirectory, "wwwroot/css/site.min.css");

async function buildJS() {
  console.log("Building JS bundle...");
  
  let files = fs
    .readdirSync(jsInputDir)
    .filter((f) => f.endsWith(".js"))
    .sort();

  let code = "";
  for (const file of files) {
    const filePath = path.join(jsInputDir, file);
    console.log("Adding", filePath);
    code += fs.readFileSync(filePath, "utf-8") + "\n";
  }

  const minified = await minify(code);
  
  const outDir = path.dirname(jsOutputFile);
  if (!fs.existsSync(outDir)) fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(jsOutputFile, minified.code, "utf-8");
  console.log("✓ JS bundle created");
}

function buildSCSS() {
  console.log("Building SCSS...");
  
  const result = sass.compile(scssInput, {
    style: "compressed",
    sourceMap: false,
  });

  fs.mkdirSync(path.dirname(scssOutput), { recursive: true });
  fs.writeFileSync(scssOutput, result.css);
  console.log("✓ CSS bundle created");
}

await buildJS();
buildSCSS();
```

Don't forget to add dependencies in `package.json`:

```json
{
  "type": "module",
  "dependencies": {
    "terser": "^5.36.0",
    "sass": "^1.83.4"
  }
}
```

## Development

### Building the Package

```bash
dotnet build
```

### Running Tests

Unit tests:
```bash
dotnet test tests/Scarlet.Bun.MSBuild.Tests/Scarlet.Bun.MSBuild.Tests.csproj
```

Integration tests:
```bash
dotnet test tests/Scarlet.Bun.MSBuild.IntegrationTests/Scarlet.Bun.MSBuild.IntegrationTests.csproj
```

All tests:
```bash
dotnet test
```

### Creating a Package

```bash
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj
```

## Requirements

- .NET / .NET Core (no .NET Framework support)
- Supported on Windows, Linux, and macOS

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

### Bundled Software Licenses

This package distributes Bun binaries, which include:

- **Bun**: MIT License - Copyright (c) Jarred Sumner and contributors
- **JavaScriptCore/WebKit**: LGPL-2.1 License - Bun statically links JavaScriptCore and WebKit components

Per the LGPL-2.1 license requirements, the complete source code and build instructions for Bun (including its statically linked JavaScriptCore components) are available at:
- Bun source: https://github.com/oven-sh/bun
- Patched WebKit/JavaScriptCore: https://github.com/oven-sh/webkit

To relink Bun with modifications to JavaScriptCore:
```bash
git clone https://github.com/oven-sh/bun
cd bun
git submodule update --init --recursive
make jsc
zig build
```

For more information, see the [Bun License Documentation](https://bun.sh/docs/project/license).

## Credits

- Built by [ScarletKuro](https://github.com/ScarletKuro)
- Uses [Bun](https://bun.sh/) - a fast all-in-one JavaScript runtime

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.
