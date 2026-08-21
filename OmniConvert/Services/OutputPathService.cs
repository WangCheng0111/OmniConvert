using System.IO;

namespace OmniConvert.Services;

public static class OutputPathService
{
    public static string ResolveOutputPath(string sourcePath, string targetExtension)
    {
        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);

        var candidate = Path.Combine(directory, $"{baseName}.{targetExtension}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        for (var index = 1; ; index++)
        {
            candidate = Path.Combine(directory, $"{baseName} ({index}).{targetExtension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }
}
