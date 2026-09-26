using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace Pcs7Agent;

/// <summary>Settings chosen at install time (dialog or command line).</summary>
public sealed class InstallSettings
{
    public int Port { get; set; }
    public string AccessMode { get; set; } = "read-only";
    public List<string> AllowedClients { get; set; } = new();
    public bool NewToken { get; set; }
}

/// <summary>
/// Installs / removes the agent using only tools present on every Windows version from 7 SP1 on
/// (netsh http, netsh advfirewall, icacls, registry Run key). Requires administrator rights.
/// </summary>
public static class Installer
{
    public const string AppName = "PCS7 MCP Agent";
    private const string FirewallRule = "PCS7 MCP Agent";
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Pcs7Agent";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Pcs7Agent";

    /// <summary>Program Files (x86) on 64-bit Windows, Program Files on 32-bit Windows.</summary>
    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) is { Length: > 0 } pf
            ? pf : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Pcs7Agent");

    public static string InstalledExe => Path.Combine(InstallDir, "Pcs7Agent.exe");

    public static bool IsAdministrator()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Marks the elevated copy started by <see cref="RelaunchElevated"/>; followed by the PID of the waiting parent.</summary>
    public const string ElevatedChildFlag = "--elevated-child";

    /// <summary>
    /// Runs this executable elevated (UAC prompt) with the same arguments, waits for it and returns its exit code
    /// (1223 = the user cancelled the UAC prompt).
    /// </summary>
    public static int RelaunchElevated(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule!.FileName,
                string.Join(" ", args.Concat(new[] { ElevatedChildFlag, Process.GetCurrentProcess().Id.ToString() }).Select(Pcs7Core.Cfc.CfcRunner.QuoteArg)))
            { UseShellExecute = true, Verb = "runas" };
            using var p = Process.Start(psi)!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception) { return 1223; }
    }

    /// <summary>Starts the installed agent from a non-elevated process: it runs as this user, without elevation.</summary>
    public static void StartInstalledAgent()
    {
        try { if (File.Exists(InstalledExe)) Process.Start(new ProcessStartInfo(InstalledExe) { UseShellExecute = false }); }
        catch (Exception ex) { AgentLog.Write("cannot start agent after install: " + ex.Message); }
    }

    public const int MinPort = 1024, MaxPort = 65535;

    /// <param name="startAgent">false when a non-elevated parent process starts the agent itself (right user, no elevation).</param>
    /// <param name="parentPid">non-elevated parent waiting for this install: it must not be stopped with the running agents.</param>
    public static AgentConfig Install(InstallSettings settings, Action<string> progress, bool startAgent = true, int parentPid = 0)
    {
        if (settings.Port < MinPort || settings.Port > MaxPort)
            throw new ArgumentException($"Porta non valida: {settings.Port} (ammessa {MinPort}-{MaxPort}).");
        var sourceDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        var cfg = AgentConfig.LoadOrDefault();
        var oldPort = cfg.Port;

        progress("Arresto dell'agente in esecuzione...");
        StopRunningAgents(parentPid);

        progress("Copia dei file in " + InstallDir + "...");
        if (!string.Equals(Path.GetFullPath(sourceDir), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase))
            CopyDirectory(sourceDir, InstallDir);

        progress("Configurazione...");
        cfg.Port = settings.Port;
        cfg.AccessMode = settings.AccessMode;
        cfg.AllowedClients = settings.AllowedClients;
        if (settings.NewToken || string.IsNullOrWhiteSpace(cfg.Token)) cfg.Token = AgentConfig.NewToken();
        Directory.CreateDirectory(AgentConfig.DataDir);
        Directory.CreateDirectory(cfg.WorkDir);
        Directory.CreateDirectory(AgentConfig.LogDir);
        cfg.Save();
        SecureDataFolder(cfg);

        progress("Prenotazione URL HTTP sulla porta " + cfg.Port + "...");
        if (oldPort != cfg.Port) Run("netsh", $"http delete urlacl url=http://+:{oldPort}/");
        Run("netsh", $"http delete urlacl url=http://+:{cfg.Port}/");
        // WD = Everyone (language-independent); access is protected by the token.
        Run("netsh", $"http add urlacl url=http://+:{cfg.Port}/ sddl=D:(A;;GX;;;WD)", mustSucceed: true);

        progress("Regola firewall...");
        Run("netsh", $"advfirewall firewall delete rule name=\"{FirewallRule}\"");
        var remote = cfg.AllowedClients.Count > 0 ? " remoteip=" + string.Join(",", cfg.AllowedClients) : "";
        Run("netsh", $"advfirewall firewall add rule name=\"{FirewallRule}\" dir=in action=allow protocol=TCP localport={cfg.Port} profile=any{remote}");

        progress("Avvio automatico all'accesso...");
        using (var run = Registry.LocalMachine.CreateSubKey(RunKey))
            run!.SetValue(RunValue, $"\"{InstalledExe}\"");

        using (var un = Registry.LocalMachine.CreateSubKey(UninstallKey))
        {
            un!.SetValue("DisplayName", AppName);
            un.SetValue("DisplayVersion", typeof(Installer).Assembly.GetName().Version!.ToString(3));
            un.SetValue("Publisher", "pcs7-mcp");
            un.SetValue("InstallLocation", InstallDir);
            un.SetValue("DisplayIcon", InstalledExe);
            un.SetValue("UninstallString", $"\"{InstalledExe}\" uninstall");
            un.SetValue("NoModify", 1, RegistryValueKind.DWord);
            un.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        File.WriteAllText(Path.Combine(AgentConfig.DataDir, "client-config.txt"), ClientInstructions(cfg), new UTF8Encoding(true));

        if (startAgent)
        {
            progress("Avvio dell'agente...");
            StartAgentAsDesktopUser();
        }
        AgentLog.Write($"installed version {typeof(Installer).Assembly.GetName().Version} in {InstallDir}, port {cfg.Port}, mode {cfg.AccessMode}");
        return cfg;
    }

    public static void Uninstall(bool purge, Action<string> progress, int parentPid = 0)
    {
        var cfg = AgentConfig.LoadOrDefault();
        progress("Arresto dell'agente...");
        StopRunningAgents(parentPid);
        progress("Rimozione di avvio automatico, firewall e prenotazione URL...");
        try { using var run = Registry.LocalMachine.OpenSubKey(RunKey, true); run?.DeleteValue(RunValue, false); } catch { }
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); } catch { }
        Run("netsh", $"advfirewall firewall delete rule name=\"{FirewallRule}\"");
        Run("netsh", $"http delete urlacl url=http://+:{cfg.Port}/");
        progress("Eliminazione dei file del programma...");
        var self = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstallDir), StringComparison.OrdinalIgnoreCase))
        {
            // The running executable cannot delete its own folder: a detached cmd retries for up to 2 minutes,
            // until this process has exited (e.g. after the user closed the final message).
            var dir = InstallDir;
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c for /l %i in (1,1,60) do (ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"{dir}\" 2>nul & if not exist \"{dir}\" exit)")
            { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
        }
        else if (Directory.Exists(InstallDir))
        {
            try { Directory.Delete(InstallDir, true); } catch { }
        }
        if (purge)
        {
            // Last step, with no logging afterwards (logging would recreate the logs folder).
            progress("Eliminazione di configurazione, log ed export...");
            try { Directory.Delete(AgentConfig.DataDir, true); } catch { }
        }
    }

    /// <summary>
    /// agent.json holds the token and the access mode: only administrators may change it, users (the agent) may read it.
    /// Users may write only the export and log folders. SIDs keep this language-independent:
    /// S-1-5-32-544 Administrators, S-1-5-18 SYSTEM, S-1-5-32-545 Users.
    /// </summary>
    private static void SecureDataFolder(AgentConfig cfg)
    {
        // Folder only (not /T: on files the (OI)(CI) flags do not apply and would leave an empty ACL),
        // then everything inside goes back to inheriting from it.
        Run("icacls", $"\"{AgentConfig.DataDir}\" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F *S-1-5-18:(OI)(CI)F *S-1-5-32-545:(OI)(CI)RX /C /Q", mustSucceed: true);
        Run("icacls", $"\"{Path.Combine(AgentConfig.DataDir, "*")}\" /reset /T /C /Q", mustSucceed: true);
        foreach (var dir in new[] { cfg.WorkDir, AgentConfig.LogDir })
            Run("icacls", $"\"{dir}\" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q", mustSucceed: true);
    }

    public static string ClientInstructions(AgentConfig cfg)
    {
        var ips = AgentConfig.LocalAddresses();
        var sb = new StringBuilder();
        sb.AppendLine("PCS7 MCP Agent - configurazione per Claude Code sul PC client");
        sb.AppendLine("=============================================================");
        sb.AppendLine();
        sb.AppendLine("Macchina:          " + Environment.MachineName);
        sb.AppendLine("Indirizzi IP:      " + (ips.Count > 0 ? string.Join(", ", ips) : "(nessuno rilevato)"));
        sb.AppendLine("Porta:             " + cfg.Port);
        sb.AppendLine("Modalita':         " + cfg.AccessMode);
        sb.AppendLine("Client consentiti: " + (cfg.AllowedClients.Count > 0 ? string.Join(", ", cfg.AllowedClients) : "tutti (serve comunque il token)"));
        sb.AppendLine("Token:             " + cfg.Token);
        sb.AppendLine();
        sb.AppendLine("Sul PC con Claude Code, in %USERPROFILE%\\.claude.json dentro \"mcpServers\":");
        sb.AppendLine();
        sb.AppendLine(cfg.ClientSnippet(ips.FirstOrDefault()));
        sb.AppendLine();
        sb.AppendLine("Sostituire C:\\percorso\\pcs7-mcp con la cartella di pcs7-mcp sul PC.");
        sb.AppendLine("Per scrivere sul progetto servono read-write sia qui (agente) sia nel comando sul PC.");
        return sb.ToString();
    }

    public static void StopRunningAgents(int parentPid = 0)
    {
        var me = Process.GetCurrentProcess().Id;
        // Pcs7CfcReader may still be running for a request: it locks files under the install folder too.
        foreach (var p in Process.GetProcessesByName("Pcs7Agent").Concat(Process.GetProcessesByName("Pcs7CfcReader")).Where(p => p.Id != me && p.Id != parentPid))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }

    /// <summary>Starts the installed agent non-elevated in the desktop session (explorer.exe launches with the user's normal token).</summary>
    private static void StartAgentAsDesktopUser()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{InstalledExe}\"") { UseShellExecute = false }); }
        catch (Exception ex) { AgentLog.Write("cannot start agent after install: " + ex.Message); }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, target));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = file.Replace(source, target);
            for (int attempt = 0; ; attempt++)
            {
                try { File.Copy(file, dest, true); break; }
                catch (IOException) when (attempt < 10) { Thread.Sleep(500); }
            }
        }
    }

    private static void Run(string exe, string args, bool mustSucceed = false)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        AgentLog.Write($"setup: {exe} {args} -> exit {p.ExitCode}");
        if (mustSucceed && p.ExitCode != 0)
            throw new InvalidOperationException($"{exe} {args} failed (exit {p.ExitCode}): {output.Trim()}");
    }

    public static List<string> ParseClients(string text)
    {
        var list = new List<string>();
        foreach (var part in text.Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IPAddress.TryParse(part.Trim(), out var ip)) throw new ArgumentException("Indirizzo IP non valido: " + part);
            list.Add(ip.ToString());
        }
        return list.Distinct().ToList();
    }
}
