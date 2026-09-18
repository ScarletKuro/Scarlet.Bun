# Scarlet.Bun.MSBuild

Run [Bun](https://bun.sh) — the fast all-in-one JavaScript runtime and bundler — as part of your .NET
build. Bundle and minify JavaScript and TypeScript, compile Sass/SCSS, and install npm dependencies
during `dotnet build`, on Windows, Linux and macOS (x64 and ARM64), with no Node.js required.

Works with ASP.NET Core, Blazor and Razor Class Library static web assets.

```xml
<ItemGroup>
  <BunBeforeStaticWebAssets Include="run">
    <Arguments>build.mjs</Arguments>
  </BunBeforeStaticWebAssets>
</ItemGroup>
```

> Looking for Bun on the **command line** instead of during a build? See
> [`Scarlet.Bun.Cli`](https://www.nuget.org/packages/Scarlet.Bun.Cli/), which runs Bun as a .NET tool with
> `dotnet bun ...`.

## Table of Contents

- [Installation](#installation)
- [Runtime Options](#runtime-options)
  - [Option 1: Runtime Download](#option-1-runtime-download)
  - [Option 2: Platform-Specific Runtime Packages](#option-2-platform-specific-runtime-packages)
  - [Option 3: Conditional Package References](#option-3-conditional-package-references)
  - [How the Runtime Is Discovered](#how-the-runtime-is-discovered)
- [Usage](#usage)
  - [Blazor and Razor Static Web Assets](#blazor-and-razor-static-web-assets)
  - [Basic Example](#basic-example)
  - [Using Runtime Download](#using-runtime-download)
  - [Multi-Target Framework Projects](#multi-target-framework-projects)
  - [dotnet watch Integration](#dotnet-watch-integration)
  - [BunBeforeStaticWebAssets Metadata](#bunbeforestaticwebassets-metadata)
  - [Task Parameters](#task-parameters)
  - [Output Parameters](#output-parameters)
- [Example: JavaScript/SCSS Build Script](#example-javascriptscss-build-script)
- [Supported Platforms](#supported-platforms)

## Installation

Install the main MSBuild task package:

```bash
dotnet add package Scarlet.Bun.MSBuild
```

Or via Package Manager:

```powershell
Install-Package Scarlet.Bun.MSBuild
```

> **Note:** The base package does not include any Bun runtime. You must choose a runtime option (see [Runtime Options](#runtime-options) below).

## Runtime Options

After installing `Scarlet.Bun.MSBuild`, you need to provide the Bun runtime. There are three approaches to choose from based on your needs:

### Option 1: Runtime Download

Download the Bun runtime automatically during build by setting the `BunRuntimeDownload` property:

```xml
<PropertyGroup>
  <BunRuntimeDownload>true</BunRuntimeDownload>
  <BunVersionDownload>1.3.6</BunVersionDownload> <!-- Optional: specify version -->
  <BunRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</BunRuntimeDirectory>
</PropertyGroup>
```

**Advantages:**
- Simplest configuration - just set a few properties
- No additional package dependencies to manage
- Each developer/CI agent only downloads the runtime package they need
- Runtime downloaded only once and cached locally for subsequent builds
- Can easily switch Bun versions by changing `BunVersionDownload` property

See [Using Runtime Download](#using-runtime-download) for detailed configuration.

### Option 2: Platform-Specific Runtime Packages

Install only the runtime package(s) you need for your target platform(s):

```bash
# For Windows x64
dotnet add package Scarlet.Bun.Runtime.windows-x64-baseline

# For Windows ARM64
dotnet add package Scarlet.Bun.Runtime.windows-aarch64

# For Linux x64
dotnet add package Scarlet.Bun.Runtime.linux-x64-baseline

# For Linux ARM64
dotnet add package Scarlet.Bun.Runtime.linux-aarch64

# For Linux x64, musl (Alpine)
dotnet add package Scarlet.Bun.Runtime.linux-x64-musl-baseline

# For Linux ARM64, musl (Alpine)
dotnet add package Scarlet.Bun.Runtime.linux-aarch64-musl

# For macOS x64
dotnet add package Scarlet.Bun.Runtime.darwin-x64-baseline

# For macOS ARM64 (Apple Silicon)
dotnet add package Scarlet.Bun.Runtime.darwin-aarch64
```

> **Note:** Runtime packages are versioned independently from `Scarlet.Bun.MSBuild`. Their package version corresponds to the bundled Bun version (e.g., package version `1.3.6` contains Bun `1.3.6`).

**Advantages:**
- Explicit control over which runtimes are included
- No runtime downloads during build (runtimes come from NuGet packages)
- Works offline

**Trade-offs:**
- Package with needed runtime version might be missing

**Available Runtime Packages:**

| Platform     | Runtime                    | Package Name                                      | Package Version |
|--------------|----------------------------|--------------------------------------------------|---------|
| Windows x64  | bun-windows-x64-baseline   | Scarlet.Bun.Runtime.windows-x64-baseline         | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.windows-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.windows-x64-baseline/) |
| Windows ARM64 | bun-windows-aarch64       | Scarlet.Bun.Runtime.windows-aarch64              | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.windows-aarch64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.windows-aarch64/) |
| Linux x64    | bun-linux-x64-baseline     | Scarlet.Bun.Runtime.linux-x64-baseline           | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-x64-baseline/) |
| Linux ARM64  | bun-linux-aarch64          | Scarlet.Bun.Runtime.linux-aarch64                | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-aarch64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-aarch64/) |
| Linux x64, musl (Alpine)   | bun-linux-x64-musl-baseline | Scarlet.Bun.Runtime.linux-x64-musl-baseline | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-x64-musl-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-x64-musl-baseline/) |
| Linux ARM64, musl (Alpine) | bun-linux-aarch64-musl      | Scarlet.Bun.Runtime.linux-aarch64-musl      | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.linux-aarch64-musl?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-aarch64-musl/) |
| macOS x64    | bun-darwin-x64-baseline    | Scarlet.Bun.Runtime.darwin-x64-baseline          | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.darwin-x64-baseline?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-x64-baseline/) |
| macOS ARM64  | bun-darwin-aarch64         | Scarlet.Bun.Runtime.darwin-aarch64               | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Runtime.darwin-aarch64?color=ff4081&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-aarch64/) |

### Option 3: Conditional Package References

Use MSBuild conditions to reference only the runtime package matching the current build platform:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>

  <!-- Reference Scarlet.Bun.MSBuild -->
  <ItemGroup>
    <PackageReference Include="Scarlet.Bun.MSBuild" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <!-- Detect current platform -->
  <PropertyGroup>
    <IsWindows Condition="'$(OS)' == 'Windows_NT'">true</IsWindows>
    <IsLinux Condition="Exists('/proc')">true</IsLinux>
    <IsMacOS Condition="Exists('/System/Library/CoreServices/SystemVersion.plist')">true</IsMacOS>
    <IsMusl Condition="Exists('/lib/ld-musl-x86_64.so.1') OR Exists('/lib/ld-musl-aarch64.so.1')">true</IsMusl>
  </PropertyGroup>

  <!-- Detect architecture -->
  <PropertyGroup>
    <IsARM64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'ARM64' OR '$(PROCESSOR_IDENTIFIER)' == 'ARM64'">true</IsARM64>
    <IsX64 Condition="'$(PROCESSOR_ARCHITECTURE)' == 'AMD64' OR '$(PROCESSOR_IDENTIFIER)' == 'AMD64'">true</IsX64>
  </PropertyGroup>

  <!-- Conditionally reference runtime packages based on platform -->
  <ItemGroup Condition="'$(IsWindows)' == 'true' AND '$(IsX64)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.windows-x64-baseline" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsWindows)' == 'true' AND '$(IsARM64)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.windows-aarch64" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' != 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.linux-x64-baseline" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' != 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.linux-aarch64" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsX64)' == 'true' AND '$(IsMusl)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.linux-x64-musl-baseline" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsLinux)' == 'true' AND '$(IsARM64)' == 'true' AND '$(IsMusl)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.linux-aarch64-musl" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsX64)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.darwin-x64-baseline" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsMacOS)' == 'true' AND '$(IsARM64)' == 'true'">
    <PackageReference Include="Scarlet.Bun.Runtime.darwin-aarch64" Version="*" PrivateAssets="all" IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  </ItemGroup>

</Project>
```

**NB!** You can add them without conditions, but the runtime packages have big size.

**Advantages:**
- Builds work on any platform without modification
- Each developer/CI agent only downloads the runtime package they need
- No runtime downloads during build (runtimes come from NuGet packages)
- Deterministic builds with version-locked packages
- Works offline

**Trade-offs:**
- Package with needed runtime version might be missing
- More verbose project file configuration
- Need to maintain platform detection logic

---

**Which option should I choose?**

- **Use Option 1 (Runtime Download)** if you want a multi-platform with the simplest setup and configuration
- **Use Option 2 (Single Runtime Package)** if you want a single platform and want embedded runtime (without downloads)
- **Use Option 3 (Conditional References)** if you want a multi-platform and want embedded runtimes (without downloads)

### How the Runtime Is Discovered

Bun runs on the machine doing the build, not on the machine the project targets, so NuGet's usual RID
resolution is the wrong mechanism here — it resolves against `$(RuntimeIdentifier)`. Instead, each
`Scarlet.Bun.Runtime.*` package contributes a `BunRuntimePack` item from its `build/*.props`, and the task
picks the one matching the build host:

```xml
<ItemGroup>
  <BunRuntimePack Include="Scarlet.Bun.Runtime.darwin-aarch64">
    <Rid>osx-arm64</Rid>
    <RuntimesPath>...\runtimes\</RuntimesPath>
    <Variant>default</Variant>
    <Priority>0</Priority>
  </BunRuntimePack>
</ItemGroup>
```

You normally never write one of these. You would if you want the build to use a Bun you supply yourself —
a non-baseline build, a pre-release build, or a locally compiled one — without waiting for a runtime
package:

```xml
<ItemGroup>
  <BunRuntimePack Include="MyCompany.Bun.linux-x64-custom">
    <Rid>linux-x64</Rid>
    <RuntimesPath>$(MSBuildProjectDirectory)/bun/runtimes</RuntimesPath>
    <Priority>100</Priority>
  </BunRuntimePack>
</ItemGroup>
```

| Metadata | Required | Description |
|----------|----------|-------------|
| `Rid` | Yes | The runtime identifier this pack serves, for example `osx-arm64` |
| `RuntimesPath` | Yes | Directory containing `<rid>/native/bun` (`bun.exe` on Windows) |
| `Variant` | No | Bun build variant, shown in build logs and error messages |
| `Priority` | No | Higher wins when several packs serve the same `Rid`. Defaults to `0`; ties are broken by pack id so the result never depends on restore order |

Precedence: an explicit `BunRuntimeDirectory` wins over every pack, and `BunRuntimeDownload=true` bypasses
pack resolution entirely. If a pack's `bun` is missing, the next candidate for the same RID is tried.

> **Note:** Runtime packages are development dependencies, so their props apply to the project that
> references them directly. They do not flow to projects that reference *that* project — add the
> `PackageReference` in each project that runs Bun, or in a shared `Directory.Build.props`.

> **Legacy contract (deprecated):** runtime packages also still set a `BunRuntime_<rid>` property (for
> example `BunRuntime_osx_arm64`), pointing at the package root. It exists so that new runtime packages keep
> working with older `Scarlet.Bun.MSBuild` versions and vice versa. Runtime packages declare `BunRuntimePack`
> from version **1.4.2** onwards; if a runtime is found *only* through the property — meaning the package is
> older than that — the build logs a message at normal verbosity (`dotnet build -v:n`) telling you which
> package to update. It is a message rather than a warning because pinning an older runtime package is how
> you pin a Bun version, and that must not fail builds using `TreatWarningsAsErrors`. The property will be
> removed in a future major version.

## Usage

### Blazor and Razor Static Web Assets

For Blazor apps, ASP.NET Core apps and Razor Class Libraries that generate files into `wwwroot`, declare
the Bun steps as `BunBeforeStaticWebAssets` items:

```xml
<ItemGroup>
  <BunBeforeStaticWebAssets Include="install">
    <Arguments>--frozen-lockfile</Arguments>
  </BunBeforeStaticWebAssets>

  <BunBeforeStaticWebAssets Include="run">
    <Arguments>build.mjs</Arguments>
  </BunBeforeStaticWebAssets>
</ItemGroup>
```

These steps run once before the .NET static web assets SDK discovers files in `wwwroot`. Generated files
are added back to the build as `Content`, so clean builds, fingerprinting, publish and NuGet packing see
the assets without a custom target.

### Basic Example

For non-static-web-asset scenarios, you can call the reusable `Bun` target from your own targets:

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

### Multi-Target Framework Projects

When your project targets multiple frameworks (`<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>`), MSBuild dispatches parallel inner builds for each TFM. Bun commands like `install` must run **once** before the inner builds start, otherwise concurrent writes to `node_modules` will fail on Windows.

`BunBeforeStaticWebAssets` handles this for static web asset builds:

```xml
<ItemGroup>
  <BunBeforeStaticWebAssets Include="install">
    <Arguments>--frozen-lockfile</Arguments>
  </BunBeforeStaticWebAssets>

  <BunBeforeStaticWebAssets Include="run">
    <Arguments>build.mjs</Arguments>
  </BunBeforeStaticWebAssets>
</ItemGroup>
```

Internally, the target uses the usual outer-build condition:
- **Outer build** (multi-TFM): `TargetFramework` is empty — target runs
- **Inner builds** (per-TFM): both `TargetFrameworks` and `TargetFramework` are set — target skips
- **Single-TFM build**: `TargetFrameworks` is empty — target runs

If you are not generating static web assets, keep using your own target and call `Bun` directly.

### dotnet watch Integration

`dotnet watch` only reloads on changes to files it already knows about (`.cs`, `.razor`, `.cshtml`, and a
few others) — it has no idea your JS/TS/SCSS sources exist, so editing them does nothing until you rebuild
manually. Tell it about them with a `Watch` item, and it will trigger a normal build (including your Bun
target) whenever they change:

```xml
<ItemGroup>
  <Watch Include="assets\**\*.js;assets\**\*.ts;assets\**\*.scss" Exclude="node_modules\**" />
</ItemGroup>
```

Point the globs at your source directory, not `wwwroot` — watching the Bun output would make every rebuild
trigger another rebuild.

`Watch` has no idea Bun exists — it is only a trip-wire that tells `dotnet watch` "treat a change to this
file like a change to a `.cs` file." When it fires, `dotnet watch` just runs a normal build, the same as
`dotnet build`. Your `BunBeforeStaticWebAssets` items or custom Bun targets already run on *every* build
regardless of what triggered it, so they run here too — the `Watch` item doesn't invoke Bun itself, it just
causes the build that was always going to invoke Bun to happen more often.

Run `dotnet watch build` (or `dotnet watch run` for a Blazor/ASP.NET Core app, which also gets browser
refresh for the resulting static assets) and saving a `.js`/`.scss` file re-runs Bun like any other source
change.

This rides entirely on `dotnet watch`'s existing file-watching — no code in this repo — so it triggers a
full MSBuild build per save rather than an instant incremental rebuild. That's fine for most JS/CSS bundling
setups; if the rebuild latency becomes the bottleneck, running Bun's own `--watch` mode as a separate
long-lived process is a further option, at the cost of managing that process's lifecycle yourself.

### BunBeforeStaticWebAssets Metadata

| Metadata | Required | Description | Default |
|----------|----------|-------------|---------|
| `Include` | Yes | The Bun command to execute, for example `install`, `run` or `test` | - |
| `Arguments` | No | Arguments to pass after the command | "" |
| `WorkingDirectory` | No | Working directory for command execution | `$(MSBuildProjectDirectory)` |
| `TimeoutMilliseconds` | No | Timeout in milliseconds (`0` = no timeout) | `$(BunTimeoutMilliseconds)` |
| `ContinueOnError` | No | Whether to continue the build if this step fails | `$(BunContinueOnError)` |

### Task Parameters

The `BunRunTask` supports the following parameters:

| Parameter | Required | Description | Default |
|-----------|----------|-------------|---------|
| `Command` | Yes | The Bun command to execute (e.g., "run", "install", "build") | - |
| `Arguments` | No | Arguments to pass to the Bun command | "" |
| `WorkingDirectory` | No | Working directory for command execution | Current directory |
| `RuntimeDirectory` | No | Path to the runtime directory containing Bun executables. Overrides `RuntimePacks` when set. Required when using `BunRuntimeDownload`. | null |
| `RuntimePacks` | No | The Bun runtimes available to the build, normally `@(BunRuntimePack)`. See [How the Runtime Is Discovered](#how-the-runtime-is-discovered). | empty |
| `TimeoutMilliseconds` | No | Timeout in milliseconds (0 = no timeout) | 0 |
| `ContinueOnError` | No | Whether to continue build if command fails | false |
| `BunRuntimeDownload` | No | When true, downloads the Bun runtime from GitHub releases instead of using embedded runtimes | false |
| `BunVersionDownload` | No | Specific Bun version to download (e.g., "1.3.6"). If not specified, downloads latest version. Only used when `BunRuntimeDownload=true`. | latest |
| `DownloadMutexTimeoutSeconds` | No | Maximum seconds to wait for the download mutex when another process is already downloading. Only used when `BunRuntimeDownload=true`. | 300 |

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


## Supported Platforms

Windows, Linux and macOS on **x64 or arm64**, including musl-based Linux distributions such as Alpine. Any
other architecture gets an explanatory error rather than a mismatched binary — point `BunRuntimeDirectory`
at your own Bun if you need one.

Requires the .NET SDK. The task itself targets `netstandard2.0`, so it loads in both `dotnet build` and
Visual Studio's MSBuild.

## Links

- [Source, samples and full documentation](https://github.com/ScarletKuro/Scarlet.Bun)
- [Scarlet.Bun.Cli](https://www.nuget.org/packages/Scarlet.Bun.Cli/) — Bun on the command line
- [Bun documentation](https://bun.sh/docs)

Licensed under MIT. Bun itself is licensed separately — see the
[Bun repository](https://github.com/oven-sh/bun).
