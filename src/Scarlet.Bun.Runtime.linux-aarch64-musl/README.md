# Scarlet.Bun.Runtime.linux-aarch64-musl

This package contains the Bun runtime for Linux ARM64, built against musl libc (e.g. Alpine).

`Scarlet.Bun.MSBuild` does **not** depend on this package directly.
Install it only if you want to provide a pre-packaged Bun runtime — `Scarlet.Bun.MSBuild` will discover it automatically when present.

The package version corresponds to the bundled Bun version (e.g., package version `1.3.6` contains Bun `1.3.6`).
