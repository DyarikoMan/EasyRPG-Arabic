using System.IO.Compression;
using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class TemporaryManiacsPackageTests
{
	[Fact]
	public void Extract_CreatesPackageRootAndDisposeRemovesTemporaryFiles()
	{
		var archiveBytes = CreateArchive((archive) =>
		{
			AddFile(archive, "Common/accord.dll", [1, 2, 3]);
			AddFile(archive, "LICENSE.txt", [4, 5]);
		});

		string extractedPath;
		using (var package = TemporaryManiacsPackage.Extract(() => new MemoryStream(archiveBytes)))
		{
			extractedPath = package.DirectoryPath;
			Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(extractedPath, "Common", "accord.dll")));
			Assert.Equal(new byte[] { 4, 5 }, File.ReadAllBytes(Path.Combine(extractedPath, "LICENSE.txt")));
		}

		Assert.False(Directory.Exists(extractedPath));
	}

	[Fact]
	public void Extract_RejectsPathTraversalAndRemovesPartialExtraction()
	{
		var escapedName = "outside-" + Guid.NewGuid().ToString("N") + ".txt";
		var archiveBytes = CreateArchive(archive => AddFile(archive, "../" + escapedName, [1]));

		Assert.Throws<InvalidDataException>(() => TemporaryManiacsPackage.Extract(() => new MemoryStream(archiveBytes)));
		Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "EasyRpgArabic", escapedName)));
	}

	private static byte[] CreateArchive(Action<ZipArchive> write)
	{
		using var output = new MemoryStream();
		using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true)) { write(archive); }
		return output.ToArray();
	}

	private static void AddFile(ZipArchive archive, string path, byte[] contents)
	{
		var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
		using var output = entry.Open();
		output.Write(contents);
	}
}
