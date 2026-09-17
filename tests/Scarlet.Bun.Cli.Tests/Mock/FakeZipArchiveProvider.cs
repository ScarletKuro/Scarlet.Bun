using System.IO.Abstractions;
using System.IO.Compression;

namespace Scarlet.Bun.Cli.Tests.Mock;

/// <summary>
/// An in-memory Bun archive, so the download path can be exercised without a network or a real zip.
/// </summary>
internal sealed class FakeZipArchiveProvider : IZipArchiveProvider
{
    private readonly byte[] _zipBytes;
    private readonly IFileSystem _fileSystem;

    public FakeZipArchiveProvider(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;

        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Real Bun archives nest the executable inside a directory; mirror that so the downloader's
            // entry search is exercised rather than short-circuited.
            foreach (var name in new[] { "bun-linux-x64-baseline/bun", "bun-windows-x64-baseline/bun.exe" })
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream);
                writer.Write("fake bun executable");
            }
        }

        _zipBytes = buffer.ToArray();
    }

    public ZipArchive OpenRead(string archiveFileName) => new(new MemoryStream(_zipBytes), ZipArchiveMode.Read);

    public void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite)
    {
        using var entryStream = source.Open();
        using var fileStream = _fileSystem.File.Create(destinationFileName);
        entryStream.CopyTo(fileStream);
    }
}
