using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace OmniConvert.Services.ShellIntegration;

public static class ImportCommandLine
{
    public const string ImportArgument = "--import";

    public static List<string> ParseImportPaths(IReadOnlyList<string> args)
    {
        var paths = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (!string.Equals(args[i], ImportArgument, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var j = i + 1; j < args.Count; j++)
            {
                if (args[j].StartsWith("--", StringComparison.Ordinal))
                {
                    break;
                }
                paths.Add(args[j]);
            }
            break;
        }
        return paths;
    }

    public static string[] SplitCommandLine(string commandLine)
    {
        var ptr = CommandLineToArgvW(commandLine, out var count);
        if (ptr == IntPtr.Zero)
        {
            return Array.Empty<string>();
        }

        try
        {
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                var argPtr = Marshal.ReadIntPtr(ptr, i * IntPtr.Size);
                result[i] = Marshal.PtrToStringUni(argPtr) ?? string.Empty;
            }
            return result;
        }
        finally
        {
            LocalFree(ptr);
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
