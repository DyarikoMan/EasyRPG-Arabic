using EasyRpgArabic.Setup.Core.Detection;
using EasyRpgArabic.Setup.Core.Modules;

namespace EasyRpgArabic.Setup.App;

internal static class Program
{
	[STAThread]
	private static void Main()
	{
		ApplicationConfiguration.Initialize();
		var modules = RecommendedSetupModules.Create(OpenArabicEditorPackage);
		var detector = new SteamRpgMaker2003Detector(new SteamRegistryLibrarySource());
		Application.Run(new MainForm(detector, modules));
	}

	private static Stream OpenArabicEditorPackage() =>
		typeof(Program).Assembly.GetManifestResourceStream("EasyRpgArabic.Setup.ArabicEditor.zip")
		?? throw new FileNotFoundException("The Arabic Editor release ZIP is not embedded in this setup executable.");
}
