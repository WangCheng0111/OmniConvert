using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace OmniConvert.Services.Conversion;

/// <summary>
/// 校验 pdf2docx 引擎产出的 DOCX 包:必要部件齐全、根元素命名空间正确、
/// 主关系与图片关系完整、存在可编辑文字。
/// </summary>
public static class DocxValidator
{
    private static readonly XNamespace ContentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace RelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void Validate(string docxPath)
    {
        if (!File.Exists(docxPath) || new FileInfo(docxPath).Length == 0)
        {
            throw Invalid();
        }

        try
        {
            using var archive = ZipFile.OpenRead(docxPath);
            var entryNames = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var contentTypesXml = ReadEntry(archive, entryNames, "[Content_Types].xml");
            var relsXml = ReadEntry(archive, entryNames, "_rels/.rels");
            var documentXml = ReadEntry(archive, entryNames, "word/document.xml");
            if (contentTypesXml is null || relsXml is null || documentXml is null)
            {
                throw Invalid();
            }

            var contentTypes = XDocument.Parse(contentTypesXml);
            var relationships = XDocument.Parse(relsXml);
            var document = XDocument.Parse(documentXml);
            if (contentTypes.Root?.Name != ContentTypesNs + "Types"
                || relationships.Root?.Name != RelationshipsNs + "Relationships"
                || document.Root?.Name != WordNs + "document")
            {
                throw Invalid();
            }

            var hasMainOverride = contentTypes.Root.Elements(ContentTypesNs + "Override").Any(element =>
                (string?)element.Attribute("PartName") == "/word/document.xml"
                && (string?)element.Attribute("ContentType") == "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            var hasMainRelationship = relationships.Root.Elements(RelationshipsNs + "Relationship").Any(element =>
                ((string?)element.Attribute("Type"))?.EndsWith("/officeDocument", StringComparison.Ordinal) == true
                && (string?)element.Attribute("Target") == "word/document.xml"
                && element.Attribute("TargetMode") is null);
            if (!hasMainOverride || !hasMainRelationship)
            {
                throw Invalid();
            }

            // 图片关系完整性:document.xml 中每个 a:blip 的 r:embed 必须在
            // word/_rels/document.xml.rels 中存在且指向包内 media 部件。
            var blipIds = document.Descendants(DrawingNs + "blip")
                .Select(element => (string?)element.Attribute(OfficeRelationshipsNs + "embed"))
                .ToList();
            if (blipIds.Any(string.IsNullOrEmpty))
            {
                throw Invalid();
            }
            if (blipIds.Count > 0)
            {
                var documentRelsXml = ReadEntry(archive, entryNames, "word/_rels/document.xml.rels");
                if (documentRelsXml is null)
                {
                    throw Invalid();
                }

                var documentRels = XDocument.Parse(documentRelsXml);
                if (documentRels.Root?.Name != RelationshipsNs + "Relationships")
                {
                    throw Invalid();
                }

                var imageTargets = documentRels.Root.Elements(RelationshipsNs + "Relationship")
                    .Where(element => ((string?)element.Attribute("Type"))?.EndsWith("/image", StringComparison.Ordinal) == true
                        && element.Attribute("TargetMode") is null)
                    .ToDictionary(
                        element => (string?)element.Attribute("Id") ?? string.Empty,
                        element => (string?)element.Attribute("Target") ?? string.Empty,
                        StringComparer.Ordinal);

                foreach (var blipId in blipIds)
                {
                    if (!imageTargets.TryGetValue(blipId!, out var target)
                        || !target.StartsWith("media/", StringComparison.Ordinal)
                        || !entryNames.Contains("word/" + target))
                    {
                        throw Invalid();
                    }
                }
            }

            var hasEditableText = document.Descendants(WordNs + "t").Any(element => !string.IsNullOrWhiteSpace(element.Value));
            if (!hasEditableText)
            {
                throw new ConversionException("这个 PDF 没有可提取的文字(可能是扫描版),无法转换为可编辑的 Word 文档。");
            }
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ConversionException("转换产出的 Word 文档无效或不完整。", ex);
        }
    }

    private static string? ReadEntry(ZipArchive archive, HashSet<string> entryNames, string entryName)
    {
        if (!entryNames.Contains(entryName))
        {
            return null;
        }

        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.FullName, entryName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static ConversionException Invalid()
    {
        return new ConversionException("转换产出的 Word 文档无效或不完整。");
    }
}
