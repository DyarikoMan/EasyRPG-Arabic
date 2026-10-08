using System.Security.Cryptography;
using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class InstallationCoordinatorTests
{
	[Fact]
	public async Task ManagedInstallContainsOnlyFilesExplicitlyProducedByModules()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		SteamDetectionTests.CreateVanillaInstall(source);
		var unrelatedDirectory = Path.Combine(source, "old-maniacs-installation");
		Directory.CreateDirectory(unrelatedDirectory);
		File.WriteAllText(Path.Combine(unrelatedDirectory, "sentinel.txt"), "vanilla-only");
		File.WriteAllText(Path.Combine(source, "explicit-module-input.txt"), "owned input");
		var sourceSentinel = Path.Combine(unrelatedDirectory, "sentinel.txt");
		var destination = Path.Combine(fixture.Path, "managed", "RPG Maker 2003 - Arabic");
		var module = new ExplicitCopyModule();
		var plan = SetupModulePlanner.CreatePlan([module.Descriptor.Id], [module]);

		await new InstallationCoordinator().InstallAsync(source, destination, plan, replaceExisting: false);

		Assert.Equal("owned input", await File.ReadAllTextAsync(Path.Combine(destination, "module-owned.txt")));
		Assert.False(Directory.Exists(Path.Combine(destination, "old-maniacs-installation")));
		Assert.False(File.Exists(Path.Combine(destination, "rpg2003.exe")));
		Assert.Equal("vanilla-only", await File.ReadAllTextAsync(sourceSentinel));
		Assert.Equal("owned input", await File.ReadAllTextAsync(Path.Combine(source, "explicit-module-input.txt")));
	}

	private sealed class ExplicitCopyModule : ISetupModule
	{
		public SetupModuleDescriptor Descriptor { get; } = new(
			"test-explicit-copy", "Explicit copy test", "1", [], [], ModuleReadiness.Available, "Synthetic test module.");

		public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
		{
			var source = Path.Combine(context.VanillaDirectory, "explicit-module-input.txt");
			var target = Path.Combine(context.StagingDirectory, "module-owned.txt");
			await File.WriteAllTextAsync(target, await File.ReadAllTextAsync(source, cancellationToken), cancellationToken);
			var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target, cancellationToken)));
			return new ModuleApplyResult([new ModuleOwnedFile("module-owned.txt", hash)]);
		}

		public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var target = Path.Combine(context.StagingDirectory, "module-owned.txt");
			var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target)));
			return Task.FromResult(hash == result.OwnedFiles[0].Sha256
				? ModuleVerificationResult.Success()
				: ModuleVerificationResult.Failure("Synthetic module output hash mismatch."));
		}

		public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
		{
			var target = Path.Combine(context.StagingDirectory, "module-owned.txt");
			if (File.Exists(target)) { File.Delete(target); }
			return Task.CompletedTask;
		}

		public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default)
		{
			var target = Path.Combine(context.StagingDirectory, "module-owned.txt");
			if (File.Exists(target)) { File.Delete(target); }
			return Task.FromResult(new ModuleUninstallResult(["module-owned.txt"], []));
		}
	}
}
