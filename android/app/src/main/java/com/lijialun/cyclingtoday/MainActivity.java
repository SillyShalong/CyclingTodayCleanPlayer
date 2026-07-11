package com.lijialun.cyclingtoday;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.pm.ActivityInfo;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.view.MotionEvent;
import android.view.Gravity;
import android.view.InputDevice;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.io.ByteArrayInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

public class MainActivity extends Activity {
    private static final String SOURCE_URL = "https://cycling.today/";

    private final Handler handler = new Handler(Looper.getMainLooper());
    private WebView webView;
    private FrameLayout root;
    private View loadingCover;
    private LinearLayout controlBar;
    private TextView statusText;
    private View customView;
    private WebChromeClient.CustomViewCallback customViewCallback;
    private String cleanerScript;
    private boolean cleanerReady;
    private boolean cleanerPollScheduled;
    private boolean autoTapScheduled;
    private int autoTapAttempts;

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
    ));

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN, WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE);

        root = new FrameLayout(this);
        root.setBackgroundColor(0xff000000);
        setContentView(root);
        enterImmersiveMode();
        createWebView();
        status("Loading player...");
        webView.loadUrl(SOURCE_URL);
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void createWebView() {
        webView = new WebView(this);
        webView.setBackgroundColor(0xff000000);
        webView.setOverScrollMode(View.OVER_SCROLL_NEVER);
        webView.setScrollBarStyle(View.SCROLLBARS_INSIDE_OVERLAY);

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

        webView.setWebViewClient(new CleanWebViewClient());
        webView.setWebChromeClient(new CleanChromeClient());

        FrameLayout.LayoutParams webViewParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT);
        webViewParams.bottomMargin = dp(58);
        root.addView(webView, webViewParams);

        loadingCover = new View(this);
        loadingCover.setBackgroundColor(0xff000000);
        FrameLayout.LayoutParams coverParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT);
        coverParams.bottomMargin = dp(58);
        root.addView(loadingCover, coverParams);

        createControlBar();
    }

    private void createControlBar() {
        controlBar = new LinearLayout(this);
        controlBar.setOrientation(LinearLayout.HORIZONTAL);
        controlBar.setGravity(Gravity.CENTER_VERTICAL);
        controlBar.setPadding(dp(10), dp(6), dp(10), dp(6));
        controlBar.setBackgroundColor(0xcc161616);

        statusText = new TextView(this);
        statusText.setText("Loading player...");
        statusText.setTextColor(0xffeeeeee);
        statusText.setTextSize(12f);
        statusText.setSingleLine(true);
        LinearLayout.LayoutParams statusParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f);
        controlBar.addView(statusText, statusParams);

        Button unmuteButton = new Button(this);
        unmuteButton.setText("Unmute");
        unmuteButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                status("Sending unmute/resume...");
                manualUnmute();
            }
        });
        controlBar.addView(unmuteButton, new LinearLayout.LayoutParams(dp(112), dp(42)));

        Button refreshButton = new Button(this);
        refreshButton.setText("Refresh");
        refreshButton.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                refreshPlayer();
            }
        });
        LinearLayout.LayoutParams refreshParams = new LinearLayout.LayoutParams(dp(112), dp(42));
        refreshParams.leftMargin = dp(8);
        controlBar.addView(refreshButton, refreshParams);

        FrameLayout.LayoutParams barParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                dp(58),
                Gravity.BOTTOM);
        root.addView(controlBar, barParams);
        controlBar.bringToFront();
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void status(String text) {
        if (statusText != null) {
            statusText.setText(text);
        }
    }

    private void refreshPlayer() {
        if (webView == null) {
            return;
        }
        status("Refreshing player...");
        resetCleanerState();
        webView.loadUrl(SOURCE_URL);
    }

    private void manualUnmute() {
        autoTapAttempts = 0;
        sendUnmuteTap(false);
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

    private void injectCleaner() {
        try {
            webView.evaluateJavascript(getCleanerScript(), null);
        } catch (IOException ignored) {
        }
    }

    private String getCleanerScript() throws IOException {
        if (cleanerScript == null) {
            cleanerScript = readAsset("cleaner.js");
        }
        return cleanerScript;
    }

    private String readAsset(String name) throws IOException {
        InputStream input = getAssets().open(name);
        byte[] buffer = new byte[input.available()];
        int read = input.read(buffer);
        input.close();
        if (read <= 0) {
            return "";
        }
        return new String(buffer, 0, read, StandardCharsets.UTF_8);
    }

    private final Runnable autoTapRunnable = new Runnable() {
        @Override
        public void run() {
            sendUnmuteTap(true);
        }
    };

    private final Runnable cleanerPollRunnable = new Runnable() {
        @Override
        public void run() {
            cleanerPollScheduled = false;
            injectCleaner();
            checkCleanerReady();
        }
    };

    private void resetCleanerState() {
        cleanerReady = false;
        cleanerPollScheduled = false;
        autoTapScheduled = false;
        autoTapAttempts = 0;
        handler.removeCallbacks(cleanerPollRunnable);
        handler.removeCallbacks(autoTapRunnable);
        if (loadingCover != null) {
            loadingCover.setVisibility(View.VISIBLE);
            status("Loading page...");
        }
    }

    private void scheduleCleanerPoll(long delayMillis) {
        if (cleanerReady || cleanerPollScheduled) {
            return;
        }
        cleanerPollScheduled = true;
        handler.postDelayed(cleanerPollRunnable, delayMillis);
    }

    private void checkCleanerReady() {
        if (webView == null || cleanerReady) {
            return;
        }

        String script = "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getStateText){return '';};h.apply&&h.apply();return h.getStateText();})()";
        webView.evaluateJavascript(script, new ValueCallback<String>() {
            @Override
            public void onReceiveValue(String value) {
                String state = decodeScriptString(value);
                if (state.contains("playerFound=true")) {
                    cleanerReady = true;
                    if (loadingCover != null) {
                        loadingCover.setVisibility(View.GONE);
                        status("Player ready. Use Refresh if it freezes.");
                    }
                    scheduleAutoUnmuteTap();
                    return;
                }

                scheduleCleanerPoll(250);
            }
        });
    }

    private void scheduleAutoUnmuteTap() {
        if (autoTapScheduled) {
            return;
        }
        autoTapScheduled = true;
        autoTapAttempts = 0;
        handler.postDelayed(autoTapRunnable, 8000);
    }
    private void sendUnmuteTap(final boolean automatic) {
        autoTapAttempts++;
        if (webView == null || webView.getWidth() <= 0 || webView.getHeight() <= 0) {
            return;
        }

        String script = "(function(){var h=window.__cyclingTodayCleanPlayer;if(!h||!h.getClickPointText){return '';}return h.getClickPointText();})()";
        webView.evaluateJavascript(script, new ValueCallback<String>() {
            @Override
            public void onReceiveValue(String value) {
                final List<float[]> points = parseClickPoints(decodeScriptString(value));
                if (points.isEmpty()) {
                    if (autoTapAttempts < 18) {
                        handler.postDelayed(autoTapRunnable, 1000);
                    }
                    return;
                }

                status("Sending unmute/resume...");
                handler.postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        // Multiple taps can toggle mute/play back off. Keep the player touchable.
                        float[] point = points.get(0);
                        tapWebView(point[0], point[1]);
                        status("Player ready. Tap the video if sound is still muted.");
                    }
                }, 120);
            }
        });
    }


    private List<float[]> parseClickPoints(String text) {
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
                float rawX = Float.parseFloat(parts[0]);
                float rawY = Float.parseFloat(parts[1]);
                float maxX = Math.max(10f, webView.getWidth() - 10f);
                float maxY = Math.max(10f, webView.getHeight() - 10f);
                float x = Math.max(10f, Math.min(maxX, rawX));
                float y = Math.max(10f, Math.min(maxY, rawY));

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
        if (decoded.length() >= 2 && decoded.charAt(0) == '"' && decoded.charAt(decoded.length() - 1) == '"') {
            decoded = decoded.substring(1, decoded.length() - 1);
        }

        decoded = decoded.replace("\\\"", "\"");
        decoded = decoded.replace("\\\\", "\\");
        decoded = decoded.replace("\\n", "\n");
        decoded = decoded.replace("\\r", "\r");
        decoded = decoded.replace("\\t", "\t");
        return decoded;
    }

    private void tapWebView(float x, float y) {
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
                if (webView == null) {
                    return;
                }
                long upTime = SystemClock.uptimeMillis();
                MotionEvent up = MotionEvent.obtain(downTime, upTime, MotionEvent.ACTION_UP, tapX, tapY, 0);
                up.setSource(InputDevice.SOURCE_TOUCHSCREEN);
                webView.dispatchTouchEvent(up);
                up.recycle();
            }
        }, 80);
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

    @Override
    protected void onResume() {
        super.onResume();
        enterImmersiveMode();
        if (webView != null) {
            webView.onResume();
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
        handler.removeCallbacksAndMessages(null);
        if (webView != null) {
            root.removeView(webView);
            webView.destroy();
        }
        super.onDestroy();
    }

    private final class CleanWebViewClient extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (uri == null) {
                return true;
            }
            if (request.isForMainFrame()) {
                return !isAllowedTopLevel(uri);
            }
            return isBlocked(uri);
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (uri != null && isBlocked(uri)) {
                return emptyResponse();
            }
            return super.shouldInterceptRequest(view, request);
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            injectCleaner();
            checkCleanerReady();
        }

        @Override
        public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
            resetCleanerState();
            injectCleaner();
            scheduleCleanerPoll(250);
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            injectCleaner();
            checkCleanerReady();
        }
    }

    private final class CleanChromeClient extends WebChromeClient {
        @Override
        public boolean onCreateWindow(WebView view, boolean isDialog, boolean isUserGesture, android.os.Message resultMsg) {
            return false;
        }

        @Override
        public void onPermissionRequest(PermissionRequest request) {
            request.deny();
        }

        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (customView != null) {
                callback.onCustomViewHidden();
                return;
            }
            customView = view;
            customViewCallback = callback;
            root.addView(customView, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));
            webView.setVisibility(View.GONE);
            enterImmersiveMode();
        }

        @Override
        public void onHideCustomView() {
            if (customView == null) {
                return;
            }
            root.removeView(customView);
            customView = null;
            if (customViewCallback != null) {
                customViewCallback.onCustomViewHidden();
                customViewCallback = null;
            }
            webView.setVisibility(View.VISIBLE);
            enterImmersiveMode();
        }
    }
}
