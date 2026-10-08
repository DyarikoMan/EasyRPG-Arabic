using System.Security.Cryptography;
using System.Xml.Linq;
using EasyRpgArabic.Setup.Core.Maniacs;
using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class ManiacsWDiffModuleTests
{
	[Fact]
	public async Task Local241029ReferenceInstallsAllModulesAndEasyRpgRuntime()
	{
		var study = Environment.GetEnvironmentVariable("EASYRPG_MANIACS_STUDY") ?? @"C:\Dev\Maniacs-Utility-Study";
		var playerPath = Environment.GetEnvironmentVariable("EASYRPG_ARABIC_PLAYER");
		var referenceSource = Path.Combine(study, "Source-Vanilla-Copy");
		var package = Path.Combine(study, "Official-Package");
		if (!Directory.Exists(referenceSource) || !Directory.Exists(package) || string.IsNullOrWhiteSpace(playerPath) || !File.Exists(playerPath)) { return; }

		var root = Path.Combine(Path.GetTempPath(), "easyrpg-maniacs-wdiff-reference-" + Guid.NewGuid().ToString("N"));
		var source = Path.Combine(root, "vanilla-source");
		var destination = Path.Combine(root, "managed", "Arabic Maniacs");
		try
		{
			CreateDisposableVanillaSource(referenceSource, source);
			var sourceSnapshot = SnapshotFiles(source);
			var playerPayload = EasyRpgArabicPlayerPayload.LoadEmbedded();
			var modules = RecommendedSetupModules.Create(OpenArabicEditorPackage, () => File.OpenRead(playerPath), playerPayload);
			Assert.All(modules, module => Assert.Equal(ModuleReadiness.Available, module.Descriptor.Readiness));
			var plan = SetupModulePlanner.CreatePlan(["arabic-editor", "easyrpg-arabic"], modules);
			var result = await new InstallationCoordinator().InstallAsync(source, destination, plan, false, package, true);
			var output = Path.Combine(destination, "rpg2003.exe");
			Assert.Equal(4_673_536, new FileInfo(output).Length);
			Assert.Equal("02AB04E571797A69181F3E9BEDEF1BED27F9DD5AB7135EBD2D50FF09FFFA162E", Hash(output));
			Assert.Equal("29D5B064E8D7453A092CBA7C724348BC5A3E3F1B4218F3C53E9A32B3246E8D5C", Hash(Path.Combine(destination, "rpg2003.OLD")));
			Assert.Equal(playerPayload.Sha256, Hash(Path.Combine(destination, "Player.exe")));
			Assert.Equal("49BA424F3FF1FA7D4FC1334EEA272FDE4E437D07A6901DC6B87EFB95383EC44D", Hash(Path.Combine(destination, "RPG_RT.exe")));
			Assert.Equal("29D5B064E8D7453A092CBA7C724348BC5A3E3F1B4218F3C53E9A32B3246E8D5C", Hash(Path.Combine(source, "rpg2003.exe")));
			foreach (var file in new[]
			{
				"ultimate_eb.dll", "rpg_rt.ldb.dat", "ultimate_rt_eb.dll.dat", "UNLHA32.DLL",
				"accord.dll", "backupper.exe", "battle_command_list.txt", "cmdcs.dll", "cmdl_editor.exe", "Player.exe",
				"command_list.txt", "Footy2.dll", "LICENSE.txt", "cmdTos.dll", "patch.exe", "RPG_RT.exe",
				Path.Combine("Plugin", "EasyCmdTos.dll"), Path.Combine("Plugin", "EasyRpgCommands.dll"), Path.Combine("Plugin", "0Harmony.dll"),
				Path.Combine("Plugin", "cmdcs.xml")
			})
			{
				Assert.True(File.Exists(Path.Combine(destination, file)), $"Expected managed module output is missing: {file}");
			}
			Assert.Equal("1256", XDocument.Load(Path.Combine(destination, "Plugin", "cmdcs.xml"))
				.Root?.Element("OtherInfo")?.Element("PluginOtherInfo")?.Element("CodePage")?.Value);
			Assert.False(Directory.Exists(Path.Combine(destination, "old-maniacs-installation")));
			Assert.False(Directory.Exists(Path.Combine(destination, "RTP")));
			Assert.False(Directory.Exists(Path.Combine(destination, "Sample")));
			Assert.False(File.Exists(Path.Combine(destination, "steam_appid.txt")));
			Assert.False(File.Exists(Path.Combine(destination, "2k3_install.vdf")));
			Assert.False(File.Exists(Path.Combine(destination, "rpg_rt.exe.dat")));
			var expectedOwnedPaths = result.State.Modules.SelectMany(module => module.Files)
				.Select(file => file.RelativePath.Replace('\\', '/'))
				.Append(SetupStateStore.StateRelativePath.Replace('\\', '/'))
				.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
			var actualManagedPaths = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
				.Select(path => Path.GetRelativePath(destination, path).Replace('\\', '/'))
				.OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
			Assert.Equal(expectedOwnedPaths, actualManagedPaths);
			Assert.Equal(sourceSnapshot.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase), SnapshotFiles(source).OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase));
			Assert.Equal("keep source only", await File.ReadAllTextAsync(Path.Combine(source, "old-maniacs-installation", "sentinel.txt")));
			Assert.Equal(new[] { "maniacs", "maniacs-english", "arabic-editor", "easyrpg-arabic" }, result.State.Modules.Select(module => module.Id));
			Assert.All(result.State.Modules.Where(module => module.Id is "maniacs" or "maniacs-english"),
				module => Assert.Equal("maniacs-241029", module.Version));
		}
		finally
		{
			if (Directory.Exists(root)) { Directory.Delete(root, recursive: true); }
		}
	}

	[Fact]
	public async Task EnglishModuleRollbackRestoresVanillaBytesAndRemovesBackup()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(stage);
		var original = "vanilla editor bytes"u8.ToArray();
		var patched = "patched editor bytes"u8.ToArray();
		var target = Path.Combine(stage, "rpg2003.exe");
		var backup = Path.Combine(stage, "rpg2003.OLD");
		await File.WriteAllBytesAsync(target, patched);
		await File.WriteAllBytesAsync(backup, original);
		var owned = new ModuleApplyResult([
			new ModuleOwnedFile("rpg2003.exe", Sha(patched)),
			new ModuleOwnedFile("rpg2003.OLD", Sha(original))
		]);
		var catalog = ManiacsRecipeCatalog.LoadEmbedded();
		var module = new ManiacsEnglishSetupModule(new ManiacsPackageInspector(catalog), catalog);
		var context = new SetupModuleContext(fixture.Path, stage, Path.Combine(fixture.Path, "final"));

		await module.RollbackAsync(context, owned);

		Assert.Equal(original, await File.ReadAllBytesAsync(target));
		Assert.False(File.Exists(backup));
	}

	private static Stream OpenArabicEditorPackage() =>
		typeof(ManiacsWDiffModuleTests).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.ArabicEditor.zip")
		?? throw new FileNotFoundException("Embedded Arabic Editor package missing from test assembly.");

	private static string Hash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
	private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

	private static void CreateDisposableVanillaSource(string referenceSource, string destination)
	{
		Directory.CreateDirectory(destination);
		var files = new[]
		{
			"rpg2003.exe", "rpg_rt.exe.dat", "rpg_rt.ldb.dat", "ultimate_eb.dll",
			"ultimate_rt_eb.dll.dat", "UNLHA32.DLL", "rpg2003.ini", "tpc.exe"
		};
		foreach (var file in files)
		{
			var sourcePath = Path.Combine(referenceSource, file);
			if (File.Exists(sourcePath)) { File.Copy(sourcePath, Path.Combine(destination, file)); }
		}
		File.WriteAllText(Path.Combine(destination, "steam_appid.txt"), "362870");
		File.WriteAllText(Path.Combine(destination, "2k3_install.vdf"), "unrelated Steam metadata");
		Directory.CreateDirectory(Path.Combine(destination, "old-maniacs-installation"));
		File.WriteAllText(Path.Combine(destination, "old-maniacs-installation", "sentinel.txt"), "keep source only");
		Directory.CreateDirectory(Path.Combine(destination, "RTP"));
		File.WriteAllText(Path.Combine(destination, "RTP", "unrelated.txt"), "source RTP content");
		Directory.CreateDirectory(Path.Combine(destination, "Sample"));
		File.WriteAllText(Path.Combine(destination, "Sample", "unrelated.txt"), "source sample content");
	}

	private static Dictionary<string, string> SnapshotFiles(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
		.ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), Hash, StringComparer.OrdinalIgnoreCase);
}
