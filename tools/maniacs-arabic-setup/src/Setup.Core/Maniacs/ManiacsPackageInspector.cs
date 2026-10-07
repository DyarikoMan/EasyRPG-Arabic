using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EasyRpgArabic.Setup.Core.Maniacs;

public enum ManiacsPackageStatus
{
	InvalidPackage,
	UnsupportedLayout,
	KnownBuildModified,
	UnknownBuildSupportedLayout,
	KnownSupportedBuild
}

public sealed record ManiacsPackageInspection(
	ManiacsPackageStatus Status,
	string? PackageRoot,
	string? LayoutId,
	string? CandidateRecipeId,
	string? DetectedVersion,
	string? DetectedBuild,
	string? EditorArchitecture,
	string? RuntimeArchitecture,
	bool ArabicEditorValidated,
	string? ArabicEditorEvidence,
	IReadOnlyList<string> Issues)
{
	public bool IsKnownSupportedBuild => Status == ManiacsPackageStatus.KnownSupportedBuild;
	public string TechnicalSummary => LayoutId is null
		? string.Empty
		: $"Layout {LayoutId} · editor/plugins {EditorArchitecture ?? "unknown"} · runtime {RuntimeArchitecture ?? "unknown"} · Arabic Editor {(ArabicEditorValidated ? "validated" : "not validated for this build")}";

	public string Summary => Status switch
	{
		ManiacsPackageStatus.InvalidPackage => Issues.FirstOrDefault() ?? "Select the extracted Maniacs package directory.",
		ManiacsPackageStatus.UnsupportedLayout => "The Maniacs package layout does not match a supported recipe. This release needs validation.",
		ManiacsPackageStatus.KnownBuildModified => $"Maniacs {DetectedVersion}, detected build {DetectedBuild}, was identified, but package files differ from its validated recipe. Stop for validation.",
		ManiacsPackageStatus.UnknownBuildSupportedLayout => $"The package matches a supported layout but has unrecognized build {DetectedBuild ?? "(not detected)"}. This release needs validation.",
		ManiacsPackageStatus.KnownSupportedBuild => $"Maniacs {DetectedVersion} (build {DetectedBuild}) is recognized.",
		_ => "Maniacs package could not be inspected."
	};
}

public sealed class ManiacsPackageInspector
{
	private readonly ManiacsRecipeCatalog _catalog;

	public ManiacsPackageInspector(ManiacsRecipeCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

	public ManiacsPackageInspection Inspect(string selectedDirectory)
	{
		if (string.IsNullOrWhiteSpace(selectedDirectory) || !Directory.Exists(selectedDirectory))
		{
			return Invalid("The selected Maniacs package directory does not exist.");
		}

		string packageRoot;
		try { packageRoot = ResolvePackageRoot(Path.GetFullPath(selectedDirectory)); }
		catch (InvalidDataException exception) { return Invalid(exception.Message); }

		var layoutEvaluations = _catalog.Layouts
			.Select(layout => EvaluateLayout(packageRoot, layout))
			.OrderBy(evaluation => evaluation.Issues.Count)
			.ToArray();
		var matchedLayouts = layoutEvaluations.Where(evaluation => evaluation.Issues.Count == 0).ToArray();
		if (matchedLayouts.Length != 1)
		{
			var closest = layoutEvaluations.FirstOrDefault();
			var issues = closest?.Issues ?? ["No supported Maniacs package layout is registered."];
			return new ManiacsPackageInspection(ManiacsPackageStatus.UnsupportedLayout, packageRoot, closest?.Layout.Id, null, null, null,
				closest?.EditorArchitecture, closest?.RuntimeArchitecture, false, null, issues);
		}

		var layout = matchedLayouts[0];
		var recipes = _catalog.Recipes.Where(recipe => string.Equals(recipe.LayoutId, layout.Layout.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
		var markerMatches = recipes.Where(recipe => MarkersMatch(packageRoot, recipe)).ToArray();
		if (markerMatches.Length == 0)
		{
			return new ManiacsPackageInspection(ManiacsPackageStatus.UnknownBuildSupportedLayout, packageRoot, layout.Layout.Id, null, null,
				DetectBuild(packageRoot, layout.Layout), layout.EditorArchitecture, layout.RuntimeArchitecture, false, null,
				["The package matches a supported layout, but no version recipe recognizes this build."]);
		}

		var exactRecipes = markerMatches.Where(recipe => IdentityFilesMatch(packageRoot, recipe)).ToArray();
		if (exactRecipes.Length != 1)
		{
			var candidate = markerMatches[0];
			var changedFiles = GetChangedIdentityFiles(packageRoot, candidate);
			return new ManiacsPackageInspection(ManiacsPackageStatus.KnownBuildModified, packageRoot, layout.Layout.Id, candidate.Id,
				candidate.DisplayVersion, DetectBuild(packageRoot, layout.Layout) ?? candidate.BuildId, layout.EditorArchitecture, layout.RuntimeArchitecture, false, null,
				changedFiles.Count == 0 ? ["Build identity is ambiguous across registered recipes."] : changedFiles);
		}

		var recipe = exactRecipes[0];
		var validated = _catalog.ArabicEditorCompatibility.ValidatedRecipeIds.Contains(recipe.Id, StringComparer.OrdinalIgnoreCase);
		_catalog.ArabicEditorCompatibility.ValidationEvidence.TryGetValue(recipe.Id, out var evidence);
		return new ManiacsPackageInspection(ManiacsPackageStatus.KnownSupportedBuild, packageRoot, layout.Layout.Id, recipe.Id,
			recipe.DisplayVersion, recipe.BuildId, layout.EditorArchitecture, layout.RuntimeArchitecture, validated, evidence, []);
	}

	private static ManiacsPackageInspection Invalid(string message) => new(
		ManiacsPackageStatus.InvalidPackage, null, null, null, null, null, null, null, false, null, [message]);

	private static string ResolvePackageRoot(string selectedDirectory)
	{
		if (LooksLikePackageRoot(selectedDirectory)) { return selectedDirectory; }
		var childRoots = Directory.EnumerateDirectories(selectedDirectory, "*", SearchOption.TopDirectoryOnly)
			.Where(LooksLikePackageRoot)
			.ToArray();
		return childRoots.Length switch
		{
			1 => childRoots[0],
			0 => throw new InvalidDataException("The selected folder does not contain a Maniacs package root (utility.exe, Common, and English)."),
			_ => throw new InvalidDataException("The selected folder contains more than one possible Maniacs package root; select the package folder directly.")
		};
	}

	private static bool LooksLikePackageRoot(string path) =>
		File.Exists(Path.Combine(path, "utility.exe")) ||
		Directory.Exists(Path.Combine(path, "Common")) ||
		Directory.Exists(Path.Combine(path, "English"));

	private static LayoutEvaluation EvaluateLayout(string root, ManiacsPackageLayout layout)
	{
		var issues = new List<string>();
		foreach (var relative in layout.RequiredDirectories)
		{
			if (!Directory.Exists(ResolvePath(root, relative))) { issues.Add($"Missing directory: {relative}"); }
		}
		var actualArchitectures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var required in layout.RequiredFiles)
		{
			var path = ResolvePath(root, required.Path);
			if (!File.Exists(path))
			{
				issues.Add($"Missing file: {required.Path}");
				continue;
			}
			if (required.Architecture is null) { continue; }
			var architecture = ReadArchitecture(path);
			actualArchitectures[required.Path] = architecture;
			if (!string.Equals(architecture, required.Architecture, StringComparison.OrdinalIgnoreCase))
			{
				issues.Add($"Unexpected architecture for {required.Path}: expected {required.Architecture}, found {architecture}.");
			}
		}

		string? RoleArchitecture(string role) => layout.ArchitectureRoles.TryGetValue(role, out var definition) && actualArchitectures.TryGetValue(definition.Path, out var value)
			? value
			: null;
		return new LayoutEvaluation(layout, issues, RoleArchitecture("editorAndPlugins"), RoleArchitecture("gameRuntime"));
	}

	private static bool MarkersMatch(string root, ManiacsVersionRecipe recipe)
	{
		foreach (var marker in recipe.BuildMarkers)
		{
			var path = ResolvePath(root, marker.Path);
			if (!File.Exists(path)) { return false; }
			var content = File.ReadAllBytes(path);
			var needle = Encoding.ASCII.GetBytes(marker.ContainsAscii);
			if (content.AsSpan().IndexOf(needle) < 0) { return false; }
		}
		return recipe.BuildMarkers.Count > 0;
	}

	private static string? DetectBuild(string root, ManiacsPackageLayout layout)
	{
		foreach (var detector in layout.BuildDetectors ?? [])
		{
			var path = ResolvePath(root, detector.Path);
			if (!File.Exists(path)) { continue; }
			var text = Encoding.Latin1.GetString(File.ReadAllBytes(path));
			var match = Regex.Match(text, detector.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
			if (match.Success) { return match.Groups[detector.Group].Value; }
		}
		return null;
	}

	private static bool IdentityFilesMatch(string root, ManiacsVersionRecipe recipe) => GetChangedIdentityFiles(root, recipe).Count == 0;

	private static IReadOnlyList<string> GetChangedIdentityFiles(string root, ManiacsVersionRecipe recipe)
	{
		var changed = new List<string>();
		foreach (var (relativePath, expectedHash) in recipe.IdentityFileSha256)
		{
			var path = ResolvePath(root, relativePath);
			if (!File.Exists(path) || !string.Equals(ComputeSha256(path), expectedHash, StringComparison.OrdinalIgnoreCase))
			{
				changed.Add($"Build fingerprint differs: {relativePath}");
			}
		}
		return changed;
	}

	private static string ResolvePath(string root, string relativePath)
	{
		ManiacsRecipeCatalog.ValidateRelativePath(relativePath);
		var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
		var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var fullPath = Path.GetFullPath(Path.Combine(root, normalized));
		if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"Package path escapes its root: {relativePath}");
		}
		return fullPath;
	}

	private static string ComputeSha256(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}

	private static string ReadArchitecture(string path)
	{
		using var stream = File.OpenRead(path);
		if (stream.Length < 64) { return "invalid PE"; }
		Span<byte> dosHeader = stackalloc byte[64];
		stream.ReadExactly(dosHeader);
		if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z') { return "invalid PE"; }
		var peOffset = BinaryPrimitives.ReadInt32LittleEndian(dosHeader[0x3c..]);
		if (peOffset < 64 || peOffset > stream.Length - 6) { return "invalid PE"; }
		stream.Position = peOffset;
		Span<byte> peHeader = stackalloc byte[6];
		stream.ReadExactly(peHeader);
		if (!peHeader[..4].SequenceEqual("PE\0\0"u8)) { return "invalid PE"; }
		return BinaryPrimitives.ReadUInt16LittleEndian(peHeader[4..]) switch
		{
			0x014c => "x86",
			0x8664 => "x64",
			0x01c0 or 0x01c4 => "arm",
			0xaa64 => "arm64",
			var machine => $"unknown PE machine 0x{machine:X4}"
		};
	}

	private sealed record LayoutEvaluation(ManiacsPackageLayout Layout, IReadOnlyList<string> Issues, string? EditorArchitecture, string? RuntimeArchitecture);
}
