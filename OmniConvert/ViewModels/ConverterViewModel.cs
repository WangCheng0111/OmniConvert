using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniConvert.Models;
using OmniConvert.Services;
using OmniConvert.Services.Conversion;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OmniConvert.ViewModels;

public partial class ConverterViewModel : ObservableObject
{
    public ObservableCollection<FileItem> Files { get; } = new();

    public ObservableCollection<FileItem> SelectedFiles { get; } = new();

    public bool HasFiles => Files.Count > 0;

    public bool ShowEmptyState => !HasFiles;

    public bool CanSelectTarget => TryGetSelectedCategory(out _);

    public bool CanStart => !IsConverting
        && WordAvailable
        && CanSelectTarget
        && SelectedTarget is not null;

    public bool ShowWordRequiredHint => CanSelectTarget && !WordAvailable;

    public IReadOnlyList<FormatDefinition> TargetFormats { get; private set; } = Array.Empty<FormatDefinition>();

    [ObservableProperty]
    public partial string TargetPlaceholderText { get; set; } = "请选择文件";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial FormatDefinition? SelectedTarget { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    public partial bool IsConverting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    [NotifyPropertyChangedFor(nameof(ShowWordRequiredHint))]
    public partial bool WordAvailable { get; set; }

    [ObservableProperty]
    public partial bool ShowSummary { get; set; }

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "";

    [ObservableProperty]
    public partial string? LastOutputDirectory { get; set; }

    private readonly WordConverter _wordConverter = new();

    private readonly PdfConverter _pdfConverter = new();

    private CancellationTokenSource? _conversionCts;

    public ConverterViewModel()
    {
        WordAvailable = WordConverter.IsAvailable;
        Files.CollectionChanged += Files_CollectionChanged;
        SelectedFiles.CollectionChanged += SelectedFiles_CollectionChanged;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!existing.Add(path))
            {
                continue;
            }

            var item = new FileItem(path);
            item.SourceCategory = FormatCatalog.GetCategory(item.Extension);
            Files.Add(item);
        }
    }

    public void SetSelectedFiles(IEnumerable<object> selectedItems)
    {
        SelectedFiles.Clear();
        foreach (var item in selectedItems.OfType<FileItem>())
        {
            SelectedFiles.Add(item);
        }
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void SelectedFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateTargetState();
        OnPropertyChanged(nameof(CanSelectTarget));
        OnPropertyChanged(nameof(ShowWordRequiredHint));
        OnPropertyChanged(nameof(CanStart));
    }

    private bool TryGetSelectedCategory(out FormatCategory category)
    {
        category = default;
        if (SelectedFiles.Count == 0)
        {
            return false;
        }

        var categories = SelectedFiles
            .Select(file => file.SourceCategory)
            .Distinct()
            .ToList();
        if (categories.Count != 1 || categories[0] is null)
        {
            return false;
        }

        category = categories[0]!.Value;
        return true;
    }

    private void UpdateTargetState()
    {
        IReadOnlyList<FormatDefinition> targets;
        if (TryGetSelectedCategory(out var category))
        {
            targets = FormatCatalog.GetTargets(category);
            TargetPlaceholderText = "请选择文件";
        }
        else
        {
            targets = Array.Empty<FormatDefinition>();
            TargetPlaceholderText = SelectedFiles.Count > 0 ? "不支持该文件格式" : "请选择文件";
        }

        TargetFormats = targets;
        OnPropertyChanged(nameof(TargetFormats));

        if (SelectedTarget is null || !targets.Contains(SelectedTarget))
        {
            SelectedTarget = targets.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsConverting)
        {
            return;
        }

        // 每次启动转换前重新探测 Word,支持用户安装 Office 后不重启应用。
        WordAvailable = WordConverter.IsAvailable;
        if (!WordAvailable)
        {
            ShowSummary = true;
            SummaryText = "未检测到 Microsoft Word,无法转换 Word 文档。请安装 Office 后重试。";
            return;
        }

        var target = SelectedTarget;
        if (target is null)
        {
            return;
        }

        var conversions = SelectedFiles.ToList();
        if (conversions.Count == 0)
        {
            return;
        }

        _conversionCts = new CancellationTokenSource();
        var token = _conversionCts.Token;

        IsConverting = true;
        ShowSummary = false;

        foreach (var file in conversions)
        {
            file.ErrorMessage = null;
            file.OutputPath = null;
            file.Status = ConversionStatus.Queued;
        }

        var succeeded = 0;
        var failed = 0;
        var cancelled = 0;
        string? outputDirectory = null;

        try
        {
            foreach (var file in conversions)
            {
                file.Status = ConversionStatus.Running;
                try
                {
                    var converter = GetConverter(file.SourceCategory!.Value);
                    var outputExtension = converter.GetOutputExtension(target);
                    var outputPath = OutputPathService.ResolveOutputPath(file.FullPath, outputExtension);
                    await converter.ConvertAsync(file.FullPath, outputPath, file.SourceCategory.Value, target, token);

                    file.OutputPath = outputPath;
                    file.Status = ConversionStatus.Succeeded;
                    succeeded++;
                    outputDirectory ??= Path.GetDirectoryName(outputPath);
                }
                catch (OperationCanceledException)
                {
                    file.Status = ConversionStatus.None;
                    file.ErrorMessage = "已取消";
                    cancelled++;
                    break;
                }
                catch (ConversionException ex)
                {
                    file.Status = ConversionStatus.Failed;
                    file.ErrorMessage = ex.Message;
                    failed++;
                }
                catch (Exception ex)
                {
                    file.Status = ConversionStatus.Failed;
                    file.ErrorMessage = "转换失败:" + ex.Message;
                    failed++;
                }
            }
        }
        finally
        {
            foreach (var file in conversions.Where(file => file.Status == ConversionStatus.Queued))
            {
                file.Status = ConversionStatus.None;
            }

            if (outputDirectory is not null)
            {
                LastOutputDirectory = outputDirectory;
            }

            IsConverting = false;
            _conversionCts.Dispose();
            _conversionCts = null;

            ShowSummary = true;
            SummaryText = $"转换完成:{succeeded} 个成功,{failed} 个失败" + (cancelled > 0 ? ",已取消" : "") + "。";
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _conversionCts?.Cancel();
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        var directory = LastOutputDirectory;
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
    }

    private IConverter GetConverter(FormatCategory category)
    {
        return category switch
        {
            FormatCategory.Document => _wordConverter,
            FormatCategory.Pdf => _pdfConverter,
            _ => throw new InvalidOperationException($"暂不支持 {FormatCatalog.GetDisplayName(category)} 类别的转换。")
        };
    }
}
