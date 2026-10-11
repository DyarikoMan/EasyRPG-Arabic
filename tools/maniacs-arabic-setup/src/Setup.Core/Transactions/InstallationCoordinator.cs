using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Modules;
using System.Security.Cryptography;
using System.Text;

namespace EasyRpgArabic.Setup.Core.Transactions;

public sealed record SetupInstallResult(string InstallationDirectory, string? PreviousGenerationBackup, SetupState State);

public sealed record InstallationProgress(string Stage, int Percent, bool Failed = false);

public sealed class SetupInstallException : Exception
{
	public IReadOnlyList<string> RollbackFailures { get; }

	public SetupInstallException(string message, Exception innerException, IReadOnlyList<string> rollbackFailures)
		: base(message, innerException)
	{
		RollbackFailures = rollbackFailures;
	}
}

public sealed class InstallationCoordinator
{
	public async Task<SetupInstallResult> InstallAsync(
		string sourceDirectory,
		string destinationDirectory,
		SetupPlan plan,
		bool replaceExisting,
		CancellationToken cancellationToken = default) =>
		await InstallAsync(sourceDirectory, destinationDirectory, plan, replaceExisting, null, false, cancellationToken).ConfigureAwait(false);

	public async Task<SetupInstallResult> InstallAsync(
		string sourceDirectory,
		string destinationDirectory,
		SetupPlan plan,
		bool replaceExisting,
		string? maniacsPackageDirectory,
		bool maniacsLicenseAccepted,
		CancellationToken cancellationToken = default,
		IProgress<InstallationProgress>? progress = null,
		NewProjectDefaults? projectDefaults = null)
	{
		var lastPercent = 0;
		void Report(string stage, int percent, bool failed = false)
		{
			lastPercent = Math.Clamp(percent, 0, 100);
			progress?.Report(new InstallationProgress(stage, lastPercent, failed));
		}

		Report("Preparing installation...", 1);
		var validation = RpgMaker2003InstallationValidator.Validate(sourceDirectory);
		if (!validation.IsValid) { throw new InvalidOperationException(validation.Message); }
		var fullDestination = Path.GetFullPath(destinationDirectory);
		SetupState? previousState = null;
		var priorStatePath = Path.Combine(fullDestination, SetupStateStore.StateRelativePath);
		if (File.Exists(priorStatePath))
		{
			previousState = await SetupStateStore.ReadAsync(fullDestination, cancellationToken).ConfigureAwait(false);
			if (!string.Equals(Path.GetFullPath(previousState.InstallPath), fullDestination, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The existing managed installation state points to a different installation path.");
			}
		}

		await using var transaction = await InstallTransaction.CreateAsync(sourceDirectory, destinationDirectory, cancellationToken).ConfigureAwait(false);
		Report("Preparing installation...", 8);
		var moduleCompletePercent = 8;
		var context = new SetupModuleContext(transaction.SourceDirectory, transaction.StagingDirectory, transaction.FinalDirectory,
			maniacsPackageDirectory, maniacsLicenseAccepted, stage => Report(stage, moduleCompletePercent),
			(projectDefaults ?? NewProjectDefaults.AllFalse).Normalize());
		var applied = new List<(ISetupModule Module, ModuleApplyResult Result)>();
		try
		{
			for (var index = 0; index < plan.OrderedModules.Count; index++)
			{
				var module = plan.OrderedModules[index];
				cancellationToken.ThrowIfCancellationRequested();
				var targetPercent = 8 + (82 * (index + 1) / Math.Max(1, plan.OrderedModules.Count));
				Report(GetModuleStage(module.Descriptor.Id), moduleCompletePercent);
				var result = await module.ApplyAsync(context, cancellationToken).ConfigureAwait(false);
				applied.Add((module, result));
				var verification = await module.VerifyAsync(context, result, cancellationToken).ConfigureAwait(false);
				if (!verification.Succeeded)
				{
					throw new InvalidOperationException($"{module.Descriptor.Name} verification failed: {verification.Message}");
				}
				moduleCompletePercent = targetPercent;
				Report(GetModuleStage(module.Descriptor.Id), moduleCompletePercent);
			}

			Report("Finalizing installation...", 94);
			var executableHash = await ComputeSha256Async(Path.Combine(sourceDirectory, "rpg2003.exe"), cancellationToken).ConfigureAwait(false);
			var state = new SetupState
			{
				InstallId = previousState?.InstallId ?? Guid.NewGuid().ToString("D"),
				Generation = (previousState?.Generation ?? 0) + 1,
				CreatedUtc = previousState?.CreatedUtc ?? DateTimeOffset.UtcNow,
				UpdatedUtc = DateTimeOffset.UtcNow,
				SourcePath = Path.GetFullPath(sourceDirectory),
				SourceExecutableSha256 = executableHash,
				InstallPath = Path.GetFullPath(destinationDirectory),
				Modules = applied.Select(item => new InstalledModuleState(
					item.Module.Descriptor.Id,
					item.Result.VersionOverride ?? item.Module.Descriptor.Version,
					ComputeModuleFingerprint(item.Module.Descriptor, item.Result),
					item.Result.OwnedFiles,
					item.Module.Descriptor.Architecture)).ToArray()
			};
			await SetupStateStore.WriteAsync(transaction.StagingDirectory, state, cancellationToken).ConfigureAwait(false);
			await transaction.CommitAsync(replaceExisting, cancellationToken).ConfigureAwait(false);
			Report("Installation complete.", 100);
			return new SetupInstallResult(transaction.FinalDirectory, transaction.PreviousGenerationBackup, state);
		}
		catch (Exception exception)
		{
			Report("Setup failed; rolling back...", lastPercent, failed: true);
			var rollbackFailures = new List<string>();
			foreach (var item in applied.AsEnumerable().Reverse())
			{
				try { await item.Module.RollbackAsync(context, item.Result, CancellationToken.None).ConfigureAwait(false); }
				catch (Exception rollbackException) { rollbackFailures.Add($"{item.Module.Descriptor.Name}: {rollbackException.Message}"); }
			}
			try { await transaction.RollbackAsync().ConfigureAwait(false); }
			catch (Exception rollbackException) { rollbackFailures.Add($"Staging cleanup: {rollbackException.Message}"); }
			throw new SetupInstallException($"Installation failed: {exception.Message}", exception, rollbackFailures);
		}
	}

	private static string GetModuleStage(string moduleId) => moduleId switch
	{
		"maniacs" => "Installing Maniacs...",
		"maniacs-english" => "Installing Maniacs English...",
		"arabic-editor" => "Installing Arabic Editor support...",
		"easyrpg-arabic" => "Installing EasyRPG-Arabic...",
		_ => "Installing " + moduleId + "..."
	};

	private static string ComputeModuleFingerprint(SetupModuleDescriptor descriptor, ModuleApplyResult result)
	{
		var content = new StringBuilder()
			.Append(descriptor.Id).Append('\n')
			.Append(result.VersionOverride ?? descriptor.Version).Append('\n')
			.Append(descriptor.Architecture ?? string.Empty).Append('\n')
			.AppendJoin(',', descriptor.Dependencies.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).Append('\n');
		foreach (var file in result.OwnedFiles.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase))
		{
			content.Append(file.RelativePath.Replace('\\', '/')).Append(':').Append(file.Sha256.ToUpperInvariant()).Append('\n');
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString())));
	}

	private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
	{
		await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
		var hash = await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
		return Convert.ToHexString(hash);
	}
}
