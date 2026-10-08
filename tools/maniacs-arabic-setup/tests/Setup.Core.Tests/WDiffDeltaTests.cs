using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using EasyRpgArabic.Setup.Core.Maniacs;

namespace EasyRpgArabic.Setup.Core.Tests;

public sealed class WDiffDeltaTests
{
	private static readonly byte[] Input = Encoding.ASCII.GetBytes("abcdef");
	private static readonly byte[] Output = Encoding.ASCII.GetBytes("abXYZ");

	[Fact]
	public void AppliesCopyAndLiteralOperationsAndChecksHashes()
	{
		var stream = BuildDelta();
		var result = WDiffDelta.Apply(stream, Input, Expectation());
		Assert.Equal(Output, result);
	}

	[Fact]
	public void ExtractsDeltaFromPeOverlay()
	{
		var delta = BuildDelta();
		var executable = new byte[132 + delta.Length];
		executable[0] = (byte)'M'; executable[1] = (byte)'Z';
		BinaryPrimitives.WriteInt32LittleEndian(executable.AsSpan(0x3c), 64);
		"PE\0\0"u8.CopyTo(executable.AsSpan(64));
		BinaryPrimitives.WriteUInt16LittleEndian(executable.AsSpan(70), 1);
		BinaryPrimitives.WriteUInt32LittleEndian(executable.AsSpan(104), 4);
		BinaryPrimitives.WriteUInt32LittleEndian(executable.AsSpan(108), 128);
		Encoding.ASCII.GetBytes("stub").CopyTo(executable, 128);
		delta.CopyTo(executable, 132);

		Assert.Equal(delta, WDiffDelta.ExtractFromPeOverlay(executable));
	}

	[Fact]
	public void RejectsMalformedSignatureAndUnsupportedVersion()
	{
		var invalidSignature = BuildDelta();
		invalidSignature[0] = (byte)'X';
		Assert.Throws<InvalidDataException>(() => WDiffDelta.ReadHeader(invalidSignature));

		var unsupported = BuildDelta();
		unsupported[5] = 5;
		Assert.Throws<InvalidDataException>(() => WDiffDelta.ReadHeader(unsupported));
	}

	[Fact]
	public void RejectsTruncatedAndInvalidOperations()
	{
		var truncated = BuildDelta();
		Array.Resize(ref truncated, truncated.Length - 1);
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(truncated, Input, Expectation()));

		var zeroOperation = BuildHeaderOnly(Input, Output, "rpg2003.exe").Concat(new byte[] { 0, 0 }).ToArray();
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(zeroOperation, Input, Expectation()));
	}

	[Fact]
	public void RejectsInputOutputSizeAndOutputHashMismatches()
	{
		var stream = BuildDelta();
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(stream, Input, Expectation() with { InputSize = Input.Length + 1 }));
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(stream, Input, Expectation() with { OutputSize = Output.Length + 1 }));
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(stream, Input, Expectation() with { OutputSha256 = new string('0', 64) }));
	}

	[Fact]
	public void RejectsWrongTargetAndPathTraversal()
	{
		Assert.Throws<InvalidDataException>(() => WDiffDelta.Apply(BuildDelta(), Input, Expectation() with { TargetFileName = "other.exe" }));
		Assert.Throws<InvalidDataException>(() => WDiffDelta.ReadHeader(BuildHeaderOnly(Input, Output, "../rpg2003.exe")));
	}

	private static WDiffExpectation Expectation() => new(
		"rpg2003.exe", 104, Input.Length, Sha(Input), Output.Length, Sha(Output),
		WDiffDelta.ComputeRawCrc32(Input), WDiffDelta.ComputeRawCrc32(Output), new DateTime(2024, 8, 22, 0, 0, 0, DateTimeKind.Utc));

	private static byte[] BuildDelta()
	{
		using var stream = new MemoryStream();
		stream.Write(BuildHeaderOnly(Input, Output, "rpg2003.exe"));
		WriteUInt16(stream, 0x8002); WriteUInt32(stream, 0); // copy input[0..2]
		WriteUInt16(stream, 3); stream.Write("XYZ"u8); // literal
		return stream.ToArray();
	}

	private static byte[] BuildHeaderOnly(byte[] input, byte[] output, string target)
	{
		using var stream = new MemoryStream();
		stream.Write("Diff"u8);
		stream.Write(new byte[] { 1, 4 });
		WriteUInt16(stream, 0);
		WriteUInt32(stream, WDiffDelta.ComputeRawCrc32(input));
		WriteUInt32(stream, WDiffDelta.ComputeRawCrc32(output));
		WriteUInt32(stream, (uint)input.Length);
		WriteUInt32(stream, (uint)output.Length);
		WriteUInt16(stream, 2024);
		stream.Write(new byte[] { 8, 22, 0, 0, 0 });
		stream.Write(Encoding.ASCII.GetBytes(target)); stream.WriteByte(0);
		stream.WriteByte(0); // description
		stream.WriteByte(0); // 1.04 extension field
		return stream.ToArray();
	}

	private static void WriteUInt16(Stream stream, ushort value)
	{
		Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, value); stream.Write(bytes);
	}
	private static void WriteUInt32(Stream stream, uint value)
	{
		Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes);
	}
	private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
