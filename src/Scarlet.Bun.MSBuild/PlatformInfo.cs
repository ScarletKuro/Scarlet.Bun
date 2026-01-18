namespace Scarlet.Bun.MSBuild;

public sealed class PlatformInfo
{
    public string Rid { get; }
    public string DirectoryName { get; }
    public string PackageName { get; }
    public string ExecutableName { get; }

    public PlatformInfo(
        string rid,
        string directoryName,
        string packageName,
        string executableName)
    {
        Rid = rid;
        DirectoryName = directoryName;
        PackageName = packageName;
        ExecutableName = executableName;
    }
}
