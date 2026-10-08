using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;

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
			owned.AddRange(await ConfigureTextEncodingAsync(context, created, cancellationToken).ConfigureAwait(false));
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
			var hasConfigBackup = result.OwnedFiles.Any(file => string.Equals(file.RelativePath, ManiacsTextEncodingConfig.OriginalBackupPath, StringComparison.OrdinalIgnoreCase));
			var expectedOwnedCount = PayloadFiles.Length + 1 + (hasConfigBackup ? 1 : 0);
			if (result.OwnedFiles.Count != expectedOwnedCount)
			{
				return Task.FromResult(ModuleVerificationResult.Failure("The module ownership record does not match its plugin files and encoding configuration."));
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
			var configPath = ResolveOwnedPath(context.StagingDirectory, ManiacsTextEncodingConfig.RelativePath);
			if (!File.Exists(configPath)) { return Task.FromResult(ModuleVerificationResult.Failure("Plugin/cmdcs.xml is missing.")); }
			var configBytes = File.ReadAllBytes(configPath);
			if (!string.Equals(ManiacsTextEncodingConfig.ReadCodePage(configBytes), ManiacsTextEncodingConfig.ArabicWindowsCodePage, StringComparison.Ordinal))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Maniacs Text Encoding is not Arabic (Windows) / CP1256."));
			}
			var configOwned = result.OwnedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, ManiacsTextEncodingConfig.RelativePath, StringComparison.OrdinalIgnoreCase));
			if (configOwned is null || !string.Equals(configOwned.Sha256, Convert.ToHexString(SHA256.HashData(configBytes)), StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Plugin/cmdcs.xml does not match the module's recorded configuration."));
			}
			var backupOwned = result.OwnedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, ManiacsTextEncodingConfig.OriginalBackupPath, StringComparison.OrdinalIgnoreCase));
			if (backupOwned is not null)
			{
				var backupPath = ResolveOwnedPath(context.StagingDirectory, backupOwned.RelativePath);
				if (!File.Exists(backupPath) || !string.Equals(ComputeFileHash(backupPath), backupOwned.Sha256, StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(ModuleVerificationResult.Failure("The preserved pre-install cmdcs.xml backup is missing or changed."));
				}
			}
			return Task.FromResult(ModuleVerificationResult.Success("Arabic Editor files match the package and Maniacs Text Encoding is Arabic (Windows) / CP1256."));
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or XmlException)
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
		cancellationToken.ThrowIfCancellationRequested();
		var removed = new List<string>();
		var preserved = new List<string>();
		var config = ownedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, ManiacsTextEncodingConfig.RelativePath, StringComparison.OrdinalIgnoreCase));
		var backup = ownedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, ManiacsTextEncodingConfig.OriginalBackupPath, StringComparison.OrdinalIgnoreCase));
		if (backup is not null)
		{
			var backupPath = ResolveOwnedPath(context.StagingDirectory, backup.RelativePath);
			var configPath = ResolveOwnedPath(context.StagingDirectory, ManiacsTextEncodingConfig.RelativePath);
			if (File.Exists(backupPath) && string.Equals(ComputeFileHash(backupPath), backup.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				if (Directory.Exists(configPath)) { throw new IOException("The Plugin/cmdcs.xml path became a directory; refusing to replace it during uninstall."); }
				var configStillOwned = config is not null && File.Exists(configPath) &&
					string.Equals(ComputeFileHash(configPath), config.Sha256, StringComparison.OrdinalIgnoreCase);
				if (!File.Exists(configPath) || configStillOwned)
				{
					Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
					EnsureNoReparsePoint(Path.GetDirectoryName(configPath)!);
					EnsureNoReparsePoint(configPath);
					EnsureNoReparsePoint(backupPath);
					cancellationToken.ThrowIfCancellationRequested();
					File.Copy(backupPath, configPath, overwrite: true);
					File.SetLastWriteTimeUtc(configPath, File.GetLastWriteTimeUtc(backupPath));
				}
				else
				{
					preserved.Add(ManiacsTextEncodingConfig.RelativePath);
				}
				File.Delete(backupPath);
				removed.Add(backup.RelativePath);
			}
			else
			{
				preserved.Add(backup.RelativePath);
			}
		}
		else if (config is not null)
		{
			var configPath = ResolveOwnedPath(context.StagingDirectory, config.RelativePath);
			if (File.Exists(configPath) && string.Equals(ComputeFileHash(configPath), config.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				File.Delete(configPath);
				removed.Add(config.RelativePath);
			}
			else if (File.Exists(configPath))
			{
				preserved.Add(config.RelativePath);
			}
		}
		foreach (var owned in ownedFiles.Where(file =>
			!string.Equals(file.RelativePath, ManiacsTextEncodingConfig.RelativePath, StringComparison.OrdinalIgnoreCase) &&
			!string.Equals(file.RelativePath, ManiacsTextEncodingConfig.OriginalBackupPath, StringComparison.OrdinalIgnoreCase)))
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

	private static async Task<IReadOnlyList<ModuleOwnedFile>> ConfigureTextEncodingAsync(
		SetupModuleContext context,
		ICollection<string> created,
		CancellationToken cancellationToken)
	{
		var stagedConfig = ResolveOwnedPath(context.StagingDirectory, ManiacsTextEncodingConfig.RelativePath);
		if (File.Exists(stagedConfig) || Directory.Exists(stagedConfig))
		{
			throw new IOException("Refusing to overwrite an existing staged Plugin/cmdcs.xml.");
		}

		var previousConfig = ResolveOwnedPath(context.FinalDirectory, ManiacsTextEncodingConfig.RelativePath);
		var hasPreviousConfig = File.Exists(previousConfig);
		if (Directory.Exists(previousConfig)) { throw new IOException("The existing Plugin/cmdcs.xml path is a directory."); }
		EnsureNoReparsePoint(context.FinalDirectory);
		EnsureNoReparsePoint(Path.Combine(context.FinalDirectory, "Plugin"));
		EnsureNoReparsePoint(previousConfig);

		var owned = new List<ModuleOwnedFile>();
		byte[] configBytes;
		DateTime? previousTimestamp = null;
		if (hasPreviousConfig)
		{
			var original = await File.ReadAllBytesAsync(previousConfig, cancellationToken).ConfigureAwait(false);
			configBytes = ManiacsTextEncodingConfig.SetArabicWindowsCodePage(original);
			previousTimestamp = File.GetLastWriteTimeUtc(previousConfig);

			var backupPath = ResolveOwnedPath(context.StagingDirectory, ManiacsTextEncodingConfig.OriginalBackupPath);
			if (File.Exists(backupPath) || Directory.Exists(backupPath)) { throw new IOException("Refusing to overwrite the cmdcs.xml rollback backup."); }
			await WriteNewFileAsync(backupPath, original, created, cancellationToken).ConfigureAwait(false);
			File.SetLastWriteTimeUtc(backupPath, previousTimestamp.Value);
			owned.Add(new ModuleOwnedFile(ManiacsTextEncodingConfig.OriginalBackupPath, Convert.ToHexString(SHA256.HashData(original))));
		}
		else
		{
			configBytes = ManiacsTextEncodingConfig.CreateArabicDefault();
		}

		await WriteNewFileAsync(stagedConfig, configBytes, created, cancellationToken).ConfigureAwait(false);
		if (previousTimestamp is { } timestamp) { File.SetLastWriteTimeUtc(stagedConfig, timestamp); }
		owned.Add(new ModuleOwnedFile(ManiacsTextEncodingConfig.RelativePath, Convert.ToHexString(SHA256.HashData(configBytes))));
		return owned;
	}

	private static async Task WriteNewFileAsync(string path, byte[] bytes, ICollection<string> created, CancellationToken cancellationToken)
	{
		var parent = Path.GetDirectoryName(path)!;
		Directory.CreateDirectory(parent);
		created.Add(path);
		await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
		await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private Dictionary<string, byte[]> ReadPayload()
	{
		using var stream = _openPackage();
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
		var payload = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
		foreach (var fileName in PayloadFiles)
		{
			var expectedEntryName = $"Plugin/{fileName}";
			var entry = archive.Entries.FirstOrDefault(candidate =>
				string.Equals(candidate.FullName.Replace('\\', '/'), expectedEntryName, StringComparison.OrdinalIgnoreCase))
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
		EnsureNoReparsePoint(fullRoot.TrimEnd(Path.DirectorySeparatorChar));
		var current = fullRoot.TrimEnd(Path.DirectorySeparatorChar);
		foreach (var part in Path.GetRelativePath(current, fullPath).Split(Path.DirectorySeparatorChar))
		{
			current = Path.Combine(current, part);
			EnsureNoReparsePoint(current);
		}
		return fullPath;
	}

	private static void EnsureNoReparsePoint(string path)
	{
		if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException($"Refusing linked/reparse configuration path: {path}");
		}
	}

	private static string ComputeFileHash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}

public static partial class RecommendedSetupModules
{
	public static IReadOnlyList<ISetupModule> Create(Func<Stream> openArabicEditorPackage)
	{
		var catalog = EasyRpgArabic.Setup.Core.Maniacs.ManiacsRecipeCatalog.LoadEmbedded();
		var inspector = new EasyRpgArabic.Setup.Core.Maniacs.ManiacsPackageInspector(catalog);
		return
	[
		new ManiacsSetupModule(inspector, catalog),
		new ManiacsEnglishSetupModule(inspector, catalog),
		new ArabicEditorSetupModule(openArabicEditorPackage),
		new UnavailableSetupModule(new SetupModuleDescriptor(
			"easyrpg-arabic", "EasyRPG-Arabic", "unavailable", [], ["EasyRPG-Arabic Player release payload"], ModuleReadiness.NotImplemented,
			"Runtime installation is not implemented in this version."))
	];
	}
}
