// Ported package structure from GridCraft (crates/xlsx/src/write.rs @ 01abd9542076101ffff80e98ef4e502596bfce50)
// and WordCraft (crates/docx/src/write/parts.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409).
// MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft and WordCraft contributors.
using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Em.Documents.Model;

namespace Em.Documents.Packaging;

internal sealed class OpcPackage(Stream destination) : IDisposable
{
    internal const string RelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
    internal const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string ContentTypeNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string CorePropertiesNamespace = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private const string DublinCore = "http://purl.org/dc/elements/1.1/";

    private readonly ZipArchive _zip = new(destination, ZipArchiveMode.Create, leaveOpen: true);
    private readonly Dictionary<string, string> _parts = new(StringComparer.Ordinal);

    public XmlWriter Part(string name, string? contentType = null)
    {
        if (contentType is not null)
            _parts.Add(name, contentType);
        return XmlWriter.Create(_zip.CreateEntry(name, CompressionLevel.Fastest).Open(), new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            CloseOutput = true,
            CheckCharacters = true
        });
    }

    /// <summary>Opens a part as a raw stream for <see cref="XmlOutput"/>; dispose it before opening the next part.</summary>
    public Stream OpenPart(string name, string contentType)
    {
        _parts.Add(name, contentType);
        return _zip.CreateEntry(name, CompressionLevel.Fastest).Open();
    }

    public void Relationships(string part, params Relationship[] items)
    {
        using var xml = Part(part);
        xml.WriteStartElement("Relationships", RelationshipNamespace);
        foreach (var relationship in items)
        {
            xml.WriteStartElement("Relationship", RelationshipNamespace);
            xml.WriteAttributeString("Id", relationship.Id);
            xml.WriteAttributeString("Type", relationship.Type);
            xml.WriteAttributeString("Target", relationship.Target);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
    }

    public void Finish(Document document, string mainPart)
    {
        using (var properties = Part("docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml"))
        {
            properties.WriteStartElement("cp", "coreProperties", CorePropertiesNamespace);
            properties.WriteElementString("dc", "title", DublinCore, document.Title);
            properties.WriteStartElement("dcterms", "created", "http://purl.org/dc/terms/");
            properties.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "dcterms:W3CDTF");
            properties.WriteString(document.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            properties.WriteEndElement();
            properties.WriteEndElement();
        }
        Relationships("_rels/.rels", new Relationship("rId1", OfficeRelationship + "officeDocument", mainPart),
            new Relationship("rId2", RelationshipNamespace + "/metadata/core-properties", "docProps/core.xml"));

        using var xml = Part("[Content_Types].xml");
        xml.WriteStartElement("Types", ContentTypeNamespace);
        Default("rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default("xml", "application/xml");
        foreach (var (name, type) in _parts)
        {
            xml.WriteStartElement("Override", ContentTypeNamespace);
            xml.WriteAttributeString("PartName", "/" + name);
            xml.WriteAttributeString("ContentType", type);
            xml.WriteEndElement();
        }
        xml.WriteEndElement();

        void Default(string extension, string type)
        {
            xml.WriteStartElement("Default", ContentTypeNamespace);
            xml.WriteAttributeString("Extension", extension);
            xml.WriteAttributeString("ContentType", type);
            xml.WriteEndElement();
        }
    }

    public void Dispose() => _zip.Dispose();

    public readonly record struct Relationship(string Id, string Type, string Target);
}
