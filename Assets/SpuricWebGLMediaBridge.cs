using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// WebGL-only helpers for:
/// - opening a browser file picker and returning a DataURL back into Unity via SendMessage
/// - showing an in-app HTML5 video overlay (no new tab / no external URL open)
/// - downloading a remote URL as a file (best-effort, stays in-app)
///
/// Place the matching .jslib at: Assets/Plugins/WebGL/SpuricWebGLMedia.jslib
/// </summary>
public static class SpuricWebGLMediaBridge
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void Spuric_OpenFilePicker(string gameObjectName, string callbackMethod, string accept);
    [DllImport("__Internal")] private static extern void Spuric_ShowVideoOverlay(string urlOrDataUrl);
    [DllImport("__Internal")] private static extern void Spuric_ShowVideoInRect(string urlOrDataUrl, int x, int y, int w, int h, int screenW, int screenH);
    [DllImport("__Internal")] private static extern void Spuric_HideInlineVideo();
    [DllImport("__Internal")] private static extern void Spuric_HideVideoOverlay();
    [DllImport("__Internal")] private static extern void Spuric_DownloadUrlAsFile(string url, string filename);
#endif

    public static bool IsWebGLRuntime
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    public static void OpenFilePicker(string gameObjectName, string callbackMethod, string accept)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_OpenFilePicker(gameObjectName, callbackMethod, string.IsNullOrEmpty(accept) ? "*/*" : accept); }
        catch (Exception e) { Debug.LogWarning("[SpuricWebGLMediaBridge] OpenFilePicker failed: " + e.Message); }
#else
        Debug.LogWarning("[SpuricWebGLMediaBridge] OpenFilePicker is WebGL-only.");
#endif
    }

        public static void ShowVideoInRect(string urlOrDataUrl, int x, int y, int w, int h, int screenW, int screenH)
    {
        if (string.IsNullOrEmpty(urlOrDataUrl)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_ShowVideoInRect(urlOrDataUrl, x, y, w, h, screenW, screenH); }
        catch (Exception e) { Debug.LogWarning("[SpuricWebGLMediaBridge] ShowVideoInRect failed: " + e.Message); }
#else
        Debug.Log("[SpuricWebGLMediaBridge] ShowVideoInRect is WebGL-only. URL: " + urlOrDataUrl);
#endif
    }

    public static void HideInlineVideo()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_HideInlineVideo(); }
        catch { }
#endif
    }

public static void ShowVideoOverlayFromUrl(string urlOrDataUrl)
    {
        if (string.IsNullOrEmpty(urlOrDataUrl)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_ShowVideoOverlay(urlOrDataUrl); }
        catch (Exception e) { Debug.LogWarning("[SpuricWebGLMediaBridge] ShowVideoOverlay failed: " + e.Message); }
#else
        Debug.Log("[SpuricWebGLMediaBridge] Video overlay is WebGL-only. URL: " + urlOrDataUrl);
#endif
    }

    public static void HideVideoOverlay()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_HideVideoOverlay(); }
        catch { }
#endif
    }

    public static void DownloadUrlAsFile(string url, string filename)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (string.IsNullOrEmpty(filename)) filename = "download.bin";
#if UNITY_WEBGL && !UNITY_EDITOR
        try { Spuric_DownloadUrlAsFile(url, filename); }
        catch (Exception e) { Debug.LogWarning("[SpuricWebGLMediaBridge] DownloadUrlAsFile failed: " + e.Message); }
#else
        Debug.Log("[SpuricWebGLMediaBridge] DownloadUrlAsFile is WebGL-only. URL: " + url);
#endif
    }

    /// <summary>
    /// JS sends: "{name}|||{mime}|||{dataUrl}"
    /// </summary>
    public static bool TryParseFilePayload(string payload, out string fileName, out string mimeType, out string dataUrl)
    {
        fileName = null; mimeType = null; dataUrl = null;
        if (string.IsNullOrEmpty(payload)) return false;

        string[] parts = payload.Split(new string[] { "|||" }, StringSplitOptions.None);
        if (parts.Length < 3) return false;

        fileName = parts[0];
        mimeType = parts[1];
        dataUrl = parts[2];
        return true;
    }

    public static bool TryExtractBase64FromDataUrl(string dataUrl, out string mimeType, out string base64)
    {
        mimeType = null;
        base64 = null;
        if (string.IsNullOrEmpty(dataUrl)) return false;

        // data:<mime>;base64,<payload>
        int comma = dataUrl.IndexOf(',');
        if (comma < 0) return false;

        string header = dataUrl.Substring(0, comma);
        string body = dataUrl.Substring(comma + 1);

        int colon = header.IndexOf(':');
        int semi = header.IndexOf(';');
        if (colon >= 0 && semi > colon) mimeType = header.Substring(colon + 1, semi - colon - 1);

        base64 = body;
        return !string.IsNullOrEmpty(base64);
    }
}
