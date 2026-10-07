using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class SetupStateTests
{
	[Fact]
	public async Task StateRoundTripsThroughJson()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var state = new SetupState
		{
			InstallId = Guid.NewGuid().ToString("D"),
			Generation = 3,
			SourcePath = @"C:\Steam\steamapps\common\RPG Maker 2003",
			SourceExecutableSha256 = "AABBCC",
			InstallPath = @"C:\Steam\steamapps\common\RPG Maker 2003 - Arabic",
			Modules = [new InstalledModuleState("arabic-editor", "1.0.0", "manifest-hash", [new ModuleOwnedFile("Plugin/EasyCmdTos.dll", "1234")])]
		};

		await SetupStateStore.WriteAsync(fixture.Path, state);
		var restored = await SetupStateStore.ReadAsync(fixture.Path);

		Assert.Equal(state.InstallId, restored.InstallId);
		Assert.Equal(state.Generation, restored.Generation);
		Assert.Equal(state.SourcePath, restored.SourcePath);
		Assert.Equal("arabic-editor", Assert.Single(restored.Modules).Id);
		Assert.Equal("Plugin/EasyCmdTos.dll", Assert.Single(restored.Modules[0].Files).RelativePath);
	}
}
