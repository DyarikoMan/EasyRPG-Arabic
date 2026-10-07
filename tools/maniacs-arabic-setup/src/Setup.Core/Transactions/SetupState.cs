using System.Text.Json;
using System.Text.Json.Serialization;
using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Modules;

namespace EasyRpgArabic.Setup.Core.Transactions;

public sealed record InstalledModuleState(
	string Id,
	string Version,
	string ManifestHash,
	IReadOnlyList<ModuleOwnedFile> Files);

public sealed record SetupState
{
	public int SchemaVersion { get; init; } = SetupStateStore.CurrentSchemaVersion;
	public string InstallId { get; init; } = Guid.NewGuid().ToString("D");
	public int Generation { get; init; } = 1;
	public string SourceKind { get; init; } = "steam-rpg-maker-2003";
	public string SourceAppId { get; init; } = RpgMaker2003InstallationValidator.SteamAppId;
	public string SourcePath { get; init; } = string.Empty;
	public string SourceExecutableSha256 { get; init; } = string.Empty;
	public string InstallPath { get; init; } = string.Empty;
	public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
	public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;
	public IReadOnlyList<InstalledModuleState> Modules { get; init; } = Array.Empty<InstalledModuleState>();
}

public static class SetupStateStore
{
	public const int CurrentSchemaVersion = 1;
	public const string StateRelativePath = ".EasyRPG-Arabic/setup-state.json";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
	};

	public static async Task WriteAsync(string installationRoot, SetupState state, CancellationToken cancellationToken = default)
	{
		Validate(state, message => new InvalidOperationException(message));

		var statePath = Path.Combine(installationRoot, StateRelativePath);
		var stateDirectory = Path.GetDirectoryName(statePath)!;
		Directory.CreateDirectory(stateDirectory);
		var temporaryPath = statePath + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, useAsync: true))
			{
				await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
				await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			File.Move(temporaryPath, statePath, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporaryPath)) { File.Delete(temporaryPath); }
		}
	}

	public static async Task<SetupState> ReadAsync(string installationRoot, CancellationToken cancellationToken = default)
	{
		var statePath = Path.Combine(installationRoot, StateRelativePath);
		await using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, useAsync: true);
		var state = await JsonSerializer.DeserializeAsync<SetupState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidDataException("The setup state file is empty or invalid.");
		Validate(state, message => new InvalidDataException(message));
		return state;
	}

	private static void Validate(SetupState state, Func<string, Exception> exceptionFactory)
	{
		if (state.SchemaVersion != CurrentSchemaVersion)
		{
			throw exceptionFactory($"Unsupported setup-state schema version: {state.SchemaVersion}.");
		}
		if (!Guid.TryParse(state.InstallId, out _))
		{
			throw exceptionFactory("The setup state contains an invalid install ID.");
		}
		if (state.Modules is null)
		{
			throw exceptionFactory("The setup state has no module list.");
		}
		foreach (var module in state.Modules)
		{
			if (string.IsNullOrWhiteSpace(module.Id) || module.Files is null)
			{
				throw exceptionFactory("The setup state contains an invalid module record.");
			}
			foreach (var file in module.Files)
			{
				var pathParts = file.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
				if (string.IsNullOrWhiteSpace(file.RelativePath) || Path.IsPathRooted(file.RelativePath) || pathParts.Any(part => part == ".."))
				{
					throw exceptionFactory($"The setup state contains an unsafe owned-file path: {file.RelativePath}");
				}
			}
		}
	}
}
