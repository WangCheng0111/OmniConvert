using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OmniConvert.Services.ShellIntegration;

/// <summary>
/// 跨进程导入通道：写入方把待导入路径写入临时文件，主窗口实例在
/// OnLaunched/OnActivated 中读取并清理。目录为 Environment.GetFolderPath(
/// SpecialFolder.LocalApplicationData) 下的 OmniConvert\PendingImports——
/// 打包运行时该 API 被重定向到包私有目录（...\Packages\&lt;包族名&gt;\LocalCache\Local），
/// 写入方与读取方同属一个包、路径一致，通道正常工作。
/// 绕开 AppActivationArguments 无法可靠携带自定义数据的限制。
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
