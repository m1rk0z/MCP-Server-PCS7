using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Pcs7Agent;

/// <summary>Install dialog: port, access mode, allowed clients, token regeneration.</summary>
public sealed class InstallForm : Form
{
    private readonly NumericUpDown _port = new() { Minimum = Installer.MinPort, Maximum = Installer.MaxPort, Width = 90 };
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _clients = new() { Width = 260 };
    private readonly CheckBox _newToken = new() { Text = "Genera un nuovo token (i PC gia' configurati andranno aggiornati)", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _ok = new() { Text = "Installa", Width = 100 };
    private readonly Button _cancel = new() { Text = "Annulla", Width = 100, DialogResult = DialogResult.Cancel };

    public InstallSettings? Settings { get; private set; }

    public InstallForm(AgentConfig current)
    {
        Text = Installer.AppName + " - installazione";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        Font = SystemFonts.MessageBoxFont;

        _mode.Items.AddRange(new object[] { "read-only  (sola lettura)", "read-write (lettura e scrittura)" });
        _mode.SelectedIndex = current.IsReadWrite ? 1 : 0;
        _port.Value = Math.Max(Installer.MinPort, Math.Min(Installer.MaxPort, current.Port));
        _clients.Text = string.Join(", ", current.AllowedClients);
        var hasToken = !string.IsNullOrWhiteSpace(current.Token);
        _newToken.Visible = hasToken;

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 12, 3) });
            grid.Controls.Add(c);
        }
        grid.Controls.Add(new Label
        {
            Text = "Agente per Claude Code: espone il progetto PCS 7 di questa macchina al server MCP sul PC.\n" +
                   "Installazione in " + Installer.InstallDir + ", avvio automatico all'accesso dell'utente.",
            AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 3, 3, 12),
        });
        grid.SetColumnSpan(grid.Controls[0], 2);
        Row("Porta TCP:", _port);
        Row("Modalita':", _mode);
        Row("IP client consentiti:", _clients);
        grid.Controls.Add(new Label { Text = "" });
        grid.Controls.Add(new Label { Text = "vuoto = qualsiasi PC (serve comunque il token); es. 192.168.56.1", AutoSize = true, ForeColor = Color.DimGray });
        grid.Controls.Add(new Label { Text = "" });
        grid.Controls.Add(_newToken);
        grid.Controls.Add(new Label { Text = "" });
        grid.Controls.Add(_status);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);
        grid.Controls.Add(buttons);
        grid.SetColumnSpan(buttons, 2);
        Controls.Add(grid);

        AcceptButton = _ok;
        CancelButton = _cancel;
        _ok.Click += (_, _) => Confirm();
    }

    private void Confirm()
    {
        try
        {
            if (_mode.SelectedIndex == 1 &&
                MessageBox.Show(this,
                    "In modalita' read-write Claude potra' importare e compilare sorgenti, compilare chart e hardware, " +
                    "salvare il progetto e scrivere variabili OPC UA sul processo in esercizio (sempre con anteprima e conferma).\n\n" +
                    "Usarla solo su copie del progetto o con un backup. Continuare?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            Settings = new InstallSettings
            {
                Port = (int)_port.Value,
                AccessMode = _mode.SelectedIndex == 1 ? "read-write" : "read-only",
                AllowedClients = Installer.ParseClients(_clients.Text),
                NewToken = _newToken.Checked,
            };
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Shows the connection data and the block to paste in .claude.json on the Claude Code PC.</summary>
public sealed class ClientConfigForm : Form
{
    public ClientConfigForm(AgentConfig cfg, string? headline = null)
    {
        Text = Installer.AppName + " - configurazione client";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(760, 560);
        MinimumSize = new Size(500, 350);
        Font = SystemFonts.MessageBoxFont;

        var text = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9.5f), Text = Installer.ClientInstructions(cfg).Replace("\n", "\r\n").Replace("\r\r", "\r"),
            BackColor = SystemColors.Window,
        };
        var top = new Label
        {
            Dock = DockStyle.Top, AutoSize = false, Height = headline is null ? 0 : 32, Text = headline ?? "",
            ForeColor = Color.DarkGreen, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Padding = new Padding(8, 8, 8, 0),
        };
        var copySnippet = new Button { Text = "Copia blocco .claude.json", AutoSize = true };
        var copyToken = new Button { Text = "Copia token", AutoSize = true };
        var close = new Button { Text = "Chiudi", AutoSize = true, DialogResult = DialogResult.OK };
        copySnippet.Click += (_, _) => Clipboard.SetText(cfg.ClientSnippet(AgentConfig.LocalAddresses().FirstOrDefault()));
        copyToken.Click += (_, _) => Clipboard.SetText(cfg.Token);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange(new Control[] { close, copyToken, copySnippet });

        Controls.Add(text);
        Controls.Add(buttons);
        Controls.Add(top);
        AcceptButton = close;
    }
}

/// <summary>Tray icon of the running agent.</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly AgentServer? _server;
    private readonly AgentConfig _config;

    public TrayApp(AgentConfig config)
    {
        _config = config;
        string status;
        try
        {
            _server = new AgentServer(config);
            _server.Start();
            status = $"porta {config.Port} - {(config.IsReadWrite ? "lettura e scrittura" : "sola lettura")}";
        }
        catch (Exception ex)
        {
            _server?.Dispose();
            _server = null;
            status = "NON ATTIVO: " + ex.Message;
            AgentLog.Write("start failed: " + ex);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Installer.AppName + " - " + status) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Configurazione per Claude Code...", null, (_, _) => ShowClientConfig());
        menu.Items.Add("Apri cartella export", null, (_, _) => Open(config.WorkDir));
        menu.Items.Add("Apri cartella log", null, (_, _) => Open(AgentConfig.LogDir));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Esci", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = MakeIcon(_server is null ? Color.Firebrick : config.IsReadWrite ? Color.DarkOrange : Color.SeaGreen),
            Text = Truncate("PCS7 MCP Agent - " + status, 63),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => ShowClientConfig();
        if (_server is null)
            _icon.ShowBalloonTip(10000, Installer.AppName, status, ToolTipIcon.Error);
    }

    private void ShowClientConfig()
    {
        using var f = new ClientConfigForm(_config);
        f.ShowDialog();
    }

    private static void Open(string dir)
    {
        try { System.IO.Directory.CreateDirectory(dir); Process.Start("explorer.exe", "\"" + dir + "\""); } catch { }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n);

    /// <summary>Draws the tray icon at runtime (green = read-only, orange = read-write, red = not running).</summary>
    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, 0, 0, 16, 16);
            using var font = new Font("Arial", 7f, FontStyle.Bold, GraphicsUnit.Point);
            TextRenderer.DrawText(g, "S7", font, new Rectangle(0, 0, 16, 16), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _server?.Dispose();
        AgentLog.Write("agent stopped");
        base.ExitThreadCore();
    }
}
