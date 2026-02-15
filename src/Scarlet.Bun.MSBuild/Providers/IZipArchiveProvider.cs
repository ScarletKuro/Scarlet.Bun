using System.IO.Compression;

namespace Scarlet.Bun.MSBuild.Providers;

public interface IZipArchiveProvider
{
    /// <summary>Opens a zip archive for reading at the specified path.</summary>
    /// <param name="archiveFileName">The path to the archive to open, specified as a relative or absolute path. A relative path is interpreted as relative to the current working directory.</param>
    /// <returns>The opened zip archive.</returns>
    /// <exception cref="T:System.ArgumentException"><paramref name="archiveFileName">archiveFileName</paramref> is <see cref="F:System.String.Empty"></see>, contains only white space, or contains at least one invalid character.</exception>
    /// <exception cref="T:System.ArgumentNullException"><paramref name="archiveFileName">archiveFileName</paramref> is null.</exception>
    /// <exception cref="T:System.IO.PathTooLongException">In <paramref name="archiveFileName">archiveFileName</paramref>, the specified path, file name, or both exceed the system-defined maximum length. For example, on Windows-based platforms, paths must not exceed 248 characters, and file names must not exceed 260 characters.</exception>
    /// <exception cref="T:System.IO.DirectoryNotFoundException"><paramref name="archiveFileName">archiveFileName</paramref> is invalid or does not exist (for example, it is on an unmapped drive).</exception>
    /// <exception cref="T:System.IO.IOException"><paramref name="archiveFileName">archiveFileName</paramref> could not be opened.</exception>
    /// <exception cref="T:System.UnauthorizedAccessException"><paramref name="archiveFileName">archiveFileName</paramref> specifies a directory.   -or-   The caller does not have the required permission to access the file specified in <paramref name="archiveFileName">archiveFileName</paramref>.</exception>
    /// <exception cref="T:System.IO.FileNotFoundException">The file specified in <paramref name="archiveFileName">archiveFileName</paramref> is not found.</exception>
    /// <exception cref="T:System.NotSupportedException"><paramref name="archiveFileName">archiveFileName</paramref> contains an invalid format.</exception>
    /// <exception cref="T:System.IO.InvalidDataException"><paramref name="archiveFileName">archiveFileName</paramref> could not be interpreted as a zip archive.</exception>
    ZipArchive OpenRead(string archiveFileName);

    /// <summary>Extracts an entry in the zip archive to a file, and optionally overwrites an existing file that has the same name.</summary>
    /// <param name="source">The zip archive entry to extract a file from.</param>
    /// <param name="destinationFileName">The path of the file to create from the contents of the entry. You can specify either a relative or an absolute path. A relative path is interpreted as relative to the current working directory.</param>
    /// <param name="overwrite">true to overwrite an existing file that has the same name as the destination file; otherwise, false.</param>
    /// <exception cref="T:System.ArgumentException"><paramref name="destinationFileName">destinationFileName</paramref> is a zero-length string, contains only white space, or contains one or more invalid characters as defined by <see cref="F:System.IO.Path.InvalidPathChars"></see>.   -or-  <paramref name="destinationFileName">destinationFileName</paramref> specifies a directory.</exception>
    /// <exception cref="T:System.ArgumentNullException"><paramref name="destinationFileName">destinationFileName</paramref> is null.</exception>
    /// <exception cref="T:System.IO.PathTooLongException">The specified path, file name, or both exceed the system-defined maximum length. For example, on Windows-based platforms, paths must not exceed 248 characters, and file names must not exceed 260 characters.</exception>
    /// <exception cref="T:System.IO.DirectoryNotFoundException">The specified path is invalid (for example, it is on an unmapped drive).</exception>
    /// <exception cref="T:System.IO.IOException"><paramref name="destinationFileName">destinationFileName</paramref> already exists and <paramref name="overwrite">overwrite</paramref> is false.   -or-   An I/O error occurred.   -or-   The entry is currently open for writing.   -or-   The entry has been deleted from the archive.</exception>
    /// <exception cref="T:System.UnauthorizedAccessException">The caller does not have the required permission to create the new file.</exception>
    /// <exception cref="T:System.IO.InvalidDataException">The entry is missing from the archive or is corrupt and cannot be read.   -or-   The entry has been compressed by using a compression method that is not supported.</exception>
    /// <exception cref="T:System.ObjectDisposedException">The zip archive that this entry belongs to has been disposed.</exception>
    /// <exception cref="T:System.NotSupportedException"><paramref name="destinationFileName">destinationFileName</paramref> is in an invalid format.   -or-   The zip archive for this entry was opened in <see cref="F:System.IO.Compression.ZipArchiveMode.Create"></see> mode, which does not permit the retrieval of entries.</exception>
    void ExtractToFile(ZipArchiveEntry source, string destinationFileName, bool overwrite);
}