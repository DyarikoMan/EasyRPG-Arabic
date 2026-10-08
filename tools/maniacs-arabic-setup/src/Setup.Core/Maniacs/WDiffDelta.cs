using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace EasyRpgArabic.Setup.Core.Maniacs;

public sealed record WDiffExpectation(
	string TargetFileName,
	int Version,
	long InputSize,
	string InputSha256,
	long OutputSize,
	string OutputSha256,
	uint InputCrc32,
	uint OutputCrc32,
	DateTime? OutputLastWriteTimeUtc = null);

public sealed record WDiffHeader(
	int Version,
	ushort Flags,
	uint InputCrc32,
	uint OutputCrc32,
	uint InputSize,
	uint OutputSize,
	DateTime? OutputLastWriteTimeUtc,
	string TargetFileName,
	int OperationsOffset);

/// <summary>Reads and applies the compact copy/literal operation stream used by WDiff 1.04.</summary>
public static class WDiffDelta
{
	private const int MaximumVersion = 104;
	private const int MaximumOutputBytes = 512 * 1024 * 1024;

	public static byte[] ExtractFromPeOverlay(ReadOnlySpan<byte> patchExecutable)
	{
		if (patchExecutable.Length < 64 || patchExecutable[0] != (byte)'M' || patchExecutable[1] != (byte)'Z')
		{
			throw new InvalidDataException("The supplied patch is not a valid PE executable.");
		}
		var peOffset = ReadInt32(patchExecutable, 0x3c, "PE header offset");
		if (peOffset < 64 || peOffset > patchExecutable.Length - 24 || !patchExecutable.Slice(peOffset, 4).SequenceEqual("PE\0\0"u8))
		{
			throw new InvalidDataException("The supplied patch has an invalid PE header.");
		}
		var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(patchExecutable.Slice(peOffset + 6, 2));
		var optionalHeaderSize = BinaryPrimitives.ReadUInt16LittleEndian(patchExecutable.Slice(peOffset + 20, 2));
		var sectionTable = checked(peOffset + 24 + optionalHeaderSize);
		if (sectionCount == 0 || sectionTable > patchExecutable.Length || sectionCount > (patchExecutable.Length - sectionTable) / 40)
		{
			throw new InvalidDataException("The supplied patch has a truncated PE section table.");
		}
		long overlayStart = 0;
		for (var index = 0; index < sectionCount; index++)
		{
			var section = patchExecutable.Slice(sectionTable + index * 40, 40);
			var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(section.Slice(16, 4));
			var rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(section.Slice(20, 4));
			var end = checked((long)rawOffset + rawSize);
			if (end > patchExecutable.Length) { throw new InvalidDataException("A PE section extends beyond the supplied patch file."); }
			overlayStart = Math.Max(overlayStart, end);
		}
		if (overlayStart >= patchExecutable.Length) { throw new InvalidDataException("The patch executable contains no WDiff overlay."); }
		return patchExecutable[(int)overlayStart..].ToArray();
	}

	public static WDiffHeader ReadHeader(ReadOnlySpan<byte> stream)
	{
		if (stream.Length < 8 || !stream[..4].SequenceEqual("Diff"u8)) { throw new InvalidDataException("Invalid WDiff signature."); }
	var version = stream[4] * 100 + stream[5];
		if (version is < 100 or > MaximumVersion) { throw new InvalidDataException($"Unsupported WDiff version: {version}."); }
		var flags = BinaryPrimitives.ReadUInt16LittleEndian(stream.Slice(6, 2));
		if (flags != 0) { throw new InvalidDataException($"Unsupported WDiff flags: 0x{flags:X4}."); }
		if (stream.Length < 31) { throw new InvalidDataException("Truncated WDiff fixed header."); }
		var inputCrc = BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(8, 4));
		var outputCrc = BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(12, 4));
		var inputSize = BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(16, 4));
		var outputSize = BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(20, 4));
		var year = BinaryPrimitives.ReadUInt16LittleEndian(stream.Slice(24, 2));
		var month = stream[26];
		var day = stream[27];
		var hour = stream[28];
		var minute = stream[29];
		var second = stream[30];
		DateTime? timestamp = null;
		if (year != 0 || month != 0 || day != 0 || hour != 0 || minute != 0 || second != 0)
		{
			try { timestamp = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc); }
			catch (ArgumentOutOfRangeException exception) { throw new InvalidDataException("Invalid WDiff output timestamp.", exception); }
		}

		var offset = 31;
		var targetBytes = ReadCString(stream, ref offset, "target filename");
		if (targetBytes.Length == 0) { throw new InvalidDataException("WDiff target filename must be nonempty ASCII."); }
		foreach (var value in targetBytes) { if (value > 0x7f) { throw new InvalidDataException("WDiff target filename must be nonempty ASCII."); } }
		var target = Encoding.ASCII.GetString(targetBytes);
		if (Path.IsPathRooted(target) || target.Contains('/') || target.Contains('\\') || target is "." or "..")
		{
			throw new InvalidDataException("WDiff target filename must be a single safe filename.");
		}
		_ = ReadCString(stream, ref offset, "description");
		if (version > 100) { _ = ReadCString(stream, ref offset, "version extension"); }
		return new WDiffHeader(version, flags, inputCrc, outputCrc, inputSize, outputSize, timestamp, target, offset);
	}

	public static byte[] Apply(ReadOnlySpan<byte> stream, ReadOnlySpan<byte> input, WDiffExpectation expected)
	{
		ArgumentNullException.ThrowIfNull(expected);
		var header = ReadHeader(stream);
		if (!string.Equals(header.TargetFileName, expected.TargetFileName, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException($"WDiff targets '{header.TargetFileName}', expected '{expected.TargetFileName}'.");
		}
		if (header.Version != expected.Version) { throw new InvalidDataException($"WDiff version {header.Version} does not match recipe version {expected.Version}."); }
		if (header.InputSize != expected.InputSize || input.Length != expected.InputSize)
		{
			throw new InvalidDataException("WDiff input size does not match the validated recipe and source executable.");
		}
		if (header.OutputSize != expected.OutputSize) { throw new InvalidDataException("WDiff output size does not match the validated recipe."); }
		if (header.InputCrc32 != expected.InputCrc32 || header.OutputCrc32 != expected.OutputCrc32)
		{
			throw new InvalidDataException("WDiff CRC metadata does not match the validated recipe.");
		}
		if (expected.OutputLastWriteTimeUtc is { } timestamp && header.OutputLastWriteTimeUtc != DateTime.SpecifyKind(timestamp, DateTimeKind.Utc))
		{
			throw new InvalidDataException("WDiff output timestamp does not match the validated recipe.");
		}
		if (expected.OutputSize <= 0 || expected.OutputSize > MaximumOutputBytes || expected.OutputSize > int.MaxValue)
		{
			throw new InvalidDataException("WDiff output size is outside the supported safety limit.");
		}
		if (!HashMatches(input, expected.InputSha256)) { throw new InvalidDataException("The source executable SHA-256 does not match the validated recipe."); }
		if (ComputeRawCrc32(input) != header.InputCrc32) { throw new InvalidDataException("The source executable CRC does not match the WDiff header."); }

		var output = new byte[(int)expected.OutputSize];
		var inputOffset = header.OperationsOffset;
		var outputOffset = 0;
		while (outputOffset < output.Length)
		{
			if (inputOffset > stream.Length - 2) { throw new InvalidDataException("Truncated WDiff operation stream."); }
			var operation = BinaryPrimitives.ReadUInt16LittleEndian(stream.Slice(inputOffset, 2));
			inputOffset += 2;
			var copy = (operation & 0x8000) != 0;
			var length = operation & 0x7fff;
			if (length == 0) { throw new InvalidDataException("Invalid zero-length WDiff operation before output completion."); }
			if (length > output.Length - outputOffset) { throw new InvalidDataException("WDiff operation exceeds the declared output size."); }
			if (copy)
			{
				if (inputOffset > stream.Length - 4) { throw new InvalidDataException("Truncated WDiff copy operation."); }
				var sourceOffset = BinaryPrimitives.ReadUInt32LittleEndian(stream.Slice(inputOffset, 4));
				inputOffset += 4;
				if (sourceOffset > input.Length || length > input.Length - sourceOffset)
				{
					throw new InvalidDataException("WDiff copy operation reads beyond the source executable.");
				}
				input.Slice((int)sourceOffset, length).CopyTo(output.AsSpan(outputOffset, length));
			}
			else
			{
				if (inputOffset > stream.Length - length) { throw new InvalidDataException("Truncated WDiff literal operation."); }
				stream.Slice(inputOffset, length).CopyTo(output.AsSpan(outputOffset, length));
				inputOffset += length;
			}
			outputOffset += length;
		}

		if (inputOffset != stream.Length)
		{
			if (stream.Length - inputOffset != 6 || BinaryPrimitives.ReadUInt16LittleEndian(stream.Slice(inputOffset, 2)) != 0x8000)
			{
				throw new InvalidDataException("Unexpected trailing bytes after the complete WDiff operation stream.");
			}
			inputOffset += 6;
		}
		if (inputOffset != stream.Length) { throw new InvalidDataException("Malformed WDiff terminator."); }
		if (ComputeRawCrc32(output) != header.OutputCrc32) { throw new InvalidDataException("WDiff output CRC does not match the header."); }
		if (!HashMatches(output, expected.OutputSha256)) { throw new InvalidDataException("WDiff output SHA-256 does not match the validated recipe."); }
		return output;
	}

	public static uint ComputeRawCrc32(ReadOnlySpan<byte> bytes)
	{
		uint crc = 0;
		foreach (var value in bytes)
		{
			crc ^= value;
			for (var bit = 0; bit < 8; bit++) { crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; }
		}
		return crc;
	}

	private static bool HashMatches(ReadOnlySpan<byte> bytes, string expected) =>
		expected.Length == 64 && Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase);

	private static int ReadInt32(ReadOnlySpan<byte> data, int offset, string field)
	{
		if (offset < 0 || offset > data.Length - 4) { throw new InvalidDataException($"Truncated PE {field}."); }
		return BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
	}

	private static ReadOnlySpan<byte> ReadCString(ReadOnlySpan<byte> data, ref int offset, string field)
	{
		if ((uint)offset > (uint)data.Length) { throw new InvalidDataException($"Truncated WDiff {field}."); }
		var remaining = data[offset..];
		var length = remaining.IndexOf((byte)0);
		if (length < 0) { throw new InvalidDataException($"Unterminated WDiff {field}."); }
		var value = remaining[..length];
		offset = checked(offset + length + 1);
		return value;
	}
}
