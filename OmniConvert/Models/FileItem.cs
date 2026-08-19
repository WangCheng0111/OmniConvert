using System;
using System.IO;

namespace OmniConvert.Models;

public sealed class FileItem
{
    public string Name { get; }

    public string Extension { get; }

    public string FullPath { get; }

    public string SizeText { get; }

    public FileItem(string fullPath)
    {
        FullPath = fullPath;
        Name = Path.GetFileName(fullPath);
        Extension = Path.GetExtension(fullPath).TrimStart('.').ToUpperInvariant();

        long size = 0;
        try
        {
            size = new FileInfo(fullPath).Length;
        }
        catch
        {
        }
        SizeText = FormatSize(size);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        }
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}
