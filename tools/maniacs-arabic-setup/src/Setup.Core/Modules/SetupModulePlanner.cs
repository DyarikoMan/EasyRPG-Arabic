namespace EasyRpgArabic.Setup.Core.Modules;

public sealed record SetupPlan(IReadOnlyList<ISetupModule> OrderedModules);

public sealed class SetupModulePlanException : Exception
{
	public IReadOnlyList<string> Blockers { get; }

	public SetupModulePlanException(string message, IReadOnlyList<string>? blockers = null) : base(message)
	{
		Blockers = blockers ?? Array.Empty<string>();
	}
}

public static class SetupModulePlanner
{
	public static SetupPlan CreatePlan(IEnumerable<string> selectedModuleIds, IEnumerable<ISetupModule> modules)
	{
		var moduleMap = new Dictionary<string, ISetupModule>(StringComparer.OrdinalIgnoreCase);
		foreach (var module in modules)
		{
			if (!moduleMap.TryAdd(module.Descriptor.Id, module))
			{
				throw new SetupModulePlanException($"Duplicate module ID: {module.Descriptor.Id}.");
			}
		}

		var selected = selectedModuleIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (selected.Length == 0)
		{
			throw new SetupModulePlanException("Select at least one setup module.");
		}

		var state = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
		var ordered = new List<ISetupModule>();
		void Visit(string id)
		{
			if (!moduleMap.TryGetValue(id, out var module))
			{
				throw new SetupModulePlanException($"Required module is not registered: {id}.");
			}

			if (state.TryGetValue(id, out var current))
			{
				if (current == 1)
				{
					throw new SetupModulePlanException($"Module dependency cycle detected at '{id}'.");
				}
				if (current == 2)
				{
					return;
				}
			}

			state[id] = 1;
			foreach (var dependency in module.Descriptor.Dependencies)
			{
				Visit(dependency);
			}
			state[id] = 2;
			ordered.Add(module);
		}

		foreach (var id in selected)
		{
			Visit(id);
		}

		var blockers = ordered
			.Where(module => module.Descriptor.Readiness != ModuleReadiness.Available)
			.Select(module => $"{module.Descriptor.Name}: {module.Descriptor.StatusMessage}")
			.ToArray();
		if (blockers.Length != 0)
		{
			throw new SetupModulePlanException("The selected setup includes modules that are not ready:", blockers);
		}

		return new SetupPlan(ordered);
	}
}
