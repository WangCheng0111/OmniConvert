using CommunityToolkit.Mvvm.ComponentModel;
using OmniConvert.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace OmniConvert.ViewModels;

public partial class ConverterViewModel : ObservableObject
{
    public ObservableCollection<FileItem> Files { get; } = new();

    public bool HasFiles => Files.Count > 0;

    public bool ShowEmptyState => !HasFiles;

    public ConverterViewModel()
    {
        Files.CollectionChanged += Files_CollectionChanged;
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(Files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (existing.Add(path))
            {
                Files.Add(new FileItem(path));
            }
        }
    }

    private void Files_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasFiles));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
