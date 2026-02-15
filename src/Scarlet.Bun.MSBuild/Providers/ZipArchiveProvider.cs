using System.IO.Compression;

namespace Scarlet.Bun.MSBuild.Providers;

public sealed class ZipArchiveProvider : IZipArchiveProvider
{
    /// <inheritdoc />
    public ZipArchive OpenRead(string path) => ZipFile.OpenRead(path);

    /// <inheritdoc />
    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite) => source.ExtractToFile(destinationFileName, overwrite);

    public static ZipArchiveProvider Instance { get; } = new();
}