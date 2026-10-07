using EasyRpgArabic.Setup.Core.Modules;

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
		Assert.Equal(3, result.OwnedFiles.Count);
		await module.RollbackAsync(context, result);
		Assert.Empty(Directory.EnumerateFiles(Path.Combine(stage, "Plugin")));
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
		Assert.Equal(2, uninstall.RemovedFiles.Count);
		Assert.True(File.Exists(changedPath));
	}

	private static Stream OpenPackage() =>
		typeof(ArabicEditorModuleTests).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.ArabicEditor.zip")
		?? throw new FileNotFoundException("Embedded editor package missing from test assembly.");
}
