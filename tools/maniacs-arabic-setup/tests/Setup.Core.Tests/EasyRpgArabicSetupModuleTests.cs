using System.Security.Cryptography;
using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class EasyRpgArabicSetupModuleTests
{
	[Fact]
	public void EmbeddedPlayerManifestPinsTheRequestedCommitAndReleaseBinary()
	{
		var payload = EasyRpgArabicPlayerPayload.LoadEmbedded();

		Assert.Equal("0f79458c8e97a98d4a51e5e2472b84e5bc49dfef", payload.BuildId);
		Assert.Equal("0.8.1", payload.Version);
		Assert.Equal("x64", payload.Architecture);
		Assert.Equal(14_572_032, payload.Size);
		Assert.Equal("A300BE9787B73373976F60569052C8AB3C660261DCB5094B2DDFFFE460FBA6DB", payload.Sha256);
		Assert.Equal("Player.exe", payload.InstalledPath);
	}

	[Fact]
	public async Task InstallsVerifiedPlayerBesideUnchangedManiacsRuntimeAndTracksOnlyPlayer()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		SteamDetectionTests.CreateVanillaInstall(source);
		var vanillaExeHashBefore = Hash(await File.ReadAllBytesAsync(Path.Combine(source, "rpg2003.exe")));
		var destination = Path.Combine(fixture.Path, "managed", "RPG Maker 2003 - Arabic");
		var original = MakePe("validated Maniacs runtime", 0x8664);
		var player = MakePe("EasyRPG-Arabic Player build", 0x8664);
		var seed = new SeedManiacsRuntimeModule(original);
		var module = CreateModule(player, original);
		var plan = new SetupPlan([seed, module]);
		var defaults = NewProjectDefaults.FromIniOptions(true, true, true, true);

		var install = await new InstallationCoordinator().InstallAsync(source, destination, plan, replaceExisting: false,
			maniacsPackageDirectory: null, maniacsLicenseAccepted: false, projectDefaults: defaults);

		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(destination, "RPG_RT.exe")));
		Assert.Equal(player, await File.ReadAllBytesAsync(Path.Combine(destination, "Player.exe")));
		var defaultsPath = Path.Combine(destination, "ArabicSupport", "new-project-defaults.ini");
		Assert.Equal(defaults.ToIniBytes(), await File.ReadAllBytesAsync(defaultsPath));
		Assert.False(File.Exists(Path.Combine(destination, "RPG_RT-Maniacs.exe")));
		var installed = Assert.Single(install.State.Modules, item => item.Id == "easyrpg-arabic");
		Assert.Equal("x64", installed.Architecture);
		Assert.Collection(installed.Files.OrderBy(file => file.RelativePath),
			owned =>
			{
				Assert.Equal("ArabicSupport/new-project-defaults.ini", owned.RelativePath);
				Assert.Equal(Hash(defaults.ToIniBytes()), owned.Sha256);
			},
			owned =>
			{
				Assert.Equal("Player.exe", owned.RelativePath);
				Assert.Equal(Hash(player), owned.Sha256);
			});
		Assert.Equal(64, installed.ManifestHash.Length);
		var persisted = await SetupStateStore.ReadAsync(destination);
		Assert.Equal("x64", Assert.Single(persisted.Modules, item => item.Id == "easyrpg-arabic").Architecture);
		Assert.Equal(vanillaExeHashBefore, Hash(await File.ReadAllBytesAsync(Path.Combine(source, "rpg2003.exe"))));
	}

	[Fact]
	public void RecommendedSetupWithEmbeddedPayloadMarksAllFourModulesReadyAndOrdersDependencies()
	{
		var player = MakePe("EasyRPG-Arabic Player build", 0x8664);
		var payload = new EasyRpgArabicPlayerPayload(
			1, "0.8.1", "fc479ae90f8b47217545ee94d4c754f6c0984845", "x64", Hash(player), player.Length,
			"241029", "Player.exe");
		var modules = RecommendedSetupModules.Create(
			() => new MemoryStream([]), () => new MemoryStream(player, writable: false), payload);

		Assert.All(modules, module => Assert.Equal(ModuleReadiness.Available, module.Descriptor.Readiness));
		Assert.Equal("x64", Assert.Single(modules, module => module.Descriptor.Id == "easyrpg-arabic").Descriptor.Architecture);
		var plan = SetupModulePlanner.CreatePlan(modules.Select(module => module.Descriptor.Id), modules);
		Assert.Equal(["maniacs", "maniacs-english", "arabic-editor", "easyrpg-arabic"], plan.OrderedModules.Select(module => module.Descriptor.Id));
	}

	[Fact]
	public async Task VerificationRejectsModifiedPlayerAndNeverChangesManiacsRuntime()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var (context, original, _, module) = CreateStage(fixture.Path);
		var applied = await module.ApplyAsync(context);
		var playerPath = Path.Combine(context.StagingDirectory, "Player.exe");
		var changed = MakePe("modified after apply", 0x8664);
		await File.WriteAllBytesAsync(playerPath, changed);

		var result = await module.VerifyAsync(context, applied);

		Assert.False(result.Succeeded);
		Assert.Contains("does not match", result.Message);
		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(context.StagingDirectory, "RPG_RT.exe")));
		Assert.Equal(changed, await File.ReadAllBytesAsync(playerPath));
	}

	[Fact]
	public async Task ApplyRefusesToOverwriteExistingPlayerAndLeavesBothExistingFilesUntouched()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var original = MakePe("validated Maniacs runtime", 0x8664);
		var existingPlayer = MakePe("user existing Player", 0x8664);
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(stage);
		await File.WriteAllBytesAsync(Path.Combine(stage, "RPG_RT.exe"), original);
		await File.WriteAllBytesAsync(Path.Combine(stage, "Player.exe"), existingPlayer);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, Path.Combine(fixture.Path, "final"));
		var module = CreateModule(MakePe("bundled Player", 0x8664), original);

		await Assert.ThrowsAsync<IOException>(() => module.ApplyAsync(context));

		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(stage, "RPG_RT.exe")));
		Assert.Equal(existingPlayer, await File.ReadAllBytesAsync(Path.Combine(stage, "Player.exe")));
		Assert.Empty(Directory.EnumerateFiles(stage, "*.tmp"));
	}

	[Fact]
	public async Task RollbackRemovesOnlyOwnedPlayerAndKeepsManiacsRuntime()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var (context, original, _, module) = CreateStage(fixture.Path);
		var applied = await module.ApplyAsync(context);

		await module.RollbackAsync(context, applied);

		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(context.StagingDirectory, "RPG_RT.exe")));
		Assert.False(File.Exists(Path.Combine(context.StagingDirectory, "Player.exe")));
		Assert.False(File.Exists(Path.Combine(context.StagingDirectory, "ArabicSupport", "new-project-defaults.ini")));
		Assert.False(File.Exists(Path.Combine(context.StagingDirectory, "RPG_RT-Maniacs.exe")));
	}

	[Fact]
	public async Task BadPayloadHashLeavesManiacsRuntimeUntouchedAndCreatesNoPlayer()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var original = MakePe("validated Maniacs runtime", 0x8664);
		var expectedPlayer = MakePe("expected Player", 0x8664);
		var invalidPlayer = MakePe("wrong Player", 0x8664);
		var stage = Path.Combine(fixture.Path, "stage");
		Directory.CreateDirectory(stage);
		await File.WriteAllBytesAsync(Path.Combine(stage, "RPG_RT.exe"), original);
		var context = new SetupModuleContext(Path.Combine(fixture.Path, "vanilla"), stage, Path.Combine(fixture.Path, "final"));
		var module = CreateModule(expectedPlayer, original, invalidPlayer);

		await Assert.ThrowsAsync<InvalidDataException>(() => module.ApplyAsync(context));

		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(stage, "RPG_RT.exe")));
		Assert.False(File.Exists(Path.Combine(stage, "Player.exe")));
		Assert.Empty(Directory.EnumerateFiles(stage, "*.tmp"));
	}

	[Fact]
	public async Task UninstallRemovesOwnedPlayerButPreservesManiacsRuntime()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var (context, original, _, module) = CreateStage(fixture.Path);
		var applied = await module.ApplyAsync(context);

		var result = await module.UninstallAsync(context, applied.OwnedFiles);

		Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(context.StagingDirectory, "RPG_RT.exe")));
		Assert.False(File.Exists(Path.Combine(context.StagingDirectory, "Player.exe")));
		Assert.Equal(new[] { "ArabicSupport/new-project-defaults.ini", "Player.exe" }, result.RemovedFiles.OrderBy(path => path).ToArray());
		Assert.Empty(result.PreservedFiles);
	}

	private static (SetupModuleContext Context, byte[] Original, byte[] Player, EasyRpgArabicSetupModule Module) CreateStage(string root)
	{
		var original = MakePe("validated Maniacs runtime", 0x8664);
		var player = MakePe("EasyRPG-Arabic Player build", 0x8664);
		var stage = Path.Combine(root, "stage");
		Directory.CreateDirectory(stage);
		File.WriteAllBytes(Path.Combine(stage, "RPG_RT.exe"), original);
		return (new SetupModuleContext(Path.Combine(root, "vanilla"), stage, Path.Combine(root, "final")), original, player, CreateModule(player, original));
	}

	private static EasyRpgArabicSetupModule CreateModule(byte[] expectedPlayer, byte[] original, byte[]? providedPlayer = null)
	{
		providedPlayer ??= expectedPlayer;
		var payload = new EasyRpgArabicPlayerPayload(
			1, "0.8.1", "fc479ae90f8b47217545ee94d4c754f6c0984845", "x64", Hash(expectedPlayer), expectedPlayer.Length,
			"241029", "Player.exe");
		return new EasyRpgArabicSetupModule(() => new MemoryStream(providedPlayer, writable: false), payload, Hash(original));
	}

	private static byte[] MakePe(string marker, ushort machine)
	{
		var bytes = new byte[512];
		bytes[0] = (byte)'M';
		bytes[1] = (byte)'Z';
		BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
		"PE\0\0"u8.CopyTo(bytes.AsSpan(0x80));
		BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
		System.Text.Encoding.UTF8.GetBytes(marker).CopyTo(bytes, 0x100);
		return bytes;
	}

	private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

	private sealed class SeedManiacsRuntimeModule(byte[] runtime) : ISetupModule
	{
		public SetupModuleDescriptor Descriptor { get; } = new("maniacs-english", "Test Maniacs English", "test", [], [], ModuleReadiness.Available, "Synthetic seed.", "x64");

		public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
		{
			var path = Path.Combine(context.StagingDirectory, "RPG_RT.exe");
			await File.WriteAllBytesAsync(path, runtime, cancellationToken);
			return new ModuleApplyResult([new ModuleOwnedFile("RPG_RT.exe", Hash(runtime))]);
		}

		public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) =>
			Task.FromResult(ModuleVerificationResult.Success());

		public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
		{
			var path = Path.Combine(context.StagingDirectory, "RPG_RT.exe");
			if (File.Exists(path)) { File.Delete(path); }
			return Task.CompletedTask;
		}

		public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) =>
			Task.FromResult(new ModuleUninstallResult([], []));
	}
}
