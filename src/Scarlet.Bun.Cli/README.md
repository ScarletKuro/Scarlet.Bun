# Scarlet.Bun.Cli

Run [Bun](https://bun.sh) — the fast all-in-one JavaScript runtime, bundler and package manager — as a
.NET tool.

```bash
dotnet bun install
dotnet bun run build.mjs
dotnet bun --version      # prints Bun's version, not the tool's
```

Every argument is forwarded to Bun verbatim, so anything valid after `bun` is valid after `dotnet bun`.

## Why not just install Bun?

Because this pins it. The Bun version lives in `.config/dotnet-tools.json` next to the rest of your
tooling, `dotnet tool restore` brings it down with the repository, and NuGet hash-verifies the binary.

The platform-specific package **contains the Bun binary**, so it needs no network at run time — no
download on first use, no dependency on github.com being reachable, and no chance of a CI agent quietly
picking up a different Bun than your laptop did.

## Install

Per repository (recommended — this is the part that pins):

```bash
dotnet new tool-manifest      # once per repository
dotnet tool install Scarlet.Bun.Cli
dotnet bun --version
```

Commit `.config/dotnet-tools.json` and every contributor and CI agent gets the same Bun from
`dotnet tool restore`.

Globally:

```bash
dotnet tool install -g Scarlet.Bun.Cli
```

Or once, without installing anything (.NET 10 SDK):

```bash
dnx Scarlet.Bun.Cli -- run build.mjs
```

> **The package version is the Bun version.** `Scarlet.Bun.Cli` 1.4.2 contains Bun 1.4.2, the same as the
> `Scarlet.Bun.Runtime.*` packages.

## How it finds Bun

`Scarlet.Bun.Cli` is a pointer package: it owns the `dotnet-bun` command but carries no Bun binary itself.
Installing it makes `dotnet tool install`/`dotnet tool restore` also pull one matching sub-package for your
machine's RID — `Scarlet.Bun.Cli.win-x64`, `Scarlet.Bun.Cli.linux-arm64`, and so on — and *that* package
embeds the actual Bun binary. This is automatic; you never name a sub-package yourself, and running
`dotnet add package Scarlet.Bun.Cli.<rid>` on one directly installs nothing usable — it carries no library
assets, only a tool payload NuGet places when `Scarlet.Bun.Cli` asks for it.

**Supported platforms are Windows, Linux and macOS on x64 or arm64**, including musl-based Linux
distributions such as Alpine — one sub-package per combination, eight in total. Hosts outside that matrix
restore `Scarlet.Bun.Cli.any` instead: a portable fallback with no embedded binary, so it downloads Bun on
first use and caches it per user rather than shipping a mismatched one. Any other architecture gets an
explanatory error rather than a mismatched binary — install Bun through its own installer and point at it
with `SCARLET_BUN_PATH` if you need one.

Resolution order:

1. `SCARLET_BUN_PATH`, if set — errors if it points at nothing, rather than quietly falling back
2. the Bun embedded in the installed package
3. a previously downloaded Bun in the per-user cache
4. a download

To see what it chose and why:

```bash
dotnet bun --scarlet-info
dotnet bun --scarlet-info --json
```

That is the only argument the tool reserves for itself, it is recognised only as the *first* argument, and
`SCARLET_BUN_PASSTHROUGH=1` disables even that. It never downloads anything — it reports the URL it
*would* use.

## Configuration

| Variable | Effect |
|----------|--------|
| `SCARLET_BUN_PATH` | Use this Bun executable. Highest precedence. |
| `SCARLET_BUN_VERSION` | Resolve a different Bun version, or `latest`. Bypasses the embedded binary. |
| `SCARLET_BUN_CACHE` | Override the download cache root. |
| `SCARLET_BUN_NO_EMBEDDED` | Ignore the embedded binary. |
| `SCARLET_BUN_DIAGNOSTICS` | Print the resolved Bun path to stderr before running. |
| `SCARLET_BUN_PASSTHROUGH` | Disable `--scarlet-info` so every argument reaches Bun. |
| `SCARLET_BUN_DOWNLOAD_TIMEOUT` | Seconds to wait for a concurrent download. Defaults to 300. |

Configuration is environment variables rather than command-line flags on purpose: every argument belongs
to Bun, so a flag Bun adds in future keeps working without a release of this package.

`SCARLET_BUN_VERSION` changes which **Bun binary** gets downloaded and run — it never changes which
**NuGet package** is installed; that's decided once, at `dotnet tool install` time (see
[How it finds Bun](#how-it-finds-bun)). Setting it to anything other than the version baked into the
installed package skips the embedded binary and downloads the requested one into the per-user cache,
scoped by version (`<cache>/runtimes/<version>/`), so later runs with the same value reuse it instead of
re-downloading.

That caching applies to `latest` too, literally: the first run resolves whatever GitHub currently tags as
newest and caches it under a folder named `latest`, and every run after that reuses that cached binary
without checking GitHub again — `latest` means "newest at the time I first asked," not "always current."
Clear `<cache>/runtimes/latest/` (or point `SCARLET_BUN_CACHE` elsewhere) to pick up a newer release.

## Running Bun during a build instead

If you want Bun to run as part of `dotnet build` — bundling JavaScript, compiling Sass, installing npm
dependencies — use [`Scarlet.Bun.MSBuild`](https://www.nuget.org/packages/Scarlet.Bun.MSBuild/) instead.
The two are independent; this tool is for the command line.

## Links

- [Source and full documentation](https://github.com/ScarletKuro/Scarlet.Bun)
- [Bun documentation](https://bun.sh/docs)

Licensed under MIT. Bun itself is licensed separately — see the
[Bun repository](https://github.com/oven-sh/bun).
