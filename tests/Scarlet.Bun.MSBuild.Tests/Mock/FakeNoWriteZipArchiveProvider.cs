using System.IO.Compression;
using Scarlet.Bun.MSBuild.Providers;

namespace Scarlet.Bun.MSBuild.Tests.Mock;

public sealed class FakeNoWriteZipArchiveProvider : IZipArchiveProvider
{
    public ZipArchive OpenRead(string archiveFileName)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("bun-linux-x64-baseline/bun");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("bun exists in archive");
        }

        stream.Position = 0;
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        // Intentionally no-op to simulate a provider that reports extraction path
        // without writing the file, so post-extraction existence validation is exercised.
    }
}