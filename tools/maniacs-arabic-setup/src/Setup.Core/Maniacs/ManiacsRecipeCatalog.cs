using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyRpgArabic.Setup.Core.Maniacs;

public sealed record ManiacsPackageLayout(
	int SchemaVersion,
	string Id,
	string DisplayName,
	IReadOnlyList<string> RequiredDirectories,
	IReadOnlyList<ManiacsRequiredFile> RequiredFiles,
	IReadOnlyDictionary<string, ManiacsArchitectureRole> ArchitectureRoles,
	IReadOnlyList<ManiacsBuildDetector>? BuildDetectors = null);

public sealed record ManiacsRequiredFile(string Path, string? Architecture = null);

public sealed record ManiacsArchitectureRole(string Path, string Expected);

public sealed record ManiacsBuildDetector(string Path, string Pattern, string Group = "build");

public sealed record ManiacsVersionRecipe(
	int SchemaVersion,
	string Id,
	string DisplayVersion,
	string BuildId,
	string LayoutId,
	IReadOnlyList<ManiacsBuildMarker> BuildMarkers,
	IReadOnlyDictionary<string, string> IdentityFileSha256,
	ManiacsOfficialInstallProcess OfficialInstallProcess,
	IReadOnlyDictionary<string, string>? VanillaSourceSha256 = null,
	IReadOnlyDictionary<string, string>? VanillaSourceArchitectures = null,
	IReadOnlyList<ManiacsPackageFile>? BasePayloadFiles = null,
	ManiacsEnglishPatchRecipe? EnglishPatch = null);

public sealed record ManiacsPackageFile(string PackagePath, string InstallPath);

public sealed record ManiacsEnglishPatchRecipe(
	string TargetFileName,
	string BackupFileName,
	int WDiffVersion,
	long InputSize,
	string InputSha256,
	long OutputSize,
	string OutputSha256,
	string InputCrc32,
	string OutputCrc32,
	DateTime? OutputLastWriteTimeUtc,
	IReadOnlyDictionary<string, string> PayloadFiles,
	IReadOnlyList<string> ExpectedPluginFiles);

public sealed record ManiacsBuildMarker(string Path, string ContainsAscii);

public sealed record ManiacsOfficialInstallProcess(
	string UtilityPath,
	string UtilityAutomation,
	string EnglishPatchPath,
	string EnglishPatchAutomation);

public sealed record ArabicEditorCompatibility(
	int SchemaVersion,
	string Id,
	string PatchVersion,
	IReadOnlyList<string> ValidatedRecipeIds,
	IReadOnlyDictionary<string, string> ValidationEvidence);

public sealed class ManiacsRecipeCatalog
{
	private const string EmbeddedResourcePrefix = "EasyRpgArabic.Setup.Core.Maniacs.Data.";
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip
	};

	public IReadOnlyList<ManiacsPackageLayout> Layouts { get; }
	public IReadOnlyList<ManiacsVersionRecipe> Recipes { get; }
	public ArabicEditorCompatibility ArabicEditorCompatibility { get; }

	public ManiacsRecipeCatalog(
		IReadOnlyList<ManiacsPackageLayout> layouts,
		IReadOnlyList<ManiacsVersionRecipe> recipes,
		ArabicEditorCompatibility arabicEditorCompatibility)
	{
		Layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
		Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
		ArabicEditorCompatibility = arabicEditorCompatibility ?? throw new ArgumentNullException(nameof(arabicEditorCompatibility));
		Validate();
	}

	public static ManiacsRecipeCatalog LoadEmbedded(Assembly? assembly = null)
	{
		assembly ??= typeof(ManiacsRecipeCatalog).Assembly;
		var resources = assembly.GetManifestResourceNames()
			.Where(name => name.StartsWith(EmbeddedResourcePrefix, StringComparison.Ordinal))
			.ToArray();
		var layouts = resources.Where(name => name.Contains(".layouts.", StringComparison.Ordinal))
			.Select(name => ReadResource<ManiacsPackageLayout>(assembly, name)).ToArray();
		var recipes = resources.Where(name => name.Contains(".recipes.", StringComparison.Ordinal))
			.Select(name => ReadResource<ManiacsVersionRecipe>(assembly, name)).ToArray();
		var compatibility = resources.Where(name => name.Contains(".compatibility.", StringComparison.Ordinal))
			.Select(name => ReadResource<ArabicEditorCompatibility>(assembly, name)).SingleOrDefault()
			?? throw new InvalidDataException("The embedded Arabic Editor compatibility manifest is missing or ambiguous.");
		return new ManiacsRecipeCatalog(layouts, recipes, compatibility);
	}

	public static ManiacsRecipeCatalog LoadFromDirectory(string dataDirectory)
	{
		var root = Path.GetFullPath(dataDirectory);
		var layoutDirectory = Path.Combine(root, "layouts");
		var recipeDirectory = Path.Combine(root, "recipes");
		var compatibilityDirectory = Path.Combine(root, "compatibility");
		var layouts = ReadDirectory<ManiacsPackageLayout>(layoutDirectory);
		var recipes = ReadDirectory<ManiacsVersionRecipe>(recipeDirectory);
		var compatibility = ReadDirectory<ArabicEditorCompatibility>(compatibilityDirectory).SingleOrDefault()
			?? throw new InvalidDataException("Exactly one Arabic Editor compatibility manifest is required.");
		return new ManiacsRecipeCatalog(layouts, recipes, compatibility);
	}

	private void Validate()
	{
		if (Layouts.Count == 0 || Recipes.Count == 0)
		{
			throw new InvalidDataException("At least one Maniacs layout and version recipe are required.");
		}
		EnsureUnique(Layouts.Select(layout => layout.Id), "layout");
		EnsureUnique(Recipes.Select(recipe => recipe.Id), "recipe");
		foreach (var layout in Layouts)
		{
			if (layout.SchemaVersion != 1) { throw new InvalidDataException($"Unsupported layout schema version in '{layout.Id}'."); }
			foreach (var path in layout.RequiredDirectories.Concat(layout.RequiredFiles.Select(file => file.Path))) { ValidateRelativePath(path); }
			var requiredPaths = layout.RequiredFiles.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
			if (requiredPaths.Count != layout.RequiredFiles.Count) { throw new InvalidDataException($"Layout '{layout.Id}' contains duplicate required file paths."); }
			foreach (var requiredFile in layout.RequiredFiles)
			{
				if (requiredFile.Architecture is not null && !IsKnownArchitecture(requiredFile.Architecture))
				{
					throw new InvalidDataException($"Unknown architecture '{requiredFile.Architecture}' in layout '{layout.Id}'.");
				}
			}
			foreach (var role in layout.ArchitectureRoles.Values)
			{
				ValidateRelativePath(role.Path);
				if (!IsKnownArchitecture(role.Expected)) { throw new InvalidDataException($"Unknown architecture role '{role.Expected}' in layout '{layout.Id}'."); }
				var file = layout.RequiredFiles.SingleOrDefault(candidate => string.Equals(candidate.Path, role.Path, StringComparison.OrdinalIgnoreCase));
				if (file is null || !string.Equals(file.Architecture, role.Expected, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Architecture role in layout '{layout.Id}' does not match a required file definition: {role.Path}");
				}
			}
			foreach (var detector in layout.BuildDetectors ?? [])
			{
				ValidateRelativePath(detector.Path);
				if (!requiredPaths.Contains(detector.Path)) { throw new InvalidDataException($"Build detector in layout '{layout.Id}' references a non-required file: {detector.Path}"); }
				try
				{
					var regex = new Regex(detector.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
					if (!regex.GetGroupNames().Contains(detector.Group, StringComparer.Ordinal))
					{
						throw new InvalidDataException($"Build detector in layout '{layout.Id}' references missing group '{detector.Group}'.");
					}
				}
				catch (ArgumentException exception) { throw new InvalidDataException($"Invalid build detector in layout '{layout.Id}'.", exception); }
			}
		}
		foreach (var recipe in Recipes)
		{
			if (recipe.SchemaVersion != 1) { throw new InvalidDataException($"Unsupported recipe schema version in '{recipe.Id}'."); }
			if (recipe.BuildMarkers.Count == 0 || recipe.IdentityFileSha256.Count == 0)
			{
				throw new InvalidDataException($"Recipe '{recipe.Id}' must contain build markers and at least one identity fingerprint.");
			}
			var layout = Layouts.SingleOrDefault(candidate => string.Equals(candidate.Id, recipe.LayoutId, StringComparison.OrdinalIgnoreCase));
			if (layout is null)
			{
				throw new InvalidDataException($"Recipe '{recipe.Id}' references unknown layout '{recipe.LayoutId}'.");
			}
			var requiredPaths = layout.RequiredFiles.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var marker in recipe.BuildMarkers)
			{
				ValidateRelativePath(marker.Path);
				if (!requiredPaths.Contains(marker.Path)) { throw new InvalidDataException($"Recipe '{recipe.Id}' build marker is not required by layout '{layout.Id}': {marker.Path}"); }
			}
			foreach (var (path, hash) in recipe.IdentityFileSha256)
			{
				ValidateRelativePath(path);
				if (!requiredPaths.Contains(path)) { throw new InvalidDataException($"Recipe '{recipe.Id}' fingerprints a file that is not required by layout '{layout.Id}': {path}"); }
				if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) { throw new InvalidDataException($"Recipe '{recipe.Id}' has an invalid SHA-256 for '{path}'."); }
			}
			foreach (var (path, hash) in recipe.VanillaSourceSha256 ?? new Dictionary<string, string>())
			{
				ValidateRelativePath(path);
				if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) { throw new InvalidDataException($"Recipe '{recipe.Id}' has an invalid vanilla source SHA-256 for '{path}'."); }
			}
			foreach (var (path, architecture) in recipe.VanillaSourceArchitectures ?? new Dictionary<string, string>())
			{
				ValidateRelativePath(path);
				if (!IsKnownArchitecture(architecture)) { throw new InvalidDataException($"Recipe '{recipe.Id}' has an invalid source architecture for '{path}'."); }
				if (!(recipe.VanillaSourceSha256?.ContainsKey(path) ?? false)) { throw new InvalidDataException($"Recipe '{recipe.Id}' architecture has no matching source fingerprint: {path}"); }
			}
			foreach (var payloadFile in recipe.BasePayloadFiles ?? [])
			{
				ValidateRelativePath(payloadFile.PackagePath);
				ValidateRelativePath(payloadFile.InstallPath);
				if (!recipe.IdentityFileSha256.ContainsKey(payloadFile.PackagePath))
				{
					throw new InvalidDataException($"Recipe '{recipe.Id}' base payload is not fingerprinted: {payloadFile.PackagePath}");
				}
			}
			EnsureUnique((recipe.BasePayloadFiles ?? []).Select(file => file.InstallPath), $"base payload path in recipe '{recipe.Id}'");
			if (recipe.EnglishPatch is { } patch)
			{
				ValidateRelativePath(patch.TargetFileName);
				ValidateRelativePath(patch.BackupFileName);
				if (patch.TargetFileName.Contains('/') || patch.TargetFileName.Contains('\\') || patch.BackupFileName.Contains('/') || patch.BackupFileName.Contains('\\'))
				{
					throw new InvalidDataException($"Recipe '{recipe.Id}' WDiff target and backup must be filenames in the install root.");
				}
				if (patch.WDiffVersion is < 100 or > 104 || patch.InputSize <= 0 || patch.OutputSize <= 0 || patch.OutputSize > 512L * 1024 * 1024 ||
					patch.InputSha256.Length != 64 || !patch.InputSha256.All(Uri.IsHexDigit) ||
					patch.OutputSha256.Length != 64 || !patch.OutputSha256.All(Uri.IsHexDigit) ||
					!Regex.IsMatch(patch.InputCrc32, "^[0-9A-Fa-f]{8}$", RegexOptions.CultureInvariant) ||
					!Regex.IsMatch(patch.OutputCrc32, "^[0-9A-Fa-f]{8}$", RegexOptions.CultureInvariant))
				{
					throw new InvalidDataException($"Recipe '{recipe.Id}' has invalid English patch metadata.");
				}
				if (!string.Equals(patch.TargetFileName, Path.GetFileName(patch.TargetFileName), StringComparison.Ordinal) ||
					!string.Equals(patch.BackupFileName, Path.GetFileName(patch.BackupFileName), StringComparison.Ordinal) ||
					string.Equals(patch.TargetFileName, patch.BackupFileName, StringComparison.OrdinalIgnoreCase) ||
					!(recipe.VanillaSourceSha256?.TryGetValue(patch.TargetFileName, out var sourceHash) ?? false) ||
					!string.Equals(sourceHash, patch.InputSha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Recipe '{recipe.Id}' WDiff input does not match its vanilla source fingerprint.");
				}
				foreach (var (packagePath, installPath) in patch.PayloadFiles)
				{
					ValidateRelativePath(packagePath);
					ValidateRelativePath(installPath);
					if (!recipe.IdentityFileSha256.ContainsKey(packagePath))
					{
						throw new InvalidDataException($"Recipe '{recipe.Id}' English payload is not fingerprinted: {packagePath}");
					}
				}
				EnsureUnique(patch.PayloadFiles.Values, $"English payload install path in recipe '{recipe.Id}'");
				var pluginNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var path in patch.ExpectedPluginFiles)
				{
					ValidateRelativePath(path);
					if (!pluginNames.Add(path) || !recipe.IdentityFileSha256.ContainsKey("English/Plugin/" + path))
					{
						throw new InvalidDataException($"Recipe '{recipe.Id}' has an un-fingerprinted or duplicate English plugin file: {path}");
					}
				}
			}
			ValidateRelativePath(recipe.OfficialInstallProcess.UtilityPath);
			ValidateRelativePath(recipe.OfficialInstallProcess.EnglishPatchPath);
			if (!requiredPaths.Contains(recipe.OfficialInstallProcess.UtilityPath) || !requiredPaths.Contains(recipe.OfficialInstallProcess.EnglishPatchPath))
			{
				throw new InvalidDataException($"Recipe '{recipe.Id}' refers to utility or English patch files outside its package layout.");
			}
		}
		if (ArabicEditorCompatibility.SchemaVersion != 1) { throw new InvalidDataException("Unsupported Arabic Editor compatibility schema version."); }
		foreach (var recipeId in ArabicEditorCompatibility.ValidatedRecipeIds)
		{
			if (!Recipes.Any(recipe => string.Equals(recipe.Id, recipeId, StringComparison.OrdinalIgnoreCase)))
			{
				throw new InvalidDataException($"Arabic Editor compatibility references unknown recipe '{recipeId}'.");
			}
			if (!ArabicEditorCompatibility.ValidationEvidence.TryGetValue(recipeId, out var evidence) || string.IsNullOrWhiteSpace(evidence))
			{
				throw new InvalidDataException($"Arabic Editor compatibility for '{recipeId}' requires validation evidence.");
			}
		}
	}

	private static T ReadResource<T>(Assembly assembly, string name)
	{
		using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException($"Embedded manifest is missing: {name}");
		return JsonSerializer.Deserialize<T>(stream, JsonOptions) ?? throw new InvalidDataException($"Embedded manifest is empty: {name}");
	}

	private static IReadOnlyList<T> ReadDirectory<T>(string directory)
	{
		if (!Directory.Exists(directory)) { throw new DirectoryNotFoundException($"Maniacs manifest directory not found: {directory}"); }
		return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.Select(path =>
			{
				using var stream = File.OpenRead(path);
				return JsonSerializer.Deserialize<T>(stream, JsonOptions)
					?? throw new InvalidDataException($"Manifest is empty: {path}");
			})
			.ToArray();
	}

	private static void EnsureUnique(IEnumerable<string> values, string kind)
	{
		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var id in values)
		{
			if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) { throw new InvalidDataException($"Maniacs {kind} IDs must be nonempty and unique."); }
		}
	}

	internal static void ValidateRelativePath(string path)
	{
		var normalized = path.Replace('\\', '/');
		if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(path) ||
			normalized.Split('/').Any(part => part is "" or "." or ".."))
		{
			throw new InvalidDataException($"Manifest path must be relative and cannot traverse directories: '{path}'.");
		}
	}

	private static bool IsKnownArchitecture(string architecture) => architecture is "x86" or "x64" or "arm" or "arm64";
}
