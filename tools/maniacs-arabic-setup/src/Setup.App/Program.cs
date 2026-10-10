using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Modules;

namespace EasyRpgArabic.Setup.App;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		ApplicationConfiguration.Initialize();
		var modules = RecommendedSetupModules.Create(OpenArabicEditorPackage, OpenEasyRpgArabicPlayer, EasyRpgArabicPlayerPayload.LoadEmbedded());
		var detector = new SteamRpgMaker2003Detector(new SteamRegistryLibrarySource());
		Func<Stream>? openEmbeddedManiacsPackage = null;
#if EMBEDDED_VALIDATED_241029
		openEmbeddedManiacsPackage = OpenEmbeddedManiacsPackage;
#endif
		Application.Run(new MainForm(detector, modules, GetDefaultManiacsPackageSource(), openEmbeddedManiacsPackage));
	}

	private static ManiacsPackageSourceMode GetDefaultManiacsPackageSource()
	{
#if EMBEDDED_VALIDATED_241029
		return ManiacsPackageSourceMode.EmbeddedValidated241029;
#else
		return ManiacsPackageSourceMode.UserSuppliedValidatedPackage;
#endif
	}

	private static Stream OpenArabicEditorPackage() =>
		typeof(Program).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.ArabicEditor.zip")
		?? throw new FileNotFoundException("The Arabic Editor release ZIP is not embedded in this setup executable.");

	private static Stream OpenEasyRpgArabicPlayer() =>
		typeof(Program).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.EasyRpgArabic.Player.exe")
		?? throw new FileNotFoundException("The EasyRPG-Arabic Player payload is not embedded in this setup executable.");

#if EMBEDDED_VALIDATED_241029
	private static Stream OpenEmbeddedManiacsPackage() =>
		typeof(Program).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.Maniacs241029.zip")
			?? throw new FileNotFoundException("The validated Maniacs 241029 payload is not embedded in this private setup executable.");
#endif
}
