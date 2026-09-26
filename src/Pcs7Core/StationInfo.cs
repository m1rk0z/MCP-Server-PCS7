using System;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace Pcs7Core;

/// <summary>What the PCS 7 side looks like: returned by the agent health check and by the pcs7_status tool.</summary>
public static class StationInfo
{
    public static object Collect(CoreOptions options, string component)
    {
        var s7bin = FindS7Bin();
        var cfcReader = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cfcreader", "Pcs7CfcReader.exe");
        return new
        {
            component,
            version = typeof(StationInfo).Assembly.GetName().Version?.ToString(),
            machine = Environment.MachineName,
            user = Environment.UserDomainName + "\\" + Environment.UserName,
            os = OsName(),
            os64Bit = Environment.Is64BitOperatingSystem,
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            accessMode = CoreOptions.AccessModeName(options.AccessMode),
            workDir = options.WorkDir,
            opcUaEndpoint = options.OpcUaEndpoint,
            simaticCommandInterface = Type.GetTypeFromProgID("Simatic.Simatic") is not null,
            step7Bin = s7bin,
            cfcReader = File.Exists(cfcReader),
            time = DateTime.Now,
        };
    }

    /// <summary>STEP 7 S7BIN folder ("Program Files (x86)" on 64-bit Windows, "Program Files" on 32-bit Windows 7).</summary>
    public static string? FindS7Bin()
    {
        var env = Environment.GetEnvironmentVariable("PCS7_S7BIN");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     @"C:\Program Files (x86)", @"C:\Program Files",
                 })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var dir = Path.Combine(root, "SIEMENS", "STEP7", "S7BIN");
            if (Directory.Exists(dir)) return dir;
        }
        return null;
    }

    private static string OsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var name = key?.GetValue("ProductName") as string;
            var build = key?.GetValue("CurrentBuildNumber") as string;
            // Windows 11 still reports "Windows 10" in ProductName: the build number tells them apart.
            if (name is not null && int.TryParse(build, out var b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
            var sp = key?.GetValue("CSDVersion") as string;
            return $"{name} {sp} (build {build})".Replace("  ", " ");
        }
        catch { return Environment.OSVersion.VersionString; }
    }
}
