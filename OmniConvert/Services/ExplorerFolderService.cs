using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniConvert.Services;

/// <summary>
/// 资源管理器集成:打开输出目录并自动选中产出文件(多文件全部选中)。
/// 使用系统官方 API SHOpenFolderAndSelectItems,不同目录的输出各开一个窗口。
/// </summary>
public static class ExplorerFolderService
{
    public static void OpenAndSelect(IEnumerable<string> filePaths)
    {
        var groups = filePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .GroupBy(path => Path.GetDirectoryName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var directory = group.Key;
            if (string.IsNullOrEmpty(directory))
            {
                continue;
            }

            if (!TryOpenAndSelect(directory, group.ToList()))
            {
                // 兜底:仅打开文件夹(不选中)
                try
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", directory) { UseShellExecute = true });
                }
                catch
                {
                }
            }
        }
    }

    private static bool TryOpenAndSelect(string directory, List<string> files)
    {
        var itemPidls = new List<IntPtr>();
        var folderPidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(directory, IntPtr.Zero, out folderPidl, 0, out _) != 0 || folderPidl == IntPtr.Zero)
            {
                return false;
            }

            foreach (var file in files)
            {
                if (SHParseDisplayName(file, IntPtr.Zero, out var pidl, 0, out _) == 0 && pidl != IntPtr.Zero)
                {
                    itemPidls.Add(pidl);
                }
            }

            if (itemPidls.Count == 0)
            {
                return false;
            }

            return SHOpenFolderAndSelectItems(folderPidl, (uint)itemPidls.Count, itemPidls.ToArray(), 0) == 0;
        }
        finally
        {
            foreach (var pidl in itemPidls)
            {
                ILFree(pidl);
            }
            if (folderPidl != IntPtr.Zero)
            {
                ILFree(folderPidl);
            }
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, uint dwFlags);

    [DllImport("shell32.dll")]
    private static extern int SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}
