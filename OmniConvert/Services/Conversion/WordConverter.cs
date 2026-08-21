using Microsoft.Win32;
using OmniConvert.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.Services.Conversion;

/// <summary>
/// 通过 Microsoft Word COM 自动化把 Word 文档导出为 PDF。
/// 与用户在 Word 中"另存为 PDF"使用完全相同的渲染管线,输出逐字节一致。
/// 未安装 MS Word 时不提供任何兜底引擎(按产品决策:直接不转)。
/// </summary>
public sealed class WordConverter : IConverter
{
    private const int WdExportFormatPdf = 17;
    private const int WdDoNotSaveChanges = 0;
    private const int WdAlertsNone = 0;
    private const int MsoAutomationSecurityForceDisable = 3;

    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan QuitWaitTimeout = TimeSpan.FromSeconds(15);

    private static bool? _cachedAvailability;

    /// <summary>
    /// 检测本机是否安装 MS Word(不是 WPS 冒充的 Word.Application)。
    /// 通过注册表 CLSID 的 LocalServer32 路径必须指向 WINWORD.EXE。
    /// </summary>
    public static bool IsAvailable
    {
        get
        {
            _cachedAvailability ??= DetectWordInstallation();
            return _cachedAvailability.Value;
        }
    }

    public bool CanConvert(FormatCategory category, FormatDefinition target)
    {
        return category == FormatCategory.Document
            && string.Equals(target.Extension, "pdf", StringComparison.OrdinalIgnoreCase);
    }

    public Task ConvertAsync(string inputPath, string outputPath, FormatCategory category, FormatDefinition target, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            throw new ConversionException("未检测到 Microsoft Word,无法转换 Word 文档。请安装 Office 后重试。");
        }

        return Task.Run(() => ConvertCore(inputPath, outputPath, cancellationToken), cancellationToken);
    }

    private static bool DetectWordInstallation()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32, RegistryView.Default })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, view);
                using var key = baseKey.OpenSubKey(@"Word.Application\CLSID");
                var clsid = key?.GetValue(null) as string;
                if (string.IsNullOrEmpty(clsid))
                {
                    continue;
                }

                using var serverKey = baseKey.OpenSubKey($@"CLSID\{clsid}\LocalServer32");
                var serverPath = serverKey?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(serverPath)
                    && serverPath.IndexOf("WINWORD.EXE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            catch
            {
            }
        }
        return false;
    }

    private static HashSet<int> GetWinwordPids()
    {
        try
        {
            return Process.GetProcessesByName("WINWORD").Select(process => process.Id).ToHashSet();
        }
        catch
        {
            return new HashSet<int>();
        }
    }

    private static void TryKill(int? pid)
    {
        if (pid is null)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid.Value);
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
        }
    }

    private static void WaitForExitOrKill(int? pid)
    {
        if (pid is null)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(pid.Value);
            if (!process.WaitForExit((int)QuitWaitTimeout.TotalMilliseconds))
            {
                TryKill(pid);
            }
        }
        catch
        {
        }
    }

    private void ConvertCore(string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            throw new ConversionException("源文件不存在,无法转换。");
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "OmniConvert-Word-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        int? wordPid = null;
        dynamic? word = null;
        CancellationTokenSource? timeoutCts = null;

        try
        {
            // 复制到临时副本再交给 Word:避免源文件被其他程序占用,也不会与
            // 用户已打开的 Word 文档实例产生打开冲突。
            var workingInput = Path.Combine(tempDir, Path.GetFileName(inputPath));
            File.Copy(inputPath, workingInput);

            cancellationToken.ThrowIfCancellationRequested();

            var existingPids = GetWinwordPids();
            word = CreateWordApplication(existingPids, out wordPid);

            // 超时/取消时杀掉本次创建的 Word 实例,COM 阻塞调用随即抛出并返回。
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ConversionTimeout);
            using var registration = timeoutCts.Token.Register(() => TryKill(wordPid));

            try
            {
                dynamic doc = word.Documents.Open(
                    FileName: workingInput,
                    ConfirmConversions: false,
                    ReadOnly: true,
                    AddToRecentFiles: false);
                try
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    doc.ExportAsFixedFormat(OutputFileName: outputPath, ExportFormat: WdExportFormatPdf);
                }
                finally
                {
                    try
                    {
                        doc.Close(SaveChanges: WdDoNotSaveChanges);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                try
                {
                    word.Quit(SaveChanges: WdDoNotSaveChanges);
                }
                catch
                {
                }
                word = null;
                WaitForExitOrKill(wordPid);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                throw new ConversionException("Word 未产出有效的 PDF 文件。");
            }
        }
        catch (OperationCanceledException)
        {
            TryKill(wordPid);
            throw;
        }
        catch (COMException ex)
        {
            if (timeoutCts is not null && timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new ConversionException("Word 转换超时,已终止转换进程。若 Word 是首次运行,请先手动打开一次完成初始化后重试。", ex);
            }
            throw new ConversionException($"Word 转换失败(错误码 0x{ex.HResult:X8})。请确认 Word 能正常打开该文档后重试。", ex);
        }
        catch (ConversionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                throw new ConversionException("Word COM 接口调用失败,Office 版本可能过旧。", ex);
            }
            throw new ConversionException($"Word 转换失败:{ex.Message}", ex);
        }
        finally
        {
            if (word is not null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(word);
                }
                catch
                {
                }
            }
            TryKill(wordPid);
            try
            {
                Directory.Delete(tempDir, true);
            }
            catch
            {
            }
        }
    }

    private dynamic CreateWordApplication(HashSet<int> existingPids, out int? pid)
    {
        var wordType = Type.GetTypeFromProgID("Word.Application")
            ?? throw new ConversionException("未检测到 Microsoft Word,无法转换 Word 文档。请安装 Office 后重试。");

        dynamic word = Activator.CreateInstance(wordType)
            ?? throw new ConversionException("无法创建 Word 转换实例。");

        pid = null;
        var deadline = DateTime.UtcNow + StartupTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var newPid = GetWinwordPids().Except(existingPids).FirstOrDefault();
            if (newPid > 0)
            {
                pid = newPid;
                break;
            }
            Thread.Sleep(200);
        }

        // 消灭一切可能阻塞自动化的对话框:隐藏窗口、静默告警、禁用宏、关闭屏幕刷新。
        word.Visible = false;
        word.DisplayAlerts = WdAlertsNone;
        word.ScreenUpdating = false;
        try
        {
            word.AutomationSecurity = MsoAutomationSecurityForceDisable;
        }
        catch
        {
        }

        return word;
    }
}
