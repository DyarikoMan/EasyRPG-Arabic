using System.Text;

namespace EasyRpgArabic.Setup.Core.Modules;

/// <summary>Optional movement defaults that Setup writes only into projects created later.</summary>
public sealed record NewProjectDefaults(
	bool EightDirectionMovement = false,
	bool PixelMovement = false,
	bool RunWithShift = false,
	bool SmoothCamera = false)
{
	public static NewProjectDefaults AllFalse { get; } = new();

	public NewProjectDefaults Normalize() => PixelMovement
		? this
		: this with { RunWithShift = false, SmoothCamera = false };

	public static NewProjectDefaults FromIniOptions(
		bool eightDirectionMovement,
		bool pixelMovement,
		bool runWithShift,
		bool smoothCamera) =>
		new NewProjectDefaults(eightDirectionMovement, pixelMovement, runWithShift, smoothCamera).Normalize();

	public byte[] ToIniBytes()
	{
		var value = Normalize();
		var text = "[EasyRPG]\r\n"
			+ $"EightDirectionMovement={ToIniBoolean(value.EightDirectionMovement)}\r\n"
			+ $"PixelMovement={ToIniBoolean(value.PixelMovement)}\r\n"
			+ $"RunWithShift={ToIniBoolean(value.RunWithShift)}\r\n"
			+ $"SmoothCamera={ToIniBoolean(value.SmoothCamera)}\r\n";
		return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
	}

	private static string ToIniBoolean(bool value) => value ? "true" : "false";
}
