namespace Scarlet.Bun.MSBuild;

/// <summary>
/// Represents the supported platforms for Bun runtime.
/// </summary>
public enum Platform
{
    /// <summary>Windows x64</summary>
    WindowsX64,
    /// <summary>Linux x64</summary>
    LinuxX64,
    /// <summary>Linux ARM64</summary>
    LinuxArm64,
    /// <summary>macOS x64</summary>
    MacOsX64,
    /// <summary>macOS ARM64</summary>
    MacOsArm64,
    /// <summary>Windows ARM64</summary>
    WindowsArm64
}
