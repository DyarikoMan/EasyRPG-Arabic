using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class ManiacsPackageInspectorTests
{
	[Fact]
	public void EmbeddedManifestsKeepBuildAndArabicCompatibilityDataSeparate()
	{
		var catalog = ManiacsRecipeCatalog.LoadEmbedded();
		var recipe = Assert.Single(catalog.Recipes);
		var layout = Assert.Single(catalog.Layouts);

		Assert.Equal("maniacs-241029", recipe.Id);
		Assert.Equal("common-english-v1", layout.Id);
		Assert.Contains("maniacs-241029", catalog.ArabicEditorCompatibility.ValidatedRecipeIds);
		Assert.Equal("x86", layout.ArchitectureRoles["editorAndPlugins"].Expected);
		Assert.Equal("x64", layout.ArchitectureRoles["gameRuntime"].Expected);
	}

	[Fact]
	public void ManifestDataCanBeLoadedWithoutGuiResources()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "data", "maniacs");
		var catalog = ManiacsRecipeCatalog.LoadFromDirectory(directory);

		Assert.Contains(catalog.Recipes, recipe => recipe.Id == "maniacs-241029");
		Assert.Contains("maniacs-241029", catalog.ArabicEditorCompatibility.ValidatedRecipeIds);
	}

	[Fact]
	public void RecognizesExactKnownBuildAndArabicEditorCompatibility()
	{
		using var fixture = PackageFixture.Create();
		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(fixture.Root);

		Assert.Equal(ManiacsPackageStatus.KnownSupportedBuild, result.Status);
		Assert.Equal("fixture-build", result.CandidateRecipeId);
		Assert.Equal("x86", result.EditorArchitecture);
		Assert.Equal("x64", result.RuntimeArchitecture);
		Assert.True(result.ArabicEditorValidated);
	}

	[Fact]
	public void CompatibleLayoutWithUnknownBuildNeedsValidation()
	{
		using var fixture = PackageFixture.Create();
		File.WriteAllText(Path.Combine(fixture.Root, "update.txt"), "build 2031/04/05", Encoding.ASCII);

		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(fixture.Root);

		Assert.Equal(ManiacsPackageStatus.UnknownBuildSupportedLayout, result.Status);
		Assert.Equal("2031/04/05", result.DetectedBuild);
		Assert.False(result.ArabicEditorValidated);
		Assert.False(result.IsKnownSupportedBuild);
	}

	[Fact]
	public void ChangedFilesInKnownBuildStopForValidation()
	{
		using var fixture = PackageFixture.Create();
		File.WriteAllBytes(Path.Combine(fixture.Root, "Common", "cmdcs.dll"), CreatePe(0x014c, 7));

		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(fixture.Root);

		Assert.Equal(ManiacsPackageStatus.KnownBuildModified, result.Status);
		Assert.False(result.ArabicEditorValidated);
		Assert.Contains(result.Issues, issue => issue.Contains("cmdcs.dll", StringComparison.Ordinal));
	}

	[Fact]
	public void MissingRequiredFileIsUnsupportedLayout()
	{
		using var fixture = PackageFixture.Create();
		File.Delete(Path.Combine(fixture.Root, "English", "patch.exe"));

		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(fixture.Root);

		Assert.Equal(ManiacsPackageStatus.UnsupportedLayout, result.Status);
		Assert.Contains(result.Issues, issue => issue.Contains("English/patch.exe", StringComparison.Ordinal));
	}

	[Fact]
	public void UnexpectedRuntimeArchitectureIsUnsupportedLayout()
	{
		using var fixture = PackageFixture.Create();
		File.WriteAllBytes(Path.Combine(fixture.Root, "English", "RPG_RT.exe"), CreatePe(0x014c));

		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(fixture.Root);

		Assert.Equal(ManiacsPackageStatus.UnsupportedLayout, result.Status);
		Assert.Contains(result.Issues, issue => issue.Contains("RPG_RT.exe", StringComparison.Ordinal));
	}

	[Fact]
	public void FindsPackageRootOneLevelBelowSelectedFolder()
	{
		using var fixture = PackageFixture.Create();
		var root = Path.Combine(Path.GetTempPath(), "easyrpg-maniacs-wrapper-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		var nestedPackage = Path.Combine(root, "patch_maniacs_fixture");
		Directory.Move(fixture.Root, nestedPackage);
		try
		{
			var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(root);
			Assert.Equal(ManiacsPackageStatus.KnownSupportedBuild, result.Status);
			Assert.Equal(nestedPackage, result.PackageRoot);
		}
		finally
		{
			if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
		}
	}

	[Fact]
	public void RejectsNonPackageDirectory()
	{
		using var fixture = PackageFixture.Create();
		var empty = Path.Combine(fixture.Root, "empty");
		Directory.CreateDirectory(empty);

		var result = new ManiacsPackageInspector(fixture.Catalog).Inspect(empty);

		Assert.Equal(ManiacsPackageStatus.InvalidPackage, result.Status);
	}

	private static byte[] CreatePe(ushort machine, byte fill = 0)
	{
		var bytes = new byte[128];
		Array.Fill(bytes, fill);
		bytes[0] = (byte)'M';
		bytes[1] = (byte)'Z';
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0x3c), 64);
		"PE\0\0"u8.CopyTo(bytes.AsSpan(64));
		BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(68), machine);
		return bytes;
	}

	private sealed class PackageFixture : IDisposable
	{
		private static readonly string[] Directories = ["Common", "English", "English/Plugin"];
		private static readonly (string Path, string? Architecture)[] Files =
		[
			("utility.exe", "x86"),
			("Common/cmdcs.dll", "x86"),
			("English/cmdTos.dll", "x86"),
			("English/patch.exe", "x86"),
			("English/RPG_RT.exe", "x64"),
			("update.txt", null)
		];

		public string Root { get; }
		public ManiacsRecipeCatalog Catalog { get; }

		private PackageFixture(string root, ManiacsRecipeCatalog catalog)
		{
			Root = root;
			Catalog = catalog;
		}

		public static PackageFixture Create()
		{
			var root = Path.Combine(Path.GetTempPath(), "easyrpg-maniacs-fixture-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			foreach (var directory in Directories) { Directory.CreateDirectory(Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar))); }
			var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var (relativePath, architecture) in Files)
			{
				var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
				Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
				var bytes = architecture switch
				{
					"x86" => CreatePe(0x014c),
					"x64" => CreatePe(0x8664),
					_ => Encoding.ASCII.GetBytes("build 2024/10/28")
				};
				File.WriteAllBytes(fullPath, bytes);
				identities[relativePath] = Convert.ToHexString(SHA256.HashData(bytes));
			}
			var layout = new ManiacsPackageLayout(1, "fixture-layout", "Fixture layout", Directories,
				Files.Select(file => new ManiacsRequiredFile(file.Path, file.Architecture)).ToArray(),
				new Dictionary<string, ManiacsArchitectureRole>
				{
					["editorAndPlugins"] = new("Common/cmdcs.dll", "x86"),
					["gameRuntime"] = new("English/RPG_RT.exe", "x64")
				},
				[new ManiacsBuildDetector("update.txt", @"(?m)^build (?<build>\d{4}/\d{2}/\d{2})$", "build")]);
			var recipe = new ManiacsVersionRecipe(1, "fixture-build", "Fixture Build 1", "fixture", layout.Id,
				[new ManiacsBuildMarker("update.txt", "2024/10/28")], identities,
				new ManiacsOfficialInstallProcess("utility.exe", "interactive-user-required", "English/patch.exe", "not-verified"));
			var compatibility = new ArabicEditorCompatibility(1, "fixture-editor", "test", [recipe.Id],
				new Dictionary<string, string> { [recipe.Id] = "Test compatibility evidence." });
			return new PackageFixture(root, new ManiacsRecipeCatalog([layout], [recipe], compatibility));
		}

		public void Dispose()
		{
			if (Directory.Exists(Root)) { Directory.Delete(Root, recursive: true); }
		}
	}
}
