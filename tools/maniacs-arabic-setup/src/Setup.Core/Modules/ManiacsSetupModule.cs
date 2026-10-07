using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Modules;

/// <summary>
/// Maniacs package inspection is implemented; applying the official utility process remains gated
/// until its supported automation or guided handoff is verified.
/// </summary>
public sealed class ManiacsSetupModule : ISetupModule
{
	private readonly ManiacsPackageInspector _inspector;

	public SetupModuleDescriptor Descriptor { get; } = new(
		"maniacs",
		"Maniacs",
		"recipe-catalog",
		[],
		["A recognized official Maniacs package and a verified application process"],
		ModuleReadiness.NotImplemented,
		"Package version and layout inspection is available. Official utility installation is still gated until its safe launch/verification flow is validated.");

	public ManiacsSetupModule(ManiacsPackageInspector inspector) => _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));

	public ManiacsPackageInspection InspectPackage(string packageDirectory) => _inspector.Inspect(packageDirectory);

	public Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default) =>
		Task.FromException<ModuleApplyResult>(new InvalidOperationException("The Maniacs official utility application flow has not been validated."));

	public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) =>
		Task.FromResult(ModuleVerificationResult.Failure("The Maniacs official utility application flow has not been validated."));

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;

	public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) =>
		Task.FromException<ModuleUninstallResult>(new InvalidOperationException("Maniacs module uninstall is not implemented."));
}
