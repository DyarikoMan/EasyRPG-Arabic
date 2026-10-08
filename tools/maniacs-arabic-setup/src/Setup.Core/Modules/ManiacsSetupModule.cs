using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Modules;

/// <summary>Applies the validated Maniacs Common package payload to the isolated vanilla stage.</summary>
public sealed class ManiacsSetupModule : ISetupModule
{
	private readonly ManiacsPackageInspector _inspector;
	private readonly ManiacsRecipeCatalog _catalog;

	public SetupModuleDescriptor Descriptor { get; }

	public ManiacsSetupModule(ManiacsPackageInspector inspector)
		: this(inspector, ManiacsRecipeCatalog.LoadEmbedded()) { }

	public ManiacsSetupModule(ManiacsPackageInspector inspector, ManiacsRecipeCatalog catalog)
	{
		_inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		Descriptor = new SetupModuleDescriptor(
			"maniacs", "Maniacs", "recipe-driven", [],
			["Validated vanilla RPG Maker 2003 source", "Exact user-supplied Maniacs package", "Maniacs license acceptance"],
			ModuleReadiness.Available,
			"Applies the verified Common payload to a separate staged installation.");
	}

	public ManiacsPackageInspection InspectPackage(string packageDirectory) => _inspector.Inspect(packageDirectory);

	public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
	{
		var (packageRoot, recipe) = ManiacsModuleSupport.GetValidatedPackage(context, _inspector, _catalog);
		ManiacsModuleSupport.ValidatePackageIdentity(packageRoot, recipe);
		var payloadFiles = recipe.BasePayloadFiles ?? throw new InvalidDataException($"Recipe '{recipe.Id}' has no Maniacs base payload mapping.");
		var created = new List<string>();
		try
		{
			await ManiacsModuleSupport.CopyVanillaFilesToStageAsync(context, recipe, created, cancellationToken).ConfigureAwait(false);
			foreach (var payload in payloadFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var source = ManiacsModuleSupport.SafePath(packageRoot, payload.PackagePath);
				var target = ManiacsModuleSupport.SafePath(context.StagingDirectory, payload.InstallPath);
				var expected = recipe.IdentityFileSha256[payload.PackagePath];
				if (!ManiacsModuleSupport.HashMatches(source, expected)) { throw new InvalidDataException($"Package payload hash mismatch: {payload.PackagePath}"); }
				if (File.Exists(target))
				{
					if (!ManiacsModuleSupport.HashMatches(target, expected)) { throw new IOException($"Refusing to replace a conflicting staged file: {payload.InstallPath}"); }
					continue;
				}
				created.Add(target);
				await ManiacsModuleSupport.CopyNewFileAsync(source, target, cancellationToken).ConfigureAwait(false);
				if (!ManiacsModuleSupport.HashMatches(target, expected)) { throw new IOException($"Copied payload failed hash verification: {payload.InstallPath}"); }
				var expectedArchitecture = _catalog.Layouts.Single(layout => layout.Id == recipe.LayoutId).RequiredFiles
					.SingleOrDefault(file => string.Equals(file.Path, payload.PackagePath, StringComparison.OrdinalIgnoreCase))?.Architecture;
				if (expectedArchitecture is not null && !string.Equals(ManiacsModuleSupport.ReadPeArchitecture(target), expectedArchitecture, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Copied package architecture mismatch: {payload.InstallPath}");
				}
			}
			return new ModuleApplyResult(created.Select(path => new ModuleOwnedFile(
				Path.GetRelativePath(context.StagingDirectory, path).Replace('\\', '/'), ManiacsModuleSupport.HashFile(path))).ToArray(), recipe.Id);
		}
		catch
		{
			foreach (var path in created) { try { if (File.Exists(path)) { File.Delete(path); } } catch { /* Staging cleanup is the final rollback boundary. */ } }
			throw;
		}
	}

	public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			var (packageRoot, recipe) = ManiacsModuleSupport.GetValidatedPackage(context, _inspector, _catalog);
			ManiacsModuleSupport.ValidatePackageIdentity(packageRoot, recipe);
			ManiacsModuleSupport.ValidateVanillaAndStage(context, recipe);
			ManiacsModuleSupport.ValidateBasePayload(packageRoot, context.StagingDirectory, recipe, _catalog);
			return Task.FromResult(ModuleVerificationResult.Success("Vanilla editor files and all Common/ license payload hashes and architectures match the exact recipe."));
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
		{
			return Task.FromResult(ModuleVerificationResult.Failure(exception.Message));
		}
	}

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) =>
		RemoveOwnedFilesAsync(context, result.OwnedFiles, cancellationToken, preserveModified: false);

	public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) =>
		RemoveOwnedFilesAsync(context, ownedFiles, cancellationToken, preserveModified: true);

	private static Task<ModuleUninstallResult> RemoveOwnedFilesAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> files, CancellationToken cancellationToken, bool preserveModified)
	{
		var removed = new List<string>();
		var preserved = new List<string>();
		foreach (var owned in files)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var path = ManiacsModuleSupport.OwnedPath(context, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!ManiacsModuleSupport.HashMatches(path, owned.Sha256))
			{
				if (!preserveModified) { throw new IOException($"Refusing to remove modified file during rollback: {owned.RelativePath}"); }
				preserved.Add(owned.RelativePath);
				continue;
			}
			File.Delete(path);
			removed.Add(owned.RelativePath);
		}
		return Task.FromResult(new ModuleUninstallResult(removed, preserved));
	}
}
