using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OmniConvert.Services;
using OmniConvert.Services.ShellIntegration;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OmniConvert
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public const string MainInstanceKey = "OmniConvertMain";

        private Window? _window;

        public static Window? MainWindow { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            var commandLineArgs = Environment.GetCommandLineArgs();

            // 一级菜单 COM 服务器模式：Shell 以 -contextmenu 启动本进程，
            // 只注册 IExplorerCommand 类对象，不创建主窗口。
            if (commandLineArgs.Any(a => string.Equals(a, "-contextmenu", StringComparison.OrdinalIgnoreCase)))
            {
                ExplorerCommandServer.RunServer();
                return;
            }

            var importPaths = ImportCommandLine.ParseImportPaths(commandLineArgs);

            // 单实例：已有实例则把待导入路径写入通道文件，唤醒现有实例后退出
            var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
            if (!mainInstance.IsCurrent)
            {
                if (importPaths.Count > 0)
                {
                    PendingImportStore.Write(importPaths);
                }

                // AppActivationArguments 无公开构造函数：把当前进程的激活参数
                // 原样转发给现有实例（仅作为唤醒信号，数据走文件通道）。
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activationArgs is not null)
                {
                    mainInstance.RedirectActivationToAsync(activationArgs)
                        .AsTask().GetAwaiter().GetResult();
                }
                Environment.Exit(0);
                return;
            }

            AppInstance.GetCurrent().Activated += OnActivated;

            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();

            if (importPaths.Count > 0)
            {
                ((MainWindow)_window).ImportFiles(importPaths);
            }
            ImportPendingFiles();

            _ = Task.Run(ShortcutService.EnsureDesktopShortcut);
            _ = Task.Run(ContextMenuRegistration.EnsureRegistered);
        }

        private void OnActivated(object? sender, AppActivationArguments e)
        {
            // 重定向唤醒（信使/COM 场景写入的路径经文件通道传递）
            if (MainWindow is MainWindow window)
            {
                window.DispatcherQueue.TryEnqueue(ImportPendingFiles);
            }
        }

        private void ImportPendingFiles()
        {
            var paths = PendingImportStore.ReadAndClear();
            if (paths.Count > 0 && MainWindow is MainWindow window)
            {
                window.ImportFiles(paths);
            }
        }
    }
}
