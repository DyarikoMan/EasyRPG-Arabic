using EasyRpgArabic.Setup.Core.Modules;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class ModulePlannerTests
{
	[Fact]
	public void OrdersDependenciesBeforeDependents()
	{
		var modules = new ISetupModule[]
		{
			Ready("arabic-editor", "maniacs-english"),
			Ready("maniacs-english", "maniacs"),
			Ready("maniacs"),
			Ready("easyrpg-arabic")
		};

		var plan = SetupModulePlanner.CreatePlan(["arabic-editor", "easyrpg-arabic"], modules);

		Assert.Equal(["maniacs", "maniacs-english", "arabic-editor", "easyrpg-arabic"], plan.OrderedModules.Select(module => module.Descriptor.Id));
	}

	[Fact]
	public void RejectsDependencyCycles()
	{
		var modules = new ISetupModule[]
		{
			Ready("first", "second"),
			Ready("second", "first")
		};

		var exception = Assert.Throws<SetupModulePlanException>(() => SetupModulePlanner.CreatePlan(["first"], modules));

		Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RejectsSelectedModulesThatAreNotImplemented()
	{
		var module = new UnavailableSetupModule(new SetupModuleDescriptor(
			"future", "Future component", "unavailable", [], [], ModuleReadiness.NotImplemented, "Not implemented yet."));

		var exception = Assert.Throws<SetupModulePlanException>(() => SetupModulePlanner.CreatePlan(["future"], [module]));

		Assert.Contains(exception.Blockers, blocker => blocker.Contains("Not implemented yet.", StringComparison.Ordinal));
	}

	private static TestModule Ready(string id, params string[] dependencies) => new(new SetupModuleDescriptor(
		id, id, "1.0", dependencies, [], ModuleReadiness.Available, "Ready."));

	private sealed class TestModule(SetupModuleDescriptor descriptor) : ISetupModule
	{
		public SetupModuleDescriptor Descriptor { get; } = descriptor;
		public Task<ModuleApplyResult> ApplyAsync(SetupModuleContext context, CancellationToken cancellationToken = default) => Task.FromResult(new ModuleApplyResult([]));
		public Task<ModuleVerificationResult> VerifyAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) => Task.FromResult(ModuleVerificationResult.Success());
		public Task RollbackAsync(SetupModuleContext context, ModuleApplyResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task<ModuleUninstallResult> UninstallAsync(SetupModuleContext context, IReadOnlyList<ModuleOwnedFile> ownedFiles, CancellationToken cancellationToken = default) => Task.FromResult(new ModuleUninstallResult([], []));
	}
}
