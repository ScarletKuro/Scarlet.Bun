# Scarlet.Bun

![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/ScarletKuro/Scarlet.Bun/.github/workflows/ci.yml?branch=master&logo=github&style=flat-square)
[![codecov](https://codecov.io/gh/ScarletKuro/Scarlet.Bun/graph/badge.svg?token=A7MOQE06ZQ)](https://codecov.io/gh/ScarletKuro/Scarlet.Bun)
[![GitHub](https://img.shields.io/github/license/ScarletKuro/Scarlet.Bun?color=594ae2&logo=github&style=flat-square)](https://github.com/ScarletKuro/Scarlet.Bun/blob/master/LICENSE)

[Bun](https://bun.sh/) — the fast all-in-one JavaScript runtime, bundler and package manager — for .NET,
as a pinned NuGet dependency rather than something you install separately.

Bundle and minify JavaScript and TypeScript, compile Sass/SCSS, and install npm dependencies without
Node.js, on Windows, Linux and macOS (x64 and ARM64).

## Which package do I want?

| | Package | Use it when |
|---|---|---|
| **During a build** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.MSBuild?color=ff4081&label=Scarlet.Bun.MSBuild&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.MSBuild/) | You want `dotnet build` to bundle your assets — Blazor, Razor Class Libraries, ASP.NET Core |
| **On the command line** | [![NuGet](https://img.shields.io/nuget/v/Scarlet.Bun.Cli?color=ff4081&label=Scarlet.Bun.Cli&logo=nuget&style=flat-square)](https://www.nuget.org/packages/Scarlet.Bun.Cli/) | You want `dotnet bun install` / `dotnet bun run build.mjs`, pinned per repository |

They are independent — use either, or both.

### Scarlet.Bun.MSBuild — Bun during `dotnet build`

```bash
dotnet add package Scarlet.Bun.MSBuild
```

```xml
<Target Name="BunBuildAssets" BeforeTargets="Build">
  <MSBuild Projects="$(MSBuildProjectFullPath)"
           Targets="Bun"
           Properties="BunCommand=run;BunArguments=build.mjs;BunWorkingDirectory=$(MSBuildProjectDirectory)" />
</Target>
```

The Bun binary comes either from a platform-specific `Scarlet.Bun.Runtime.*` package or from an on-demand
download, whichever suits your build.

📖 **[Full documentation →](src/Scarlet.Bun.MSBuild/README.md)** — installation, the three runtime options,
task parameters, multi-targeting, and a worked JavaScript/SCSS build script.

### Scarlet.Bun.Cli — Bun on the command line

```bash
dotnet new tool-manifest
dotnet tool install Scarlet.Bun.Cli
dotnet bun run build.mjs
```

The tool version *is* the Bun version, so `.config/dotnet-tools.json` pins Bun alongside the rest of your
tooling. `Scarlet.Bun.Cli` is a pointer package; installing it also pulls a matching `Scarlet.Bun.Cli.*`
sub-package for your platform, and that one embeds the Bun binary, so it needs no network at run time.

📖 **[Full documentation →](src/Scarlet.Bun.Cli/README.md)** — installing, how it finds Bun, and the
environment variables.

## Available Packages

| Package | Contains |
|---------|----------|
| [Scarlet.Bun.MSBuild](https://www.nuget.org/packages/Scarlet.Bun.MSBuild/) | The MSBuild task. Versioned independently. |
| [Scarlet.Bun.Cli](https://www.nuget.org/packages/Scarlet.Bun.Cli/) | The `dotnet bun` tool. Version = the embedded Bun version. |
| [Scarlet.Bun.Runtime.windows-x64-baseline](https://www.nuget.org/packages/Scarlet.Bun.Runtime.windows-x64-baseline/) | Bun for Windows x64 |
| [Scarlet.Bun.Runtime.windows-aarch64](https://www.nuget.org/packages/Scarlet.Bun.Runtime.windows-aarch64/) | Bun for Windows ARM64 |
| [Scarlet.Bun.Runtime.linux-x64-baseline](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-x64-baseline/) | Bun for Linux x64 |
| [Scarlet.Bun.Runtime.linux-aarch64](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-aarch64/) | Bun for Linux ARM64 |
| [Scarlet.Bun.Runtime.linux-x64-musl-baseline](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-x64-musl-baseline/) | Bun for Linux x64, musl (Alpine) |
| [Scarlet.Bun.Runtime.linux-aarch64-musl](https://www.nuget.org/packages/Scarlet.Bun.Runtime.linux-aarch64-musl/) | Bun for Linux ARM64, musl (Alpine) |
| [Scarlet.Bun.Runtime.darwin-x64-baseline](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-x64-baseline/) | Bun for macOS x64 |
| [Scarlet.Bun.Runtime.darwin-aarch64](https://www.nuget.org/packages/Scarlet.Bun.Runtime.darwin-aarch64/) | Bun for macOS ARM64 |

The `Scarlet.Bun.Runtime.*` packages are consumed by `Scarlet.Bun.MSBuild` and can also be installed
directly (see its README); the CLI embeds its own Bun and does not use them. Their package version is the
Bun version they contain.

`Scarlet.Bun.Cli` restores its own per-platform `Scarlet.Bun.Cli.*` sub-packages (one per RID, plus a
portable `.any` fallback) automatically — unlike the `Runtime.*` packages, these are a `dotnet tool`
implementation detail, never meant to be installed directly, so they aren't listed here.

## Supported Platforms

Windows, Linux and macOS on **x64 or arm64**, including musl-based Linux distributions such as Alpine. Any
other architecture gets an explanatory error rather than a mismatched binary — point at your own Bun with
`SCARLET_BUN_PATH` (CLI) or `BunRuntimeDirectory` (MSBuild) if you need one of them.

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

CLI tests:
```bash
dotnet test tests/Scarlet.Bun.Cli.Tests/Scarlet.Bun.Cli.Tests.csproj
```

All tests:
```bash
dotnet test
```

End-to-end scenarios pack real packages into a local feed and consume them from a temporary project. They
take a workspace path, a package version and a Bun version:
```bash
tests/e2e/package-installation/verify.sh "$PWD" 1.0.0-local 1.4.2
tests/e2e/cli-tool/verify.sh            "$PWD" 1.0.0-local 1.4.2
```

### Creating a Package

```bash
dotnet pack src/Scarlet.Bun.MSBuild/Scarlet.Bun.MSBuild.csproj
```

The CLI packs into ten packages at once — one per runtime identifier, a portable fallback and a
top-level pointer package:
```bash
dotnet pack src/Scarlet.Bun.Cli/Scarlet.Bun.Cli.csproj
```

`src/Scarlet.Bun.Core` is a shared library used by both shipping packages. It is deliberately not
published: the MSBuild package packs the assembly into its `tools/` folder, and the CLI carries it in its
publish output.

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
