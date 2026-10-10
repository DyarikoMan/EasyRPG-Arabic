using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class InstallTransactionTests
{
	[Fact]
	public async Task CreatesEmptySiblingStageAndLeavesSourceUntouched()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "project.dat"), "source-data");
		var destination = Path.Combine(fixture.Path, "managed", "RPG Maker 2003 - Arabic");

		await using var transaction = await InstallTransaction.CreateAsync(source, destination);

		Assert.True(Directory.Exists(transaction.StagingDirectory));
		Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(transaction.StagingDirectory));
		Assert.Empty(Directory.EnumerateFileSystemEntries(transaction.StagingDirectory));
		Assert.Equal("source-data", await File.ReadAllTextAsync(Path.Combine(source, "project.dat")));
		Assert.False(Directory.Exists(destination));
	}

	[Fact]
	public async Task RollbackDiscardsStageAndPreservesSourceAndDestination()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "original.txt"), "unchanged");
		var destination = Path.Combine(fixture.Path, "managed", "RPG Maker 2003 - Arabic");
		var transaction = await InstallTransaction.CreateAsync(source, destination);
		var stagePath = transaction.StagingDirectory;
		File.WriteAllText(Path.Combine(stagePath, "partial-module-output.txt"), "partial");

		await transaction.RollbackAsync();
		await transaction.DisposeAsync();

		Assert.False(Directory.Exists(stagePath));
		Assert.False(Directory.Exists(destination));
		Assert.Equal("unchanged", await File.ReadAllTextAsync(Path.Combine(source, "original.txt")));
	}

	[Fact]
	public async Task CommitRequiresExplicitReplacementAndKeepsPreviousGeneration()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "version.txt"), "new");
		var destination = Path.Combine(fixture.Path, "managed");
		Directory.CreateDirectory(destination);
		File.WriteAllText(Path.Combine(destination, "version.txt"), "old");
		await using var transaction = await InstallTransaction.CreateAsync(source, destination);
		await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(replaceExisting: false));
		File.WriteAllText(Path.Combine(transaction.StagingDirectory, "version.txt"), "new");

		await transaction.CommitAsync(replaceExisting: true);

		Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(destination, "version.txt")));
		Assert.NotNull(transaction.PreviousGenerationBackup);
		Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(transaction.PreviousGenerationBackup!, "version.txt")));
	}

	[Fact]
	public async Task ArabicChildStagesAndCommitsOnlyInsideManagedFolder()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "RPG Maker 2003");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "rpg2003.exe"), "vanilla-runtime");
		File.WriteAllText(Path.Combine(source, "vanilla-only.txt"), "keep");
		var destination = Path.Combine(source, "Arabic");

		await using var transaction = await InstallTransaction.CreateAsync(source, destination);

		Assert.StartsWith(destination + Path.DirectorySeparatorChar, transaction.StagingDirectory, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(destination, Path.GetDirectoryName(transaction.StagingDirectory));
		File.WriteAllText(Path.Combine(transaction.StagingDirectory, "Player.exe"), "player");
		await transaction.CommitAsync(replaceExisting: false);

		Assert.Equal("player", await File.ReadAllTextAsync(Path.Combine(destination, "Player.exe")));
		Assert.Equal("vanilla-runtime", await File.ReadAllTextAsync(Path.Combine(source, "rpg2003.exe")));
		Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(source, "vanilla-only.txt")));
		Assert.Empty(Directory.EnumerateDirectories(source, ".EasyRPG-Arabic.staging-*", SearchOption.TopDirectoryOnly));
		Assert.Empty(Directory.EnumerateDirectories(destination, ".EasyRPG-Arabic.staging-*", SearchOption.TopDirectoryOnly));
	}

	[Fact]
	public async Task ArabicChildRollbackPreservesExistingDestinationAndSource()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "RPG Maker 2003");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "rpg2003.exe"), "vanilla-runtime");
		var destination = Path.Combine(source, "Arabic");
		Directory.CreateDirectory(destination);
		File.WriteAllText(Path.Combine(destination, "sentinel.txt"), "old-managed-content");

		var transaction = await InstallTransaction.CreateAsync(source, destination);
		File.WriteAllText(Path.Combine(transaction.StagingDirectory, "partial.txt"), "partial");
		await transaction.RollbackAsync();
		await transaction.DisposeAsync();

		Assert.Equal("old-managed-content", await File.ReadAllTextAsync(Path.Combine(destination, "sentinel.txt")));
		Assert.Equal("vanilla-runtime", await File.ReadAllTextAsync(Path.Combine(source, "rpg2003.exe")));
		Assert.Empty(Directory.EnumerateDirectories(destination, ".EasyRPG-Arabic.staging-*", SearchOption.TopDirectoryOnly));
	}
}
