namespace EasyRpgArabic.Setup.Core.Transactions;

public sealed class InstallTransaction : IAsyncDisposable
{
	private bool _committed;
	private bool _disposed;
	private readonly bool _destinationIsArabicChild;
	private readonly bool _destinationExistedAtStart;
	private readonly bool _createdDestination;

	public string SourceDirectory { get; }
	public string FinalDirectory { get; }
	public string StagingDirectory { get; }
	public string? PreviousGenerationBackup { get; private set; }

	private InstallTransaction(string sourceDirectory, string finalDirectory, string stagingDirectory,
		bool destinationIsArabicChild, bool destinationExistedAtStart, bool createdDestination)
	{
		SourceDirectory = sourceDirectory;
		FinalDirectory = finalDirectory;
		StagingDirectory = stagingDirectory;
		_destinationIsArabicChild = destinationIsArabicChild;
		_destinationExistedAtStart = destinationExistedAtStart;
		_createdDestination = createdDestination;
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
		var destinationIsArabicChild = IsArabicChildOfSource(source, destination);
		EnsureDirectoriesAreSeparate(source, destination, destinationIsArabicChild);
		if (File.Exists(destination)) { throw new IOException("The destination path is a file."); }
		var destinationExisted = Directory.Exists(destination);
		if (destinationIsArabicChild && destinationExisted) { EnsureNoReparsePoints(destination); }
		var parent = destinationIsArabicChild ? destination : Path.GetDirectoryName(destination);
		if (string.IsNullOrWhiteSpace(parent)) { throw new InvalidOperationException("The destination must have a parent folder."); }
		Directory.CreateDirectory(parent);
		var createdDestination = destinationIsArabicChild && !destinationExisted;
		if (destinationIsArabicChild) { Directory.CreateDirectory(destination); }
		var stage = Path.Combine(parent, ".EasyRPG-Arabic.staging-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stage);
		return Task.FromResult(new InstallTransaction(source, destination, stage,
			destinationIsArabicChild, destinationExisted, createdDestination));
	}

	public Task CommitAsync(bool replaceExisting, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		ThrowIfDisposed();
		if (_committed) { throw new InvalidOperationException("This installation transaction has already been committed."); }
		if (!Directory.Exists(StagingDirectory)) { throw new DirectoryNotFoundException("The staging directory no longer exists."); }

		if (File.Exists(FinalDirectory)) { throw new IOException("The destination path is a file."); }
		if (_destinationIsArabicChild)
		{
			if (!Directory.Exists(FinalDirectory)) { throw new DirectoryNotFoundException("The managed Arabic destination disappeared during installation."); }
			if (_destinationExistedAtStart && !replaceExisting)
			{
				throw new IOException("The destination already exists. Explicit replacement confirmation is required.");
			}
			CommitArabicChildDirectory();
			_committed = true;
			return Task.CompletedTask;
		}
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
		if (_createdDestination && Directory.Exists(FinalDirectory) && !Directory.EnumerateFileSystemEntries(FinalDirectory).Any())
		{
			Directory.Delete(FinalDirectory);
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
		var expectedStageParent = _destinationIsArabicChild
			? Path.GetFullPath(FinalDirectory)
			: Path.GetDirectoryName(Path.GetFullPath(FinalDirectory))!;
		var stageParent = Path.GetDirectoryName(Path.GetFullPath(StagingDirectory))!;
		if (!PathEquals(expectedStageParent, stageParent) || !Path.GetFileName(StagingDirectory).StartsWith(".EasyRPG-Arabic.staging-", StringComparison.Ordinal))
		{
			throw new InvalidOperationException("Refusing to remove an unexpected staging directory.");
		}
		if (_destinationIsArabicChild)
		{
			if (!IsWithin(Path.GetFullPath(StagingDirectory), Path.GetFullPath(SourceDirectory)) ||
				!PathEquals(Path.GetDirectoryName(Path.GetFullPath(StagingDirectory))!, Path.GetFullPath(FinalDirectory)))
			{
				throw new InvalidOperationException("The nested staging directory is outside the managed Arabic destination.");
			}
		}
		else { EnsureDirectoriesAreSeparate(SourceDirectory, StagingDirectory, false); }
	}

	private void CommitArabicChildDirectory()
	{
		EnsureSafeStagePath();
		EnsureNoReparsePoints(FinalDirectory, excludeDirectory: StagingDirectory);
		var previousGeneration = Path.Combine(StagingDirectory, ".previous-generation");
		Directory.CreateDirectory(previousGeneration);
		var movedOld = new List<string>();
		var movedNew = new List<string>();
		try
		{
			foreach (var existing in Directory.EnumerateFileSystemEntries(FinalDirectory).ToArray())
			{
				if (PathEquals(existing, StagingDirectory)) { continue; }
				var backup = Path.Combine(previousGeneration, Path.GetFileName(existing));
				MoveEntry(existing, backup);
				movedOld.Add(backup);
			}

			foreach (var staged in Directory.EnumerateFileSystemEntries(StagingDirectory).ToArray())
			{
				if (PathEquals(staged, previousGeneration)) { continue; }
				var target = Path.Combine(FinalDirectory, Path.GetFileName(staged));
				MoveEntry(staged, target);
				movedNew.Add(target);
			}
		}
		catch
		{
			foreach (var installed in movedNew.AsEnumerable().Reverse())
			{
				var stagedPath = Path.Combine(StagingDirectory, Path.GetFileName(installed));
				if (EntryExists(installed) && !EntryExists(stagedPath)) { MoveEntry(installed, stagedPath); }
			}
			foreach (var backup in movedOld.AsEnumerable().Reverse())
			{
				var original = Path.Combine(FinalDirectory, Path.GetFileName(backup));
				if (EntryExists(backup) && !EntryExists(original)) { MoveEntry(backup, original); }
			}
			throw;
		}

		// The prior contents stay recoverable until every new top-level item is in place.
		// Cleanup is best effort after the replacement is committed: a locked old file
		// must not turn a valid new installation into a reported failure.
		try
		{
			Directory.Delete(previousGeneration, recursive: true);
			Directory.Delete(StagingDirectory, recursive: false);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
		{
			PreviousGenerationBackup = EntryExists(previousGeneration) ? previousGeneration : StagingDirectory;
		}
	}

	private static void MoveEntry(string source, string destination)
	{
		if (Directory.Exists(source)) { Directory.Move(source, destination); }
		else { File.Move(source, destination); }
	}

	private static bool EntryExists(string path) => File.Exists(path) || Directory.Exists(path);

	private static void EnsureNoReparsePoints(string directory, string? excludeDirectory = null)
	{
		var pending = new Stack<string>();
		pending.Push(directory);
		while (pending.Count != 0)
		{
			var current = pending.Pop();
			foreach (var entry in Directory.EnumerateFileSystemEntries(current))
			{
				if (excludeDirectory is not null && PathEquals(entry, excludeDirectory)) { continue; }
				if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
				{
					throw new IOException($"Refusing to replace a managed destination containing a link/reparse point: {entry}");
				}
				if (Directory.Exists(entry)) { pending.Push(entry); }
			}
		}
	}

	private static void EnsureDirectoriesAreSeparate(string source, string destination, bool allowArabicChild)
	{
		var sourcePath = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var destinationPath = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (PathEquals(sourcePath, destinationPath) || IsWithin(sourcePath, destinationPath) ||
			(IsWithin(destinationPath, sourcePath) && !(allowArabicChild && IsArabicChildOfSource(sourcePath, destinationPath))))
		{
			throw new InvalidOperationException("The managed destination must be separate from the vanilla source, except for its dedicated Arabic child folder.");
		}
	}

	private static bool IsArabicChildOfSource(string source, string destination)
	{
		var fullSource = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var fullDestination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return string.Equals(Path.GetFileName(fullDestination), "Arabic", StringComparison.OrdinalIgnoreCase) &&
			PathEquals(Path.GetDirectoryName(fullDestination)!, fullSource);
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
