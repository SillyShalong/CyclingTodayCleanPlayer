package com.lijialun.cyclingtoday;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.ActivityInfo;
import android.graphics.Bitmap;
import android.graphics.Typeface;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.Settings;
import android.text.InputType;
import android.view.Gravity;
import android.view.InputDevice;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.PermissionRequest;
import android.webkit.RenderProcessGoneDetail;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.Spinner;
import android.widget.TextView;

import androidx.webkit.ProxyConfig;
import androidx.webkit.ProxyController;
import androidx.webkit.WebViewFeature;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Date;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.concurrent.Executor;
import java.util.concurrent.atomic.AtomicInteger;

public class MainActivity extends Activity {
    private static final String SOURCE_URL = "https://cycling.today/";
    private static final String PROXY_PREFERENCES = "proxy-settings";
    private static final String PROXY_MODE_KEY = "mode";
    private static final String PROXY_HOST_KEY = "host";
    private static final String PROXY_PORT_KEY = "port";
    private static final String PROXY_MODE_DIRECT = "Direct";
    private static final String PROXY_MODE_HTTP = "HTTP";
    private static final String PROXY_MODE_SOCKS5 = "SOCKS5";
    private static final String[] PROXY_MODES = {
            PROXY_MODE_DIRECT, PROXY_MODE_HTTP, PROXY_MODE_SOCKS5
    };

    private static final int CONTROL_BAR_HEIGHT_DP = 68;
    private static final int MAX_VISIBLE_LOG_LINES = 3;
    private static final long PLAYER_FIRST_CHECK_DELAY_MILLIS = 8000L;
    private static final long PLAYER_SECOND_CHECK_DELAY_MILLIS = 12000L;
    private static final long SOURCE_STALL_TIMEOUT_MILLIS = 30000L;
    private static final int MAX_NETWORK_RECOVERY_ATTEMPTS = 2;
    private static final long[] NETWORK_RECOVERY_DELAYS_MILLIS = {2500L, 6000L};

    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Executor mainExecutor = new Executor() {
        @Override
        public void execute(Runnable command) {
            handler.post(command);
        }
    };
    private final List<String> visibleLogLines = new ArrayList<>();
    private final AtomicInteger blockedRequestCount = new AtomicInteger();

    private WebView webView;
    private FrameLayout root;
    private FrameLayout loadingCover;
    private LinearLayout controlBar;
    private TextView loadingText;
    private TextView statusText;
    private Button fullscreenButton;
    private View customView;
    private WebChromeClient.CustomViewCallback customViewCallback;
    private String cleanerScript;
    private File logFile;
    private SharedPreferences proxyPreferences;

    private boolean cleanerReady;
    private boolean cleanerPollScheduled;
    private boolean autoTapScheduled;
    private boolean autoFullScreenScheduled;
    private boolean networkRecoveryScheduled;

    private boolean controlsHidden;
    private boolean controlsHiddenBeforeCustomView;
    private boolean destroyed;
    private int autoTapAttempts;
    private int manualTapIndex;
    private int cleanerPollAttempts;
    private int loadGeneration;
    private int networkRecoveryAttempts;

    private static final Set<String> BLOCKED_HOSTS = new HashSet<>(Arrays.asList(
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
            "amazon-adsystem.com",
            "analytics.google.com",
            "bluekai.com",
            "casalemedia.com",
            "contextweb.com",
            "criteo.com",
            "criteo.net",
            "demdex.net",
            "doubleclick.net",
            "everesttech.net",
            "exoclick.com",
            "facebook.com",
            "google-analytics.com",
            "googleadservices.com",
            "googlesyndication.com",
            "googletagmanager.com",
            "googletagservices.com",
            "imasdk.googleapis.com",
            "indexww.com",
            "instagram.com",
            "lijit.com",
            "media.net",
            "mgid.com",
            "moatads.com",
            "onclickads.net",
            "onesignal.com",
            "openx.net",
            "outbrain.com",
            "platform.twitter.com",
            "popads.net",
            "popcash.net",
            "propellerads.com",
            "pubmatic.com",
            "quantserve.com",
            "revcontent.com",
            "rubiconproject.com",
            "scorecardresearch.com",
            "sharethrough.com",
            "smartadserver.com",
            "spotxchange.com",
            "springserve.com",
            "syndication.twitter.com",
            "taboola.com",
            "teads.tv",
            "themoneytizer.com",
            "trafficjunky.net",
            "twitter.com",
            "x.com",
            "yieldmo.com",
            "yllix.com",
            "zedo.com",
            "zeotap.com"
    ));

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);

        logFile = new File(getFilesDir(), "last-run.log");
        resetLog();
        proxyPreferences = getSharedPreferences(PROXY_PREFERENCES, MODE_PRIVATE);

        root = new FrameLayout(this);
        root.setBackgroundColor(0xff000000);
        setContentView(root);
        enterImmersiveMode();
        createWebView();
        createLoadingCover();
        createControlBar();
        applyContentInsets();

        status("Starting Android player...");
        applySavedProxyAndLoad("startup");
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void createWebView() {
        webView = new WebView(this);
        webView.setBackgroundColor(0xff000000);
        webView.setOverScrollMode(View.OVER_SCROLL_NEVER);
        webView.setScrollBarStyle(View.SCROLLBARS_INSIDE_OVERLAY);
        webView.setFocusable(true);
        webView.setFocusableInTouchMode(true);

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setLoadWithOverviewMode(true);
        settings.setUseWideViewPort(true);
        settings.setSupportMultipleWindows(false);
        settings.setJavaScriptCanOpenWindowsAutomatically(false);
        settings.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        settings.setBuiltInZoomControls(false);
        settings.setDisplayZoomControls(false);
        settings.setAllowFileAccess(false);
        settings.setAllowContentAccess(false);
        settings.setCacheMode(WebSettings.LOAD_DEFAULT);

        CookieManager.getInstance().setAcceptCookie(true);
        CookieManager.getInstance().setAcceptThirdPartyCookies(webView, true);
        webView.setWebViewClient(Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? new OreoCleanWebViewClient()
                : new CleanWebViewClient());
        webView.setWebChromeClient(new CleanChromeClient());

        root.addView(webView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void createLoadingCover() {
        loadingCover = new FrameLayout(this);
        loadingCover.setBackgroundColor(0xff050706);
        loadingCover.setClickable(true);

        LinearLayout center = new LinearLayout(this);
        center.setOrientation(LinearLayout.VERTICAL);
        center.setGravity(Gravity.CENTER);
        center.setPadding(dp(28), dp(20), dp(28), dp(20));

        ProgressBar progress = new ProgressBar(this, null, android.R.attr.progressBarStyleLarge);
        center.addView(progress, new LinearLayout.LayoutParams(dp(52), dp(52)));

        TextView heading = new TextView(this);
        heading.setText("Preparing live player");
        heading.setTextColor(0xfff0eee5);
        heading.setTextSize(20f);
        heading.setGravity(Gravity.CENTER);
        LinearLayout.LayoutParams headingParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        headingParams.topMargin = dp(14);
        center.addView(heading, headingParams);

        loadingText = new TextView(this);
        loadingText.setText("Loading Cycling Today...");
        loadingText.setTextColor(0xffaebdb6);
        loadingText.setTextSize(12f);
        loadingText.setGravity(Gravity.CENTER);
        loadingText.setMaxLines(3);
        LinearLayout.LayoutParams loadingTextParams = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT);
        loadingTextParams.topMargin = dp(8);
        center.addView(loadingText, loadingTextParams);

        FrameLayout.LayoutParams centerParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT,
                ViewGroup.LayoutParams.WRAP_CONTENT,
                Gravity.CENTER);
        loadingCover.addView(center, centerParams);
        root.addView(loadingCover, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void createControlBar() {
        controlBar = new LinearLayout(this);
        controlBar.setOrientation(LinearLayout.HORIZONTAL);
        controlBar.setGravity(Gravity.CENTER_VERTICAL);
        controlBar.setPadding(dp(10), dp(7), dp(10), dp(7));
        controlBar.setBackgroundColor(0xee131716);

        statusText = new TextView(this);
        statusText.setTextColor(0xffe9eee9);
        statusText.setTextSize(10f);
        statusText.setTypeface(Typeface.MONOSPACE);
        statusText.setMaxLines(MAX_VISIBLE_LOG_LINES);
        statusText.setGravity(Gravity.CENTER_VERTICAL);
        LinearLayout.LayoutParams statusParams = new LinearLayout.LayoutParams(
                0,
                ViewGroup.LayoutParams.MATCH_PARENT,
                1f);
        controlBar.addView(statusText, statusParams);

        addControlButton("Unmute / Resume", 132, new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                sendUnmuteTap(false);
            }
        });
        addControlButton("Cast Screen", 108, new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                openCastSettings();
            }
        });
        addControlButton("Proxy", 82, new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                showProxySettings();
            }
        });
        fullscreenButton = addControlButton("Full Screen", 104, new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                sendPlayerFullScreenTap(false);
            }
        });
        addControlButton("Refresh", 92, new View.OnClickListener() {
            @Override
            public void onClick(View view) {
                loadSource("manual refresh");
            }
        });

        FrameLayout.LayoutParams barParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                dp(CONTROL_BAR_HEIGHT_DP),
                Gravity.BOTTOM);
        root.addView(controlBar, barParams);
        controlBar.bringToFront();
    }

    private Button addControlButton(String text, int widthDp, View.OnClickListener listener) {
        Button button = new Button(this);
        button.setText(text);
        button.setTextSize(11f);
        button.setAllCaps(false);
        button.setSingleLine(true);
        button.setOnClickListener(listener);
        LinearLayout.LayoutParams params = new LinearLayout.LayoutParams(dp(widthDp), dp(46));
        params.leftMargin = dp(7);
        controlBar.addView(button, params);
        return button;
    }
    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void applyContentInsets() {
        int bottomMargin = controlsHidden ? 0 : dp(CONTROL_BAR_HEIGHT_DP);
        if (webView != null) {
            FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) webView.getLayoutParams();
            params.bottomMargin = bottomMargin;
            webView.setLayoutParams(params);
        }
        if (loadingCover != null) {
            FrameLayout.LayoutParams params = (FrameLayout.LayoutParams) loadingCover.getLayoutParams();
            params.bottomMargin = bottomMargin;
            loadingCover.setLayoutParams(params);
        }
    }

    private void setControlsHidden(boolean hidden) {
        controlsHidden = hidden;
        if (controlBar != null) {
            controlBar.setVisibility(hidden ? View.GONE : View.VISIBLE);
        }
        applyContentInsets();
        enterImmersiveMode();
        if (hidden) {
            logOnly("App full screen enabled. Press Back to restore controls.");
        } else {
            status("Controls restored.");
        }
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                injectCleaner();
            }
        }, 180L);
    }

    private void enterImmersiveMode() {
        getWindow().getDecorView().setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                        | View.SYSTEM_UI_FLAG_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) {
            enterImmersiveMode();
        }
    }

    private void resetLog() {
        try (FileOutputStream output = new FileOutputStream(logFile, false)) {
            String line = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(new Date())
                    + " Starting Android player\n";
            output.write(line.getBytes(StandardCharsets.UTF_8));
        } catch (IOException ignored) {
        }
    }

    private void status(String message) {
        if (message == null || message.trim().length() == 0) {
            return;
        }
        logOnly(message);
        String line = new SimpleDateFormat("HH:mm:ss", Locale.US).format(new Date()) + "  " + message;
        visibleLogLines.add(line);
        while (visibleLogLines.size() > MAX_VISIBLE_LOG_LINES) {
            visibleLogLines.remove(0);
        }
        if (statusText != null) {
            statusText.setText(joinLines(visibleLogLines));
        }
        if (loadingText != null && loadingCover != null && loadingCover.getVisibility() == View.VISIBLE) {
            loadingText.setText(message);
        }
    }

    private void logOnly(String message) {
        if (logFile == null || message == null) {
            return;
        }
        try (FileOutputStream output = new FileOutputStream(logFile, true)) {
            String line = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss", Locale.US).format(new Date())
                    + " " + message + "\n";
            output.write(line.getBytes(StandardCharsets.UTF_8));
        } catch (IOException ignored) {
        }
    }

    private String joinLines(List<String> lines) {
        StringBuilder result = new StringBuilder();
        for (String line : lines) {
            if (result.length() > 0) {
                result.append('\n');
            }
            result.append(line);
        }
        return result.toString();
    }

    private void loadSource(String reason) {
        loadSource(reason, true);
    }

    private void loadSource(String reason, boolean resetNetworkRecovery) {
        if (webView == null) {
            return;
        }
        if (resetNetworkRecovery) {
            networkRecoveryAttempts = 0;
        }
        networkRecoveryScheduled = false;
        loadGeneration++;
        final int generation = loadGeneration;

        blockedRequestCount.set(0);
        resetCleanerState();
        showLoadingCover("Loading Cycling Today source...");
        status("Loading source page. reason=" + reason);
        webView.stopLoading();
        webView.loadUrl(SOURCE_URL);
        scheduleCleanerPoll(200L);
        schedulePlayerWatchdog(generation);
        scheduleStalledSourceRecovery(generation);
    }

    private void scheduleStalledSourceRecovery(final int generation) {
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (destroyed || generation != loadGeneration || cleanerReady) {
                    return;
                }
                logOnly("Source page stalled before a player was detected.");
                scheduleNetworkRecovery("page-load-timeout");
            }
        }, SOURCE_STALL_TIMEOUT_MILLIS);
    }

    private void scheduleNetworkRecovery(final String errorDescription) {
        if (destroyed || webView == null || cleanerReady || networkRecoveryScheduled
                || networkRecoveryAttempts >= MAX_NETWORK_RECOVERY_ATTEMPTS) {
            return;
        }

        final int generation = loadGeneration;
        final int attempt = ++networkRecoveryAttempts;
        final long delay = NETWORK_RECOVERY_DELAYS_MILLIS[Math.min(
                attempt - 1, NETWORK_RECOVERY_DELAYS_MILLIS.length - 1)];
        networkRecoveryScheduled = true;
        logOnly("Network recovery scheduled. error=" + errorDescription
                + " attempt=" + attempt + "/" + MAX_NETWORK_RECOVERY_ATTEMPTS
                + " delayMs=" + delay);
        status("Connection closed. Retrying live page " + attempt + "/"
                + MAX_NETWORK_RECOVERY_ATTEMPTS + "...");
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (destroyed || generation != loadGeneration || cleanerReady) {
                    return;
                }
                loadSource("network recovery " + attempt + "/"
                        + MAX_NETWORK_RECOVERY_ATTEMPTS, false);
            }
        }, delay);
    }

    private void resetCleanerState() {
        cleanerReady = false;
        cleanerPollScheduled = false;
        autoTapScheduled = false;
        autoFullScreenScheduled = false;
        autoTapAttempts = 0;
        manualTapIndex = 0;
        cleanerPollAttempts = 0;
        handler.removeCallbacks(cleanerPollRunnable);
        handler.removeCallbacks(autoTapRunnable);
        handler.removeCallbacks(autoFullScreenRunnable);
    }

    private void showLoadingCover(String message) {
        if (loadingCover != null) {
            loadingCover.setVisibility(View.VISIBLE);
            loadingCover.bringToFront();
            if (controlBar != null && !controlsHidden) {
                controlBar.bringToFront();
            }
        }
        if (loadingText != null) {
            loadingText.setText(message);
        }
    }

    private void hideLoadingCover() {
        if (loadingCover != null) {
            loadingCover.setVisibility(View.GONE);
        }
    }

    private void schedulePlayerWatchdog(final int generation) {
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (destroyed || generation != loadGeneration || cleanerReady) {
                    return;
                }
                status("Current Cycling Today player is still loading; waiting for the source page...");
                handler.postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        if (destroyed || generation != loadGeneration || cleanerReady) {
                            return;
                        }
                        hideLoadingCover();
                        status("Current live player was not detected. No obsolete fallback was loaded; use Refresh to retry.");
                    }
                }, PLAYER_SECOND_CHECK_DELAY_MILLIS);
            }
        }, PLAYER_FIRST_CHECK_DELAY_MILLIS);
    }
    private void injectCleaner() {
        if (webView == null) {
            return;
        }
        try {
            webView.evaluateJavascript(getCleanerScript(), null);
        } catch (IOException exception) {
            logOnly("Cleaner asset could not be read: " + exception.getMessage());
        } catch (RuntimeException exception) {
            logOnly("Cleaner injection failed: " + exception.getMessage());
        }
    }

    private String getCleanerScript() throws IOException {
        if (cleanerScript == null) {
            cleanerScript = readAsset("cleaner.js");
            logOnly("Cleaner script loaded. bytes=" + cleanerScript.length());
        }
        return cleanerScript;
    }

    private String readAsset(String name) throws IOException {
        try (InputStream input = getAssets().open(name);
             ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[8192];
            int read;
            while ((read = input.read(buffer)) >= 0) {
                output.write(buffer, 0, read);
            }
            return new String(output.toByteArray(), StandardCharsets.UTF_8);
        }
    }

    private final Runnable cleanerPollRunnable = new Runnable() {
        @Override
        public void run() {
            cleanerPollScheduled = false;
            if (destroyed || cleanerReady || webView == null) {
                return;
            }
            cleanerPollAttempts++;
            injectCleaner();
            checkCleanerReady();
        }
    };

    private void scheduleCleanerPoll(long delayMillis) {
        if (destroyed || cleanerReady || cleanerPollScheduled) {
            return;
        }
        cleanerPollScheduled = true;
        handler.postDelayed(cleanerPollRunnable, delayMillis);
    }

    private void checkCleanerReady() {
        if (webView == null || cleanerReady) {
            return;
        }
        final int generation = loadGeneration;
        String script = "(function(){var h=window.__cyclingTodayCleanPlayer;"
                + "if(!h||!h.getStateText){return 'helper=missing';}"
                + "h.apply&&h.apply();return h.getStateText();})()";
        webView.evaluateJavascript(script, new ValueCallback<String>() {
            @Override
            public void onReceiveValue(String value) {
                if (destroyed || generation != loadGeneration || cleanerReady) {
                    return;
                }
                String state = decodeScriptString(value);
                if (state.contains("playerFound=true")) {
                    cleanerReady = true;
                    handler.removeCallbacks(cleanerPollRunnable);
                    hideLoadingCover();
                    logOnly("Player detected: " + state);
                    status("Player ready. Blocked " + blockedRequestCount.get() + " ad/tracking requests. Auto unmute is starting.");
                    scheduleAutoUnmuteTap();
                    scheduleAutoPlayerFullScreen();
                    return;
                }

                if (cleanerPollAttempts == 1 || cleanerPollAttempts == 8 || cleanerPollAttempts == 20) {
                    logOnly("Player scan: " + state);
                }
                if (loadingText != null && loadingCover != null && loadingCover.getVisibility() == View.VISIBLE) {
                    loadingText.setText("Scanning for live player...\n" + shortenState(state));
                }
                scheduleCleanerPoll(cleanerPollAttempts < 24 ? 300L : 1200L);
            }
        });
    }

    private String shortenState(String state) {
        if (state == null || state.length() == 0) {
            return "Waiting for page scripts";
        }
        int sourceIndex = state.indexOf(" src=");
        String result = sourceIndex >= 0 ? state.substring(0, sourceIndex) : state;
        return result.length() > 140 ? result.substring(0, 140) : result;
    }
    private final Runnable autoTapRunnable = new Runnable() {
        @Override
        public void run() {
            sendUnmuteTap(true);
        }
    };

    private void scheduleAutoUnmuteTap() {
        if (autoTapScheduled || webView == null) {
            return;
        }
        autoTapScheduled = true;
        autoTapAttempts = 0;
        handler.postDelayed(autoTapRunnable, 3500L);
    }

    private final Runnable autoFullScreenRunnable = new Runnable() {
        @Override
        public void run() {
            sendPlayerFullScreenTap(true);
        }
    };

    private void scheduleAutoPlayerFullScreen() {
        if (autoFullScreenScheduled || webView == null) {
            return;
        }
        autoFullScreenScheduled = true;
        handler.postDelayed(autoFullScreenRunnable, 5000L);
    }

    private void sendPlayerFullScreenTap(final boolean automatic) {
        if (webView == null || customView != null) {
            return;
        }
        String script = "(function(){var h=window.__cyclingTodayCleanPlayer;"
                + "if(!h||!h.getNormalizedFullScreenPointText){return '';}"
                + "h.apply&&h.apply();return h.getNormalizedFullScreenPointText();})()";
        webView.evaluateJavascript(script, new ValueCallback<String>() {
            @Override
            public void onReceiveValue(String value) {
                final List<float[]> points = parseNormalizedClickPoints(decodeScriptString(value));
                if (points.isEmpty()) {
                    logOnly("Player full-screen controls are not ready.");
                    if (!automatic) {
                        status("Player full-screen control is not ready yet.");
                    }
                    return;
                }
                tryPlayerFullScreenPoint(points, 0);
            }
        });
    }

    private void tryPlayerFullScreenPoint(final List<float[]> points, final int index) {
        if (webView == null || destroyed || customView != null || index >= points.size()) {
            if (customView == null && index >= points.size()) {
                status("Player full screen was not confirmed. Tap Full Screen to retry.");
            }
            return;
        }
        final float[] point = points.get(index);
        tapWebView(point[0], point[1]);
        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (destroyed || customView != null || webView == null) {
                    return;
                }
                tapWebView(point[0], point[1]);
                handler.postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        if (destroyed || customView != null) {
                            return;
                        }
                        logOnly("Player full-screen touch not confirmed. point="
                                + Math.round(point[0]) + "," + Math.round(point[1])
                                + " attempt=" + (index + 1));
                        tryPlayerFullScreenPoint(points, index + 1);
                    }
                }, 900L);
            }
        }, 450L);
    }
    private void sendUnmuteTap(final boolean automatic) {
        if (webView == null) {
            return;
        }
        if (automatic) {
            autoTapAttempts++;
        }
        if (webView.getWidth() <= 0 || webView.getHeight() <= 0) {
            retryAutomaticTapIfNeeded(automatic, "WebView has no size yet");
            return;
        }

        String script = "(function(){var h=window.__cyclingTodayCleanPlayer;"
                + "if(!h||!h.getNormalizedClickPointText){return '';}"
                + "h.apply&&h.apply();return h.getNormalizedClickPointText();})()";
        webView.evaluateJavascript(script, new ValueCallback<String>() {
            @Override
            public void onReceiveValue(String value) {
                final List<float[]> points = parseNormalizedClickPoints(decodeScriptString(value));
                if (points.isEmpty()) {
                    retryAutomaticTapIfNeeded(automatic, "player click points unavailable");
                    if (!automatic) {
                        status("Unmute/resume is waiting for the player. Try again or press Refresh.");
                    }
                    return;
                }

                final int index;
                if (automatic) {
                    index = 0;
                } else {
                    index = manualTapIndex % points.size();
                    manualTapIndex++;
                }
                final float[] point = points.get(index);
                handler.postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        if (webView == null || destroyed) {
                            return;
                        }
                        tapWebView(point[0], point[1]);
                        String source = automatic ? "Auto unmute" : "Manual unmute/resume "
                                + (index + 1) + "/" + points.size();
                        status(source + " touch sent at " + Math.round(point[0]) + "," + Math.round(point[1]) + ".");
                    }
                }, 120L);
            }
        });
    }

    private void retryAutomaticTapIfNeeded(boolean automatic, String reason) {
        if (!automatic) {
            return;
        }
        if (autoTapAttempts < 12) {
            if (autoTapAttempts == 1 || autoTapAttempts == 6) {
                logOnly("Auto unmute waiting: " + reason + ". attempt=" + autoTapAttempts);
            }
            handler.postDelayed(autoTapRunnable, 750L);
        } else {
            status("Auto unmute could not find controls. Use Unmute / Resume after video appears.");
        }
    }

    private List<float[]> parseNormalizedClickPoints(String text) {
        List<float[]> points = new ArrayList<>();
        if (text == null || text.trim().length() == 0 || webView == null) {
            return points;
        }
        String[] pairs = text.split("\\|");
        for (String pair : pairs) {
            String[] parts = pair.split(",");
            if (parts.length != 2) {
                continue;
            }
            try {
                float ratioX = Math.max(0.01f, Math.min(0.99f, Float.parseFloat(parts[0])));
                float ratioY = Math.max(0.01f, Math.min(0.99f, Float.parseFloat(parts[1])));
                float x = Math.max(8f, Math.min(webView.getWidth() - 8f, ratioX * webView.getWidth()));
                float y = Math.max(8f, Math.min(webView.getHeight() - 8f, ratioY * webView.getHeight()));
                boolean duplicate = false;
                for (float[] existing : points) {
                    if (Math.abs(existing[0] - x) < 4f && Math.abs(existing[1] - y) < 4f) {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) {
                    points.add(new float[]{x, y});
                }
            } catch (NumberFormatException ignored) {
            }
        }
        return points;
    }

    private String decodeScriptString(String value) {
        if (value == null || value.equals("null")) {
            return "";
        }
        String decoded = value;
        if (decoded.length() >= 2 && decoded.charAt(0) == '"'
                && decoded.charAt(decoded.length() - 1) == '"') {
            decoded = decoded.substring(1, decoded.length() - 1);
        }
        return decoded.replace("\\\"", "\"")
                .replace("\\\\", "\\")
                .replace("\\n", "\n")
                .replace("\\r", "\r")
                .replace("\\t", "\t");
    }

    private void tapWebView(float x, float y) {
        if (webView == null) {
            return;
        }
        webView.requestFocus();
        final long downTime = SystemClock.uptimeMillis();
        final float tapX = x;
        final float tapY = y;
        MotionEvent down = MotionEvent.obtain(downTime, downTime, MotionEvent.ACTION_DOWN, tapX, tapY, 0);
        down.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        webView.dispatchTouchEvent(down);
        down.recycle();

        handler.postDelayed(new Runnable() {
            @Override
            public void run() {
                if (webView == null || destroyed) {
                    return;
                }
                long upTime = SystemClock.uptimeMillis();
                MotionEvent up = MotionEvent.obtain(downTime, upTime, MotionEvent.ACTION_UP, tapX, tapY, 0);
                up.setSource(InputDevice.SOURCE_TOUCHSCREEN);
                webView.dispatchTouchEvent(up);
                up.recycle();
            }
        }, 75L);
    }

    private void openCastSettings() {
        status("Opening Android wireless display scan. Select the TV, then return to the player.");
        Intent castIntent = new Intent(Settings.ACTION_CAST_SETTINGS);
        try {
            startActivity(castIntent);
        } catch (ActivityNotFoundException firstError) {
            try {
                startActivity(new Intent(Settings.ACTION_WIRELESS_SETTINGS));
            } catch (ActivityNotFoundException secondError) {
                status("This device does not expose Android wireless display settings.");
            }
        }
    }

    private void showProxySettings() {
        LinearLayout content = new LinearLayout(this);
        content.setOrientation(LinearLayout.VERTICAL);
        content.setPadding(dp(24), dp(8), dp(24), 0);

        TextView note = new TextView(this);
        note.setText("HTTP or SOCKS5 proxy for this player only. Proxy authentication is not supported.");
        note.setTextSize(13f);
        content.addView(note);

        Spinner modeInput = new Spinner(this);
        ArrayAdapter<String> modeAdapter = new ArrayAdapter<>(this,
                android.R.layout.simple_spinner_dropdown_item, PROXY_MODES);
        modeInput.setAdapter(modeAdapter);
        String currentMode = getProxyMode();
        modeInput.setSelection(proxyModeIndex(currentMode));
        content.addView(modeInput);

        EditText hostInput = new EditText(this);
        hostInput.setHint("Host or IP address");
        hostInput.setSingleLine(true);
        hostInput.setText(getProxyHost());
        content.addView(hostInput);

        EditText portInput = new EditText(this);
        portInput.setHint("Port (HTTP default 80, SOCKS5 default 1080)");
        portInput.setInputType(InputType.TYPE_CLASS_NUMBER);
        portInput.setSingleLine(true);
        portInput.setText(getProxyPort());
        content.addView(portInput);

        final AlertDialog dialog = new AlertDialog.Builder(this)
                .setTitle("Playback proxy")
                .setView(content)
                .setNegativeButton("Cancel", null)
                .setNeutralButton("Disable", null)
                .setPositiveButton("Save", null)
                .create();
        dialog.setOnShowListener(ignored -> {
            dialog.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener(view -> {
                saveProxy(PROXY_MODE_DIRECT, "", "");
                dialog.dismiss();
                applySavedProxyAndLoad("proxy disabled");
            });
            dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener(view -> {
                String mode = String.valueOf(modeInput.getSelectedItem());
                String host = hostInput.getText().toString().trim();
                String port = portInput.getText().toString().trim();
                if (!PROXY_MODE_DIRECT.equals(mode) && buildProxyRule(mode, host, port) == null) {
                    hostInput.setError("Enter a valid host and port");
                    return;
                }
                saveProxy(mode, host, port);
                dialog.dismiss();
                applySavedProxyAndLoad("proxy changed");
            });
        });
        dialog.show();
    }

    private void saveProxy(String mode, String host, String port) {
        proxyPreferences.edit()
                .putString(PROXY_MODE_KEY, mode)
                .putString(PROXY_HOST_KEY, host)
                .putString(PROXY_PORT_KEY, port)
                .apply();
    }

    private String getProxyMode() {
        return proxyPreferences.getString(PROXY_MODE_KEY, PROXY_MODE_DIRECT);
    }

    private String getProxyHost() {
        return proxyPreferences.getString(PROXY_HOST_KEY, "");
    }

    private String getProxyPort() {
        return proxyPreferences.getString(PROXY_PORT_KEY, "");
    }

    private int proxyModeIndex(String mode) {
        for (int index = 0; index < PROXY_MODES.length; index++) {
            if (PROXY_MODES[index].equals(mode)) {
                return index;
            }
        }
        return 0;
    }

    private String buildProxyRule(String mode, String host, String portText) {
        if (!PROXY_MODE_HTTP.equals(mode) && !PROXY_MODE_SOCKS5.equals(mode)) {
            return null;
        }
        if (host.length() == 0 || host.contains("://") || host.contains("/")
                || host.contains("@") || host.matches(".*\\s+.*")) {
            return null;
        }
        int port;
        try {
            port = Integer.parseInt(portText);
        } catch (NumberFormatException ignored) {
            return null;
        }
        if (port < 1 || port > 65535) {
            return null;
        }
        String normalizedHost = host;
        if (host.indexOf(':') >= 0 && !host.startsWith("[")) {
            normalizedHost = "[" + host + "]";
        }
        String scheme = PROXY_MODE_HTTP.equals(mode) ? "http" : "socks";
        return scheme + "://" + normalizedHost + ":" + port;
    }

    private void applySavedProxyAndLoad(final String reason) {
        String mode = getProxyMode();
        if (PROXY_MODE_DIRECT.equals(mode)) {
            clearProxyAndLoad(reason);
            return;
        }
        String rule = buildProxyRule(mode, getProxyHost(), getProxyPort());
        if (rule == null) {
            status("Proxy setting is incomplete. Open Proxy to correct it.");
            return;
        }
        if (!WebViewFeature.isFeatureSupported(WebViewFeature.PROXY_OVERRIDE)) {
            status("This Android WebView does not support app proxy settings.");
            return;
        }
        try {
            ProxyConfig config = new ProxyConfig.Builder().addProxyRule(rule).build();
            ProxyController.getInstance().setProxyOverride(config, mainExecutor, new Runnable() {
                @Override
                public void run() {
                    status(getProxyMode() + " proxy applied. Reloading player...");
                    loadSource(reason);
                }
            });
        } catch (IllegalArgumentException | UnsupportedOperationException exception) {
            logOnly("Proxy configuration failed: " + exception.getMessage());
            status("Proxy configuration failed. Check host and port.");
        }
    }

    private void clearProxyAndLoad(final String reason) {
        if (!WebViewFeature.isFeatureSupported(WebViewFeature.PROXY_OVERRIDE)) {
            loadSource(reason);
            return;
        }
        try {
            ProxyController.getInstance().clearProxyOverride(mainExecutor, new Runnable() {
                @Override
                public void run() {
                    if (!PROXY_MODE_DIRECT.equals(getProxyMode())) {
                        return;
                    }
                    status("Direct connection enabled. Reloading player...");
                    loadSource(reason);
                }
            });
        } catch (UnsupportedOperationException exception) {
            logOnly("Could not clear proxy override: " + exception.getMessage());
            loadSource(reason);
        }
    }
    private boolean isAllowedTopLevel(Uri uri) {


        String host = uri.getHost();
        if (host == null) {
            return false;
        }
        host = host.toLowerCase(Locale.US);
        return host.equals("cycling.today") || host.equals("www.cycling.today");
    }

    private boolean isBlocked(Uri uri) {
        String host = uri.getHost();
        if (host == null) {
            return false;
        }
        host = host.toLowerCase(Locale.US);
        for (String blocked : BLOCKED_HOSTS) {
            if (host.equals(blocked) || host.endsWith("." + blocked)) {
                return true;
            }
        }
        return false;
    }

    private WebResourceResponse emptyResponse() {
        HashMap<String, String> headers = new HashMap<>();
        headers.put("Access-Control-Allow-Origin", "*");
        headers.put("Cache-Control", "no-store");
        return new WebResourceResponse(
                "text/plain",
                "utf-8",
                204,
                "No Content",
                headers,
                new ByteArrayInputStream(new byte[0]));
    }

    private String safeUrlForLog(String value) {
        if (value == null) {
            return "";
        }
        try {
            Uri uri = Uri.parse(value);
            String host = uri.getHost();
            String path = uri.getPath();
            return (host == null ? "" : host) + (path == null ? "" : path);
        } catch (RuntimeException ignored) {
            return value.length() > 160 ? value.substring(0, 160) : value;
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        enterImmersiveMode();
        if (webView != null) {
            webView.onResume();
            webView.resumeTimers();
        }
    }

    @Override
    protected void onPause() {
        if (webView != null) {
            webView.onPause();
        }
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        destroyed = true;
        handler.removeCallbacksAndMessages(null);
        if (customView != null && root != null) {
            root.removeView(customView);
            customView = null;
        }
        if (webView != null) {
            if (root != null) {
                root.removeView(webView);
            }
            webView.stopLoading();
            webView.setWebChromeClient(null);
            webView.setWebViewClient(null);
            webView.destroy();
            webView = null;
        }
        super.onDestroy();
    }

    @Override
    public void onBackPressed() {
        if (customView != null) {
            hideCustomView();
            return;
        }
        if (controlsHidden) {
            setControlsHidden(false);
            return;
        }
        super.onBackPressed();
    }
    private void hideCustomView() {
        if (customView == null) {
            return;
        }
        root.removeView(customView);
        customView = null;
        if (customViewCallback != null) {
            customViewCallback.onCustomViewHidden();
            customViewCallback = null;
        }
        if (webView != null) {
            webView.setVisibility(View.VISIBLE);
        }
        controlsHidden = controlsHiddenBeforeCustomView;
        if (controlBar != null) {
            controlBar.setVisibility(controlsHidden ? View.GONE : View.VISIBLE);
        }
        applyContentInsets();
        enterImmersiveMode();
    }

    private class CleanWebViewClient extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (uri == null) {
                return true;
            }
            if (request.isForMainFrame()) {
                boolean blocked = !isAllowedTopLevel(uri);
                if (blocked) {
                    status("Blocked top-level navigation to " + safeUrlForLog(uri.toString()));
                }
                return blocked;
            }
            if (isBlocked(uri)) {
                blockedRequestCount.incrementAndGet();
                return true;
            }
            return false;
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (uri != null && isBlocked(uri)) {
                blockedRequestCount.incrementAndGet();
                return emptyResponse();
            }
            return super.shouldInterceptRequest(view, request);
        }

        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            logOnly("Navigation started: " + safeUrlForLog(url));
            showLoadingCover("Loading Cycling Today source...");
            injectCleaner();
            scheduleCleanerPoll(200L);
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            logOnly("Navigation content visible: " + safeUrlForLog(url));
            injectCleaner();
            checkCleanerReady();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            logOnly("Navigation finished: " + safeUrlForLog(url)
                    + " blockedRequests=" + blockedRequestCount.get());
            injectCleaner();
            checkCleanerReady();
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (!request.isForMainFrame()) {
                return;
            }
            String description = error == null ? "unknown" : String.valueOf(error.getDescription());
            logOnly("Main navigation error: " + description);
            scheduleNetworkRecovery(description);
            if (networkRecoveryScheduled) {
                return;
            }
            hideLoadingCover();
            status("Cycling Today failed to load. Check the network and press Refresh.");

        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse errorResponse) {
            if (!request.isForMainFrame()) {
                return;
            }
            int statusCode = errorResponse == null ? 0 : errorResponse.getStatusCode();
            logOnly("Main navigation HTTP status=" + statusCode);
            if (statusCode >= 400) {
                hideLoadingCover();
                status("Cycling Today returned HTTP " + statusCode + ". Press Refresh to retry.");
            }

        }

    }

    @TargetApi(Build.VERSION_CODES.O)
    private final class OreoCleanWebViewClient extends CleanWebViewClient {
        @Override
        public boolean onRenderProcessGone(WebView view, RenderProcessGoneDetail detail) {
            boolean crashed = detail != null && detail.didCrash();
            logOnly("WebView renderer ended. crashed=" + crashed + ". Recreating activity.");
            handler.removeCallbacksAndMessages(null);
            if (root != null) {
                root.removeView(view);
            }
            if (webView == view) {
                webView = null;
            }
            view.destroy();
            handler.post(new Runnable() {
                @Override
                public void run() {
                    if (!destroyed) {
                        recreate();
                    }
                }
            });
            return true;
        }
    }

    private final class CleanChromeClient extends WebChromeClient {
        @Override
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, android.os.Message resultMsg) {
            logOnly("Popup window blocked.");
            return false;
        }

        @Override
        public void onPermissionRequest(PermissionRequest request) {
            request.deny();
            logOnly("Web permission request denied for privacy.");
        }

        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            if (!cleanerReady && loadingText != null && loadingCover != null
                    && loadingCover.getVisibility() == View.VISIBLE
                    && cleanerPollAttempts < 2) {
                loadingText.setText("Loading page... " + newProgress + "%\nThen locating the live player");
            }
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (customView != null) {
                callback.onCustomViewHidden();
                return;
            }
            customView = view;
            customViewCallback = callback;
            controlsHiddenBeforeCustomView = controlsHidden;
            if (loadingCover != null) {
                loadingCover.setVisibility(View.GONE);
            }
            if (controlBar != null) {
                controlBar.setVisibility(View.GONE);
            }
            if (webView != null) {
                webView.setVisibility(View.GONE);
            }
            root.addView(customView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));
            customView.bringToFront();
            enterImmersiveMode();
            logOnly("Player requested native full screen.");
        }

        @Override
        public void onHideCustomView() {
            hideCustomView();
            logOnly("Player exited native full screen.");
        }
    }
}
