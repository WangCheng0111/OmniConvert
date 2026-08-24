using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OmniConvert.Services.ShellIntegration;

/// <summary>
/// Win11 一级右键菜单（"导入到 OmniConvert"）的 IExplorerCommand 进程外 COM
/// 服务器。Shell 以 "-contextmenu" 参数启动本应用进程，注册类对象后等待调用；
/// Invoke 中收集选中文件路径，唤醒主窗口实例完成导入，随后自行退出。
/// </summary>
public static class ExplorerCommandServer
{
    public const string Clsid = "218a51b4-cea7-4b37-8e74-fc5e56ae12d2";

    private const string Title = "导入到 OmniConvert";
    private const string ExecutionAliasName = "OmniConvert.exe";

    private const uint ClsctxLocalServer = 4;
    private const uint RegclsMultipleUse = 1;
    private const int SigdnFileSysPath = unchecked((int)0x80058000);
    private const int ENotImpl = unchecked((int)0x80004001);

    public static void RunServer()
    {
        var guid = new Guid(Clsid);
        var factory = new ExplorerCommandClassFactory();
        var hr = CoRegisterClassObject(ref guid, factory, ClsctxLocalServer, RegclsMultipleUse, out _);
        if (hr != 0)
        {
            Environment.Exit(hr);
        }

        // 兜底：Shell 从未调用 Invoke 时避免进程残留
        var timeoutTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timeoutTimer.Interval = TimeSpan.FromSeconds(30);
        timeoutTimer.IsRepeating = false;
        timeoutTimer.Tick += (_, _) => Environment.Exit(0);
        timeoutTimer.Start();
    }

    private static void ActivateMainApp(List<string> paths)
    {
        // 待导入路径写入文件通道，再启动一个应用进程：
        // 主实例不存在则该进程成为主实例并导入；已存在则其重定向到现有实例
        // （现有实例的 OnActivated 会读取通道文件完成导入）。
        PendingImportStore.Write(paths);

        var startInfo = new ProcessStartInfo
        {
            FileName = GetLaunchCommandPath(),
            UseShellExecute = false
        };
        Process.Start(startInfo);
    }

    private static string GetLaunchCommandPath()
    {
        try
        {
            var alias = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", ExecutionAliasName);
            if (File.Exists(alias))
            {
                return alias;
            }
        }
        catch
        {
        }
        return Environment.ProcessPath!;
    }

    private static List<string> GetPaths(IShellItemArray items)
    {
        var paths = new List<string>();
        if (items.GetCount(out var count) != 0)
        {
            return paths;
        }

        for (var i = 0; i < count; i++)
        {
            if (items.GetItemAt(i, out var item) != 0)
            {
                continue;
            }

            try
            {
                if (item.GetDisplayName(SigdnFileSysPath, out var path) == 0 && !string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }
            catch
            {
            }
        }
        return paths;
    }

    // ---- COM 接口定义 ----

    [ComImport]
    [Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetIcon(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszIcon);
        [PreserveSig] int GetToolTip(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.LPWStr)] out string ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
        [PreserveSig] int Invoke(IShellItemArray psiItemArray, [MarshalAs(UnmanagedType.Interface)] object? pbc);
        [PreserveSig] int GetFlags(out uint pFlags);
        [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
    }

    [ComImport]
    [Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemArray
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(int attribFlags, int sfgaoMask, out int psfgaoAttribs);
        [PreserveSig] int GetCount(out int pdwNumItems);
        [PreserveSig] int GetItemAt(int dwIndex, out IShellItem ppsi);
        [PreserveSig] int EnumItems(out IntPtr ppenumShellItems);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(int sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
        [PreserveSig] int GetAttributes(int sfgaoMask, out int psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, int hint, out int piOrder);
    }

    [ComImport]
    [Guid("00000001-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        void CreateInstance([MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
        void LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(ref Guid rclsid, [MarshalAs(UnmanagedType.IUnknown)] object pUnk, uint dwClsContext, uint flags, out uint lpdwRegister);

    // ---- 实现 ----

    [Guid(Clsid)]
    private sealed class ExplorerCommand : IExplorerCommand
    {
        public int GetTitle(IShellItemArray psiItemArray, out string ppszName)
        {
            ppszName = Title;
            return 0;
        }

        public int GetIcon(IShellItemArray psiItemArray, out string ppszIcon)
        {
            ppszIcon = Environment.ProcessPath + ",0";
            return 0;
        }

        public int GetToolTip(IShellItemArray psiItemArray, out string ppszInfotip)
        {
            ppszInfotip = string.Empty;
            return ENotImpl;
        }

        public int GetCanonicalName(out Guid pguidCommandName)
        {
            pguidCommandName = Guid.Empty;
            return ENotImpl;
        }

        public int GetState(IShellItemArray psiItemArray, bool fOkToBeSlow, out uint pCmdState)
        {
            pCmdState = 0;
            return 0;
        }

        public int Invoke(IShellItemArray psiItemArray, object? pbc)
        {
            var paths = GetPaths(psiItemArray);
            if (paths.Count > 0)
            {
                ActivateMainApp(paths);
            }

            // 激活完成后延迟退出 COM 服务器进程
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
                Environment.Exit(0);
            });
            return 0;
        }

        public int GetFlags(out uint pFlags)
        {
            pFlags = 0;
            return 0;
        }

        public int EnumSubCommands(out IntPtr ppEnum)
        {
            ppEnum = IntPtr.Zero;
            return ENotImpl;
        }
    }

    private sealed class ExplorerCommandClassFactory : IClassFactory
    {
        public void CreateInstance(object? pUnkOuter, ref Guid riid, out object ppvObject)
        {
            ppvObject = new ExplorerCommand();
        }

        public void LockServer(bool fLock)
        {
        }
    }
}
