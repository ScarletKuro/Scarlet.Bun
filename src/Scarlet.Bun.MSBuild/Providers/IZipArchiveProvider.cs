using System.IO.Compression;

namespace Scarlet.Bun.MSBuild.Providers;

public interface IZipArchiveProvider
{
    ZipArchive OpenRead(string path);

    void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite);
}