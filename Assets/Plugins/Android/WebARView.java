package com.realitydrop.webar;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.Dialog;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Intent;
import android.media.MediaScannerConnection;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;
import android.util.Base64;
import android.graphics.Color;
import android.net.Uri;
import android.util.Log;
import android.view.HapticFeedbackConstants;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.ConsoleMessage;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.HashMap;
import java.util.Map;

/**
 * Full-screen WebView over the Unity player that runs the 8th Wall XR8 WebAR page
 * (StreamingAssets/webar → APK assets/webar).
 *
 * Everything is served from https://appassets.androidplatform.net (Android's reserved in-app host):
 *   /webar/...   → APK assets (page, XR8 engine, three.js)
 *   /model.glb   → the downloaded model file
 * An https origin is a "secure context", which getUserMedia (camera) and XR8 require.
 * Page console output goes to logcat with tag RealityDropWebAR; chrome://inspect works in debug builds.
 */
public class WebARView {
    private static final String TAG = "RealityDropWebAR";
    private static final String HOST = "appassets.androidplatform.net";

    private static WebView webView;
    private static Dialog dialog;
    private static Uri lastPhoto;
    private static String modelPath;
    private static String unityObject;

    public static boolean isOpen() { return webView != null; }

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    public static void open(final Activity activity, final String model, final String title, final String callbackObject) {
        activity.runOnUiThread(() -> {
            if (webView != null) return;
            modelPath = model;
            unityObject = callbackObject;

            WebView.setWebContentsDebuggingEnabled(true);
            final WebView wv = new WebView(activity);
            wv.setBackgroundColor(Color.BLACK);

            WebSettings s = wv.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            s.setMediaPlaybackRequiresUserGesture(false);
            s.setAllowFileAccess(false);
            s.setAllowContentAccess(false);
            // Present as regular Chrome (drop the "; wv" WebView marker) so XR8 treats it as a supported browser
            s.setUserAgentString(s.getUserAgentString().replace("; wv)", ")"));

            // Keep the AR page's renderer at top priority (smooth frame rate)
            if (Build.VERSION.SDK_INT >= 26) wv.setRendererPriorityPolicy(WebView.RENDERER_PRIORITY_IMPORTANT, false);

            wv.setWebChromeClient(new WebChromeClient() {
                @Override
                public void onPermissionRequest(final PermissionRequest request) {
                    // The page is our own bundled content: allow the camera it asks for
                    activity.runOnUiThread(() -> request.grant(request.getResources()));
                }

                @Override
                public boolean onConsoleMessage(ConsoleMessage m) {
                    Log.i(TAG, m.message() + "  (" + m.sourceId() + ":" + m.lineNumber() + ")");
                    return true;
                }
            });

            wv.setWebViewClient(new WebViewClient() {
                @Override
                public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
                    return serve(activity, request.getUrl());
                }

                @Override
                public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
                    return !HOST.equals(request.getUrl().getHost()); // never navigate away from the AR page
                }
            });

            wv.addJavascriptInterface(new Bridge(activity), "RealityDropNative");

            // Unity's activity is built with android:hardwareAccelerated="false". A WebView inside it
            // draws in software, and WebGL canvases (camera feed + 3D) then show black. So the WebView
            // lives in its own full-screen window with hardware acceleration switched on.
            Dialog d = new Dialog(activity, android.R.style.Theme_Black_NoTitleBar_Fullscreen);
            Window w = d.getWindow();
            w.setFlags(WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED,
                       WindowManager.LayoutParams.FLAG_HARDWARE_ACCELERATED);
            w.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON | WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS);
            d.setCancelable(false);
            d.setContentView(wv, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            // Android back → let the page decide (close a sheet, or leave AR)
            d.setOnKeyListener((di, keyCode, event) -> {
                if (keyCode != KeyEvent.KEYCODE_BACK) return false;
                if (event.getAction() == KeyEvent.ACTION_UP)
                    wv.evaluateJavascript("window.rdBack ? rdBack() : RealityDropNative.close()", null);
                return true;
            });
            d.show();
            // Immersive full screen like the app (hide status / navigation bars)
            w.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                    | View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                    | View.SYSTEM_UI_FLAG_LAYOUT_STABLE);
            dialog = d;
            Log.i(TAG, "WebView hardware accelerated: " + wv.isHardwareAccelerated());
            wv.setFocusable(true);
            wv.setFocusableInTouchMode(true);
            wv.requestFocus();
            webView = wv;

            wv.loadUrl("https://" + HOST + "/webar/index.html?title=" + Uri.encode(title == null ? "" : title));
            Log.i(TAG, "WebAR opened. Model: " + model);
        });
    }

    public static void close(final Activity activity) {
        activity.runOnUiThread(() -> {
            if (webView == null) return;
            WebView wv = webView;
            webView = null;
            wv.stopLoading();
            wv.loadUrl("about:blank"); // releases the camera immediately
            if (dialog != null) { dialog.dismiss(); dialog = null; }
            wv.destroy();
            Log.i(TAG, "WebAR closed.");
            if (unityObject != null) {
                try {
                    com.unity3d.player.UnityPlayer.UnitySendMessage(unityObject, "OnWebARClosed", "");
                } catch (Throwable t) {
                    Log.w(TAG, "UnitySendMessage failed: " + t);
                }
            }
        });
    }

    // ------------------------------------------------------------------ local content server

    private static WebResourceResponse serve(Activity activity, Uri uri) {
        if (!HOST.equals(uri.getHost())) return null;
        String path = uri.getPath() == null ? "/" : uri.getPath();
        try {
            InputStream in;
            long length = -1;
            if ("/model.glb".equals(path)) {
                File f = new File(modelPath);
                length = f.length();
                in = new FileInputStream(f);
            } else if (path.startsWith("/webar/") && !path.contains("..")) {
                in = activity.getAssets().open(path.substring(1));
            } else {
                return notFound(path);
            }
            String mime = mimeFor(path);
            WebResourceResponse r = new WebResourceResponse(mime, mime.startsWith("text/") || mime.endsWith("javascript") ? "utf-8" : null, in);
            Map<String, String> headers = new HashMap<>();
            headers.put("Access-Control-Allow-Origin", "*");
            headers.put("Cache-Control", "no-cache");
            if (length >= 0) headers.put("Content-Length", Long.toString(length)); // model loading progress
            r.setStatusCodeAndReasonPhrase(200, "OK");
            r.setResponseHeaders(headers);
            return r;
        } catch (Exception e) {
            Log.w(TAG, "Missing: " + path + " (" + e.getMessage() + ")");
            return notFound(path);
        }
    }

    private static WebResourceResponse notFound(String path) {
        WebResourceResponse r = new WebResourceResponse("text/plain", "utf-8", new ByteArrayInputStream(new byte[0]));
        r.setStatusCodeAndReasonPhrase(404, "Not Found");
        return r;
    }

    private static String mimeFor(String path) {
        String p = path.toLowerCase();
        if (p.endsWith(".html")) return "text/html";
        if (p.endsWith(".js") || p.endsWith(".mjs")) return "application/javascript";
        if (p.endsWith(".css")) return "text/css";
        if (p.endsWith(".json")) return "application/json";
        if (p.endsWith(".wasm")) return "application/wasm";
        if (p.endsWith(".svg")) return "image/svg+xml";
        if (p.endsWith(".png")) return "image/png";
        if (p.endsWith(".glb")) return "model/gltf-binary";
        if (p.endsWith(".ttf")) return "font/ttf";
        if (p.endsWith("license")) return "text/plain";
        return "application/octet-stream";
    }

    // ------------------------------------------------------------------ JS → native

    private static class Bridge {
        private final Activity activity;
        Bridge(Activity a) { activity = a; }

        @JavascriptInterface
        public void close() { WebARView.close(activity); }

        /** Saves a JPEG (base64, no data: prefix) to the gallery (Pictures/RealityDrop). Calls rdPhotoSaved(ok). */
        @JavascriptInterface
        public void savePhoto(String base64) {
            boolean ok = false;
            try {
                byte[] bytes = Base64.decode(base64, Base64.DEFAULT);
                String name = "RealityDrop_" + new SimpleDateFormat("yyyyMMdd_HHmmss", Locale.US).format(new Date()) + ".jpg";
                if (Build.VERSION.SDK_INT >= 29) {
                    ContentResolver cr = activity.getContentResolver();
                    ContentValues v = new ContentValues();
                    v.put(MediaStore.Images.Media.DISPLAY_NAME, name);
                    v.put(MediaStore.Images.Media.MIME_TYPE, "image/jpeg");
                    v.put(MediaStore.Images.Media.RELATIVE_PATH, Environment.DIRECTORY_PICTURES + "/RealityDrop");
                    v.put(MediaStore.Images.Media.IS_PENDING, 1);
                    Uri uri = cr.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, v);
                    if (uri != null) {
                        try (OutputStream out = cr.openOutputStream(uri)) { out.write(bytes); }
                        v.clear();
                        v.put(MediaStore.Images.Media.IS_PENDING, 0);
                        cr.update(uri, v, null, null);
                        lastPhoto = uri;
                        ok = true;
                    }
                } else {
                    // Android 7–9 without storage permission: app pictures folder, indexed by the media scanner
                    File dir = activity.getExternalFilesDir(Environment.DIRECTORY_PICTURES);
                    File file = new File(dir, name);
                    try (FileOutputStream out = new FileOutputStream(file)) { out.write(bytes); }
                    MediaScannerConnection.scanFile(activity, new String[]{file.getAbsolutePath()}, new String[]{"image/jpeg"}, null);
                    lastPhoto = null;
                    ok = true;
                }
                Log.i(TAG, "Photo saved: " + name + " (" + bytes.length / 1024 + " KB)");
            } catch (Exception e) {
                Log.e(TAG, "Photo save failed: " + e);
            }
            final boolean saved = ok;
            final boolean canShare = lastPhoto != null;
            activity.runOnUiThread(() -> {
                if (webView != null) webView.evaluateJavascript("window.rdPhotoSaved && rdPhotoSaved(" + saved + "," + canShare + ")", null);
            });
        }

        /** Opens the Android share sheet for the last saved photo. */
        @JavascriptInterface
        public void sharePhoto() {
            if (lastPhoto == null) return;
            activity.runOnUiThread(() -> {
                Intent send = new Intent(Intent.ACTION_SEND);
                send.setType("image/jpeg");
                send.putExtra(Intent.EXTRA_STREAM, lastPhoto);
                send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                activity.startActivity(Intent.createChooser(send, "Share photo"));
            });
        }

        @JavascriptInterface
        public void haptic() {
            activity.runOnUiThread(() -> {
                if (webView != null) webView.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP);
            });
        }
    }
}
