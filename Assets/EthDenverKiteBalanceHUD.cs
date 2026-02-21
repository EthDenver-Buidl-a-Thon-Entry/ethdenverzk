// EthDenverKiteBalanceHUD.cs
// Drop this into Assets/ and add to ANY GameObject in the EthDenver scene.
// It will display the connected wallet + native KITE balance (KiteAI Testnet) in the top-right.
//
// Requires WebGL + MetaMask and Metamask_WebGL.jslib (updated) in Assets/Plugins/WebGL/
//
// Notes:
// - Uses eth_getBalance (native token) via MetaMask provider.
// - No sprites, no inspector setup. Generates a small HUD at runtime.
// - If the user is on the wrong chain, it will show a warning.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// IMPORTANT: Avoid Vector2 ambiguity by aliasing BigInteger only (do NOT 'using System.Numerics;')
using BigInteger = System.Numerics.BigInteger;

public class EthDenverKiteBalanceHUD : MonoBehaviour
{
    [Header("Display")]
    [Tooltip("Show the HUD immediately on scene load.")]
    public bool showOnStart = true;

    [Tooltip("Refresh balance every N seconds.")]
    public float refreshSeconds = 5f;

    [Header("Network Gate (optional)")]
    [Tooltip("If enabled, shows a warning when not on this chain id.")]
    public bool requireExpectedChain = true;

    [Tooltip("KiteAI Testnet chain id in hex. 2368 = 0x940")]
    public string expectedChainIdHex = "0x940";

    private const string HUD_ROOT = "KiteBalanceHUD_Root";
    private Text _line1;
    private Text _line2;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void MetaMask_GetNativeBalance(
        string gameObjectName,
        string successCallbackMethod,
        string errorCallbackMethod
    );
#else
    private static void MetaMask_GetNativeBalance(string gameObjectName, string successCallbackMethod, string errorCallbackMethod) { }
#endif

    [Serializable]
    private class BalancePayload
    {
        public string address;
        public string chainId;
        public string balanceHex;
    }

    private void Start()
    {
        if (!showOnStart) return;

        EnsureEventSystem();
        BuildHUD();

        if (refreshSeconds <= 0.1f) refreshSeconds = 5f;
        InvokeRepeating(nameof(RequestBalance), 0.25f, refreshSeconds);
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(RequestBalance));
    }

    private void BuildHUD()
    {
        // If already exists, reuse
        var existing = GameObject.Find(HUD_ROOT);
        if (existing != null)
        {
            Wire(existing.transform);
            return;
        }

        var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
            canvas = CreateCanvas();

        var root = new GameObject(HUD_ROOT, typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);

        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = new UnityEngine.Vector2(1, 1);
        rrt.anchorMax = new UnityEngine.Vector2(1, 1);
        rrt.pivot = new UnityEngine.Vector2(1, 1);
        rrt.anchoredPosition = new UnityEngine.Vector2(-18, -18);
        rrt.sizeDelta = new UnityEngine.Vector2(360, 86);

        // Background panel
        var bg = new GameObject("BG", typeof(RectTransform));
        bg.transform.SetParent(root.transform, false);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = UnityEngine.Vector2.zero;
        bgRt.anchorMax = UnityEngine.Vector2.one;
        bgRt.offsetMin = UnityEngine.Vector2.zero;
        bgRt.offsetMax = UnityEngine.Vector2.zero;

        var img = bg.AddComponent<Image>();
        img.color = new Color(0.05f, 0.10f, 0.20f, 0.78f);
        img.raycastTarget = false;

        // soft outline
        var outline = bg.AddComponent<Outline>();
        outline.effectColor = new Color(0.4f, 0.85f, 1f, 0.55f);
        outline.effectDistance = new UnityEngine.Vector2(2f, -2f);

        // Layout
        var v = root.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.UpperLeft;
        v.padding = new RectOffset(14, 14, 12, 12);
        v.spacing = 6;

        var fitter = root.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

        _line1 = CreateLine(root.transform, "Wallet: —", 16, FontStyle.Bold, new Color32(180, 235, 255, 255), "Line1");
        _line2 = CreateLine(root.transform, "KITE: —", 18, FontStyle.Bold, Color.white, "Line2");
    }

    private void Wire(Transform root)
    {
        _line1 = root.Find("Line1")?.GetComponent<Text>();
        _line2 = root.Find("Line2")?.GetComponent<Text>();
    }

    private Text CreateLine(Transform parent, string text, int size, FontStyle style, Color color, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var t = go.AddComponent<Text>();
        t.text = text;
        t.fontSize = size;
        t.fontStyle = style;
        t.color = color;
        t.alignment = TextAnchor.MiddleLeft;
        t.raycastTarget = false;
        t.supportRichText = true;

        // Unity 6: avoid Arial issues
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new UnityEngine.Vector2(320, 26);

        return t;
    }

    private void RequestBalance()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MetaMask_GetNativeBalance(gameObject.name, nameof(OnBalanceSuccess), nameof(OnBalanceError));
#else
        SetLine2("KITE: (WebGL build required)");
#endif
    }

    // Called from JS
    public void OnBalanceSuccess(string json)
    {
        try
        {
            var p = JsonUtility.FromJson<BalancePayload>(json);
            if (p == null || string.IsNullOrEmpty(p.address))
            {
                SetLine2("KITE: (no wallet)");
                return;
            }

            if (_line1) _line1.text = "Wallet: " + ShortAddr(p.address);

            if (requireExpectedChain && !string.IsNullOrEmpty(p.chainId))
            {
                if (!p.chainId.Equals(expectedChainIdHex, StringComparison.OrdinalIgnoreCase))
                {
                    SetLine2($"<color=#FFCC66>Wrong network:</color> {p.chainId} (need {expectedChainIdHex})");
                    return;
                }
            }

            if (string.IsNullOrEmpty(p.balanceHex))
            {
                SetLine2("KITE: —");
                return;
            }

            var wei = HexToBigInteger(p.balanceHex);
            var kite = FormatWei(wei, 18, 4);
            SetLine2($"KITE: {kite}");
        }
        catch (Exception e)
        {
            SetLine2("KITE: (parse error)");
            Debug.LogWarning("[EthDenverKiteBalanceHUD] Parse error: " + e.Message);
        }
    }

    // Called from JS
    public void OnBalanceError(string msg)
    {
        SetLine2("KITE: (error)");
        Debug.LogWarning("[EthDenverKiteBalanceHUD] " + msg);
    }

    private void SetLine2(string s)
    {
        if (_line2) _line2.text = s;
    }

    private static string ShortAddr(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 10) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }

    private static BigInteger HexToBigInteger(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex.Substring(2);
        if (hex.Length == 0) return BigInteger.Zero;

        // Prefix 0 so it is always treated as positive
        return BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    // Formats wei-like integer to decimal string with a fixed precision (no floating errors)
    private static string FormatWei(BigInteger value, int decimals, int precision)
    {
        if (value <= 0) return "0";

        var s = value.ToString(CultureInfo.InvariantCulture);

        if (s.Length <= decimals)
            s = s.PadLeft(decimals + 1, '0');

        int split = s.Length - decimals;
        var intPart = s.Substring(0, split);
        var fracPart = s.Substring(split);

        // trim trailing zeros
        fracPart = fracPart.TrimEnd('0');

        if (fracPart.Length > precision)
            fracPart = fracPart.Substring(0, precision);

        if (fracPart.Length == 0)
            return intPart;

        return intPart + "." + fracPart;
    }

    private static Canvas CreateCanvas()
    {
        var go = new GameObject("Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<GraphicRaycaster>();

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new UnityEngine.Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private static void EnsureEventSystem()
    {
        if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();

        var inputSystemType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputSystemType != null) es.AddComponent(inputSystemType);
        else es.AddComponent<StandaloneInputModule>();
    }
}
