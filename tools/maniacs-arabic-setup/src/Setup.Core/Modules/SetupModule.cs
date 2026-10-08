namespace EasyRpgArabic.Setup.Core.Modules;

public enum ModuleReadiness
{
	Available,
	NotImplemented,
	Unavailable
}

public sealed record SetupModuleDescriptor(
	string Id,
	string Name,
	string Version,
	IReadOnlyList<string> Dependencies,
	IReadOnlyList<string> Prerequisites,
	ModuleReadiness Readiness,
	string StatusMessage,
	string? Architecture = null);

public sealed record SetupModuleContext(
	string VanillaDirectory,
	string StagingDirectory,
	string FinalDirectory,
	string? ManiacsPackageDirectory = null,
	bool ManiacsLicenseAccepted = false);

public sealed record ModuleOwnedFile(string RelativePath, string Sha256);

public sealed record ModuleApplyResult(IReadOnlyList<ModuleOwnedFile> OwnedFiles, string? VersionOverride = null);

public sealed record ModuleUninstallResult(IReadOnlyList<string> RemovedFiles, IReadOnlyList<string> PreservedFiles);

public sealed record ModuleVerificationResult(bool Succeeded, string Message)
{
	public static ModuleVerificationResult Success(string message = "Verified.") => new(true, message);
	public static ModuleVerificationResult Failure(string message) => new(false, message);
}

public interface ISetupModule
{
	SetupModuleDescriptor Descriptor { get; }
	Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default);
	Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default);
	Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default);
	Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default);
}

public sealed class UnavailableSetupModule : ISetupModule
{
	public SetupModuleDescriptor Descriptor { get; }

	public UnavailableSetupModule(SetupModuleDescriptor descriptor)
	{
		if (descriptor.Readiness == ModuleReadiness.Available)
		{
			throw new ArgumentException("An unavailable module cannot be marked available.", nameof(descriptor));
		}

		Descriptor = descriptor;
	}

	public Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default) =>
		Task.FromException<ModuleApplyResult>(new InvalidOperationException($"{Descriptor.Name} is unavailable: {Descriptor.StatusMessage}"));

	public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) =>
		Task.FromResult(ModuleVerificationResult.Failure($"{Descriptor.Name} is unavailable."));

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) =>
		Task.FromException<ModuleUninstallResult>(new InvalidOperationException($"{Descriptor.Name} uninstall is unavailable."));
}
