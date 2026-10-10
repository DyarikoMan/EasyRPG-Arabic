using System.IO.Compression;

namespace EasyRpgArabic.Setup.Core.Maniacs;

/// <summary>Extracts an embedded validated package to a temporary directory for the existing inspector and modules.</summary>
public sealed class TemporaryManiacsPackage : IDisposable
{
	private const int MaximumEntryCount = 2048;
	private const long MaximumExpandedBytes = 128L * 1024 * 1024;
	private bool _disposed;

	public string DirectoryPath { get; }

	private TemporaryManiacsPackage(string directoryPath) => DirectoryPath = directoryPath;

	public static TemporaryManiacsPackage Extract(Func<Stream> openArchive)
	{
		ArgumentNullException.ThrowIfNull(openArchive);
		var directory = Path.Combine(Path.GetTempPath(), "EasyRpgArabic", "Maniacs241029", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		try
		{
			using var input = openArchive();
			using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
			if (archive.Entries.Count > MaximumEntryCount)
			{
				throw new InvalidDataException("The embedded Maniacs package contains too many archive entries.");
			}

			long expandedBytes = 0;
			foreach (var entry in archive.Entries)
			{
				var relativePath = entry.FullName.Replace('\\', '/');
				var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
				if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal) ||
					Path.IsPathRooted(relativePath) || relativePath.Split('/').Any(part => part is ".." or ".") || unixFileType == 0xA000)
				{
					throw new InvalidDataException($"The embedded Maniacs package contains an unsafe path: {entry.FullName}");
				}

				var isDirectory = relativePath.EndsWith("/", StringComparison.Ordinal);
				var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
				var target = Path.GetFullPath(Path.Combine(directory, normalizedRelativePath));
				var fullRoot = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (!target.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"The embedded Maniacs package path escapes its temporary directory: {entry.FullName}");
				}

				if (isDirectory)
				{
					Directory.CreateDirectory(target);
					continue;
				}

				expandedBytes = checked(expandedBytes + entry.Length);
				if (expandedBytes > MaximumExpandedBytes)
				{
					throw new InvalidDataException("The embedded Maniacs package exceeds the supported expanded size.");
				}

				Directory.CreateDirectory(Path.GetDirectoryName(target)!);
				using var source = entry.Open();
				using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				source.CopyTo(destination);
				if (destination.Length != entry.Length)
				{
					throw new InvalidDataException($"The embedded Maniacs package entry was truncated: {entry.FullName}");
				}
			}

			return new TemporaryManiacsPackage(directory);
		}
		catch
		{
			try { Directory.Delete(directory, recursive: true); } catch { /* Preserve the extraction error. */ }
			throw;
		}
	}

	public void Dispose()
	{
		if (_disposed) { return; }
		_disposed = true;
		if (Directory.Exists(DirectoryPath)) { Directory.Delete(DirectoryPath, recursive: true); }
	}
}
