using System.IO.Compression;

namespace Scarlet.Bun.MSBuild.Providers;

public class ZipArchiveProvider : IZipArchiveProvider
{
    public ZipArchive OpenRead(string path) => ZipFile.OpenRead(path);

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        source.ExtractToFile(destinationFileName, overwrite);
    }

    public static ZipArchiveProvider Instance { get; } = new();
}