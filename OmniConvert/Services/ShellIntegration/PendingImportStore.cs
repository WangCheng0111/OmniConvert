using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmniConvert.Services.ShellIntegration;

/// <summary>
/// 跨进程导入通道：写入方（二级菜单启动的进程、一级菜单 COM 服务器）把待导入
/// 路径写入 %LOCALAPPDATA%\OmniConvert\PendingImports 下的临时文件，主窗口实例
/// 在 OnLaunched/OnActivated 中读取并清理。绕开 AppActivationArguments 无法
/// 可靠携带自定义数据的限制。
/// </summary>
public static class PendingImportStore
{
    private static string GetDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OmniConvert", "PendingImports");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static void Write(IEnumerable<string> paths)
    {
        try
        {
            var file = Path.Combine(GetDirectory(), Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(file, paths);
        }
        catch
        {
        }
    }

    public static List<string> ReadAndClear()
    {
        var result = new List<string>();
        try
        {
            var dir = GetDirectory();
            foreach (var file in Directory.GetFiles(dir, "*.txt"))
            {
                try
                {
                    result.AddRange(File.ReadAllLines(file).Where(p => !string.IsNullOrWhiteSpace(p)));
                    File.Delete(file);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return result;
    }
}
