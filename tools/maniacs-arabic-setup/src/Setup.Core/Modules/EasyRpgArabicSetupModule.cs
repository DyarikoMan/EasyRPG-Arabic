using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EasyRpgArabic.Setup.Core.Modules;

public sealed record EasyRpgArabicPlayerPayload(
	int SchemaVersion,
	string Version,
	string BuildId,
	string Architecture,
	string Sha256,
	long Size,
	string RequiredManiacsBuildId,
	string InstalledPath)
{
	public static EasyRpgArabicPlayerPayload LoadEmbedded(Assembly? assembly = null)
	{
		assembly ??= typeof(EasyRpgArabicPlayerPayload).Assembly;
		using var stream = assembly.GetManifestResourceStream("EasyRpgArabic.Setup.Core.Data.easyrpg-arabic.player.json")
			?? throw new FileNotFoundException("The EasyRPG-Arabic Player payload manifest is missing.");
		var payload = JsonSerializer.Deserialize<EasyRpgArabicPlayerPayload>(stream, new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true
		}) ?? throw new InvalidDataException("The EasyRPG-Arabic Player payload manifest is invalid.");
		payload.Validate();
		return payload;
	}

	public void Validate()
	{
		if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Version) || string.IsNullOrWhiteSpace(BuildId) ||
			!string.Equals(Architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
			string.IsNullOrWhiteSpace(Sha256) || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit) || Size <= 0 ||
			string.IsNullOrWhiteSpace(RequiredManiacsBuildId) || !string.Equals(InstalledPath, "Player.exe", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The EasyRPG-Arabic Player payload manifest contains invalid identity, architecture, or install-path data.");
		}

		ManiacsModuleSupport.ValidateRelativeInstallPath(InstalledPath);
	}
}

/// <summary>Installs the validated x64 Player template beside the unchanged Maniacs runtime.</summary>
public sealed class EasyRpgArabicSetupModule : ISetupModule
{
	private const string ManiacsRuntimePath = "RPG_RT.exe";
	private const string NewProjectDefaultsPath = "ArabicSupport/new-project-defaults.ini";
	private readonly Func<Stream> _openPlayerPayload;
	private readonly EasyRpgArabicPlayerPayload _payload;
	private readonly string _expectedManiacsRuntimeSha256;

	public SetupModuleDescriptor Descriptor { get; }

	public EasyRpgArabicSetupModule(
		Func<Stream> openPlayerPayload,
		EasyRpgArabicPlayerPayload payload,
		string expectedManiacsRuntimeSha256)
	{
		_openPlayerPayload = openPlayerPayload ?? throw new ArgumentNullException(nameof(openPlayerPayload));
		_payload = payload ?? throw new ArgumentNullException(nameof(payload));
		_payload.Validate();
		if (string.IsNullOrWhiteSpace(expectedManiacsRuntimeSha256) || expectedManiacsRuntimeSha256.Length != 64 || !expectedManiacsRuntimeSha256.All(Uri.IsHexDigit))
		{
			throw new ArgumentException("A SHA-256 fingerprint for the validated Maniacs runtime is required.", nameof(expectedManiacsRuntimeSha256));
		}
		_expectedManiacsRuntimeSha256 = expectedManiacsRuntimeSha256;
		Descriptor = new SetupModuleDescriptor(
			"easyrpg-arabic", "EasyRPG-Arabic", $"{payload.Version}+{payload.BuildId[..Math.Min(7, payload.BuildId.Length)]}",
			["maniacs-english"], ["Validated x64 EasyRPG-Arabic Player payload", $"Maniacs build {payload.RequiredManiacsBuildId}"],
			ModuleReadiness.Available, "Installs the validated x64 Player template while keeping Maniacs RPG_RT.exe unchanged.", payload.Architecture);
	}

	public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var root = Path.GetFullPath(context.StagingDirectory);
		var player = ManiacsModuleSupport.SafePath(root, _payload.InstalledPath);
		var maniacsRuntime = ManiacsModuleSupport.SafePath(root, ManiacsRuntimePath);
		var defaults = (context.ProjectDefaults ?? NewProjectDefaults.AllFalse).Normalize();
		var defaultsBytes = defaults.ToIniBytes();
		var defaultsHash = Convert.ToHexString(SHA256.HashData(defaultsBytes));
		var defaultsPath = ManiacsModuleSupport.SafePath(root, NewProjectDefaultsPath);
		if (!File.Exists(maniacsRuntime)) { throw new FileNotFoundException("The validated Maniacs root runtime is missing from staging.", maniacsRuntime); }
		if (File.Exists(player) || Directory.Exists(player)) { throw new IOException("Refusing to overwrite an existing project template Player.exe."); }
		if (File.Exists(defaultsPath) || Directory.Exists(defaultsPath)) { throw new IOException("Refusing to overwrite an existing new-project defaults file."); }
		if (!ManiacsModuleSupport.HashMatches(maniacsRuntime, _expectedManiacsRuntimeSha256))
		{
			throw new InvalidDataException("The staged RPG_RT.exe does not match the supported Maniacs build required by this Player payload.");
		}
		if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(maniacsRuntime), "x64", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The staged Maniacs RPG_RT.exe is not x64.");
		}

		Directory.CreateDirectory(Path.GetDirectoryName(defaultsPath)!);
		var token = Guid.NewGuid().ToString("N");
		var temporaryPlayer = ManiacsModuleSupport.SafePath(root, ".easyrpg-player-" + token + ".tmp");
		var temporaryDefaults = ManiacsModuleSupport.SafePath(root, ".easyrpg-defaults-" + token + ".tmp");
		var playerInstalled = false;
		var defaultsInstalled = false;
		try
		{
			await using (var output = new FileStream(temporaryDefaults, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
			{
				await output.WriteAsync(defaultsBytes, cancellationToken).ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			await using (var input = _openPlayerPayload())
			{
				await using var output = new FileStream(temporaryPlayer, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
				await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			if (new FileInfo(temporaryPlayer).Length != _payload.Size || !ManiacsModuleSupport.HashMatches(temporaryPlayer, _payload.Sha256))
			{
				throw new InvalidDataException("The embedded EasyRPG-Arabic Player payload failed its size or SHA-256 check.");
			}
			if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(temporaryPlayer), _payload.Architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The embedded EasyRPG-Arabic Player architecture does not match its manifest.");
			}

			// Both outputs are installed into the transaction's stage and remain rollbackable until commit.
			File.Move(temporaryDefaults, defaultsPath);
			defaultsInstalled = true;
			File.Move(temporaryPlayer, player);
			playerInstalled = true;
			return new ModuleApplyResult(
				[
					new ModuleOwnedFile(_payload.InstalledPath, ManiacsModuleSupport.HashFile(player)),
					new ModuleOwnedFile(NewProjectDefaultsPath, ManiacsModuleSupport.HashFile(defaultsPath))
				],
				$"{_payload.Version}+{_payload.BuildId}");
		}
		catch
		{
			if (File.Exists(temporaryPlayer)) { try { File.Delete(temporaryPlayer); } catch { } }
			if (File.Exists(temporaryDefaults)) { try { File.Delete(temporaryDefaults); } catch { } }
			if (playerInstalled && File.Exists(player) && ManiacsModuleSupport.HashMatches(player, _payload.Sha256))
			{
				try { File.Delete(player); } catch { }
			}
			if (defaultsInstalled && File.Exists(defaultsPath) && ManiacsModuleSupport.HashMatches(defaultsPath, defaultsHash))
			{
				try { File.Delete(defaultsPath); } catch { }
			}
			throw;
		}
	}

	public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var player = ManiacsModuleSupport.SafePath(context.StagingDirectory, _payload.InstalledPath);
			var maniacsRuntime = ManiacsModuleSupport.SafePath(context.StagingDirectory, ManiacsRuntimePath);
			var defaults = (context.ProjectDefaults ?? NewProjectDefaults.AllFalse).Normalize();
			var defaultsBytes = defaults.ToIniBytes();
			var defaultsHash = Convert.ToHexString(SHA256.HashData(defaultsBytes));
			var defaultsPath = ManiacsModuleSupport.SafePath(context.StagingDirectory, NewProjectDefaultsPath);
			if (!File.Exists(player) || new FileInfo(player).Length != _payload.Size || !ManiacsModuleSupport.HashMatches(player, _payload.Sha256))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Managed root Player.exe does not match the EasyRPG-Arabic payload manifest."));
			}
			if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(player), _payload.Architecture, StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Managed root Player.exe has the wrong architecture."));
			}
			if (!File.Exists(maniacsRuntime) || !ManiacsModuleSupport.HashMatches(maniacsRuntime, _expectedManiacsRuntimeSha256) ||
				!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(maniacsRuntime), "x64", StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("RPG_RT.exe does not preserve the validated Maniacs runtime."));
			}
			if (!File.Exists(defaultsPath) || !HashEqual(ManiacsModuleSupport.HashFile(defaultsPath), defaultsHash))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("The staged new-project movement defaults do not match the selected Setup values."));
			}
			if (result.OwnedFiles.Count != 2 ||
				!result.OwnedFiles.Any(file => PathsEqual(file.RelativePath, _payload.InstalledPath) && HashEqual(file.Sha256, _payload.Sha256)) ||
				!result.OwnedFiles.Any(file => PathsEqual(file.RelativePath, NewProjectDefaultsPath) && HashEqual(file.Sha256, defaultsHash)))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Setup state ownership does not identify the expected Player.exe and new-project defaults file."));
			}
			return Task.FromResult(ModuleVerificationResult.Success("Player.exe and transactional new-project movement defaults are verified; Maniacs RPG_RT.exe remains unchanged."));
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return Task.FromResult(ModuleVerificationResult.Failure(exception.Message));
		}
	}

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		EnsureExpectedOwnership(result.OwnedFiles);
		RemoveOwnedFiles(context.StagingDirectory, result.OwnedFiles, throwIfModified: true);
		return Task.CompletedTask;
	}

	public Task<ModuleUninstallResult> UninstallAsync(
		SetupModuleContext context,
		IReadOnlyList<ModuleOwnedFile> ownedFiles,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try { EnsureExpectedOwnership(ownedFiles); }
		catch (InvalidDataException) { return Task.FromResult(new ModuleUninstallResult([], ownedFiles.Select(file => file.RelativePath).ToArray())); }

		var removed = new List<string>();
		var preserved = new List<string>();
		foreach (var owned in ownedFiles)
		{
			var path = ManiacsModuleSupport.SafePath(context.StagingDirectory, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!ManiacsModuleSupport.HashMatches(path, owned.Sha256))
			{
				preserved.Add(owned.RelativePath);
				continue;
			}
			File.Delete(path);
			removed.Add(owned.RelativePath);
		}
		return Task.FromResult(new ModuleUninstallResult(removed, preserved));
	}

	private void EnsureExpectedOwnership(IReadOnlyList<ModuleOwnedFile> ownedFiles)
	{
		if (ownedFiles.Count != 2 ||
			!ownedFiles.Any(file => PathsEqual(file.RelativePath, _payload.InstalledPath) && HashEqual(file.Sha256, _payload.Sha256)) ||
			!ownedFiles.Any(file => PathsEqual(file.RelativePath, NewProjectDefaultsPath) && IsSha256(file.Sha256)))
		{
			throw new InvalidDataException("Module ownership does not identify the expected Player.exe and new-project defaults file.");
		}
	}

	private static void RemoveOwnedFiles(string root, IReadOnlyList<ModuleOwnedFile> ownedFiles, bool throwIfModified)
	{
		foreach (var owned in ownedFiles)
		{
			var path = ManiacsModuleSupport.SafePath(root, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!ManiacsModuleSupport.HashMatches(path, owned.Sha256))
			{
				if (throwIfModified) { throw new IOException($"Refusing to remove a modified EasyRPG-Arabic owned file during rollback: {owned.RelativePath}."); }
				continue;
			}
			File.Delete(path);
		}
	}

	private static bool PathsEqual(string left, string right) =>
		string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

	private static bool HashEqual(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

	private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
}
