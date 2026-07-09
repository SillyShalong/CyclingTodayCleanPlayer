using System;
using System.Collections.Generic;
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
    private const string BuiltInFallbackPlayerUrl = "https://merithotdog.net/e/vggq46c2zw56n";
    private const int FallbackDelayMilliseconds = 4500;
    private const int MaxVisibleLogLines = 4;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint ExecutionStateContinuous = 0x80000000;
    private const uint ExecutionStateSystemRequired = 0x00000001;
    private const uint ExecutionStateDisplayRequired = 0x00000002;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

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
    private Button reloadButton;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private bool fullScreen;
    private int blockedRequestCount;
    private bool navigationStarted;
    private bool navigationCompleted;
    private bool fallbackLoaded;
    private bool playerDetected;
    private bool autoClickScheduled;

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
        buttons.Width = 300;
        buttons.FlowDirection = FlowDirection.LeftToRight;
        buttons.WrapContents = false;
        buttons.Padding = new Padding(0, 12, 0, 0);
        buttons.BackColor = Color.Transparent;

        unmuteButton = new Button();
        unmuteButton.Text = "Unmute / Resume";
        unmuteButton.Width = 140;
        unmuteButton.Height = 34;
        unmuteButton.Click += delegate { SendManualResumeClick("button"); };

        reloadButton = new Button();
        reloadButton.Text = "Refresh";
        reloadButton.Width = 100;
        reloadButton.Height = 34;
        reloadButton.Click += delegate
        {
            NavigateToSourcePage();
            StartFallbackWatchdogAsync();
        };

        buttons.Controls.Add(unmuteButton);
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
            StartFallbackWatchdogAsync();
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

        if (enableBlocking)
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
        }

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

    private void OnActivated(object sender, EventArgs e)
    {
        PreventSystemIdle();
    }

    private void OnFormClosed(object sender, FormClosedEventArgs e)
    {
        AllowSystemIdle();
    }
    private void NavigateToSourcePage()
    {
        navigationStarted = false;
        navigationCompleted = false;
        fallbackLoaded = false;
        playerDetected = false;
        autoClickScheduled = false;
        blockedRequestCount = 0;
        Log("Navigating source page: " + SourcePageUrl);
        AddVisibleStatus("Loading Cycling Today source page...");
        webView.CoreWebView2.Navigate(SourcePageUrl);
    }

    private async void StartFallbackWatchdogAsync()
    {
        await Task.Delay(FallbackDelayMilliseconds);
        if (IsDisposed || webView == null || webView.CoreWebView2 == null || fallbackLoaded || playerDetected)
        {
            return;
        }

        if (!navigationCompleted)
        {
            LoadFallbackPlayer("source-timeout navigationStarted=" + navigationStarted + " navigationCompleted=" + navigationCompleted);
            return;
        }

        bool found = await CheckPlayerAsync("fallback-watchdog");
        if (!found && !fallbackLoaded)
        {
            LoadFallbackPlayer("player-not-detected navigationStarted=" + navigationStarted + " navigationCompleted=" + navigationCompleted);
        }
    }

    private void LoadFallbackPlayer(string reason)
    {
        if (fallbackLoaded || webView == null || webView.CoreWebView2 == null)
        {
            return;
        }

        string playerUrl = GetFallbackPlayerUrl();
        fallbackLoaded = true;
        playerDetected = true;
        autoClickScheduled = false;
        Log("Loading fallback player. reason=" + reason + " src=" + playerUrl);
        AddVisibleStatus("Source page is slow; switching directly to player...");
        webView.CoreWebView2.NavigateToString(BuildFallbackHtml(playerUrl));
        ScheduleAutoUnmuteClick();
    }

    private string GetFallbackPlayerUrl()
    {
        string[] candidates =
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "player-url.txt"),
            Path.Combine(Application.StartupPath, "player-url.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "windows", "player-url.txt")
        };

        for (int i = 0; i < candidates.Length; i++)
        {
            string path = candidates[i];
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                Uri uri;
                if (Uri.TryCreate(line, UriKind.Absolute, out uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                {
                    Log("Fallback player override loaded: " + path);
                    return line;
                }
            }
        }

        return BuiltInFallbackPlayerUrl;
    }

    private static string BuildFallbackHtml(string playerUrl)
    {
        string htmlUrl = EncodeHtmlAttribute(playerUrl);
        string jsUrl = EncodeJavaScriptString(playerUrl);
        return "<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
            "<style>html,body{width:100%;height:100%;margin:0;padding:0;overflow:hidden;background:#000;}#player{position:fixed;inset:0;width:100%;height:100%;border:0;background:#000;}</style>" +
            "</head><body><iframe id=\"player\" src=\"" + htmlUrl + "\" frameborder=\"0\" scrolling=\"no\" allow=\"autoplay; encrypted-media; fullscreen; picture-in-picture\" allowfullscreen></iframe>" +
            "<script>(function(){var src='" + jsUrl + "';function frame(){return document.getElementById('player');}function rect(){var f=frame();if(!f){return{left:0,top:0,width:innerWidth,height:innerHeight};}var r=f.getBoundingClientRect();return{left:Math.max(0,r.left),top:Math.max(0,r.top),width:Math.max(1,r.width),height:Math.max(1,r.height)};}function clamp(v,min,max){return Math.max(min,Math.min(max,v));}function pointText(){var r=rect();var maxX=Math.max(10,innerWidth-10);var maxY=Math.max(10,innerHeight-10);var pts=[[r.left+r.width*.16,r.top+r.height*.12],[r.left+r.width*.08,r.top+r.height*.88],[r.left+r.width*.50,r.top+r.height*.92],[r.left+r.width*.50,r.top+r.height*.50]];var out=[];for(var i=0;i<pts.length;i++){var p=Math.round(clamp(pts[i][0],10,maxX))+','+Math.round(clamp(pts[i][1],10,maxY));if(out.indexOf(p)<0){out.push(p);}}return out.join('|');}window.__cyclingTodayCleanPlayer={version:100,apply:function(){return true;},getClickPointText:pointText,getStateText:function(){var r=rect();return 'playerFound=true candidates=1 reason=fallback rect='+Math.round(r.left)+','+Math.round(r.top)+','+Math.round(r.width)+'x'+Math.round(r.height)+' src='+src;}};})();</script>" +
            "</body></html>";
    }

    private static string EncodeHtmlAttribute(string value)
    {
        return (value ?? "")
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static string EncodeJavaScriptString(string value)
    {
        return (value ?? "")
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
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

    private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!ShouldBlock(e.Request.Uri))
        {
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
            Log("Blocked request: " + e.Request.Uri);
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

            int sent = 0;
            for (int i = 0; i < points.Length && i < 4; i++)
            {
                Point screenPoint = webView.PointToScreen(points[i]);
                SetCursorPos(screenPoint.X, screenPoint.Y);
                mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
                sent++;
            }

            Log("Auto unmute/resume click sent from player rect. points=" + FormatPoints(points, sent));
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
            StartFallbackWatchdogAsync();
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
