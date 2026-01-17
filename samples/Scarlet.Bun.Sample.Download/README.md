# Scarlet.Bun.Sample.Download

This sample demonstrates using the **runtime download** feature of Scarlet.Bun.MSBuild.

## Features

- Downloads Bun runtime from GitHub releases on-demand
- Caches the runtime in a local directory for reuse
- Uses a specific version (1.3.6) for reproducibility
- Automatically bundles JavaScript and compiles SCSS during build

## Configuration

The runtime download mode is configured in the `.csproj` file:

```xml
<PropertyGroup>
  <!-- Enable Bun runtime download mode -->
  <BunRuntimeDownload>true</BunRuntimeDownload>
  <BunVersionDownload>1.3.6</BunVersionDownload>
  <BunRuntimeDirectory>$(MSBuildProjectDirectory)/runtimes</BunRuntimeDirectory>
</PropertyGroup>
```

## How It Works

1. **First Build**: When you build the project for the first time, the Bun runtime will be automatically downloaded from GitHub releases to the `runtimes/` directory.

2. **Subsequent Builds**: The cached runtime in `runtimes/` will be reused, so no download is needed.

3. **Asset Processing**: The downloaded runtime is used to:
   - Install npm dependencies with `bun install`
   - Bundle and minify JavaScript files
   - Compile and minify SCSS files

## Running the Sample

```bash
# Build the project (will download runtime on first build)
dotnet build

# Run the project
dotnet run
```

The first build will take longer as it downloads the Bun runtime (~20-40MB depending on platform). Subsequent builds will be fast as the runtime is cached.

## Output

After building, the following files will be generated:
- `wwwroot/js/bundle.min.js` - Minified JavaScript bundle
- `wwwroot/css/style.min.css` - Compiled and minified CSS

## Advantages of Download Mode

- **No Package Dependencies**: You don't need to install separate runtime packages (like `Scarlet.Bun.Runtime.windows-x64-baseline`)
- **Version Control**: Pin to a specific Bun version for reproducibility
- **Smaller Repository**: Runtime binaries aren't embedded in your project
- **Flexibility**: Easy to switch between different Bun versions

## Cleaning Up

To clean the downloaded runtime:

```bash
rm -rf runtimes/
```

The runtime will be re-downloaded on the next build.
