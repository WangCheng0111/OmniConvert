using System;
using System.IO;
using Windows.ApplicationModel;

namespace OmniConvert.Services.Conversion;

public static class EngineLocator
{
    private static string? _toolsRoot;

    private static string? GetToolsRoot()
    {
        if (_toolsRoot is not null)
        {
            return _toolsRoot;
        }

        try
        {
            var installed = Path.Combine(Package.Current.InstalledLocation.Path, "Tools");
            if (Directory.Exists(installed))
            {
                _toolsRoot = installed;
                return _toolsRoot;
            }
        }
        catch
        {
        }

        var local = Path.Combine(AppContext.BaseDirectory, "Tools");
        if (Directory.Exists(local))
        {
            _toolsRoot = local;
        }
        return _toolsRoot;
    }

    public static string? LocatePdftoppm()
    {
        var tools = GetToolsRoot();
        if (tools is null)
        {
            return null;
        }

        string[] candidates =
        {
            Path.Combine(tools, "poppler", "Library", "bin", "pdftoppm.exe"),
            Path.Combine(tools, "poppler", "bin", "pdftoppm.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    private static string? LocateInTools(string relativePath)
    {
        var tools = GetToolsRoot();
        if (tools is null)
        {
            return null;
        }

        var candidate = Path.Combine(tools, relativePath);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>定位 pdf2docx 引擎的嵌入式 Python 运行时。</summary>
    public static string? LocatePdf2docxPython()
    {
        return LocateInTools(Path.Combine("pdf2docx", "python", "python.exe"));
    }

    /// <summary>定位 pdf2docx 引擎启动器(含空格/伪粗体后处理)。</summary>
    public static string? LocatePdf2docxLauncher()
    {
        return LocateInTools(Path.Combine("pdf2docx", "convert.py"));
    }
}
