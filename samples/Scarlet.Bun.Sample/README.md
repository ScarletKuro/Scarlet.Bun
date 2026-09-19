# Scarlet.Bun.Sample

This is a sample application demonstrating the use of `Scarlet.Bun.MSBuild` in a real project.

## What This Sample Demonstrates

This sample project shows how to integrate Bun into your .NET build process using the Scarlet.Bun.MSBuild task. During the build:

1. **Bun Install** - Dependencies are installed from `package.json` using `bun install --frozen-lockfile`
2. **Asset Bundling** - JavaScript files are concatenated and minified using Terser
3. **SCSS Compilation** - SCSS files are compiled to CSS and minified using Sass
4. **Build Integration** - All of this happens automatically as part of the MSBuild process

## Project Structure

```
Scarlet.Bun.Sample/
├── assets/
│   ├── scripts/          # Source JavaScript files
│   │   ├── hello.js
│   │   └── utils.js
│   └── styles/           # Source SCSS files
│       ├── _variables.scss
│       └── style.scss
├── wwwroot/              # Generated output (created during build)
│   ├── js/
│   │   └── bundle.min.js # Minified JavaScript bundle
│   └── css/
│       └── style.min.css # Compiled and minified CSS
├── build.mjs             # Bun build script
├── package.json          # Node dependencies
├── Program.cs            # Simple web server
└── Scarlet.Bun.Sample.csproj  # Project file with BunBeforeStaticWebAssets
```

## How It Works

The `.csproj` file declares Bun steps that run before static web assets are discovered:

```xml
<ItemGroup>
  <BunInstallInputs Include="package.json" />
  <BunInstallInputs Include="bun.lock" Condition="Exists('$(MSBuildProjectDirectory)\bun.lock')" />
  <BunBuildInputs Include="build.mjs;assets\scripts\**\*.js;assets\styles\**\*.scss" />
  <BunBuildInputs Include="bun.lock" Condition="Exists('$(MSBuildProjectDirectory)\bun.lock')" />
  <BunBuildOutputs Include="wwwroot\js\bundle.min.js;wwwroot\css\style.min.css" />

  <BunBeforeStaticWebAssets Include="install">
    <Arguments>--frozen-lockfile</Arguments>
    <TimeoutMilliseconds>60000</TimeoutMilliseconds>
    <Inputs>@(BunInstallInputs)</Inputs>
    <Outputs>node_modules</Outputs>
  </BunBeforeStaticWebAssets>

  <BunBeforeStaticWebAssets Include="run">
    <Arguments>build.mjs</Arguments>
    <TimeoutMilliseconds>60000</TimeoutMilliseconds>
    <Inputs>@(BunBuildInputs)</Inputs>
    <Outputs>@(BunBuildOutputs)</Outputs>
  </BunBeforeStaticWebAssets>
</ItemGroup>
```

## Running the Sample

### Build the Sample

```bash
# From the repository root
dotnet build samples/Scarlet.Bun.Sample/Scarlet.Bun.Sample.csproj
```

This will:
1. Install npm dependencies via Bun
2. Run the build.mjs script to bundle JavaScript and compile SCSS
3. Verify the output files were created
4. Build the .NET application

### Run the Sample

```bash
# From the repository root
dotnet run --project samples/Scarlet.Bun.Sample/Scarlet.Bun.Sample.csproj
```

Then open your browser to `http://localhost:5000` to see the application running with the bundled assets.

### Watching for Changes

```bash
# From the repository root
dotnet watch --project samples/Scarlet.Bun.Sample/Scarlet.Bun.Sample.csproj run
```

The `.csproj` includes a `Watch` item pointing at `assets/**/*.js` and `assets/**/*.scss`, so editing a file
under `assets/` triggers `dotnet watch` to rebuild — which re-runs the Bun steps and refreshes the
browser. See [dotnet watch Integration](../../src/Scarlet.Bun.MSBuild/README.md#dotnet-watch-integration)
for the general pattern.

## What Gets Built

- **JavaScript Bundle** (`wwwroot/js/bundle.min.js`): All JavaScript files from `assets/scripts/` are concatenated and minified
- **CSS Bundle** (`wwwroot/css/style.min.css`): SCSS files from `assets/styles/` are compiled and minified

## CI/CD Integration

This sample is also built and tested as part of the CI pipeline to ensure that:
- `BunBeforeStaticWebAssets` works correctly in CI environments
- JavaScript and CSS bundles are created successfully
- The build process completes without errors

See `.github/workflows/ci.yml` for the CI configuration.
