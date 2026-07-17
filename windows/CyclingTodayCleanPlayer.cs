using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new CleanPlayerForm(args));
    }
}

internal sealed class CleanPlayerForm : Form
{
    private const string SourcePageUrl = "https://cycling.today/";


    private const int PlayerFirstCheckDelayMilliseconds = 8000;
    private const int PlayerSecondCheckDelayMilliseconds = 12000;
    private const int CastDiscoveryMilliseconds = 5000;
    private const int CastDiscoveryProcessTimeoutMilliseconds = 10000;
    private const int CastRestartDelayMilliseconds = 2500;
    private const int MaxRapidCastRestartAttempts = 3;
    private const int StableCastSeconds = 30;
    private const int MaxVisibleLogLines = 4;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const byte VirtualKeyLeftWindows = 0x5B;
    private const byte VirtualKeyK = 0x4B;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint ExecutionStateContinuous = 0x80000000;
    private const uint ExecutionStateSystemRequired = 0x00000001;
    private const uint ExecutionStateDisplayRequired = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    private readonly WebView2 webView;
    private readonly HashSet<string> blockedHosts;
    private readonly string logPath;
    private readonly bool enableCleaner;
    private readonly bool enableBlocking;
    private readonly bool enableAutoClick;
    private readonly List<string> visibleLogLines = new List<string>();
    private TableLayoutPanel mainLayout;
    private Control statusPanel;
    private Label statusLabel;
    private Button unmuteButton;
    private Button miracastButton;
    private Button directCastButton;
    private Button autoCastButton;
    private Button reloadButton;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private bool fullScreen;
    private int blockedRequestCount;
    private bool navigationStarted;
    private bool navigationCompleted;
    private int navigationGeneration;
    private bool playerDetected;
    private bool autoClickScheduled;
    private bool autoFullScreenScheduled;
    private string latestCastMediaUrl;
    private string latestCastCookie;
    private string latestCastReferer;
    private string latestCastOrigin;
    private string latestCastUserAgent;
    private Process go2TvProcess;
    private Process ffmpegProcess;
    private int castErrorLineCount;
    private bool castStopRequested = true;
    private bool castRestartPending;
    private int castSessionId;
    private int castRestartAttempt;
    private DateTime castStartedAtUtc;
    private string activeGo2TvPath;
    private string activeFfmpegPath;
    private string activeCastTargetUrl;

    private bool deviceSearchInProgress;

    private sealed class CastDevice
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public string Type { get; set; }

        public string DisplayName
        {
            get { return Name + "   [" + Type + "]   " + Url; }
        }
    }
    public CleanPlayerForm(string[] args)
    {
        Text = "Cycling Today Clean Player";
        BackColor = Color.Black;
        ClientSize = new Size(1280, 720);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        enableCleaner = !HasArg(args, "--no-clean");
        enableBlocking = !HasArg(args, "--no-block");
        enableAutoClick = !HasArg(args, "--no-auto-click");
        blockedHosts = BuildBlockedHosts();
        logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyclingTodayCleanPlayer",
            "last-run.log");
        ResetLog();

        System.Drawing.Icon appIcon = LoadAppIcon();
        if (appIcon != null)
        {
            Icon = appIcon;
        }

        webView = new WebView2();
        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.Black;

        TableLayoutPanel layout = new TableLayoutPanel();
        mainLayout = layout;
        layout.Dock = DockStyle.Fill;
        layout.BackColor = Color.Black;
        layout.ColumnCount = 1;
        layout.RowCount = 2;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 86F));
        layout.Controls.Add(webView, 0, 0);
        layout.Controls.Add(CreateStatusPanel(), 0, 1);
        Controls.Add(layout);
        AddVisibleStatus("Starting player...");

        Shown += async delegate { await InitializeAsync(); };
        Activated += OnActivated;
        FormClosed += OnFormClosed;
        KeyDown += OnKeyDown;
    }

    private Control CreateStatusPanel()
    {
        Panel panel = new Panel();
        panel.Dock = DockStyle.Fill;
        panel.BackColor = Color.FromArgb(22, 22, 22);
        panel.Padding = new Padding(10, 8, 10, 8);

        FlowLayoutPanel buttons = new FlowLayoutPanel();
        buttons.Dock = DockStyle.Right;
        buttons.Width = 550;
        buttons.FlowDirection = FlowDirection.LeftToRight;
        buttons.WrapContents = false;
        buttons.Padding = new Padding(0, 12, 0, 0);
        buttons.BackColor = Color.Transparent;

        unmuteButton = new Button();
        unmuteButton.Text = "Unmute / Resume";
        unmuteButton.Width = 140;
        unmuteButton.Height = 34;
        unmuteButton.Click += delegate { SendManualResumeClick("button"); };

        miracastButton = new Button();
        miracastButton.Text = "Miracast";
        miracastButton.Width = 90;
        miracastButton.Height = 34;
        miracastButton.Click += delegate { OpenWindowsCastPanel(); };

        directCastButton = new Button();
        directCastButton.Text = "Direct Send";
        directCastButton.Width = 100;
        directCastButton.Height = 34;
        directCastButton.Click += delegate { ToggleDirectCast(); };

        autoCastButton = new Button();
        autoCastButton.Text = "Auto Cast";
        autoCastButton.Width = 85;
        autoCastButton.Height = 34;
        autoCastButton.Click += delegate { CastToTv(true); };

        reloadButton = new Button();
        reloadButton.Text = "Refresh";
        reloadButton.Width = 100;
        reloadButton.Height = 34;
        reloadButton.Click += delegate
        {
            NavigateToSourcePage();
            StartPlayerWatchdogAsync();
        };

        buttons.Controls.Add(unmuteButton);
        buttons.Controls.Add(miracastButton);
        buttons.Controls.Add(directCastButton);
        buttons.Controls.Add(autoCastButton);
        buttons.Controls.Add(reloadButton);

        statusLabel = new Label();
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.Gainsboro;
        statusLabel.BackColor = Color.Transparent;
        statusLabel.Font = new Font("Consolas", 9F, FontStyle.Regular);
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusLabel.AutoEllipsis = true;
        statusLabel.Text = "Starting player...";

        panel.Controls.Add(statusLabel);
        panel.Controls.Add(buttons);
        statusPanel = panel;
        return panel;
    }

    private void AddVisibleStatus(string message)
    {
        if (String.IsNullOrWhiteSpace(message))
        {
            return;
        }

        string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        visibleLogLines.Add(line);
        while (visibleLogLines.Count > MaxVisibleLogLines)
        {
            visibleLogLines.RemoveAt(0);
        }

        if (statusLabel == null || statusLabel.IsDisposed)
        {
            return;
        }

        Action update = delegate { statusLabel.Text = String.Join("\r\n", visibleLogLines.ToArray()); };
        if (statusLabel.InvokeRequired)
        {
            try { statusLabel.BeginInvoke(update); } catch { }
        }
        else
        {
            update();
        }
    }
    private async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CyclingTodayCleanPlayer",
                "WebView2");

            Directory.CreateDirectory(userData);

            CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions(
                "--autoplay-policy=no-user-gesture-required --disable-features=msWebOOUI,msPdfOOUI");
            CoreWebView2Environment environment =
                await CoreWebView2Environment.CreateAsync(null, userData, options);

            await webView.EnsureCoreWebView2Async(environment);
            PreventSystemIdle();
            Log("WebView2 initialized");
            ConfigureWebView();
            NavigateToSourcePage();
            StartPlayerWatchdogAsync();
        }
        catch (Exception ex)
        {
            Log("Startup error: " + ex.Message);
            MessageBox.Show(
                "WebView2 could not start.\r\n\r\n" + ex.Message,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ConfigureWebView()
    {
        CoreWebView2 core = webView.CoreWebView2;

        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDefaultScriptDialogsEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;

        // Observe media requests for casting and apply host blocking when enabled.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.WebResourceResponseReceived += OnWebResourceResponseReceived;

        core.NewWindowRequested += OnNewWindowRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.FrameNavigationStarting += OnFrameNavigationStarting;
        core.PermissionRequested += OnPermissionRequested;
        core.ContainsFullScreenElementChanged += OnContainsFullScreenElementChanged;
        core.NavigationCompleted += OnNavigationCompleted;
        core.DOMContentLoaded += OnDomContentLoaded;

        if (enableCleaner)
        {
            string cleanerScript = LoadCleanerScript();
            if (String.IsNullOrWhiteSpace(cleanerScript))
            {
                Log("Cleaner script missing; page will not be cropped.");
            }
            else
            {
                core.AddScriptToExecuteOnDocumentCreatedAsync(cleanerScript);
            }
        }

        Log("Mode: cleaner=" + enableCleaner + " blocking=" + enableBlocking + " autoClick=" + enableAutoClick);
    }

    private void PreventSystemIdle()
    {
        try
        {
            SetThreadExecutionState(ExecutionStateContinuous | ExecutionStateSystemRequired | ExecutionStateDisplayRequired);
            Log("System/display idle prevention enabled.");
        }
        catch
        {
        }
    }

    private void AllowSystemIdle()
    {
        try
        {
            SetThreadExecutionState(ExecutionStateContinuous);
        }
        catch
        {
        }
    }

    private async void SendManualResumeClick(string reason)
    {
        Point[] points = await GetAutoClickPointsAsync();
        if (points.Length == 0)
        {
            Log("Manual resume skipped: player click points unavailable. reason=" + reason);
            AddVisibleStatus("Resume skipped: player is not ready yet.");
            return;
        }

        await AutoClickPlayerUnmuteAsync(points);
        Log("Manual resume click sent. reason=" + reason);
    }

    private void ToggleDirectCast()
    {
        if (!castStopRequested)
        {
            StopCastProcesses();
            Log("DLNA relay stopped from the Stop Cast button.");
            return;
        }

        CastToTv(false);
    }

    private async void CastToTv(bool automatic)
    {
        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        string go2TvPath = Path.Combine(baseDirectory, "go2tv-lite.exe");
        string discoveryPath = Path.Combine(baseDirectory, "go2tv-discover.exe");
        string ffmpegPath = Path.Combine(baseDirectory, "ffmpeg.exe");
        string reason = null;
        if (String.IsNullOrWhiteSpace(latestCastMediaUrl))
        {
            reason = "No HLS/DASH stream has been detected yet.";
        }
        else if (!File.Exists(go2TvPath))
        {
            reason = "go2tv-lite.exe is missing from the application folder.";
        }
        else if (!File.Exists(ffmpegPath))
        {
            reason = "ffmpeg.exe is missing from the application folder.";
        }
        else if (!File.Exists(discoveryPath))
        {
            reason = "go2tv-discover.exe is missing from the application folder.";
        }

        if (reason != null)
        {
            if (automatic)
            {
                Log(reason + " Auto Cast is opening Windows screen mirroring instead.");
                OpenWindowsCastPanel();
            }
            else
            {
                Log(reason + " Direct Send is not ready.");
            }
            return;
        }
        if (deviceSearchInProgress)
        {
            return;
        }

        deviceSearchInProgress = true;
        UpdateDirectCastButton();
        try
        {
            while (!IsDisposed && !Disposing)
            {
                Log("Searching the local network for compatible TVs...");
                List<CastDevice> devices = await DiscoverCastDevicesAsync(discoveryPath);
                if (IsDisposed || Disposing)
                {
                    return;
                }

                if (devices.Count == 0)
                {
                    Log("No compatible DLNA or Chromecast devices were found.");
                    DialogResult retry = MessageBox.Show(
                        this,
                        "No compatible TVs were found on this network.",
                        "Choose a TV",
                        MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Information);
                    if (retry == DialogResult.Retry)
                    {
                        continue;
                    }
                    if (automatic)
                    {
                        Log("Auto Cast is opening Windows screen mirroring instead.");
                        OpenWindowsCastPanel();
                    }
                    return;
                }

                Log("Device search found " + devices.Count + " compatible target(s).");
                bool rescan;
                CastDevice selected = SelectCastDevice(devices, GetPreferredCastTargetUrl(), out rescan);
                if (rescan)
                {
                    continue;
                }
                if (selected == null)
                {
                    Log("Cast device selection canceled.");
                    return;
                }

                SavePreferredCastTarget(selected.Url);
                Log("Cast target selected: " + selected.Name + " type=" + selected.Type +
                    " url=" + GetSafeUrlForLog(selected.Url));
                StartRelayedDlnaCast(go2TvPath, ffmpegPath, selected.Url);
                return;
            }
        }
        catch (Exception ex)
        {
            StopCastProcesses();
            Log("TV discovery or DLNA relay failed: " + ex.Message);
            MessageBox.Show(this, ex.Message, "Cast failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (automatic)
            {
                Log("Auto Cast is opening Windows screen mirroring instead.");
                OpenWindowsCastPanel();
            }
        }
        finally
        {
            deviceSearchInProgress = false;
            UpdateDirectCastButton();
        }
    }
    private async Task<List<CastDevice>> DiscoverCastDevicesAsync(string discoveryPath)
    {
        return await Task.Run(delegate
        {
            ProcessStartInfo start = new ProcessStartInfo();
            start.FileName = discoveryPath;
            start.Arguments = CastDiscoveryMilliseconds.ToString(CultureInfo.InvariantCulture);
            start.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
            start.UseShellExecute = false;
            start.CreateNoWindow = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;

            using (Process process = new Process())
            {
                process.StartInfo = start;
                if (!process.Start())
                {
                    throw new InvalidOperationException("TV discovery helper did not start.");
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(CastDiscoveryProcessTimeoutMilliseconds))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("TV discovery timed out.");
                }

                Task.WaitAll(new Task[] { outputTask, errorTask });
                string error = errorTask.Result.Trim();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "TV discovery failed with exit code " + process.ExitCode +
                        (error.Length == 0 ? "." : ": " + error));
                }

                return ParseCastDevices(outputTask.Result);
            }
        });
    }

    private static List<CastDevice> ParseCastDevices(string output)
    {
        List<CastDevice> result = new List<CastDevice>();
        HashSet<string> seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] lines = (output ?? "").Replace("\r", "").Split('\n');
        foreach (string rawLine in lines)
        {
            string[] fields = rawLine.Split('\t');
            if (fields.Length < 3)
            {
                continue;
            }

            string type = fields[0].Trim();
            string name = fields[1].Trim();
            string url = fields[2].Trim().TrimEnd('/');
            Uri parsed;
            if (!Uri.TryCreate(url, UriKind.Absolute, out parsed) ||
                (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) ||
                (!String.Equals(type, "DLNA", StringComparison.OrdinalIgnoreCase) &&
                 !String.Equals(type, "Chromecast", StringComparison.OrdinalIgnoreCase)) ||
                !seenUrls.Add(url))
            {
                continue;
            }

            result.Add(new CastDevice
            {
                Name = name.Length == 0 ? parsed.Host : name,
                Type = type,
                Url = url
            });
        }

        result.Sort(delegate(CastDevice left, CastDevice right)
        {
            int byType = StringComparer.OrdinalIgnoreCase.Compare(left.Type, right.Type);
            return byType != 0 ? byType : StringComparer.CurrentCultureIgnoreCase.Compare(left.Name, right.Name);
        });
        return result;
    }
    private CastDevice SelectCastDevice(List<CastDevice> devices, string preferredUrl, out bool rescan)
    {
        rescan = false;
        using (Form dialog = new Form())
        {
            dialog.Text = "Choose a TV";
            dialog.ClientSize = new Size(760, 330);
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.BackColor = Color.FromArgb(245, 243, 236);
            dialog.ForeColor = Color.FromArgb(28, 34, 33);

            Label heading = new Label();
            heading.Text = "Choose where to play";
            heading.Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold);
            heading.AutoSize = true;
            heading.Location = new Point(22, 18);

            Label hint = new Label();
            hint.Text = devices.Count + " compatible device(s) found on this network.";
            hint.Font = new Font("Segoe UI", 9.5F);
            hint.AutoSize = true;
            hint.Location = new Point(25, 55);

            ListBox list = new ListBox();
            list.Location = new Point(25, 82);
            list.Size = new Size(710, 170);
            list.Font = new Font("Segoe UI", 10F);
            list.DisplayMember = "DisplayName";
            foreach (CastDevice device in devices)
            {
                list.Items.Add(device);
            }

            Button castButton = new Button();
            castButton.Text = "Cast to selected TV";
            castButton.Size = new Size(155, 36);
            castButton.Location = new Point(580, 272);
            castButton.DialogResult = DialogResult.OK;
            castButton.Enabled = false;

            Button scanButton = new Button();
            scanButton.Text = "Scan Again";
            scanButton.Size = new Size(105, 36);
            scanButton.Location = new Point(355, 272);
            scanButton.DialogResult = DialogResult.Retry;

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Size = new Size(95, 36);
            cancelButton.Location = new Point(475, 272);
            cancelButton.DialogResult = DialogResult.Cancel;

            list.SelectedIndexChanged += delegate { castButton.Enabled = list.SelectedItem != null; };
            list.DoubleClick += delegate
            {
                if (list.SelectedItem != null)
                {
                    dialog.DialogResult = DialogResult.OK;
                    dialog.Close();
                }
            };

            dialog.Controls.Add(heading);
            dialog.Controls.Add(hint);
            dialog.Controls.Add(list);
            dialog.Controls.Add(scanButton);
            dialog.Controls.Add(cancelButton);
            dialog.Controls.Add(castButton);
            dialog.AcceptButton = castButton;
            dialog.CancelButton = cancelButton;

            int preferredIndex = -1;
            for (int i = 0; i < devices.Count; i++)
            {
                if (String.Equals(devices[i].Url, preferredUrl, StringComparison.OrdinalIgnoreCase))
                {
                    preferredIndex = i;
                    break;
                }
            }
            list.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;

            DialogResult result = dialog.ShowDialog(this);
            if (result == DialogResult.Retry)
            {
                rescan = true;
                return null;
            }
            return result == DialogResult.OK ? list.SelectedItem as CastDevice : null;
        }
    }
    private string GetPreferredCastTargetUrl()
    {
        string path = Path.Combine(Path.GetDirectoryName(logPath), "last-cast-target.txt");
        try
        {
            if (File.Exists(path))
            {
                string value = File.ReadAllText(path).Trim().TrimEnd('/');
                Uri parsed;
                if (Uri.TryCreate(value, UriKind.Absolute, out parsed) &&
                    (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                {
                    return value;
                }
            }
        }
        catch (Exception ex)
        {
            Log("Preferred cast target could not be read: " + ex.Message);
        }
        return null;
    }

    private void SavePreferredCastTarget(string url)
    {
        try
        {
            string directory = Path.GetDirectoryName(logPath);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "last-cast-target.txt"), url);
        }
        catch (Exception ex)
        {
            Log("Preferred cast target could not be saved: " + ex.Message);
        }
    }
    private void StartRelayedDlnaCast(string go2TvPath, string ffmpegPath, string targetUrl)
    {
        StopCastProcesses();
        activeGo2TvPath = go2TvPath;
        activeFfmpegPath = ffmpegPath;
        activeCastTargetUrl = targetUrl;
        castStopRequested = false;
        castRestartPending = false;
        castRestartAttempt = 0;
        UpdateDirectCastButton();
        StartRelayedDlnaCastCore();
    }

    private void StartRelayedDlnaCastCore()
    {
        if (castStopRequested || String.IsNullOrWhiteSpace(activeGo2TvPath) ||
            String.IsNullOrWhiteSpace(activeFfmpegPath) || String.IsNullOrWhiteSpace(activeCastTargetUrl))
        {
            throw new InvalidOperationException("Cast session is no longer active.");
        }
        if (String.IsNullOrWhiteSpace(latestCastMediaUrl))
        {
            throw new InvalidOperationException("The detected live stream is no longer available.");
        }

        string streamUrl = latestCastMediaUrl;
        castErrorLineCount = 0;
        castStartedAtUtc = DateTime.UtcNow;

        ProcessStartInfo go2TvStart = new ProcessStartInfo();
        go2TvStart.FileName = activeGo2TvPath;
        go2TvStart.Arguments = "-v - -t " + QuoteArgument(activeCastTargetUrl);
        go2TvStart.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
        go2TvStart.UseShellExecute = false;
        go2TvStart.CreateNoWindow = true;
        go2TvStart.RedirectStandardInput = true;
        go2TvStart.RedirectStandardOutput = true;
        go2TvStart.RedirectStandardError = true;

        Process go2Tv = new Process();
        go2Tv.StartInfo = go2TvStart;
        go2Tv.EnableRaisingEvents = true;
        go2Tv.Exited += OnGo2TvExited;
        go2Tv.OutputDataReceived += OnCastOutputDataReceived;
        go2Tv.ErrorDataReceived += OnCastErrorDataReceived;
        if (!go2Tv.Start())
        {
            go2Tv.Dispose();
            throw new InvalidOperationException("Go2TV did not start.");
        }
        go2TvProcess = go2Tv;
        go2Tv.BeginOutputReadLine();
        go2Tv.BeginErrorReadLine();

        ProcessStartInfo ffmpegStart = new ProcessStartInfo();
        ffmpegStart.FileName = activeFfmpegPath;
        ffmpegStart.Arguments = BuildFfmpegArguments(streamUrl);
        ffmpegStart.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
        ffmpegStart.UseShellExecute = false;
        ffmpegStart.CreateNoWindow = true;
        ffmpegStart.RedirectStandardOutput = true;
        ffmpegStart.RedirectStandardError = true;

        Process ffmpeg = new Process();
        ffmpeg.StartInfo = ffmpegStart;
        ffmpeg.EnableRaisingEvents = true;
        ffmpeg.Exited += OnFfmpegExited;
        ffmpeg.ErrorDataReceived += OnCastErrorDataReceived;
        if (!ffmpeg.Start())
        {
            ffmpeg.Dispose();
            throw new InvalidOperationException("FFmpeg did not start.");
        }
        ffmpegProcess = ffmpeg;
        ffmpeg.BeginErrorReadLine();

        Task.Run(async delegate
        {
            Exception pipeError = null;
            try
            {
                await ffmpeg.StandardOutput.BaseStream.CopyToAsync(go2Tv.StandardInput.BaseStream);
            }
            catch (Exception ex)
            {
                pipeError = ex;
            }
            finally
            {
                try { go2Tv.StandardInput.Close(); } catch { }
                QueueCastPipeEnded(go2Tv, ffmpeg, pipeError);
            }
        });
        Log("DLNA relay started via FFmpeg and Go2TV. target=" + activeCastTargetUrl +
            " stream=" + GetSafeUrlForLog(streamUrl) +
            " cookie=" + (!String.IsNullOrWhiteSpace(latestCastCookie)) +
            " referer=" + (!String.IsNullOrWhiteSpace(latestCastReferer)) +
            " restartAttempt=" + castRestartAttempt);
    }

    private string BuildFfmpegArguments(string streamUrl)
    {
        string arguments =
            "-hide_banner -loglevel warning -nostdin " +
            "-reconnect 1 -reconnect_streamed 1 -reconnect_delay_max 5 ";

        if (!String.IsNullOrWhiteSpace(latestCastUserAgent))
        {
            arguments += "-user_agent " + QuoteArgument(SanitizeHeaderValue(latestCastUserAgent)) + " ";
        }

        string headers = "";
        if (!String.IsNullOrWhiteSpace(latestCastReferer))
        {
            headers += "Referer: " + SanitizeHeaderValue(latestCastReferer) + "\r\n";
        }
        if (!String.IsNullOrWhiteSpace(latestCastOrigin))
        {
            headers += "Origin: " + SanitizeHeaderValue(latestCastOrigin) + "\r\n";
        }
        if (!String.IsNullOrWhiteSpace(latestCastCookie))
        {
            headers += "Cookie: " + SanitizeHeaderValue(latestCastCookie) + "\r\n";
        }
        if (headers.Length > 0)
        {
            arguments += "-headers " + QuoteArgument(headers) + " ";
        }

        arguments +=
            "-i " + QuoteArgument(streamUrl) + " " +
            "-map 0:v:0? -map 0:a:0? -c copy " +
            "-mpegts_flags +resend_headers -muxdelay 0 -f mpegts pipe:1";
        return arguments;
    }

    private static string SanitizeHeaderValue(string value)
    {
        return (value ?? "").Replace("\r", "").Replace("\n", "");
    }

    private void OnCastErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (String.IsNullOrWhiteSpace(e.Data) || castErrorLineCount >= 24)
        {
            return;
        }

        castErrorLineCount++;
        Process process = sender as Process;
        string source = process == ffmpegProcess ? "FFmpeg" : "Go2TV";
        Log(source + ": " + e.Data);
    }

    private void OnCastOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!String.IsNullOrWhiteSpace(e.Data))
        {
            Log("Go2TV: " + e.Data);
        }
    }

    private void OnGo2TvExited(object sender, EventArgs e)
    {
        QueueCastProcessExit(sender as Process, "Go2TV");
    }

    private void OnFfmpegExited(object sender, EventArgs e)
    {
        QueueCastProcessExit(sender as Process, "FFmpeg");
    }

    private void QueueCastProcessExit(Process process, string source)
    {
        int exitCode = -1;
        try { exitCode = process == null ? -1 : process.ExitCode; } catch { }

        Action handleExit = delegate
        {
            Log(source + " process exited. code=" + exitCode);
            HandleCastProcessExit(process, source);
        };

        try
        {
            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                BeginInvoke(handleExit);
            }
        }
        catch
        {
        }
    }

    private void HandleCastProcessExit(Process process, string source)
    {
        bool isCurrent =
            (source == "Go2TV" && Object.ReferenceEquals(process, go2TvProcess)) ||
            (source == "FFmpeg" && Object.ReferenceEquals(process, ffmpegProcess));
        if (!isCurrent)
        {
            try { if (process != null) { process.Dispose(); } } catch { }
            return;
        }

        HandleCurrentCastFailure(source + " exited");
    }

    private void QueueCastPipeEnded(Process go2Tv, Process ffmpeg, Exception error)
    {
        string reason = error == null ? "relay pipe completed" :
            "relay pipe error " + error.GetType().Name;
        Action handlePipeEnd = delegate
        {
            bool isCurrent = Object.ReferenceEquals(go2Tv, go2TvProcess) &&
                Object.ReferenceEquals(ffmpeg, ffmpegProcess);
            if (!isCurrent || castStopRequested)
            {
                return;
            }

            Log("Cast relay pipe ended unexpectedly. reason=" + reason);
            HandleCurrentCastFailure(reason);
        };

        try
        {
            if (!IsDisposed && !Disposing && IsHandleCreated)
            {
                BeginInvoke(handlePipeEnd);
            }
        }
        catch
        {
        }
    }

    private void HandleCurrentCastFailure(string reason)
    {
        double uptimeSeconds = (DateTime.UtcNow - castStartedAtUtc).TotalSeconds;
        StopCastProcessInstances();
        if (castStopRequested)
        {
            return;
        }

        RegisterCastFailureAndSchedule(reason, uptimeSeconds >= StableCastSeconds);
    }
    private void RegisterCastFailureAndSchedule(string reason, bool stableRun)
    {
        if (stableRun)
        {
            castRestartAttempt = 0;
        }

        castRestartAttempt++;
        if (castRestartAttempt > MaxRapidCastRestartAttempts)
        {
            castStopRequested = true;
            castRestartPending = false;
            activeGo2TvPath = null;
            activeFfmpegPath = null;
            activeCastTargetUrl = null;
            UpdateDirectCastButton();
            Log("DLNA relay stopped after " + MaxRapidCastRestartAttempts +
                " rapid reconnect failures. Click Direct Send to try again.");
            return;
        }

        ScheduleCastRestartAsync(castSessionId, reason);
    }

    private async void ScheduleCastRestartAsync(int sessionId, string reason)
    {
        if (castRestartPending || castStopRequested)
        {
            return;
        }

        castRestartPending = true;
        UpdateDirectCastButton();
        Log("DLNA relay disconnected (" + reason + "). Reconnecting in " +
            (CastRestartDelayMilliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) +
            " seconds. attempt=" + castRestartAttempt);

        await Task.Delay(CastRestartDelayMilliseconds);
        if (IsDisposed || Disposing || castStopRequested || sessionId != castSessionId)
        {
            return;
        }

        castRestartPending = false;
        UpdateDirectCastButton();
        try
        {
            StartRelayedDlnaCastCore();
        }
        catch (Exception ex)
        {
            StopCastProcessInstances();
            Log("DLNA reconnect failed to start: " + ex.Message);
            RegisterCastFailureAndSchedule("restart failed", false);
        }
    }

    private void UpdateDirectCastButton()
    {
        if (directCastButton == null || directCastButton.IsDisposed)
        {
            return;
        }

        string caption = deviceSearchInProgress ? "Searching..." :
            (castStopRequested ? "Direct Send" :
             (castRestartPending ? "Stop Cast..." : "Stop Cast"));
        Action update = delegate
        {
            directCastButton.Text = caption;
            directCastButton.Enabled = !deviceSearchInProgress;
            if (autoCastButton != null && !autoCastButton.IsDisposed)
            {
                autoCastButton.Enabled = !deviceSearchInProgress;
            }
        };
        if (directCastButton.InvokeRequired)
        {
            try { directCastButton.BeginInvoke(update); } catch { }
        }
        else
        {
            update();
        }
    }

    private void StopCastProcesses()
    {
        castStopRequested = true;
        castRestartPending = false;
        castRestartAttempt = 0;
        castSessionId++;
        activeGo2TvPath = null;
        activeFfmpegPath = null;
        activeCastTargetUrl = null;
        StopCastProcessInstances();
        UpdateDirectCastButton();
    }

    private void StopCastProcessInstances()
    {
        Process ffmpeg = ffmpegProcess;
        Process go2Tv = go2TvProcess;
        ffmpegProcess = null;
        go2TvProcess = null;

        if (ffmpeg != null)
        {
            try { ffmpeg.Exited -= OnFfmpegExited; } catch { }
            try { ffmpeg.ErrorDataReceived -= OnCastErrorDataReceived; } catch { }
            StopAndDisposeProcess(ffmpeg);
        }
        if (go2Tv != null)
        {
            try { go2Tv.Exited -= OnGo2TvExited; } catch { }
            try { go2Tv.OutputDataReceived -= OnCastOutputDataReceived; } catch { }
            try { go2Tv.ErrorDataReceived -= OnCastErrorDataReceived; } catch { }
            StopAndDisposeProcess(go2Tv);
        }
    }

    private static void StopAndDisposeProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(1500);
            }
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string QuoteArgument(string value)
    {
        return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
    }

    private static string GetSafeUrlForLog(string value)
    {
        Uri uri;
        if (Uri.TryCreate(value, UriKind.Absolute, out uri))
        {
            return uri.GetLeftPart(UriPartial.Path);
        }

        return "<invalid-url>";
    }
    private void OpenWindowsCastPanel()
    {
        try
        {
            SetForegroundWindow(Handle);
            keybd_event(VirtualKeyLeftWindows, 0, 0, UIntPtr.Zero);
            keybd_event(VirtualKeyK, 0, 0, UIntPtr.Zero);
            keybd_event(VirtualKeyK, 0, KeyEventKeyUp, UIntPtr.Zero);
            keybd_event(VirtualKeyLeftWindows, 0, KeyEventKeyUp, UIntPtr.Zero);
            Log("Opened Windows Cast panel. Select the TCL TV to mirror video and audio.");
        }
        catch (Exception ex)
        {
            Log("Could not open Windows Cast panel: " + ex.Message);
        }
    }
    private void OnActivated(object sender, EventArgs e)
    {
        PreventSystemIdle();
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        StopCastProcesses();
        AllowSystemIdle();
    }
    private void NavigateToSourcePage()
    {
        navigationStarted = false;
        navigationCompleted = false;
        navigationGeneration++;
        playerDetected = false;
        autoClickScheduled = false;
        autoFullScreenScheduled = false;
        blockedRequestCount = 0;
        latestCastMediaUrl = null;
        latestCastCookie = null;
        latestCastReferer = null;
        latestCastOrigin = null;
        latestCastUserAgent = null;
        Log("Navigating source page: " + SourcePageUrl);
        AddVisibleStatus("Loading Cycling Today source page...");
        webView.CoreWebView2.Navigate(SourcePageUrl);
    }

    private async void StartPlayerWatchdogAsync()
    {
        int generation = navigationGeneration;
        await Task.Delay(PlayerFirstCheckDelayMilliseconds);
        if (IsDisposed || webView == null || webView.CoreWebView2 == null ||
            generation != navigationGeneration || playerDetected)
        {
            return;
        }

        bool found = await CheckPlayerAsync("player-watchdog-first");
        if (found || generation != navigationGeneration)
        {
            return;
        }

        Log("Current Cycling Today player not ready yet. navigationStarted=" + navigationStarted +
            " navigationCompleted=" + navigationCompleted + ". Waiting for the source page.");
        AddVisibleStatus("Current live player is still loading; waiting for Cycling Today...");

        await Task.Delay(PlayerSecondCheckDelayMilliseconds);
        if (IsDisposed || webView == null || webView.CoreWebView2 == null ||
            generation != navigationGeneration || playerDetected)
        {
            return;
        }

        found = await CheckPlayerAsync("player-watchdog-final");
        if (!found && generation == navigationGeneration)
        {
            Log("Current Cycling Today player was not detected. No obsolete fallback was loaded.");
            AddVisibleStatus("Live player not detected yet. Click Refresh to retry the current source page.");
        }
    }
    private System.Drawing.Icon LoadAppIcon()
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"),
            Path.Combine(Application.StartupPath, "app.ico"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windows", "app.ico")
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string path = candidates[i];
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return new System.Drawing.Icon(path);
            }
            catch
            {
            }
        }

        try
        {
            return System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch
        {
            return null;
        }
    }

    private string LoadCleanerScript()
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cleaner.js"),
            Path.Combine(Application.StartupPath, "cleaner.js"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windows", "cleaner.js")
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string path = candidates[i];
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                Log("Cleaner script loaded: " + path);
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log("Cleaner script read failed: " + ex.Message);
            }
        }

        return "";
    }

    private bool RememberCastableMediaUrl(string url, string contentType = null)
    {
        if (!LooksLikeCastableMedia(url, contentType))
        {
            return false;
        }

        if (!String.Equals(latestCastMediaUrl, url, StringComparison.Ordinal))
        {
            latestCastMediaUrl = url;
            Log("Castable stream detected: " + GetSafeUrlForLog(url) +
                (String.IsNullOrWhiteSpace(contentType) ? "" : " contentType=" + contentType));
            RefreshCastCookiesAsync(url);
        }
        return true;
    }

    private static bool LooksLikeCastableMedia(string url, string contentType)
    {
        if (String.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        string type = contentType ?? "";
        return
            url.IndexOf(".m3u8", StringComparison.OrdinalIgnoreCase) >= 0 ||
            url.IndexOf(".mpd", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("mpegurl", StringComparison.OrdinalIgnoreCase) >= 0 ||
            type.IndexOf("application/dash+xml", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CaptureCastRequestHeaders(CoreWebView2WebResourceRequest request, string contentType = null)
    {
        if (request == null || !LooksLikeCastableMedia(request.Uri, contentType))
        {
            return;
        }

        string cookie = GetRequestHeader(request, "Cookie");
        string referer = GetRequestHeader(request, "Referer");
        string origin = GetRequestHeader(request, "Origin");
        string userAgent = GetRequestHeader(request, "User-Agent");
        if (!String.IsNullOrWhiteSpace(cookie)) { latestCastCookie = cookie; }
        if (!String.IsNullOrWhiteSpace(referer)) { latestCastReferer = referer; }
        if (!String.IsNullOrWhiteSpace(origin)) { latestCastOrigin = origin; }
        if (!String.IsNullOrWhiteSpace(userAgent)) { latestCastUserAgent = userAgent; }

        Log("Cast request headers captured. cookie=" + (!String.IsNullOrWhiteSpace(latestCastCookie)) +
            " referer=" + (!String.IsNullOrWhiteSpace(latestCastReferer)) +
            " origin=" + (!String.IsNullOrWhiteSpace(latestCastOrigin)) +
            " userAgent=" + (!String.IsNullOrWhiteSpace(latestCastUserAgent)));
    }

    private static string GetRequestHeader(CoreWebView2WebResourceRequest request, string name)
    {
        try
        {
            return request.Headers.Contains(name) ? request.Headers.GetHeader(name) : "";
        }
        catch
        {
            return "";
        }
    }

    private async void RefreshCastCookiesAsync(string url)
    {
        try
        {
            IReadOnlyList<CoreWebView2Cookie> cookies =
                await webView.CoreWebView2.CookieManager.GetCookiesAsync(url);
            if (!String.Equals(latestCastMediaUrl, url, StringComparison.Ordinal) || cookies.Count == 0)
            {
                return;
            }

            List<string> values = new List<string>();
            for (int i = 0; i < cookies.Count; i++)
            {
                values.Add(cookies[i].Name + "=" + cookies[i].Value);
            }
            latestCastCookie = String.Join("; ", values.ToArray());
            Log("Cast cookies loaded from WebView2. count=" + cookies.Count);
        }
        catch (Exception ex)
        {
            Log("Cast cookie lookup failed: " + ex.Message);
        }
    }
    private void OnWebResourceResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            string contentType = e.Response.Headers.GetHeader("Content-Type");
            RememberCastableMediaUrl(e.Request.Uri, contentType);
            CaptureCastRequestHeaders(e.Request, contentType);
        }
        catch
        {
        }
    }
    private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        string requestUrl = e.Request.Uri;

        if (!enableBlocking || !ShouldBlock(requestUrl))
        {
            RememberCastableMediaUrl(requestUrl);
            CaptureCastRequestHeaders(e.Request);
            return;
        }

        MemoryStream empty = new MemoryStream(new byte[0]);
        e.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
            empty,
            204,
            "No Content",
            "Access-Control-Allow-Origin: *\r\nCache-Control: no-store");
        blockedRequestCount++;
        if (blockedRequestCount <= 50)
        {
            Log("Blocked request: " + requestUrl);
        }
    }

    private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        UpdateTitle("popup blocked");
        Log("Popup blocked: " + e.Uri);
    }

    private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Uri uri;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri))
        {
            return;
        }

        navigationStarted = true;
        Log("Top navigation starting: " + e.Uri);

        if (uri.Scheme == "about" || uri.Scheme == "data")
        {
            return;
        }

        if ((enableBlocking && ShouldBlock(e.Uri)) || !IsAllowedTopLevelHost(uri.Host))
        {
            e.Cancel = true;
            UpdateTitle("navigation blocked");
            Log("Top navigation blocked: " + e.Uri);
        }
    }

    private void OnFrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (enableBlocking && ShouldBlock(e.Uri))
        {
            e.Cancel = true;
            UpdateTitle("ad frame blocked");
            Log("Frame navigation blocked: " + e.Uri);
        }
    }

    private void OnPermissionRequested(object sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (e.PermissionKind == CoreWebView2PermissionKind.Autoplay)
        {
            e.State = CoreWebView2PermissionState.Allow;
        }
        else
        {
            e.State = CoreWebView2PermissionState.Deny;
        }

        e.Handled = true;
    }

    private void OnContainsFullScreenElementChanged(object sender, object e)
    {
        if (webView.CoreWebView2.ContainsFullScreenElement)
        {
            SetFullScreen(true);
        }
        else
        {
            SetFullScreen(false);
        }
    }

    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        navigationCompleted = true;
        Log("Navigation completed. success=" + e.IsSuccess + " blockedRequests=" + blockedRequestCount);
        await CheckPlayerAsync("navigation-completed");
        ScheduleAutoUnmuteClick();
        ScheduleAutoPlayerFullScreen();
    }

    private async void OnDomContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        await CheckPlayerAsync("dom-content-loaded");
    }

    private async Task<bool> CheckPlayerAsync(string stage)
    {
        try
        {
            string script =
                "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getStateText){return 'helper=missing';}return h.getStateText();})()";
            string result = await webView.ExecuteScriptAsync(script);
            string decoded = DecodeScriptString(result);
            Log(stage + ": " + decoded);
            AddVisibleStatus(stage + ": " + decoded);
            if (decoded.IndexOf("playerFound=true", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                playerDetected = true;
                return true;
            }
        }
        catch (Exception ex)
        {
            Log(stage + ": player check failed: " + ex.Message);
        }

        return false;
    }

    private async void ScheduleAutoUnmuteClick()
    {
        if (!enableAutoClick || autoClickScheduled)
        {
            return;
        }

        autoClickScheduled = true;

        for (int attempt = 0; attempt < 18; attempt++)
        {
            await Task.Delay(attempt == 0 ? 8000 : 1000);

            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            Point[] points = await GetAutoClickPointsAsync();
            if (points.Length == 0)
            {
                if (attempt == 0 || attempt == 5 || attempt == 11)
                {
                    Log("Auto unmute waiting for player click points. attempt=" + (attempt + 1));
                    AddVisibleStatus("Waiting for player controls before unmute. attempt=" + (attempt + 1));
                }
                continue;
            }

            await AutoClickPlayerUnmuteAsync(points);
            return;
        }

        Log("Auto unmute skipped: player click points unavailable.");
        AddVisibleStatus("Auto unmute skipped: player click points unavailable. Use Refresh if needed.");
    }

    private async Task<Point[]> GetAutoClickPointsAsync()
    {
        try
        {
            if (webView.CoreWebView2 == null)
            {
                return new Point[0];
            }

            string script =
                "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getClickPointText){return '';}return h.getClickPointText();})()";
            string encoded = await webView.ExecuteScriptAsync(script);
            return ParsePointList(DecodeScriptString(encoded));
        }
        catch (Exception ex)
        {
            Log("Auto unmute point query failed: " + ex.Message);
            return new Point[0];
        }
    }

    private async void ScheduleAutoPlayerFullScreen()
    {
        if (autoFullScreenScheduled)
        {
            return;
        }

        autoFullScreenScheduled = true;
        await Task.Delay(10000);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (IsDisposed || !IsHandleCreated || webView.CoreWebView2 == null)
            {
                return;
            }
            if (webView.CoreWebView2.ContainsFullScreenElement)
            {
                Log("Player full screen already active.");
                return;
            }

            Point[] points = await GetPlayerFullScreenPointsAsync();
            if (points.Length == 0)
            {
                await Task.Delay(1000);
                continue;
            }

            Point point = points[Math.Min(attempt, points.Length - 1)];
            try
            {
                SetForegroundWindow(Handle);
                webView.Focus();
                Point screenPoint = webView.PointToScreen(point);
                SetCursorPos(screenPoint.X, screenPoint.Y);
                await Task.Delay(600);
                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                Log("Player full-screen control click sent. point=" + point.X + "," + point.Y +
                    " attempt=" + (attempt + 1));
            }
            catch (Exception ex)
            {
                Log("Player full-screen click failed: " + ex.Message);
                return;
            }

            await Task.Delay(1200);
            if (webView.CoreWebView2.ContainsFullScreenElement)
            {
                Log("Player entered full screen successfully.");
                AddVisibleStatus("Player entered full screen.");
                return;
            }
        }

        Log("Player full screen was not confirmed after retries.");
        AddVisibleStatus("Player full screen was not confirmed; use its bottom-right control.");
    }

    private async Task<Point[]> GetPlayerFullScreenPointsAsync()
    {
        try
        {
            string script =
                "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getFullScreenPointText){return '';}return h.getFullScreenPointText();})()";
            string encoded = await webView.ExecuteScriptAsync(script);
            return ParsePointList(DecodeScriptString(encoded));
        }
        catch (Exception ex)
        {
            Log("Player full-screen point query failed: " + ex.Message);
            return new Point[0];
        }
    }
    private Point[] ParsePointList(string text)
    {
        if (String.IsNullOrWhiteSpace(text))
        {
            return new Point[0];
        }

        List<Point> points = new List<Point>();
        string[] pairs = text.Split('|');
        for (int i = 0; i < pairs.Length; i++)
        {
            string[] parts = pairs[i].Split(',');
            if (parts.Length != 2)
            {
                continue;
            }

            double rawX;
            double rawY;
            if (!Double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out rawX) ||
                !Double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out rawY))
            {
                continue;
            }

            int x = Math.Max(10, Math.Min(webView.ClientSize.Width - 10, (int)Math.Round(rawX)));
            int y = Math.Max(10, Math.Min(webView.ClientSize.Height - 10, (int)Math.Round(rawY)));
            Point point = new Point(x, y);
            bool duplicate = false;
            for (int existing = 0; existing < points.Count; existing++)
            {
                if (Math.Abs(points[existing].X - point.X) < 4 && Math.Abs(points[existing].Y - point.Y) < 4)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                points.Add(point);
            }
        }

        return points.ToArray();
    }

    private static string DecodeScriptString(string encoded)
    {
        if (String.IsNullOrEmpty(encoded) || encoded == "null")
        {
            return "";
        }

        string value = encoded;
        if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
        {
            value = value.Substring(1, value.Length - 2);
        }

        value = value.Replace("\\\"", "\"");
        value = value.Replace("\\\\", "\\");
        value = value.Replace("\\n", "\n");
        value = value.Replace("\\r", "\r");
        value = value.Replace("\\t", "\t");
        return value;
    }

    private Task AutoClickPlayerUnmuteAsync(Point[] points)
    {
        try
        {
            SetForegroundWindow(Handle);
            webView.Focus();

            Point screenPoint = webView.PointToScreen(points[0]);
            SetCursorPos(screenPoint.X, screenPoint.Y);
            mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);

            Log("Unmute/resume click sent from player rect. point=" + FormatPoints(points, 1));
            AddVisibleStatus("Unmute/resume click sent.");
        }
        catch (Exception ex)
        {
            Log("Auto unmute/resume click failed: " + ex.Message);
        }

        return Task.FromResult(0);
    }

    private static string FormatPoints(Point[] points, int count)
    {
        List<string> parts = new List<string>();
        for (int i = 0; i < points.Length && i < count; i++)
        {
            parts.Add(points[i].X.ToString(CultureInfo.InvariantCulture) + "," + points[i].Y.ToString(CultureInfo.InvariantCulture));
        }

        return String.Join("|", parts.ToArray());
    }

    private void SetStatusChromeVisible(bool visible)
    {
        if (statusPanel != null)
        {
            statusPanel.Visible = visible;
        }

        if (mainLayout != null && mainLayout.RowStyles.Count > 1)
        {
            mainLayout.RowStyles[1].Height = visible ? 86F : 0F;
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            NavigateToSourcePage();
            StartPlayerWatchdogAsync();
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.R || e.KeyCode == Keys.Space)
        {
            SendManualResumeClick("hotkey");
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.F11)
        {
            SetFullScreen(!fullScreen);
            e.Handled = true;
            return;
        }

        if (e.KeyCode == Keys.Escape && fullScreen)
        {
            SetFullScreen(false);
            e.Handled = true;
        }
    }

    private void SetFullScreen(bool enabled)
    {
        if (enabled == fullScreen)
        {
            return;
        }

        fullScreen = enabled;
        if (enabled)
        {
            previousBorderStyle = FormBorderStyle;
            previousWindowState = WindowState;
            SetStatusChromeVisible(false);
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
            SetStatusChromeVisible(true);
            FormBorderStyle = previousBorderStyle == 0 ? FormBorderStyle.Sizable : previousBorderStyle;
            WindowState = previousWindowState == 0 ? FormWindowState.Normal : previousWindowState;
        }
    }

    private void UpdateTitle(string suffix)
    {
        Text = "Cycling Today Clean Player - " + suffix;
    }

    private static bool HasArg(string[] args, string expected)
    {
        if (args == null)
        {
            return false;
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (String.Equals(args[i], expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void ResetLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, DateTime.Now.ToString("s") + " Starting\r\n");
        }
        catch
        {
        }
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + message + "\r\n");
        }
        catch
        {
        }

        if (message.IndexOf("Blocked request:", StringComparison.OrdinalIgnoreCase) < 0)
        {
            AddVisibleStatus(message);
        }
    }

    private bool ShouldBlock(string url)
    {
        Uri uri;
        if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        if (blockedHosts.Contains(host))
        {
            return true;
        }

        foreach (string blocked in blockedHosts)
        {
            if (host.EndsWith("." + blocked, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedTopLevelHost(string host)
    {
        string h = (host ?? "").ToLowerInvariant();
        return h == "cycling.today" || h == "www.cycling.today";
    }

    private static HashSet<string> BuildBlockedHosts()
    {
        string[] hosts =
        {
            "2mdn.net",
            "adform.net",
            "adnxs.com",
            "adskeeper.com",
            "adsterra.com",
            "advertising.com",
            "adroll.com",
            "adservice.google.com",
            "adsafeprotected.com",
            "adsrvr.org",
            "adtrafficquality.google",
            "bluekai.com",
            "demdex.net",
            "everesttech.net",
            "indexww.com",
            "lijit.com",
            "media.net",
            "moatads.com",
            "sharethrough.com",
            "smartadserver.com",
            "spotxchange.com",
            "springserve.com",
            "teads.tv",
            "themoneytizer.com",
            "yieldmo.com",
            "zedo.com",
            "zeotap.com",
            "onesignal.com",
            "platform.twitter.com",
            "syndication.twitter.com",
            "twitter.com",
            "x.com",
            "facebook.com",
            "instagram.com",
            "amazon-adsystem.com",
            "analytics.google.com",
            "casalemedia.com",
            "cloudfront-labs.amazonaws.com",
            "contextweb.com",
            "criteo.com",
            "criteo.net",
            "doubleclick.net",
            "exoclick.com",
            "googlesyndication.com",
            "googletagmanager.com",
            "googletagservices.com",
            "google-analytics.com",
            "googleadservices.com",
            "imasdk.googleapis.com",
            "mgid.com",
            "onclickads.net",
            "openx.net",
            "outbrain.com",
            "popads.net",
            "popcash.net",
            "propellerads.com",
            "pubmatic.com",
            "quantserve.com",
            "revcontent.com",
            "rubiconproject.com",
            "scorecardresearch.com",
            "taboola.com",
            "trafficjunky.net",
            "yllix.com"
        };

        HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < hosts.Length; i++)
        {
            result.Add(hosts[i]);
        }

        string sidecar = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blocked-hosts.txt");
        if (File.Exists(sidecar))
        {
            foreach (string rawLine in File.ReadAllLines(sidecar))
            {
                string line = rawLine.Trim().ToLowerInvariant();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add(line);
            }
        }

        return result;
    }

}
