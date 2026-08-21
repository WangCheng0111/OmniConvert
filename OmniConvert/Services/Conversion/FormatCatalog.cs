using OmniConvert.Models;
using System;
using System.Collections.Generic;

namespace OmniConvert.Services.Conversion;

public static class FormatCatalog
{
    private static readonly HashSet<string> DocumentInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "doc", "docx"
    };

    private static readonly HashSet<string> PdfInputs = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf"
    };

    private static readonly FormatDefinition[] DocumentTargetDefinitions =
    {
        new(FormatCategory.Document, "pdf", "PDF")
    };

    private static readonly FormatDefinition[] PdfTargetDefinitions =
    {
        new(FormatCategory.Pdf, "png", "PNG"),
        new(FormatCategory.Pdf, "jpg", "JPG")
    };

    public static FormatCategory? GetCategory(string extension)
    {
        if (DocumentInputs.Contains(extension))
        {
            return FormatCategory.Document;
        }
        if (PdfInputs.Contains(extension))
        {
            return FormatCategory.Pdf;
        }
        return null;
    }

    public static IReadOnlyList<FormatDefinition> GetTargets(FormatCategory category)
    {
        return category switch
        {
            FormatCategory.Document => DocumentTargetDefinitions,
            FormatCategory.Pdf => PdfTargetDefinitions,
            _ => Array.Empty<FormatDefinition>()
        };
    }

    public static string GetDisplayName(FormatCategory category)
    {
        return category switch
        {
            FormatCategory.Document => "Word 文档",
            FormatCategory.Video => "视频",
            FormatCategory.Audio => "音频",
            FormatCategory.Image => "图片",
            FormatCategory.Spreadsheet => "表格",
            FormatCategory.Presentation => "演示文稿",
            FormatCategory.Pdf => "PDF",
            FormatCategory.Text => "文本",
            FormatCategory.Ebook => "电子书",
            _ => category.ToString()
        };
    }
}
