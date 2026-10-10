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

	[Fact]
	public async Task ArabicChildInstallDoesNotEnumerateOrCopyItsVanillaSubtree()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "RPG Maker 2003");
		SteamDetectionTests.CreateVanillaInstall(source);
		File.WriteAllText(Path.Combine(source, "explicit-module-input.txt"), "owned input");
		File.WriteAllText(Path.Combine(source, "vanilla-sentinel.txt"), "unchanged-parent-file");
		var destination = Path.Combine(source, "Arabic");
		Directory.CreateDirectory(destination);
		File.WriteAllText(Path.Combine(destination, "old-local-file.txt"), "must-not-be-enumerated-as-source");
		var module = new ExplicitCopyModule();
		var plan = SetupModulePlanner.CreatePlan([module.Descriptor.Id], [module]);
		var sentinelBefore = await File.ReadAllBytesAsync(Path.Combine(source, "vanilla-sentinel.txt"));

		await new InstallationCoordinator().InstallAsync(source, destination, plan, replaceExisting: true);

		Assert.Equal("owned input", await File.ReadAllTextAsync(Path.Combine(destination, "module-owned.txt")));
		Assert.False(File.Exists(Path.Combine(destination, "old-local-file.txt")));
		Assert.Equal(sentinelBefore, await File.ReadAllBytesAsync(Path.Combine(source, "vanilla-sentinel.txt")));
		Assert.Empty(Directory.EnumerateDirectories(source, ".EasyRPG-Arabic.staging-*", SearchOption.TopDirectoryOnly));
		Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(source, "*.backup-*", SearchOption.TopDirectoryOnly),
			path => Path.GetFileName(path).Contains("Arabic", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task CoordinatorReportsCompletedStagesInMonotonicOrder()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		SteamDetectionTests.CreateVanillaInstall(source);
		File.WriteAllText(Path.Combine(source, "explicit-module-input.txt"), "owned input");
		var destination = Path.Combine(fixture.Path, "managed");
		var module = new ExplicitCopyModule();
		var plan = SetupModulePlanner.CreatePlan([module.Descriptor.Id], [module]);
		var progress = new InlineProgress<InstallationProgress>();

		await new InstallationCoordinator().InstallAsync(source, destination, plan, false, null, false, progress: progress);

		Assert.Equal("Preparing installation...", progress.Values[0].Stage);
		Assert.Equal("Finalizing installation...", progress.Values[^2].Stage);
		Assert.Equal(100, progress.Values[^1].Percent);
		Assert.True(progress.Values.Zip(progress.Values.Skip(1), (left, right) => left.Percent <= right.Percent).All(value => value));
	}

	[Fact]
	public async Task CoordinatorFailureReportsRollbackBelowOneHundredPercent()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		SteamDetectionTests.CreateVanillaInstall(source);
		File.WriteAllText(Path.Combine(source, "explicit-module-input.txt"), "owned input");
		var destination = Path.Combine(fixture.Path, "managed");
		var module = new ExplicitCopyModule(failVerification: true);
		var plan = SetupModulePlanner.CreatePlan([module.Descriptor.Id], [module]);
		var progress = new InlineProgress<InstallationProgress>();

		await Assert.ThrowsAsync<SetupInstallException>(() => new InstallationCoordinator()
			.InstallAsync(source, destination, plan, false, null, false, progress: progress));

		Assert.True(progress.Values[^1].Failed);
		Assert.Contains("rolling back", progress.Values[^1].Stage, StringComparison.OrdinalIgnoreCase);
		Assert.InRange(progress.Values[^1].Percent, 0, 99);
		Assert.DoesNotContain(progress.Values, value => value.Percent == 100);
		Assert.False(Directory.Exists(destination));
	}

	private sealed class InlineProgress<T> : IProgress<T>
	{
		public List<T> Values { get; } = [];
		public void Report(T value) => Values.Add(value);
	}

	private sealed class ExplicitCopyModule : ISetupModule
	{
		private readonly bool _failVerification;

		public ExplicitCopyModule(bool failVerification = false) => _failVerification = failVerification;

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
			if (_failVerification) { return Task.FromResult(ModuleVerificationResult.Failure("Synthetic failure.")); }
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
