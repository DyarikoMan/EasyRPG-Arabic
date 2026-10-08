using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Modules;

/// <summary>Installs the validated English payload and applies its embedded WDiff delta in staging.</summary>
public sealed class ManiacsEnglishSetupModule : ISetupModule
{
	private readonly ManiacsPackageInspector _inspector;
	private readonly ManiacsRecipeCatalog _catalog;

	public SetupModuleDescriptor Descriptor { get; }

	public ManiacsEnglishSetupModule(ManiacsPackageInspector inspector, ManiacsRecipeCatalog catalog)
	{
		_inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
		Descriptor = new SetupModuleDescriptor(
			"maniacs-english", "Maniacs English", "recipe-driven", ["maniacs"],
			["Exact supported Maniacs package", "Maniacs license acceptance", "Validated vanilla rpg2003.exe"],
			ModuleReadiness.Available,
			"Copies the official English payload and applies its validated WDiff patch to the staged editor.");
	}

	public async Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default)
	{
		var (packageRoot, recipe) = ManiacsModuleSupport.GetValidatedPackage(context, _inspector, _catalog);
		ManiacsModuleSupport.ValidatePackageIdentity(packageRoot, recipe);
		ManiacsModuleSupport.ValidateVanillaAndStage(context, recipe);
		ManiacsModuleSupport.ValidateBasePayload(packageRoot, context.StagingDirectory, recipe, _catalog);
		var patchRecipe = recipe.EnglishPatch ?? throw new InvalidDataException($"Recipe '{recipe.Id}' has no WDiff English patch definition.");
		ValidatePluginPackageLayout(packageRoot, patchRecipe);
		var target = ManiacsModuleSupport.SafePath(context.StagingDirectory, patchRecipe.TargetFileName);
		var backup = ManiacsModuleSupport.SafePath(context.StagingDirectory, patchRecipe.BackupFileName);
		var inputHash = patchRecipe.InputSha256;
		if (!File.Exists(target) || !ManiacsModuleSupport.HashMatches(target, inputHash) || new FileInfo(target).Length != patchRecipe.InputSize)
		{
			throw new InvalidDataException("The staged editor does not match the validated vanilla input for the English patch.");
		}
		if (File.Exists(backup)) { throw new IOException($"Refusing to overwrite an existing backup: {patchRecipe.BackupFileName}"); }

		var created = new List<string>();
		var originalLastWrite = File.GetLastWriteTimeUtc(target);
		var patchWasApplied = false;
		string? temporaryTarget = null;
		try
		{
			foreach (var (packagePath, installPath) in patchRecipe.PayloadFiles)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var source = ManiacsModuleSupport.SafePath(packageRoot, packagePath);
				var expected = recipe.IdentityFileSha256[packagePath];
				if (!ManiacsModuleSupport.HashMatches(source, expected)) { throw new InvalidDataException($"English package payload hash mismatch: {packagePath}"); }
				var staged = ManiacsModuleSupport.SafePath(context.StagingDirectory, installPath);
				if (File.Exists(staged))
				{
					if (!ManiacsModuleSupport.HashMatches(staged, expected)) { throw new IOException($"Refusing to overwrite a conflicting staged file: {installPath}"); }
					continue;
				}
				created.Add(staged);
				await ManiacsModuleSupport.CopyNewFileAsync(source, staged, cancellationToken).ConfigureAwait(false);
				if (!ManiacsModuleSupport.HashMatches(staged, expected)) { throw new IOException($"Copied English payload failed hash verification: {installPath}"); }
			}

			var pluginDirectory = ManiacsModuleSupport.SafePath(context.StagingDirectory, "Plugin");
			Directory.CreateDirectory(pluginDirectory);
			foreach (var pluginFile in patchRecipe.ExpectedPluginFiles)
			{
				var packagePath = "English/Plugin/" + pluginFile;
				var installPath = "Plugin/" + pluginFile;
				var source = ManiacsModuleSupport.SafePath(packageRoot, packagePath);
				var expected = recipe.IdentityFileSha256.TryGetValue(packagePath, out var hash)
					? hash : throw new InvalidDataException($"English plugin file is not fingerprinted in the recipe: {packagePath}");
				var staged = ManiacsModuleSupport.SafePath(context.StagingDirectory, installPath);
				if (File.Exists(staged)) { throw new IOException($"Refusing to overwrite an existing staged English plugin: {installPath}"); }
				created.Add(staged);
				await ManiacsModuleSupport.CopyNewFileAsync(source, staged, cancellationToken).ConfigureAwait(false);
				if (!ManiacsModuleSupport.HashMatches(staged, expected)) { throw new IOException($"Copied English plugin failed hash verification: {installPath}"); }
			}

			var patchPath = ManiacsModuleSupport.SafePath(packageRoot, recipe.OfficialInstallProcess.EnglishPatchPath);
			var patchExecutable = await File.ReadAllBytesAsync(patchPath, cancellationToken).ConfigureAwait(false);
			var delta = WDiffDelta.ExtractFromPeOverlay(patchExecutable);
			var expectation = new WDiffExpectation(
				patchRecipe.TargetFileName,
				patchRecipe.WDiffVersion,
				patchRecipe.InputSize,
				patchRecipe.InputSha256,
				patchRecipe.OutputSize,
				patchRecipe.OutputSha256,
				Convert.ToUInt32(patchRecipe.InputCrc32, 16),
				Convert.ToUInt32(patchRecipe.OutputCrc32, 16),
				patchRecipe.OutputLastWriteTimeUtc);
			var input = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
			var output = WDiffDelta.Apply(delta, input, expectation);

			created.Add(backup);
			File.Copy(target, backup, overwrite: false);
			File.SetLastWriteTimeUtc(backup, originalLastWrite);
			temporaryTarget = ManiacsModuleSupport.SafePath(context.StagingDirectory, ".easyrpg-wdiff-" + Guid.NewGuid().ToString("N") + ".tmp");
			await File.WriteAllBytesAsync(temporaryTarget, output, cancellationToken).ConfigureAwait(false);
			if (patchRecipe.OutputLastWriteTimeUtc is { } timestamp) { File.SetLastWriteTimeUtc(temporaryTarget, DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)); }
			File.Move(temporaryTarget, target, overwrite: true);
			temporaryTarget = null;
			patchWasApplied = true;
			created.Add(target);

			foreach (var payload in patchRecipe.PayloadFiles)
			{
				var path = ManiacsModuleSupport.SafePath(context.StagingDirectory, payload.Value);
				var expected = recipe.IdentityFileSha256[payload.Key];
				var architecture = _catalog.Layouts.Single(layout => layout.Id == recipe.LayoutId).RequiredFiles
					.SingleOrDefault(file => string.Equals(file.Path, payload.Key, StringComparison.OrdinalIgnoreCase))?.Architecture;
				if (architecture is not null && !string.Equals(ManiacsModuleSupport.ReadPeArchitecture(path), architecture, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"English payload architecture mismatch: {payload.Value}");
				}
				if (!ManiacsModuleSupport.HashMatches(path, expected)) { throw new InvalidDataException($"English payload verification failed: {payload.Value}"); }
			}

			return new ModuleApplyResult(created.Distinct(StringComparer.OrdinalIgnoreCase).Select(path => new ModuleOwnedFile(
				Path.GetRelativePath(context.StagingDirectory, path).Replace('\\', '/'), ManiacsModuleSupport.HashFile(path))).ToArray(), recipe.Id);
		}
		catch
		{
			if (temporaryTarget is not null) { try { if (File.Exists(temporaryTarget)) { File.Delete(temporaryTarget); } } catch { } }
			if (patchWasApplied && File.Exists(backup))
			{
				try { File.Copy(backup, target, overwrite: true); File.SetLastWriteTimeUtc(target, originalLastWrite); } catch { }
			}
			foreach (var path in created.Where(path => !string.Equals(path, target, StringComparison.OrdinalIgnoreCase)))
			{
				try { if (File.Exists(path)) { File.Delete(path); } } catch { /* Transaction cleanup is the final rollback boundary. */ }
			}
			if (File.Exists(backup)) { try { File.Delete(backup); } catch { } }
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
			ManiacsModuleSupport.ValidateBasePayload(packageRoot, context.StagingDirectory, recipe, _catalog);
			var patchRecipe = recipe.EnglishPatch ?? throw new InvalidDataException("The recognized recipe has no English WDiff metadata.");
			ValidatePluginPackageLayout(packageRoot, patchRecipe);
			var target = ManiacsModuleSupport.SafePath(context.StagingDirectory, patchRecipe.TargetFileName);
			var backup = ManiacsModuleSupport.SafePath(context.StagingDirectory, patchRecipe.BackupFileName);
			if (!File.Exists(target) || new FileInfo(target).Length != patchRecipe.OutputSize || !ManiacsModuleSupport.HashMatches(target, patchRecipe.OutputSha256))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Patched rpg2003.exe does not match the validated output hash and size."));
			}
			if (!File.Exists(backup) || new FileInfo(backup).Length != patchRecipe.InputSize || !ManiacsModuleSupport.HashMatches(backup, patchRecipe.InputSha256))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("rpg2003.OLD does not preserve the exact validated vanilla executable."));
			}
			if (!Directory.Exists(ManiacsModuleSupport.SafePath(context.StagingDirectory, "Plugin")))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("The expected Plugin directory is missing."));
			}
			foreach (var (packagePath, installPath) in patchRecipe.PayloadFiles)
			{
				var staged = ManiacsModuleSupport.SafePath(context.StagingDirectory, installPath);
				if (!File.Exists(staged) || !ManiacsModuleSupport.HashMatches(staged, recipe.IdentityFileSha256[packagePath]))
				{
					return Task.FromResult(ModuleVerificationResult.Failure($"English payload file failed verification: {installPath}"));
				}
				var expectedArchitecture = _catalog.Layouts.Single(layout => layout.Id == recipe.LayoutId).RequiredFiles
					.Single(file => string.Equals(file.Path, packagePath, StringComparison.OrdinalIgnoreCase)).Architecture;
				if (expectedArchitecture is not null && !string.Equals(ManiacsModuleSupport.ReadPeArchitecture(staged), expectedArchitecture, StringComparison.OrdinalIgnoreCase))
				{
					return Task.FromResult(ModuleVerificationResult.Failure($"English payload architecture is invalid: {installPath}"));
				}
			}
			foreach (var pluginFile in patchRecipe.ExpectedPluginFiles)
			{
				var packagePath = "English/Plugin/" + pluginFile;
				var stagedPath = ManiacsModuleSupport.SafePath(context.StagingDirectory, "Plugin/" + pluginFile);
				if (!File.Exists(stagedPath) || !ManiacsModuleSupport.HashMatches(stagedPath, recipe.IdentityFileSha256[packagePath]))
				{
					return Task.FromResult(ModuleVerificationResult.Failure($"English plugin file failed verification: Plugin/{pluginFile}"));
				}
			}
			var layout = _catalog.Layouts.Single(candidate => string.Equals(candidate.Id, recipe.LayoutId, StringComparison.OrdinalIgnoreCase));
			var editorArchitecture = recipe.VanillaSourceArchitectures?.GetValueOrDefault(patchRecipe.TargetFileName)
				?? throw new InvalidDataException("The recipe does not declare the patched editor architecture.");
			var editorPluginRole = layout.ArchitectureRoles.TryGetValue("editorAndPlugins", out var role)
				? role : throw new InvalidDataException("The Maniacs layout does not declare its editor/plugin architecture role.");
			var editorPluginInstallPath = recipe.BasePayloadFiles?.SingleOrDefault(file =>
				string.Equals(file.PackagePath, editorPluginRole.Path, StringComparison.OrdinalIgnoreCase))?.InstallPath
				?? throw new InvalidDataException("The Maniacs recipe does not map the editor/plugin architecture file into the installation.");
			if (!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(target), editorArchitecture, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(ManiacsModuleSupport.ReadPeArchitecture(ManiacsModuleSupport.SafePath(context.StagingDirectory, editorPluginInstallPath)), editorPluginRole.Expected, StringComparison.OrdinalIgnoreCase))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Maniacs editor/plugin architecture is not x86."));
			}
			if (patchRecipe.OutputLastWriteTimeUtc is { } timestamp && File.GetLastWriteTimeUtc(target) != DateTime.SpecifyKind(timestamp, DateTimeKind.Utc))
			{
				return Task.FromResult(ModuleVerificationResult.Failure("Patched editor timestamp differs from the validated reference timestamp."));
			}
			return Task.FromResult(ModuleVerificationResult.Success("English payload, runtime architecture, WDiff backup, and patched editor match the validated recipe."));
		}
		catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or FormatException)
		{
			return Task.FromResult(ModuleVerificationResult.Failure(exception.Message));
		}
	}

	public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) => RestoreAndRemoveAsync(context, result.OwnedFiles, cancellationToken, preserveModified: false);

	public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) => RestoreAndRemoveAsync(context, ownedFiles, cancellationToken, preserveModified: true);

	private Task<ModuleUninstallResult> RestoreAndRemoveAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken, bool preserveModified)
	{
		var removed = new List<string>();
		var preserved = new List<string>();
		var recipesByPath = _catalog.Recipes.Select(recipe => recipe.EnglishPatch)
			.OfType<ManiacsEnglishPatchRecipe>()
			.Where(recipe =>
				ownedFiles.Any(file => string.Equals(file.RelativePath, recipe.TargetFileName, StringComparison.OrdinalIgnoreCase)) &&
				ownedFiles.Any(file => string.Equals(file.RelativePath, recipe.BackupFileName, StringComparison.OrdinalIgnoreCase))).ToArray();
		var patchRecipe = recipesByPath.FirstOrDefault(recipe =>
			ownedFiles.Any(file => string.Equals(file.RelativePath, recipe.TargetFileName, StringComparison.OrdinalIgnoreCase) && string.Equals(file.Sha256, recipe.OutputSha256, StringComparison.OrdinalIgnoreCase)) &&
			ownedFiles.Any(file => string.Equals(file.RelativePath, recipe.BackupFileName, StringComparison.OrdinalIgnoreCase) && string.Equals(file.Sha256, recipe.InputSha256, StringComparison.OrdinalIgnoreCase)))
			?? (recipesByPath.Length == 1 ? recipesByPath[0] : null)
			?? throw new InvalidDataException("Cannot identify the English patch recipe from its owned-file record.");
		var patched = ownedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, patchRecipe.TargetFileName, StringComparison.OrdinalIgnoreCase));
		var old = ownedFiles.SingleOrDefault(file => string.Equals(file.RelativePath, patchRecipe.BackupFileName, StringComparison.OrdinalIgnoreCase));
		if (patched is not null && old is not null)
		{
			var target = ManiacsModuleSupport.OwnedPath(context, patched.RelativePath);
			var backup = ManiacsModuleSupport.OwnedPath(context, old.RelativePath);
			if (File.Exists(target) && File.Exists(backup) && ManiacsModuleSupport.HashMatches(target, patched.Sha256) && ManiacsModuleSupport.HashMatches(backup, old.Sha256))
			{
				var originalTime = File.GetLastWriteTimeUtc(backup);
				File.Copy(backup, target, overwrite: true);
				File.SetLastWriteTimeUtc(target, originalTime);
				File.Delete(backup);
				removed.Add(old.RelativePath);
			}
			else if (preserveModified)
			{
				if (File.Exists(target)) { preserved.Add(patched.RelativePath); }
				if (File.Exists(backup)) { preserved.Add(old.RelativePath); }
			}
			else if (File.Exists(target) || File.Exists(backup))
			{
				throw new IOException("Refusing to roll back a changed English patched executable or its backup.");
			}
		}

		foreach (var owned in ownedFiles.Where(file => !string.Equals(file.RelativePath, patchRecipe.TargetFileName, StringComparison.OrdinalIgnoreCase) && !string.Equals(file.RelativePath, patchRecipe.BackupFileName, StringComparison.OrdinalIgnoreCase)))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var path = ManiacsModuleSupport.OwnedPath(context, owned.RelativePath);
			if (!File.Exists(path)) { continue; }
			if (!ManiacsModuleSupport.HashMatches(path, owned.Sha256))
			{
				if (!preserveModified) { throw new IOException($"Refusing to remove modified English module file: {owned.RelativePath}"); }
				preserved.Add(owned.RelativePath);
				continue;
			}
			File.Delete(path);
			removed.Add(owned.RelativePath);
		}
		return Task.FromResult(new ModuleUninstallResult(removed, preserved));
	}

	private static void ValidatePluginPackageLayout(string packageRoot, ManiacsEnglishPatchRecipe patchRecipe)
	{
		var directory = ManiacsModuleSupport.SafePath(packageRoot, "English/Plugin");
		var actual = ManiacsModuleSupport.EnumerateFilesWithoutLinks(directory)
			.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
		var expected = patchRecipe.ExpectedPluginFiles.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
		if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The English Plugin folder does not match the exact files in the supported recipe.");
		}
	}
}
