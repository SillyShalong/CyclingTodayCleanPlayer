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
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    private readonly WebView2 webView;
    private readonly HashSet<string> blockedHosts;
    private readonly string logPath;
    private readonly bool enableCleaner;
    private readonly bool enableBlocking;
    private readonly bool enableAutoClick;
    private FormBorderStyle previousBorderStyle;
    private FormWindowState previousWindowState;
    private bool fullScreen;
    private int blockedRequestCount;
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

        webView = new WebView2();
        webView.Dock = DockStyle.Fill;
        webView.DefaultBackgroundColor = Color.Black;
        Controls.Add(webView);

        Shown += async delegate { await InitializeAsync(); };
        KeyDown += OnKeyDown;
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
            Log("WebView2 initialized");
            ConfigureWebView();
            webView.Source = new Uri(SourcePageUrl);
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
        Log("Navigation completed. success=" + e.IsSuccess + " blockedRequests=" + blockedRequestCount);
        await CheckPlayerAsync("navigation-completed");
        ScheduleAutoUnmuteClick();
    }

    private async void OnDomContentLoaded(object sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        await CheckPlayerAsync("dom-content-loaded");
    }

    private async Task CheckPlayerAsync(string stage)
    {
        try
        {
            string script =
                "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getStateText){return 'helper=missing';}return h.getStateText();})()";
            string result = await webView.ExecuteScriptAsync(script);
            Log(stage + ": " + DecodeScriptString(result));
        }
        catch (Exception ex)
        {
            Log(stage + ": player check failed: " + ex.Message);
        }
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
                }
                continue;
            }

            AutoClickPlayerUnmute(points);
            return;
        }

        Log("Auto unmute skipped: player click points unavailable.");
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

    private void AutoClickPlayerUnmute(Point[] points)
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

            Log("Auto unmute click sent from player rect. points=" + FormatPoints(points, sent));
        }
        catch (Exception ex)
        {
            Log("Auto unmute click failed: " + ex.Message);
        }
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
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F5)
        {
            webView.Reload();
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
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
        }
        else
        {
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
            "instagram.com",            "amazon-adsystem.com",
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
