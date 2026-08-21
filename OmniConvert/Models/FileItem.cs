using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;

namespace OmniConvert.Models;

public sealed partial class FileItem : ObservableObject
{
    public string Name { get; }

    public string Extension { get; }

    public string FullPath { get; }

    public string SizeText { get; }

    public FormatCategory? SourceCategory { get; set; }

    public bool IsSupported => SourceCategory is not null;

    public bool IsRunning => Status == ConversionStatus.Running;

    public string StatusText => !IsSupported ? "不支持" : Status switch
    {
        ConversionStatus.None => "",
        ConversionStatus.Queued => "等待中",
        ConversionStatus.Running => "转换中",
        ConversionStatus.Succeeded => "已完成",
        ConversionStatus.Failed => "失败",
        _ => ""
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    public partial ConversionStatus Status { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? OutputPath { get; set; }

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
