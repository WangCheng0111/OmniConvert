using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using Windows.ApplicationModel;

namespace OmniConvert.Services.ShellIntegration;

/// <summary>
/// 二级菜单（Win11"显示更多选项"）注册：经典注册表静态 Verb，对所有文件
/// （HKCR\*）生效，多选时逐文件传递。启动时自动写入 HKCU，无需管理员权限；
/// 每次启动重写以保证路径随版本/安装位置更新。
/// </summary>
public static class ContextMenuRegistration
{
    private const string MenuKeyPath = @"Software\Classes\*\shell\OmniConvert.Import";
    private const string MenuDisplayName = "导入到 OmniConvert";
    private const string ExecutionAliasName = "OmniConvert.exe";

    public static void EnsureRegistered()
    {
        try
        {
            string commandPath;
            string iconPath;

            try
            {
                // 打包运行：优先执行别名（正式安装时创建）；F5 松散部署不创建
                // 执行别名，回退到当前 exe（此时位于项目 bin 目录，可直接启动）。
                var package = Package.Current;
                var alias = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", ExecutionAliasName);
                commandPath = File.Exists(alias) ? alias : (Environment.ProcessPath ?? alias);
                iconPath = Path.Combine(package.InstalledLocation.Path, "Assets", "Tiles", "GalleryIcon.ico");
                if (!File.Exists(iconPath))
                {
                    iconPath = commandPath;
                }
            }
            catch
            {
                // 解包运行：直接指向自身 exe
                commandPath = Environment.ProcessPath ?? throw new InvalidOperationException("无法获取应用可执行文件路径");
                iconPath = commandPath;
            }

            using var key = Registry.CurrentUser.CreateSubKey(MenuKeyPath, true);
            key.SetValue(null, MenuDisplayName);
            key.SetValue("MultiSelectModel", "Player");
            key.SetValue("Icon", $"\"{iconPath}\",0");

            using var commandKey = key.CreateSubKey("command", true);
            commandKey.SetValue(null, $"\"{commandPath}\" {ImportCommandLine.ImportArgument} \"%1\"");

            Log("注册成功: " + commandPath);
        }
        catch (Exception ex)
        {
            Log("注册失败: " + ex);
        }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OmniConvert", "ContextMenuRegistration.log");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
