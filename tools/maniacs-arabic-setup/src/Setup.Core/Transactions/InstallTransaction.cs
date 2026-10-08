namespace EasyRpgArabic.Setup.Core.Transactions;

public sealed class InstallTransaction : IAsyncDisposable
{
	private bool _committed;
	private bool _disposed;

	public string SourceDirectory { get; }
	public string FinalDirectory { get; }
	public string StagingDirectory { get; }
	public string? PreviousGenerationBackup { get; private set; }

	private InstallTransaction(string sourceDirectory, string finalDirectory, string stagingDirectory)
	{
		SourceDirectory = sourceDirectory;
		FinalDirectory = finalDirectory;
		StagingDirectory = stagingDirectory;
	}

	public static Task<InstallTransaction> CreateAsync(
		string sourceDirectory,
		string finalDirectory,
		CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var source = Path.GetFullPath(sourceDirectory);
		var destination = Path.GetFullPath(finalDirectory);
		if (!Directory.Exists(source)) { throw new DirectoryNotFoundException($"Source directory does not exist: {source}"); }
		if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
		{
			throw new IOException("The vanilla source folder is a link/reparse point and cannot be safely staged.");
		}
		EnsureDirectoriesAreSeparate(source, destination);
		var parent = Path.GetDirectoryName(destination);
		if (string.IsNullOrWhiteSpace(parent)) { throw new InvalidOperationException("The destination must have a parent folder."); }
		Directory.CreateDirectory(parent);
		var stage = Path.Combine(parent, ".EasyRPG-Arabic.staging-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stage);
		return Task.FromResult(new InstallTransaction(source, destination, stage));
	}

	public Task CommitAsync(bool replaceExisting, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		if (_committed) { throw new InvalidOperationException("This installation transaction has already been committed."); }
		if (!Directory.Exists(StagingDirectory)) { throw new DirectoryNotFoundException("The staging directory no longer exists."); }

		if (File.Exists(FinalDirectory)) { throw new IOException("The destination path is a file."); }
		if (Directory.Exists(FinalDirectory))
		{
			if (!replaceExisting) { throw new IOException("The destination already exists. Explicit replacement confirmation is required."); }
			PreviousGenerationBackup = FinalDirectory + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N");
			Directory.Move(FinalDirectory, PreviousGenerationBackup);
		}

		try
		{
			Directory.Move(StagingDirectory, FinalDirectory);
			_committed = true;
			return Task.CompletedTask;
		}
		catch
		{
			if (PreviousGenerationBackup is not null && Directory.Exists(PreviousGenerationBackup) && !Directory.Exists(FinalDirectory))
			{
				Directory.Move(PreviousGenerationBackup, FinalDirectory);
				PreviousGenerationBackup = null;
			}
			throw;
		}
	}

	public Task RollbackAsync()
	{
		if (_committed) { throw new InvalidOperationException("A committed transaction must be rolled back using its retained previous-generation backup."); }
		if (Directory.Exists(StagingDirectory))
		{
			EnsureSafeStagePath();
			Directory.Delete(StagingDirectory, recursive: true);
		}
		return Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed) { return; }
		if (!_committed && Directory.Exists(StagingDirectory)) { await RollbackAsync().ConfigureAwait(false); }
		_disposed = true;
	}

	private void ThrowIfDisposed()
	{
		if (_disposed) { throw new ObjectDisposedException(nameof(InstallTransaction)); }
	}

	private void EnsureSafeStagePath()
	{
		var finalParent = Path.GetDirectoryName(Path.GetFullPath(FinalDirectory))!;
		var stageParent = Path.GetDirectoryName(Path.GetFullPath(StagingDirectory))!;
		if (!PathEquals(finalParent, stageParent) || !Path.GetFileName(StagingDirectory).StartsWith(".EasyRPG-Arabic.staging-", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Refusing to remove an unexpected staging directory.");
		}
		EnsureDirectoriesAreSeparate(SourceDirectory, StagingDirectory);
	}

	private static void EnsureDirectoriesAreSeparate(string source, string destination)
	{
		var sourcePath = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var destinationPath = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (PathEquals(sourcePath, destinationPath) || IsWithin(sourcePath, destinationPath) || IsWithin(destinationPath, sourcePath))
		{
			throw new InvalidOperationException("The managed destination and its staging folder must be separate from the vanilla source.");
		}
	}

	private static bool IsWithin(string path, string parent)
	{
		var relative = Path.GetRelativePath(parent, path);
		return !Path.IsPathRooted(relative) && relative != "." && relative != ".." &&
			!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	}

	private static bool PathEquals(string left, string right) =>
		string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
