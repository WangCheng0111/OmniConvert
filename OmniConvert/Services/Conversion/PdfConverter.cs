using OmniConvert.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

/// <summary>
/// PDF 渲染为图片:内置 Poppler pdftoppm 按固定 300 DPI 逐页渲染,
/// 无论页数多少统一打包为 ZIP(page-001.png / page-002.jpg ...)。
/// 实现照搬飞鼠格式 FlyingMouse Format 的 pdf.js 渲染链路。
/// </summary>
public sealed class PdfConverter : IConverter
{
    private const int RenderDpi = 300;

    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(10);

    public bool CanConvert(FormatCategory category, FormatDefinition target)
    {
        return category == FormatCategory.Pdf
            && (string.Equals(target.Extension, "png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(target.Extension, "jpg", StringComparison.OrdinalIgnoreCase));
    }

    public string GetOutputExtension(FormatDefinition target) => "zip";

    public async Task ConvertAsync(string inputPath, string outputPath, FormatCategory category, FormatDefinition target, CancellationToken cancellationToken)
    {
        var pdftoppmPath = EngineLocator.LocatePdftoppm()
            ?? throw new ConversionException("未找到内置的 Poppler 渲染引擎。请先运行 scripts\\restore-engines.ps1 下载引擎后重试。");

        if (!File.Exists(inputPath))
        {
            throw new ConversionException("源文件不存在,无法转换。");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "OmniConvert-Pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var extension = target.Extension.ToLowerInvariant();
            var prefix = Path.Combine(tempDir, "page");
            var args = new List<string>
            {
                extension == "jpg" ? "-jpeg" : "-png",
                "-cropbox",
                "-r", RenderDpi.ToString(),
                inputPath,
                prefix
            };

            var result = await ProcessRunner.RunAsync(pdftoppmPath, args, ConversionTimeout, cancellationToken).ConfigureAwait(false);

            var files = Directory.GetFiles(tempDir, $"*{extension}", SearchOption.TopDirectoryOnly)
                .OrderBy(GetPageNumber)
                .ToList();

            if (result.ExitCode != 0 || files.Count == 0)
            {
                var detail = result.StandardError.Trim();
                throw new ConversionException("PDF 渲染失败,未生成任何页面图片。" + (string.IsNullOrEmpty(detail) ? "" : $"详情:{Truncate(detail)}"));
            }

            using (var zip = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                for (var index = 0; index < files.Count; index++)
                {
                    zip.CreateEntryFromFile(files[index], $"page-{(index + 1).ToString("D3")}.{extension}");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private static long GetPageNumber(string filePath)
    {
        var match = Regex.Match(Path.GetFileNameWithoutExtension(filePath), @"\d+$");
        return match.Success && long.TryParse(match.Value, out var number) ? number : long.MaxValue;
    }

    private static string Truncate(string text)
    {
        return text.Length <= 500 ? text : text.Substring(text.Length - 500);
    }
}
