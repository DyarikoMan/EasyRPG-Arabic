using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace EasyRpgArabic.Setup.Core.Modules;

internal static class ManiacsTextEncodingConfig
{
	public const string RelativePath = "Plugin/cmdcs.xml";
	public const string OriginalBackupPath = ".EasyRPG-Arabic/arabic-editor/cmdcs.xml.original";
	public const string ArabicWindowsCodePage = "1256";

	public static byte[] CreateArabicDefault()
	{
		XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
		XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
		var document = new XDocument(
			new XElement("CmdcsConfig",
				new XElement("ActivationInfoArray"),
				new XElement("OtherInfo",
					new XElement("PluginOtherInfo",
						new XAttribute(XNamespace.Xmlns + "xsi", xsi),
						new XAttribute(XNamespace.Xmlns + "xsd", xsd),
						new XElement("Jp", "false"),
						new XElement("CodePage", ArabicWindowsCodePage),
						new XElement("Memo"))),
				new XElement("DatInfoArray")));
		return Serialize(document);
	}

	public static byte[] SetArabicWindowsCodePage(byte[] xml)
	{
		var document = Parse(xml);
		var codePage = GetCodePageElement(document);
		if (string.Equals(codePage.Value.Trim(), ArabicWindowsCodePage, StringComparison.Ordinal)) { return xml; }
		codePage.Value = ArabicWindowsCodePage;
		return Serialize(document);
	}

	public static string ReadCodePage(byte[] xml) => GetCodePageElement(Parse(xml)).Value.Trim();

	private static XDocument Parse(byte[] xml)
	{
		using var stream = new MemoryStream(xml, writable: false);
		using var reader = XmlReader.Create(stream, new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			IgnoreComments = false,
			IgnoreWhitespace = false
		});
		return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
	}

	private static XElement GetCodePageElement(XDocument document)
	{
		var root = document.Root;
		if (root?.Name != "CmdcsConfig") { throw new InvalidDataException("Maniacs Plugin/cmdcs.xml has an unexpected root element."); }
		var matches = root.Elements("OtherInfo").Elements("PluginOtherInfo").Elements("CodePage").ToArray();
		if (matches.Length != 1) { throw new InvalidDataException("Maniacs Plugin/cmdcs.xml must contain exactly one PluginOtherInfo CodePage setting."); }
		return matches[0];
	}

	private static byte[] Serialize(XDocument document)
	{
		using var stream = new MemoryStream();
		using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
		{
			Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			Indent = true,
			OmitXmlDeclaration = document.Declaration is null,
			NewLineChars = "\r\n"
		}))
		{
			document.Save(writer);
		}
		return stream.ToArray();
	}
}
