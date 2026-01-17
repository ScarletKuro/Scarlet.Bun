# Scarlet.Bun.Sample

This is a sample application demonstrating the use of `Scarlet.Bun.MSBuild` in a real project.

## What This Sample Demonstrates

This sample project shows how to integrate Bun into your .NET build process using the Scarlet.Bun.MSBuild task. During the build:

1. **Bun Install** - Dependencies are installed from `package.json` using `bun install`
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
└── Scarlet.Bun.Sample.csproj  # Project file with BunRunTask
```

## How It Works

The `.csproj` file contains MSBuild targets that use `BunRunTask`:

```xml
<!-- Install Bun dependencies before build -->
<Target Name="BunInstall" BeforeTargets="PreBuildEvent">
  <BunRunTask 
    Command="install"
    WorkingDirectory="$(MSBuildProjectDirectory)"
    TimeoutMilliseconds="60000" />
</Target>

<!-- Build assets using Bun -->
<Target Name="BunBuildAssets" AfterTargets="BunInstall" BeforeTargets="Build">
  <BunRunTask 
    Command="run"
    Arguments="build.mjs"
    WorkingDirectory="$(MSBuildProjectDirectory)"
    TimeoutMilliseconds="60000" />
</Target>
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

## What Gets Built

- **JavaScript Bundle** (`wwwroot/js/bundle.min.js`): All JavaScript files from `assets/scripts/` are concatenated and minified
- **CSS Bundle** (`wwwroot/css/style.min.css`): SCSS files from `assets/styles/` are compiled and minified

## CI/CD Integration

This sample is also built and tested as part of the CI pipeline to ensure that:
- The BunRunTask works correctly in CI environments
- JavaScript and CSS bundles are created successfully
- The build process completes without errors

See `.github/workflows/ci.yml` for the CI configuration.
