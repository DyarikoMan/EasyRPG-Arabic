using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace EasyRpgArabic.Setup.Core.Detection;

public sealed record InstallationValidationResult(bool IsValid, string Message)
{
	public static InstallationValidationResult Valid() => new(true, "Valid Steam RPG Maker 2003 installation.");
	public static InstallationValidationResult Invalid(string message) => new(false, message);
}

public sealed record DetectedInstallation(string Directory, string SteamLibrary);

public static class RpgMaker2003InstallationValidator
{
	public const string SteamAppId = "362870";
	private static readonly string[] RequiredFiles =
	[
		"rpg2003.exe",
		"rpg_rt.exe.dat",
		"rpg_rt.ldb.dat",
		"ultimate_eb.dll",
		"ultimate_rt_eb.dll.dat",
		"UNLHA32.DLL"
	];

	public static InstallationValidationResult Validate(string directory)
	{
		if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
		{
			return InstallationValidationResult.Invalid("The selected folder does not exist.");
		}

		foreach (var file in RequiredFiles)
		{
			if (!File.Exists(Path.Combine(directory, file)))
			{
				return InstallationValidationResult.Invalid($"Missing RPG Maker 2003 file: {file}.");
			}
		}

		var appIdPath = Path.Combine(directory, "steam_appid.txt");
		try
		{
			if (!File.Exists(appIdPath) || !string.Equals(File.ReadAllText(appIdPath).Trim(), SteamAppId, StringComparison.Ordinal))
			{
				return InstallationValidationResult.Invalid("steam_appid.txt does not identify Steam RPG Maker 2003 (362870).");
			}
		}
		catch (IOException) { return InstallationValidationResult.Invalid("steam_appid.txt could not be read."); }
		catch (UnauthorizedAccessException) { return InstallationValidationResult.Invalid("steam_appid.txt could not be read."); }

		if (!IsX86Executable(Path.Combine(directory, "rpg2003.exe")))
		{
			return InstallationValidationResult.Invalid("rpg2003.exe is not a valid 32-bit Windows executable.");
		}

		foreach (var maniacsFile in new[] { "cmdcs.dll", "cmdTos.dll", "RPG_RT.exe" })
		{
			if (File.Exists(Path.Combine(directory, maniacsFile)))
			{
				return InstallationValidationResult.Invalid("This folder already appears to be patched with Maniacs. Select the vanilla Steam installation.");
			}
		}

		return InstallationValidationResult.Valid();
	}

	public static bool IsX86Executable(string path)
	{
		try
		{
			using var stream = File.OpenRead(path);
			using var reader = new BinaryReader(stream);
			if (stream.Length < 0x40)
			{
				return false;
			}

			if (reader.ReadUInt16() != 0x5A4D) { return false; }
			stream.Position = 0x3C;
			var peOffset = reader.ReadInt32();
			if (peOffset < 0 || peOffset + 6 > stream.Length)
			{
				return false;
			}

			stream.Position = peOffset;
			return reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x014C;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}
}

public interface ISteamLibrarySource
{
	IEnumerable<string> GetLibraryRoots();
}

public sealed class SteamRegistryLibrarySource : ISteamLibrarySource
{
	private static readonly Regex LibraryPathPattern = new("\\\"path\\\"\\s+\\\"([^\\\"]+)\\\"|\\\"[0-9]+\\\"\\s+\\\"([A-Za-z]:\\\\[^\\\"]+)\\\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

	public IEnumerable<string> GetLibraryRoots()
	{
		var roots = new List<string>();
		if (OperatingSystem.IsWindows())
		{
			AddRegistryRoot(roots, Registry.CurrentUser, @"Software\Valve\Steam");
			AddRegistryRoot(roots, Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam");
			AddRegistryRoot(roots, Registry.LocalMachine, @"SOFTWARE\Valve\Steam");
		}

		roots.Add(@"C:\Program Files (x86)\Steam");
		roots.Add(@"C:\Program Files\Steam");
		var scanRoots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		foreach (var root in scanRoots)
		{
			var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
			if (!File.Exists(vdf))
			{
				continue;
			}

			string content;
			try { content = File.ReadAllText(vdf); }
			catch (IOException) { continue; }
			foreach (Match match in LibraryPathPattern.Matches(content))
			{
				var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
				var library = value.Replace("\\\\", "\\", StringComparison.Ordinal);
				if (Path.IsPathRooted(library)) { roots.Add(library); }
			}
		}

		return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	[SupportedOSPlatform("windows")]
	private static void AddRegistryRoot(List<string> roots, RegistryKey hive, string subKey)
	{
		try
		{
			using var key = hive.OpenSubKey(subKey);
			foreach (var name in new[] { "SteamPath", "InstallPath" })
			{
				if (key?.GetValue(name) is string path && !string.IsNullOrWhiteSpace(path))
				{
					roots.Add(path.Replace('/', '\\'));
				}
			}
		}
		catch (System.Security.SecurityException) { }
		catch (IOException) { }
	}
}

public sealed class SteamRpgMaker2003Detector
{
	private readonly ISteamLibrarySource _librarySource;

	public SteamRpgMaker2003Detector(ISteamLibrarySource librarySource)
	{
		_librarySource = librarySource;
	}

	public DetectedInstallation? Detect()
	{
		foreach (var library in _librarySource.GetLibraryRoots())
		{
			var candidate = Path.Combine(library, "steamapps", "common", "RPG Maker 2003");
			if (RpgMaker2003InstallationValidator.Validate(candidate).IsValid)
			{
				return new DetectedInstallation(Path.GetFullPath(candidate), Path.GetFullPath(library));
			}
		}
		return null;
	}
}
