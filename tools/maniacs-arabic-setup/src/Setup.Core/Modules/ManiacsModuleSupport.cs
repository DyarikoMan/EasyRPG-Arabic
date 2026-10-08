using System.Buffers.Binary;
using System.Security.Cryptography;
using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Modules;

internal static class ManiacsModuleSupport
{
	public static (string PackageRoot, ManiacsVersionRecipe Recipe) GetValidatedPackage(
		SetupModuleContext context,
		ManiacsPackageInspector inspector,
		ManiacsRecipeCatalog catalog,
		bool requireArabicEditorCompatibility = false)
	{
		if (!context.ManiacsLicenseAccepted) { throw new InvalidOperationException("Read and accept the supplied Maniacs license before installing Maniacs components."); }
		if (string.IsNullOrWhiteSpace(context.ManiacsPackageDirectory)) { throw new InvalidOperationException("Select the extracted official Maniacs package in Advanced options."); }
		var inspection = inspector.Inspect(context.ManiacsPackageDirectory);
		if (!inspection.IsKnownSupportedBuild || (requireArabicEditorCompatibility && !inspection.ArabicEditorValidated) ||
			inspection.PackageRoot is null || inspection.CandidateRecipeId is null)
		{
			throw new InvalidDataException("The supplied Maniacs package is not an exact supported build validated with the Arabic Editor patch. " + inspection.Summary);
		}
		var recipe = catalog.Recipes.SingleOrDefault(item => string.Equals(item.Id, inspection.CandidateRecipeId, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidDataException("The inspected Maniacs recipe is missing from the catalog.");
		return (inspection.PackageRoot, recipe);
	}

	public static void ValidateVanillaSource(SetupModuleContext context, ManiacsVersionRecipe recipe)
	{
		var sourceHashes = recipe.VanillaSourceSha256;
		if (sourceHashes is null || sourceHashes.Count == 0)
		{
			throw new InvalidDataException($"Recipe '{recipe.Id}' has no validated vanilla source fingerprints.");
		}
		foreach (var (relativePath, expectedHash) in sourceHashes)
		{
			var source = SafePath(context.VanillaDirectory, relativePath);
			if (!File.Exists(source)) { throw new InvalidDataException($"Required vanilla file is missing: {relativePath}"); }
			if (!HashMatches(source, expectedHash)) { throw new InvalidDataException($"Vanilla source fingerprint mismatch: {relativePath}"); }
		}
		foreach (var (relativePath, architecture) in recipe.VanillaSourceArchitectures ?? new Dictionary<string, string>())
		{
			var source = SafePath(context.VanillaDirectory, relativePath);
			var actual = ReadPeArchitecture(source);
			if (!string.Equals(actual, architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"Vanilla source architecture mismatch for {relativePath}: expected {architecture}, found {actual}.");
			}
		}
	}

	public static async Task CopyVanillaFilesToStageAsync(
		SetupModuleContext context,
		ManiacsVersionRecipe recipe,
		ICollection<string> createdPaths,
		CancellationToken cancellationToken)
	{
		ValidateVanillaSource(context, recipe);
		var sourceHashes = recipe.VanillaSourceSha256 ?? throw new InvalidDataException($"Recipe '{recipe.Id}' has no vanilla source fingerprints.");
		var files = sourceHashes.Keys
			.Concat(new[] { "rpg2003.ini", "tpc.exe" }.Where(name => File.Exists(SafePath(context.VanillaDirectory, name))))
			.Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var relativePath in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var source = SafePath(context.VanillaDirectory, relativePath);
			var staged = SafePath(context.StagingDirectory, relativePath);
			if (File.Exists(staged) || Directory.Exists(staged))
			{
				throw new IOException($"Refusing to overwrite a conflicting staged vanilla file: {relativePath}");
			}
			createdPaths.Add(staged);
			await CopyNewFileAsync(source, staged, cancellationToken).ConfigureAwait(false);
			var expected = sourceHashes.TryGetValue(relativePath, out var hash) ? hash : HashFile(source);
			if (!HashMatches(staged, expected)) { throw new IOException($"Copied vanilla file failed hash verification: {relativePath}"); }
			if (recipe.VanillaSourceArchitectures?.TryGetValue(relativePath, out var architecture) == true &&
				!string.Equals(ReadPeArchitecture(staged), architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"Copied vanilla file architecture mismatch: {relativePath}");
			}
		}
	}

	public static void ValidateVanillaAndStage(SetupModuleContext context, ManiacsVersionRecipe recipe)
	{
		ValidateVanillaSource(context, recipe);
		var sourceHashes = recipe.VanillaSourceSha256 ?? throw new InvalidDataException($"Recipe '{recipe.Id}' has no vanilla source fingerprints.");
		var expectedFiles = sourceHashes.Keys
			.Concat(new[] { "rpg2003.ini", "tpc.exe" }.Where(name => File.Exists(SafePath(context.VanillaDirectory, name))))
			.Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var relativePath in expectedFiles)
		{
			var source = SafePath(context.VanillaDirectory, relativePath);
			var staged = SafePath(context.StagingDirectory, relativePath);
			if (!File.Exists(staged)) { throw new InvalidDataException($"Required copied vanilla file is missing: {relativePath}"); }
			var expected = sourceHashes.TryGetValue(relativePath, out var hash) ? hash : HashFile(source);
			if (!HashMatches(staged, expected)) { throw new InvalidDataException($"Staged vanilla file differs from the validated source: {relativePath}"); }
			if (recipe.VanillaSourceArchitectures?.TryGetValue(relativePath, out var architecture) == true &&
				!string.Equals(ReadPeArchitecture(staged), architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"Staged vanilla file architecture mismatch for {relativePath}: expected {architecture}.");
			}
		}
	}

	public static void ValidatePackageIdentity(string packageRoot, ManiacsVersionRecipe recipe)
	{
		foreach (var (relativePath, expectedHash) in recipe.IdentityFileSha256)
		{
			var path = SafePath(packageRoot, relativePath);
			if (!File.Exists(path) || !HashMatches(path, expectedHash))
			{
				throw new InvalidDataException($"Official Maniacs package fingerprint mismatch: {relativePath}");
			}
		}
	}

	public static void ValidateBasePayload(string packageRoot, string stagingDirectory, ManiacsVersionRecipe recipe, ManiacsRecipeCatalog catalog)
	{
		var mappings = recipe.BasePayloadFiles ?? throw new InvalidDataException($"Recipe '{recipe.Id}' has no base payload mapping.");
		var layout = catalog.Layouts.Single(candidate => string.Equals(candidate.Id, recipe.LayoutId, StringComparison.OrdinalIgnoreCase));
		foreach (var payload in mappings)
		{
			var source = SafePath(packageRoot, payload.PackagePath);
			var target = SafePath(stagingDirectory, payload.InstallPath);
			var expectedHash = recipe.IdentityFileSha256[payload.PackagePath];
			if (!File.Exists(source) || !File.Exists(target) || !HashMatches(source, expectedHash) || !HashMatches(target, expectedHash))
			{
				throw new InvalidDataException($"Maniacs base payload failed verification: {payload.InstallPath}");
			}
			var architecture = layout.RequiredFiles.SingleOrDefault(file => string.Equals(file.Path, payload.PackagePath, StringComparison.OrdinalIgnoreCase))?.Architecture;
			if (architecture is not null && !string.Equals(ReadPeArchitecture(target), architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"Maniacs base payload architecture mismatch: {payload.InstallPath}");
			}
		}
	}

	public static string SafePath(string root, string relativePath)
	{
		ManiacsRecipeCatalog.ValidateRelativePath(relativePath);
		var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (!Directory.Exists(fullRoot) || (File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException($"Refusing a missing or linked root directory: {fullRoot}");
		}
		var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
		var prefix = fullRoot + Path.DirectorySeparatorChar;
		if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { throw new InvalidDataException($"Path escapes its root: {relativePath}"); }
		var current = fullRoot;
		foreach (var part in Path.GetRelativePath(fullRoot, fullPath).Split(Path.DirectorySeparatorChar))
		{
			current = Path.Combine(current, part);
			if (File.Exists(current) || Directory.Exists(current))
			{
				if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) { throw new IOException($"Refusing a linked/reparse path: {current}"); }
			}
		}
		return fullPath;
	}

	public static bool HashMatches(string path, string expected) => HashFile(path).Equals(expected, StringComparison.OrdinalIgnoreCase);
	public static string HashFile(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	public static string ReadPeArchitecture(string path)
	{
		using var stream = File.OpenRead(path);
		if (stream.Length < 70) { throw new InvalidDataException($"Expected a PE executable or DLL: {path}"); }
		Span<byte> dos = stackalloc byte[64];
		stream.ReadExactly(dos);
		if (dos[0] != (byte)'M' || dos[1] != (byte)'Z') { throw new InvalidDataException($"Expected a PE executable or DLL: {path}"); }
		var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dos[0x3c..]);
		if (peOffset < 64 || peOffset > stream.Length - 6) { throw new InvalidDataException($"Invalid PE header: {path}"); }
		stream.Position = peOffset;
		Span<byte> pe = stackalloc byte[6];
		stream.ReadExactly(pe);
		if (!pe[..4].SequenceEqual("PE\0\0"u8)) { throw new InvalidDataException($"Invalid PE signature: {path}"); }
		return BinaryPrimitives.ReadUInt16LittleEndian(pe[4..]) switch
		{
			0x014c => "x86",
			0x8664 => "x64",
			0x01c0 or 0x01c4 => "arm",
			0xaa64 => "arm64",
			var machine => $"unknown PE machine 0x{machine:X4}"
		};
	}

	public static async Task CopyNewFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
		await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
		await using var output = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
		await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);
		File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(sourcePath));
	}

	public static string OwnedPath(SetupModuleContext context, string relativePath) => SafePath(context.StagingDirectory, relativePath);

	public static IReadOnlyList<string> EnumerateFilesWithoutLinks(string root)
	{
		var result = new List<string>();
		var pending = new Stack<string>();
		pending.Push(root);
		while (pending.Count != 0)
		{
			var directory = pending.Pop();
			if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { throw new IOException($"Refusing linked package directory: {directory}"); }
			foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
			{
				var attributes = File.GetAttributes(entry);
				if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new IOException($"Refusing linked package entry: {entry}"); }
				if ((attributes & FileAttributes.Directory) != 0) { pending.Push(entry); }
				else { result.Add(entry); }
			}
		}
		return result;
	}
}
