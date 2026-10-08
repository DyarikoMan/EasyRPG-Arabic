using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Modules;

public static partial class RecommendedSetupModules
{
	public static IReadOnlyList<ISetupModule> Create(
		Func<Stream> openArabicEditorPackage,
		Func<Stream> openEasyRpgArabicPlayer,
		EasyRpgArabicPlayerPayload playerPayload)
	{
		var catalog = ManiacsRecipeCatalog.LoadEmbedded();
		var inspector = new ManiacsPackageInspector(catalog);
		playerPayload.Validate();
		var recipe = catalog.Recipes.SingleOrDefault(candidate =>
			string.Equals(candidate.BuildId, playerPayload.RequiredManiacsBuildId, StringComparison.OrdinalIgnoreCase))
			?? throw new InvalidDataException($"The Player payload requires unsupported Maniacs build '{playerPayload.RequiredManiacsBuildId}'.");
		var expectedManiacsRuntimeHash = recipe.IdentityFileSha256.TryGetValue("English/RPG_RT.exe", out var hash)
			? hash : throw new InvalidDataException($"Maniacs recipe '{recipe.Id}' has no validated English RPG_RT.exe fingerprint.");

		return
	[
		new ManiacsSetupModule(inspector, catalog),
		new ManiacsEnglishSetupModule(inspector, catalog),
		new ArabicEditorSetupModule(openArabicEditorPackage),
		new EasyRpgArabicSetupModule(openEasyRpgArabicPlayer, playerPayload, expectedManiacsRuntimeHash)
	];
	}
}
