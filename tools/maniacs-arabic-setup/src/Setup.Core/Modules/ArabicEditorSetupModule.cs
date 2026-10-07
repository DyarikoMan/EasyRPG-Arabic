using System.IO.Compression;
using System.Security.Cryptography;

namespace EasyRpgArabic.Setup.Core.Modules;

public sealed class ArabicEditorSetupModule : ISetupModule
{
	private static readonly string[] PayloadFiles = ["EasyCmdTos.dll", "EasyRpgCommands.dll", "0Harmony.dll"];
	private readonly Func<Stream> _openPackage;

	public SetupModuleDescriptor Descriptor { get; } = new(
		"arabic-editor",
		"Arabic Editor support",
		"1.0.0",
		["maniacs-english"],
		["Maniacs English installation with rpg2003.exe, cmdcs.dll, cmdTos.dll, and Plugin folder"],
		ModuleReadiness.Available,
		"The existing Arabic Editor package is ready; it requires the Maniacs English module.");

	public ArabicEditorSetupModule(Func<Stream> openPackage)
	{
		_openPackage = openPackage ?? throw new ArgumentNullException(nameof(openPackage));
	}

	public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
	{
		ValidateHost(context.StagingDirectory);
		var payload = ReadPayload();
		var pluginDirectory = Path.Combine(context.StagingDirectory, "Plugin");
		foreach (var fileName in PayloadFiles)
		{
			var target = Path.Combine(pluginDirectory, fileName);
			if (File.Exists(target))
			{
				throw new IOException($"Refusing to overwrite an existing plugin file: {target}");
			}
		}

		var created = new List<string>();
		try
		{
			var owned = new List<ModuleOwnedFile>();
			foreach (var fileName in PayloadFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var bytes = payload[fileName];
				var target = Path.Combine(pluginDirectory, fileName);
				await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
				{
					created.Add(target);
					await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
					await output.FlushAsync(cancellationToken).ConfigureAwait(false);
				}
				owned.Add(new ModuleOwnedFile($"Plugin/{fileName}", Convert.ToHexString(SHA256.HashData(bytes))));
			}
			return new ModuleApplyResult(owned);
		}
		catch
		{
			foreach (var path in created)
			{
				try { if (File.Exists(path)) { File.Delete(path); } }
				catch { /* The staging transaction will remove its isolated tree. */ }
			}
			throw;
		}
	}

	public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			ValidateHost(context.StagingDirectory);
			var expected = ReadPayload();
			if (result.OwnedFiles.Count != PayloadFiles.Length)
			{
				return Task.FromResult(ModuleVerificationResult.Failure("The module did not report all three plugin files."));
			}
			foreach (var fileName in PayloadFiles)
			{
				var path = Path.Combine(context.StagingDirectory, "Plugin", fileName);
				if (!File.Exists(path)) { return Task.FromResult(ModuleVerificationResult.Failure($"Plugin/{fileName} is missing.")); }
				var actual = Convert.ToHexString(SHA256.HashData(expected[fileName]));
				var recorded = result.OwnedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, $"Plugin/{fileName}", StringComparison.OrdinalIgnoreCase));
				if (recorded is null || !string.Equals(actual, recorded.Sha256, StringComparison.OrdinalIgnoreCase) ||
					!string.Equals(actual, ComputeFileHash(path), StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(ModuleVerificationResult.Failure($"Plugin/{fileName} failed its package hash check."));
				}
			}
			return Task.FromResult(ModuleVerificationResult.Success("All Arabic Editor plugin files match the existing release package."));
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return Task.FromResult(ModuleVerificationResult.Failure(exception.Message));
		}
	}

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		foreach (var owned in result.OwnedFiles)
		{
			var path = ResolveOwnedPath(context.StagingDirectory, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!string.Equals(ComputeFileHash(path), owned.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				throw new IOException($"Refusing to remove modified module-owned file during rollback: {owned.RelativePath}");
			}
			File.Delete(path);
		}
		return Task.CompletedTask;
	}

	public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default)
	{
		var removed = new List<string>();
		var preserved = new List<string>();
		foreach (var owned in ownedFiles)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var path = ResolveOwnedPath(context.StagingDirectory, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!string.Equals(ComputeFileHash(path), owned.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				preserved.Add(owned.RelativePath);
				continue;
			}
			File.Delete(path);
			removed.Add(owned.RelativePath);
		}
		return Task.FromResult(new ModuleUninstallResult(removed, preserved));
	}

	private Dictionary<string, byte[]> ReadPayload()
	{
		using var stream = _openPackage();
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
		var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var fileName in PayloadFiles)
		{
			var entry = archive.GetEntry($"Plugin/{fileName}")
				?? throw new InvalidDataException($"The Arabic Editor release ZIP is missing Plugin/{fileName}.");
			using var entryStream = entry.Open();
			using var memory = new MemoryStream();
			entryStream.CopyTo(memory);
			if (memory.Length == 0) { throw new InvalidDataException($"The Arabic Editor release file is empty: Plugin/{fileName}."); }
			payload.Add(fileName, memory.ToArray());
		}
		return payload;
	}

	private static void ValidateHost(string root)
	{
		foreach (var file in new[] { "rpg2003.exe", "cmdcs.dll", "cmdTos.dll" })
		{
			if (!File.Exists(Path.Combine(root, file))) { throw new InvalidOperationException($"Arabic Editor support requires Maniacs file {file}."); }
		}
		if (!Directory.Exists(Path.Combine(root, "Plugin")))
		{
			throw new InvalidOperationException("Arabic Editor support requires the Maniacs Plugin folder.");
		}
	}

	private static string ResolveOwnedPath(string root, string relativePath)
	{
		var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
		if (Path.IsPathRooted(normalized) || normalized.Split(Path.DirectorySeparatorChar).Any(part => part == ".."))
		{
			throw new InvalidDataException($"Invalid module-owned relative path: {relativePath}");
		}
		var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Module-owned path escapes the managed directory: {relativePath}");
		}
		return fullPath;
	}

	private static string ComputeFileHash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}

public static class RecommendedSetupModules
{
	public static IReadOnlyList<ISetupModule> Create(Func<Stream> openArabicEditorPackage) =>
	[
		new ManiacsSetupModule(new EasyRpgArabic.Setup.Core.Maniacs.ManiacsPackageInspector(
			EasyRpgArabic.Setup.Core.Maniacs.ManiacsRecipeCatalog.LoadEmbedded())),
		new UnavailableSetupModule(new SetupModuleDescriptor(
			"maniacs-english", "Maniacs English", "unavailable", ["maniacs"], ["Official Maniacs installation"], ModuleReadiness.NotImplemented,
			"Official English patch application is not automated in this version.")),
		new ArabicEditorSetupModule(openArabicEditorPackage),
		new UnavailableSetupModule(new SetupModuleDescriptor(
			"easyrpg-arabic", "EasyRPG-Arabic", "unavailable", [], ["EasyRPG-Arabic Player release payload"], ModuleReadiness.NotImplemented,
			"Runtime installation is not implemented in this version."))
	];
}
