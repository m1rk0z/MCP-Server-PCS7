using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Pcs7Core;

namespace Pcs7Agent;

/// <summary>
///   Pcs7Agent.exe                 start the agent (tray icon + HTTP endpoint)
///   Pcs7Agent.exe install [--port N] [--access-mode read-only|read-write] [--allow ip,ip] [--new-token] [--silent]
///   Pcs7Agent.exe uninstall [--purge] [--silent]
///   Pcs7Agent.exe config          show the client configuration (token, .claude.json block)
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        CoreLog.Sink = msg => AgentLog.Write("[core] " + msg);
        var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "run";
        var silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        try
        {
            switch (command)
            {
                case "run": return RunAgent();
                case "install": return Install(args, silent);
                case "uninstall": return Uninstall(args, silent);
                case "config": return ShowConfig();
                default:
                    MessageBox.Show("Comando sconosciuto: " + command + "\n\nUso: Pcs7Agent.exe [install|uninstall|config]", Installer.AppName);
                    return 2;
            }
        }
        catch (Exception ex)
        {
            AgentLog.Write($"{command} failed: {ex}");
            if (!silent) MessageBox.Show(ex.Message, Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int RunAgent()
    {
        using var mutex = new Mutex(true, @"Global\Pcs7Agent", out var first);
        if (!first) return 0; // already running in this or another session
        AgentLog.Cleanup();
        var config = AgentConfig.Load();
        AgentLog.Write($"agent {typeof(Program).Assembly.GetName().Version} starting as {Environment.UserDomainName}\\{Environment.UserName}");
        Application.Run(new TrayApp(config));
        return 0;
    }

    private static int Install(string[] args, bool silent)
    {
        if (!Installer.IsAdministrator())
        {
            // The elevated copy installs; this non-elevated process then starts the agent as the right user.
            var code = Installer.RelaunchElevated(args);
            if (code == 0) Installer.StartInstalledAgent();
            return code;
        }
        int.TryParse(Opt(args, Installer.ElevatedChildFlag), out var parentPid);
        var elevatedChild = parentPid > 0;

        var current = AgentConfig.LoadOrDefault();
        InstallSettings settings;
        if (silent)
        {
            settings = new InstallSettings
            {
                Port = int.TryParse(Opt(args, "--port"), out var p) ? p : current.Port,
                AccessMode = Opt(args, "--access-mode") is { } m ? CoreOptions.AccessModeName(CoreOptions.ParseAccessMode(m)) : current.AccessMode,
                AllowedClients = Opt(args, "--allow") is { } allow ? Installer.ParseClients(allow) : current.AllowedClients,
                NewToken = args.Contains("--new-token", StringComparer.OrdinalIgnoreCase),
            };
        }
        else
        {
            if (Opt(args, "--port") is { } p && int.TryParse(p, out var port)) current.Port = port;
            using var form = new InstallForm(current);
            if (form.ShowDialog() != DialogResult.OK || form.Settings is null) return 1;
            settings = form.Settings;
        }

        var cfg = Installer.Install(settings, msg => AgentLog.Write("setup: " + msg), startAgent: !elevatedChild, parentPid: parentPid);
        if (!silent)
        {
            using var f = new ClientConfigForm(cfg, "Installazione completata: l'agente parte alla chiusura di questa finestra (icona S7 nell'area di notifica).");
            f.ShowDialog();
        }
        return 0;
    }

    private static int Uninstall(string[] args, bool silent)
    {
        if (!Installer.IsAdministrator())
            return Installer.RelaunchElevated(args);
        var purge = args.Contains("--purge", StringComparer.OrdinalIgnoreCase);
        if (!silent)
        {
            var answer = MessageBox.Show(
                "Disinstallare " + Installer.AppName + "?\n\nSi' = rimuove il programma e mantiene configurazione, token ed export in " + AgentConfig.DataDir +
                "\nNo = rimuove anche configurazione, log ed export",
                Installer.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return 1;
            purge = answer == DialogResult.No;
        }
        int.TryParse(Opt(args, Installer.ElevatedChildFlag), out var parentPid);
        Installer.Uninstall(purge, msg => AgentLog.Write("uninstall: " + msg), parentPid);
        if (!silent) MessageBox.Show(Installer.AppName + " disinstallato.", Installer.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    private static int ShowConfig()
    {
        using var f = new ClientConfigForm(AgentConfig.Load());
        f.ShowDialog();
        return 0;
    }

    private static string? Opt(string[] args, string name)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
