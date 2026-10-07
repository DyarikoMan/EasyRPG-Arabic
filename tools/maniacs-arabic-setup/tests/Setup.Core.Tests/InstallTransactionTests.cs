using EasyRpgArabic.Setup.Core.Modules;
using EasyRpgArabic.Setup.Core.Transactions;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class InstallTransactionTests
{
	[Fact]
	public async Task CreatesSiblingStageAsCopyAndLeavesSourceUntouched()
	{
		using var fixture = new SteamDetectionTests.TempDirectory();
		var source = Path.Combine(fixture.Path, "vanilla");
		Directory.CreateDirectory(source);
		File.WriteAllText(Path.Combine(source, "project.dat"), "source-data");
		var destination = Path.Combine(fixture.Path, "managed", "RPG Maker 2003 - Arabic");

		await using var transaction = await InstallTransaction.CreateAsync(source, destination);

		Assert.True(Directory.Exists(transaction.StagingDirectory));
		Assert.Equal(Path.GetDirectoryName(destination), Path.GetDirectoryName(transaction.StagingDirectory));
		Assert.Equal("source-data", await File.ReadAllTextAsync(Path.Combine(transaction.StagingDirectory, "project.dat")));
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

		await transaction.CommitAsync(replaceExisting: true);

		Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(destination, "version.txt")));
		Assert.NotNull(transaction.PreviousGenerationBackup);
		Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(transaction.PreviousGenerationBackup!, "version.txt")));
	}
}
