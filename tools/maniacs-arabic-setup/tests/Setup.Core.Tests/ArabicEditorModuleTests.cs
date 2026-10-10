using EasyRpgArabic.Setup.Core.Modules;
using System.Xml.Linq;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class ArabicEditorModuleTests
{
	[Fact]
	public async Task InstallsAndVerifiesExistingArabicEditorZipPayload()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(Path.Combine(stage, "Plugin"));
		foreach (var file in new[] { "rpg2003.exe", "cmdcs.dll", "cmdTos.dll" })
		{
			File.WriteAllBytes(Path.Combine(stage, file), [1, 2, 3]);
		}
		var module = new ArabicEditorSetupModule(OpenPackage);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, Path.Combine(fixture.Path, "final"));

		var result = await module.ApplyAsync(context);
		var verification = await module.VerifyAsync(context, result);

		Assert.True(verification.Succeeded, verification.Message);
		Assert.Equal(15, result.OwnedFiles.Count);
		Assert.Contains(result.OwnedFiles, file => file.RelativePath == "ArabicSupport/tools/lcftrans.exe");
		Assert.Contains(result.OwnedFiles, file => file.RelativePath == "ArabicSupport/template/Language/ar/Font/Font.ttf");
		var bundledFont = await File.ReadAllBytesAsync(Path.Combine(stage, "ArabicSupport", "template", "Language", "ar", "Font", "Font.ttf"));
		Assert.Equal(
			"68B412A4D8443FE677DD37412BB44D117CADE607572B2CC9689177DA3934952F",
			Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bundledFont)));
		Assert.True(File.Exists(Path.Combine(stage, "ArabicSupport", "terminology", "canonical-terms.json")));
		Assert.Equal("1256", ReadCodePage(Path.Combine(stage, "Plugin", "cmdcs.xml")));
		await module.RollbackAsync(context, result);
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(stage, "Plugin")));
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(stage, "ArabicSupport"), "*", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task UninstallPreservesFilesModifiedAfterInstallation()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(Path.Combine(stage, "Plugin"));
		foreach (var file in new[] { "rpg2003.exe", "cmdcs.dll", "cmdTos.dll" })
		{
			File.WriteAllBytes(Path.Combine(stage, file), [1, 2, 3]);
		}
		var module = new ArabicEditorSetupModule(OpenPackage);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, Path.Combine(fixture.Path, "final"));
		var applied = await module.ApplyAsync(context);
		var changedPath = Path.Combine(stage, "Plugin", "EasyCmdTos.dll");
		await File.WriteAllBytesAsync(changedPath, [9, 9, 9]);

		var uninstall = await module.UninstallAsync(context, applied.OwnedFiles);

		Assert.Contains("Plugin/EasyCmdTos.dll", uninstall.PreservedFiles);
		Assert.Equal(14, uninstall.RemovedFiles.Count);
		Assert.True(File.Exists(changedPath));
		Assert.False(File.Exists(Path.Combine(stage, "Plugin", "cmdcs.xml")));
	}

	[Fact]
	public async Task PreservesExistingPluginPreferencesAndRestoresOriginalOnUninstall()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var stage = Path.Combine(fixture.Path, "stage");
		var final = Path.Combine(fixture.Path, "final");
		Directory.CreateDirectory(Path.Combine(stage, "Plugin"));
		Directory.CreateDirectory(Path.Combine(final, "Plugin"));
		foreach (var file in new[] { "rpg2003.exe", "cmdcs.dll", "cmdTos.dll" })
		{
			File.WriteAllBytes(Path.Combine(stage, file), [1, 2, 3]);
		}
		var previousConfigPath = Path.Combine(final, "Plugin", "cmdcs.xml");
		var previousConfig = "<CmdcsConfig><ActivationInfoArray /><OtherInfo><PluginOtherInfo><Jp>false</Jp><CodePage>1252</CodePage><Memo>user setting</Memo></PluginOtherInfo></OtherInfo><DatInfoArray /></CmdcsConfig>";
		await File.WriteAllTextAsync(previousConfigPath, previousConfig);
		var originalBytes = await File.ReadAllBytesAsync(previousConfigPath);
		var module = new ArabicEditorSetupModule(OpenPackage);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, final);

		var applied = await module.ApplyAsync(context);

		var stagedConfig = Path.Combine(stage, "Plugin", "cmdcs.xml");
		const string originalBackupPath = ".EasyRPG-Arabic/arabic-editor/cmdcs.xml.original";
		Assert.Equal("1256", ReadCodePage(stagedConfig));
		Assert.Contains("<Memo>user setting</Memo>", await File.ReadAllTextAsync(stagedConfig));
		Assert.Equal(originalBytes, await File.ReadAllBytesAsync(Path.Combine(stage, originalBackupPath.Replace('/', Path.DirectorySeparatorChar))));
		var uninstall = await module.UninstallAsync(context, applied.OwnedFiles);

		Assert.Equal(originalBytes, await File.ReadAllBytesAsync(stagedConfig));
		Assert.False(File.Exists(Path.Combine(stage, originalBackupPath.Replace('/', Path.DirectorySeparatorChar))));
		Assert.Contains(originalBackupPath, uninstall.RemovedFiles);
	}

	[Fact]
	public async Task UninstallPreservesPluginSettingsModifiedAfterInstallation()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(Path.Combine(stage, "Plugin"));
		foreach (var file in new[] { "rpg2003.exe", "cmdcs.dll", "cmdTos.dll" })
		{
			File.WriteAllBytes(Path.Combine(stage, file), [1, 2, 3]);
		}
		var module = new ArabicEditorSetupModule(OpenPackage);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, Path.Combine(fixture.Path, "final"));
		var applied = await module.ApplyAsync(context);
		var configPath = Path.Combine(stage, "Plugin", "cmdcs.xml");
		var contents = await File.ReadAllTextAsync(configPath);
		await File.WriteAllTextAsync(configPath, contents.Replace("<Memo />", "<Memo>preserve me</Memo>", StringComparison.Ordinal));

		var uninstall = await module.UninstallAsync(context, applied.OwnedFiles);

		Assert.True(File.Exists(configPath));
		Assert.Contains("<Memo>preserve me</Memo>", await File.ReadAllTextAsync(configPath));
		Assert.Contains("Plugin/cmdcs.xml", uninstall.PreservedFiles);
	}

	private static Stream OpenPackage() =>
		typeof(ArabicEditorModuleTests).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.ArabicEditor.zip")
		?? throw new FileNotFoundException("Embedded editor package missing from test assembly.");

	private static string ReadCodePage(string path)
	{
		var document = XDocument.Load(path);
		return document.Root?.Element("OtherInfo")?.Element("PluginOtherInfo")?.Element("CodePage")?.Value
			?? throw new InvalidDataException("Plugin Text Encoding configuration was not found.");
	}
}
