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
		if (!File.Exists(maniacsRuntime)) { throw new FileNotFoundException("The validated Maniacs root runtime is missing from staging.", maniacsRuntime); }
		if (File.Exists(player) || Directory.Exists(player)) { throw new IOException("Refusing to overwrite an existing project template Player.exe."); }
		if (!ManiacsModuleSupport.HashMatches(maniacsRuntime, _expectedManiacsRuntimeSha256))
		{
			throw new InvalidDataException("The staged RPG_RT.exe does not match the supported Maniacs build required by this Player payload.");
		}
		if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(maniacsRuntime), "x64", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The staged Maniacs RPG_RT.exe is not x64.");
		}

		var temporary = ManiacsModuleSupport.SafePath(root, ".easyrpg-player-" + Guid.NewGuid().ToString("N") + ".tmp");
		var installed = false;
		try
		{
			await using (var input = _openPlayerPayload())
			{
				await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true);
				await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			if (new FileInfo(temporary).Length != _payload.Size || !ManiacsModuleSupport.HashMatches(temporary, _payload.Sha256))
			{
				throw new InvalidDataException("The embedded EasyRPG-Arabic Player payload failed its size or SHA-256 check.");
			}
			if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(temporary), _payload.Architecture, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The embedded EasyRPG-Arabic Player architecture does not match its manifest.");
			}

			// Same-volume move is atomic and refuses to replace a file created after the first check.
			File.Move(temporary, player);
			installed = true;
			return new ModuleApplyResult([new ModuleOwnedFile(_payload.InstalledPath, ManiacsModuleSupport.HashFile(player))], $"{_payload.Version}+{_payload.BuildId}");
		}
		catch
		{
			if (File.Exists(temporary)) { try { File.Delete(temporary); } catch { } }
			if (installed && File.Exists(player) && ManiacsModuleSupport.HashMatches(player, _payload.Sha256))
			{
				try { File.Delete(player); } catch { }
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
			if (result.OwnedFiles.Count != 1 || !result.OwnedFiles.Any(file => PathsEqual(file.RelativePath, _payload.InstalledPath) && HashEqual(file.Sha256, _payload.Sha256)))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Setup state ownership does not identify only the expected Player.exe."));
			}
			return Task.FromResult(ModuleVerificationResult.Success("Player.exe matches the supported build and architecture; Maniacs RPG_RT.exe remains unchanged."));
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
		RemoveOwnedPlayer(context.StagingDirectory, throwIfModified: true);
		return Task.CompletedTask;
	}

	public Task<ModuleUninstallResult> UninstallAsync(
		SetupModuleContext context,
		IReadOnlyList<ModuleOwnedFile> ownedFiles,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try { EnsureExpectedOwnership(ownedFiles); }
		catch (InvalidDataException) { return Task.FromResult(new ModuleUninstallResult([], [_payload.InstalledPath])); }

		var player = ManiacsModuleSupport.SafePath(context.StagingDirectory, _payload.InstalledPath);
		if (!File.Exists(player)) { return Task.FromResult(new ModuleUninstallResult([], [])); }
		if (!ManiacsModuleSupport.HashMatches(player, _payload.Sha256))
		{
			return Task.FromResult(new ModuleUninstallResult([], [_payload.InstalledPath]));
		}
		File.Delete(player);
		return Task.FromResult(new ModuleUninstallResult([_payload.InstalledPath], []));
	}

	private void EnsureExpectedOwnership(IReadOnlyList<ModuleOwnedFile> ownedFiles)
	{
		if (ownedFiles.Count != 1 || !ownedFiles.Any(file => PathsEqual(file.RelativePath, _payload.InstalledPath) && HashEqual(file.Sha256, _payload.Sha256)))
		{
			throw new InvalidDataException("Module ownership does not identify only the expected Player.exe.");
		}
	}

	private void RemoveOwnedPlayer(string root, bool throwIfModified)
	{
		var player = ManiacsModuleSupport.SafePath(root, _payload.InstalledPath);
		if (!File.Exists(player)) { return; }
		if (!ManiacsModuleSupport.HashMatches(player, _payload.Sha256))
		{
			if (throwIfModified) { throw new IOException("Refusing to remove a modified EasyRPG-Arabic Player.exe during rollback."); }
			return;
		}
		File.Delete(player);
	}

	private static bool PathsEqual(string left, string right) =>
		string.Equals(left.Replace('\\', '/'), right.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);

	private static bool HashEqual(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
