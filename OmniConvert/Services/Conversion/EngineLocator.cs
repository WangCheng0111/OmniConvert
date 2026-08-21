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
}
