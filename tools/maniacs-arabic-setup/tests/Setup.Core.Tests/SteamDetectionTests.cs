using EasyRpgArabic.Setup.Core.Detection;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class SteamDetectionTests
{
	[Fact]
	public void DetectsValidatedRpgMakerInstallInSteamLibrary()
	{
		using var fixture = new TempDirectory();
		var library = Path.Combine(fixture.Path, "SteamLibrary");
		var game = Path.Combine(library, "steamapps", "common", "RPG Maker 2003");
		CreateVanillaInstall(game);

		var detector = new SteamRpgMaker2003Detector(new FixedLibrarySource(library));

		var detected = detector.Detect();

		Assert.NotNull(detected);
		Assert.Equal(Path.GetFullPath(game), detected!.Directory);
		Assert.Equal(Path.GetFullPath(library), detected.SteamLibrary);
	}

	[Fact]
	public void RejectsSteamAppMarkerForAnotherGame()
	{
		using var fixture = new TempDirectory();
		var library = Path.Combine(fixture.Path, "SteamLibrary");
		var game = Path.Combine(library, "steamapps", "common", "RPG Maker 2003");
		CreateVanillaInstall(game);
		File.WriteAllText(Path.Combine(game, "steam_appid.txt"), "12345");

		var detected = new SteamRpgMaker2003Detector(new FixedLibrarySource(library)).Detect();

		Assert.Null(detected);
	}

	internal static void CreateVanillaInstall(string directory)
	{
		Directory.CreateDirectory(directory);
		foreach (var file in new[] { "rpg_rt.exe.dat", "rpg_rt.ldb.dat", "ultimate_eb.dll", "ultimate_rt_eb.dll.dat", "UNLHA32.DLL" })
		{
			File.WriteAllBytes(Path.Combine(directory, file), [1, 2, 3]);
		}
		File.WriteAllText(Path.Combine(directory, "steam_appid.txt"), RpgMaker2003InstallationValidator.SteamAppId);
		File.WriteAllBytes(Path.Combine(directory, "rpg2003.exe"), CreatePe32Executable());
	}

	private static byte[] CreatePe32Executable()
	{
		var bytes = new byte[512];
		bytes[0] = (byte)'M';
		bytes[1] = (byte)'Z';
		BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
		bytes[0x80] = (byte)'P';
		bytes[0x81] = (byte)'E';
		bytes[0x82] = 0;
		bytes[0x83] = 0;
		bytes[0x84] = 0x4C;
		bytes[0x85] = 0x01;
		return bytes;
	}

	private sealed class FixedLibrarySource(string library) : ISteamLibrarySource
	{
		public IEnumerable<string> GetLibraryRoots() => [library];
	}

	internal sealed class TempDirectory : IDisposable
	{
		public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "EasyRpgArabicSetupTest-" + Guid.NewGuid().ToString("N"));
		public TempDirectory() => Directory.CreateDirectory(Path);
		public void Dispose() { if (Directory.Exists(Path)) { Directory.Delete(Path, recursive: true); } }
	}
}
