using OmniConvert.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

/// <summary>
/// PDF → 可编辑 Word(DOCX):内置 pdf2docx 引擎(嵌入式 Python)。
/// 引擎自带两项后处理(见 scripts/pdf2docx-engine/convert.py):
///   1. 清除数字与中文之间由提取层插入的多余半角空格;
///   2. 通过 PyMuPDF texttrace 检测描边伪粗体并恢复加粗。
/// 扫描版 PDF 没有可提取文字,校验阶段会明确报错(OCR 属后续版本)。
/// </summary>
public sealed class PdfWordConverter : IConverter
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(10);

    public bool CanConvert(FormatCategory category, FormatDefinition target)
    {
        return category == FormatCategory.Pdf
            && string.Equals(target.Extension, "docx", StringComparison.OrdinalIgnoreCase);
    }

    public string GetOutputExtension(FormatDefinition target) => "docx";

    public async Task ConvertAsync(string inputPath, string outputPath, FormatCategory category, FormatDefinition target, CancellationToken cancellationToken)
    {
        var pythonPath = EngineLocator.LocatePdf2docxPython()
            ?? throw new ConversionException("未找到内置的 pdf2docx 引擎。请先运行 scripts\\restore-engines.ps1 下载引擎后重试。");
        var launcherPath = EngineLocator.LocatePdf2docxLauncher()
            ?? throw new ConversionException("未找到 pdf2docx 引擎启动器(convert.py)。请先运行 scripts\\restore-engines.ps1 下载引擎后重试。");

        if (!File.Exists(inputPath))
        {
            throw new ConversionException("源文件不存在,无法转换。");
        }

        var attemptPath = outputPath + ".attempt-" + Guid.NewGuid().ToString("N");
        try
        {
            var result = await ProcessRunner.RunAsync(
                pythonPath,
                new[] { "-B", launcherPath, inputPath, attemptPath },
                ConversionTimeout,
                cancellationToken).ConfigureAwait(false);

            if (result.ExitCode != 0 || !File.Exists(attemptPath) || new FileInfo(attemptPath).Length == 0)
            {
                var detail = Truncate(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError);
                throw new ConversionException("PDF 转 Word 失败。" + (string.IsNullOrEmpty(detail) ? string.Empty : $"详情:{detail}"));
            }

            DocxValidator.Validate(attemptPath);
            File.Copy(attemptPath, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(attemptPath))
                {
                    File.Delete(attemptPath);
                }
            }
            catch
            {
            }
        }
    }

    private static string Truncate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }
        var trimmed = text.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed.Substring(trimmed.Length - 500);
    }
}
