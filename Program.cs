using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BambuddyDesktop
{
    internal static class Program
    {
        internal static readonly string DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Bambuddy Desktop");

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Contains("--self-test")) return SelfTest.Run();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Directory.CreateDirectory(DataDirectory);
            bool browserTest = args.Contains("--browser-test");
            Application.Run(new ClientWindow(browserTest ? new Preferences() : Preferences.Load(), browserTest));
            return Environment.ExitCode;
        }

        // Parse untrusted address text once; the rest of the application uses Uri.
        internal static bool TryServer(string input, out Uri server)
        {
            server = null;
            var value = input.Trim();
            bool explicitScheme = value.Contains("://");
            Uri parsed;
            if (!Uri.TryCreate(explicitScheme ? value : "http://" + value, UriKind.Absolute, out parsed)
                || (parsed.Scheme != "http" && parsed.Scheme != "https")
                || string.IsNullOrEmpty(parsed.Host) || !string.IsNullOrEmpty(parsed.UserInfo)
                || parsed.HostNameType == UriHostNameType.Unknown)
                return false;

            var builder = new UriBuilder(parsed) { Fragment = "" };
            // A bare IP/host means Bambuddy's usual port. A complete URL is kept as entered.
            if (!explicitScheme && parsed.IsDefaultPort)
            {
                var authority = value.Split('/')[0];
                bool suppliedPort = authority.StartsWith("[")
                    ? authority.Contains("]:") : authority.Contains(":");
                if (!suppliedPort) builder.Port = 8000;
            }
            server = builder.Uri;
            return true;
        }
    }

    public sealed class Preferences
    {
        public string Server { get; set; } = "";
        public int Width { get; set; } = 1440;
        public int Height { get; set; } = 960;
        public bool Maximized { get; set; }
        private static string FilePath => Path.Combine(Program.DataDirectory, "settings.xml");

        internal static Preferences Load()
        {
            if (!File.Exists(FilePath)) return new Preferences();
            try
            {
                using (var reader = XmlReader.Create(FilePath, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
                    return (Preferences)new XmlSerializer(typeof(Preferences)).Deserialize(reader);
            }
            catch (Exception error) when (error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException)
            {
                MessageBox.Show("Не удалось прочитать настройки. Откроется окно выбора сервера.\n\n" + error.Message,
                    "Bambuddy Desktop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return new Preferences();
            }
        }

        internal void Save()
        {
            try
            {
                var temporary = FilePath + ".tmp";
                using (var stream = File.Create(temporary)) new XmlSerializer(typeof(Preferences)).Serialize(stream, this);
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, null);
                else File.Move(temporary, FilePath);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                MessageBox.Show("Не удалось сохранить настройки:\n" + error.Message, "Bambuddy Desktop",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    internal sealed class ServerDialog : Form
    {
        internal Uri Server { get; private set; }
        private readonly TextBox address;

        internal ServerDialog(string current)
        {
            Text = "Подключение к Bambuddy";
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(490, 210);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Controls.Add(new Label { Text = "IP-адрес или адрес сервера Bambuddy", AutoSize = true, Location = new Point(22, 20) });
            address = new TextBox { Text = current, Location = new Point(22, 54), Width = 445 };
            Controls.Add(address);
            Controls.Add(new Label { Text = "Например: 192.168.1.100 или http://192.168.1.100:8000\nБез порта используется 8000. Полный URL сохраняется как есть.",
                AutoSize = true, Location = new Point(22, 92), Font = new Font("Segoe UI", 9) });
            var connect = new Button { Text = "Подключиться", Location = new Point(320, 156), Size = new Size(147, 34) };
            var cancel = new Button { Text = "Отмена", Location = new Point(212, 156), Size = new Size(100, 34), DialogResult = DialogResult.Cancel };
            connect.Click += (_, __) =>
            {
                Uri server;
                if (!Program.TryServer(address.Text, out server))
                {
                    MessageBox.Show(this, "Укажи IP или URL с протоколом http:// или https:// без логина и пароля.",
                        "Проверь адрес", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    address.Focus();
                    return;
                }
                Server = server;
                DialogResult = DialogResult.OK;
            };
            Controls.Add(connect);
            Controls.Add(cancel);
            AcceptButton = connect;
            CancelButton = cancel;
            Shown += (_, __) => { address.Focus(); address.SelectAll(); };
        }
    }

    internal sealed class ClientWindow : Form
    {
        private readonly Preferences preferences;
        private readonly WebView2 view = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(23, 27, 31) };
        private readonly ToolStripLabel status = new ToolStripLabel("Подключение…");
        private readonly Panel errorPanel = new Panel { Dock = DockStyle.Fill, Visible = false, BackColor = Color.FromArgb(23, 27, 31) };
        private readonly Label errorMessage = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White, Padding = new Padding(30) };
        private Uri server;
        private readonly bool browserTest;

        internal ClientWindow(Preferences settings, bool browserTest = false)
        {
            this.browserTest = browserTest;
            if (browserTest) { Opacity = 0; ShowInTaskbar = false; }
            preferences = settings;
            Text = "Bambuddy Desktop";
            using (var iconStream = typeof(ClientWindow).Assembly.GetManifestResourceStream("BambuddyDesktop.Icon"))
                Icon = new Icon(iconStream);
            Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            // Keep a desktop layout, while allowing the window to fit smaller monitors.
            var workArea = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Math.Max(1100, settings.Width), workArea.Width),
                Math.Min(Math.Max(650, settings.Height), workArea.Height));
            MinimumSize = new Size(Math.Min(1100, workArea.Width), Math.Min(650, workArea.Height));
            if (settings.Maximized) WindowState = FormWindowState.Maximized;

            var toolbar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6, 3, 6, 3) };
            var back = new ToolStripButton("Назад");
            back.Click += (_, __) => { if (view.CanGoBack) view.GoBack(); };
            var home = new ToolStripButton("Главная");
            home.Click += (_, __) => Navigate();
            var reload = new ToolStripButton("Обновить");
            reload.ToolTipText = "Обновить — Ctrl+R. Без кэша — Ctrl+Shift+R";
            reload.Click += (_, __) => Reload(false);
            var change = new ToolStripButton("Сервер…");
            change.Click += (_, __) => ChooseServer();
            toolbar.Items.AddRange(new ToolStripItem[] { back, home, reload, new ToolStripSeparator(), change, new ToolStripSeparator(), status });
            status.Overflow = ToolStripItemOverflow.AsNeeded;
            errorPanel.Controls.Add(errorMessage);
            var retry = new Button { Text = "Повторить подключение", Dock = DockStyle.Bottom, Height = 44 };
            retry.Click += (_, __) => Navigate();
            errorPanel.Controls.Add(retry);
            Controls.Add(view);
            Controls.Add(errorPanel);
            Controls.Add(toolbar);
            Shown += async (_, __) => await InitializeBrowser();
            FormClosing += (_, __) =>
            {
                if (browserTest) return;
                preferences.Maximized = WindowState == FormWindowState.Maximized;
                var normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                preferences.Width = normal.Width;
                preferences.Height = normal.Height;
                preferences.Save();
            };
        }

        private async Task InitializeBrowser()
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null,
                    browserTest ? Path.Combine(Path.GetTempPath(), "BambuddyDesktop-browser-test-" + Process.GetCurrentProcess().Id)
                        : Path.Combine(Program.DataDirectory, "WebView2"));
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = true;
                core.NavigationStarting += (_, __) => { errorPanel.Visible = false; status.Text = "Загрузка…"; };
                core.NavigationCompleted += (_, e) =>
                {
                    status.Text = server?.Authority ?? "Bambuddy";
                    if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                        ShowError("Не удалось открыть Bambuddy.\n\n" + server + "\n" + e.WebErrorStatus +
                            "\n\nПроверь, что сервер работает и ПК подключён к его сети.");
                };
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    Uri link;
                    if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out link)
                        && (link.Scheme == "https" || link.Scheme == "http"))
                        Process.Start(new ProcessStartInfo(link.AbsoluteUri) { UseShellExecute = true });
                };
                view.KeyDown += (_, e) =>
                {
                    if (e.KeyCode == Keys.F5 || (e.Control && e.KeyCode == Keys.R))
                    {
                        e.Handled = true;
                        e.SuppressKeyPress = true;
                        Reload(e.Shift || (e.Control && e.KeyCode == Keys.F5));
                    }
                };
                // On high-DPI / small screens keep at least 1100 CSS pixels for the desktop layout.
                var desktopScale = Math.Min(1.0, view.ClientSize.Width / (1100.0 * DeviceDpi / 96.0));
                view.ZoomFactor = Math.Max(0.25, desktopScale);
                if (browserTest)
                {
                    core.NavigationCompleted += async (_, e) =>
                    {
                        var width = await core.ExecuteScriptAsync("window.innerWidth");
                        bool passed = e.IsSuccess && int.Parse(width) >= 1099;
                        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser-test.txt"),
                            (passed ? "PASS" : "FAIL") + ": WebView2 " + environment.BrowserVersionString +
                            "; desktop viewport=" + width + "; zoom=" + view.ZoomFactor + "; DPI=" + DeviceDpi);
                        Environment.ExitCode = passed ? 0 : 1;
                        Close();
                    };
                    core.NavigateToString("<!doctype html><meta name='viewport' content='width=device-width, initial-scale=1'><title>Bambuddy Desktop test</title><h1>Runtime test</h1>");
                    return;
                }
                if (Program.TryServer(preferences.Server, out server)) Navigate();
                else ChooseServer();
            }
            catch (Exception error)
            {
                if (browserTest)
                {
                    File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser-test.txt"), "FAIL: " + error);
                    Environment.ExitCode = 1;
                    Close();
                    return;
                }
                // Runtime initialization is an external boundary; show a usable error instead of crashing.
                ShowError("Не удалось запустить WebView2.\n\n" + error.Message +
                    "\n\nДля клиента нужна Microsoft Edge WebView2 Runtime.");
            }
        }

        private void ChooseServer()
        {
            if (view.CoreWebView2 == null) return;
            using (var dialog = new ServerDialog(server?.AbsoluteUri ?? ""))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    if (server == null) Close();
                    return;
                }
                server = dialog.Server;
                preferences.Server = server.AbsoluteUri;
                preferences.Save();
                Navigate();
            }
        }

        private void Navigate()
        {
            if (server == null || view.CoreWebView2 == null) return;
            errorPanel.Visible = false;
            view.CoreWebView2.Navigate(server.AbsoluteUri);
        }

        private async void Reload(bool bypassCache)
        {
            if (view.CoreWebView2 == null) return;
            errorPanel.Visible = false;
            if (!bypassCache) view.Reload();
            else await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}");
        }

        private void ShowError(string message)
        {
            status.Text = "Нет подключения";
            errorMessage.Text = message;
            errorPanel.Visible = true;
            errorPanel.BringToFront();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) view.Dispose();
            base.Dispose(disposing);
        }
    }

    internal static class SelfTest
    {
        internal static int Run()
        {
            var valid = new[] {
                new[] { "192.168.1.100", "http://192.168.1.100:8000/" },
                new[] { " 192.168.1.100:8001 ", "http://192.168.1.100:8001/" },
                new[] { "bambuddy.local", "http://bambuddy.local:8000/" },
                new[] { "bambuddy.local:80", "http://bambuddy.local/" },
                new[] { "https://bambuddy.example/", "https://bambuddy.example/" },
                new[] { "http://192.168.1.100", "http://192.168.1.100/" },
                new[] { "https://bambuddy.example/base/", "https://bambuddy.example/base/" },
                new[] { "[::1]", "http://[::1]:8000/" },
                new[] { "[::1]:8001", "http://[::1]:8001/" }
            };
            var failures = new System.Collections.Generic.List<string>();
            foreach (var item in valid)
            {
                Uri uri;
                if (!Program.TryServer(item[0], out uri) || uri.AbsoluteUri != item[1]) failures.Add("Valid address: " + item[0]);
            }
            foreach (var value in new[] { "", "http://", "ftp://example.com", "javascript:alert(1)", "http://user:password@host", "host:99999", "bad host" })
            {
                Uri uri;
                if (Program.TryServer(value, out uri)) failures.Add("Invalid address accepted: " + value);
            }
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "self-test.txt"),
                failures.Count == 0 ? "PASS: 16 server address cases.\r\n" : string.Join("\r\n", failures));
            return failures.Count == 0 ? 0 : 1;
        }
    }
}
