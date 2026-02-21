// KiteLoginSceneBuilder.cs
// Unity 6 (uGUI) - KiteAI Testnet gate for EthDenver demo (WebGL + MetaMask)
// ------------------------------------------------------------------------
// OPTION B (2-file setup, no Editor script):
// - No auto-generation on Play.
// - Right-click the component header in Inspector to run:
//     * Generate UI
//     * Clear UI
//   (These are [ContextMenu] actions; no Assets/Editor script needed.)
//
// Runtime:
// - Button "Connect + Switch to KiteAI" triggers MetaMask add/switch + connect
// - On success, loads scene "EthDenver" (configurable).
//
// REQUIRED FILES (ONLY 2):
// - Assets/KiteLoginSceneBuilder.cs (this file)
// - Assets/Plugins/WebGL/Metamask_WebGL.jslib
//
// IMPORTANT:
// - MetaMask works ONLY in a WebGL build served over http/https (NOT file://).
// - Unity 6 Build Profiles: Platform "Web" == WebGL build.
//
// Scene expectations:
// - Scene: kite_login
// - Scene: EthDenver
// - In kite_login: add an Empty GameObject + attach this component.
// - Use context menu to Generate UI, then SAVE the scene.

using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class KiteLoginSceneBuilder : MonoBehaviour
{
    [Header("Runtime")]
    [Tooltip("Scene to load after successful connect + correct network.")]
    public string nextSceneName = "EthDenver";

    [Tooltip("If true, the gate will load the next scene immediately after successful connect.")]
    public bool autoAdvanceOnSuccess = true;

    // KiteAI Testnet settings
    private const string CHAIN_NAME = "KiteAI Testnet";
    private const string CHAIN_ID_HEX = "0x940"; // 2368 decimal
    private const string RPC_URL = "https://rpc-testnet.gokite.ai/";
    private const string EXPLORER_URL = "https://testnet.kitescan.ai/";
    private const string NATIVE_SYMBOL = "KITE";
    private const int NATIVE_DECIMALS = 18;

    private const string UI_ROOT_NAME = "KiteLogin_UI_Root";
    private const string BTN_NAME = "ConnectButton";
    private const string STATUS_NAME = "StatusText";
    private const string WALLET_NAME = "WalletText";

    // Runtime cached refs
    private Button _connectBtn;
    private Text _status;
    private Text _wallet;

    // Procedural sprites (internal)
    private Sprite _bgSprite;
    private Sprite _cardSprite;
    private Sprite _cardShadowSprite;
    private Sprite _buttonSprite;
    private Sprite _buttonPressedSprite;
    private Sprite _pillSprite;
    private Sprite _dividerSprite;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int MetaMask_IsAvailable();
    [DllImport("__Internal")] private static extern void MetaMask_ConnectAndSwitch(
        string chainIdHex,
        string chainName,
        string rpcUrl,
        string blockExplorerUrl,
        string nativeSymbol,
        int nativeDecimals,
        string gameObjectName,
        string successCallbackMethod,
        string errorCallbackMethod
    );
#else
    private static int MetaMask_IsAvailable() => 0;
    private static void MetaMask_ConnectAndSwitch(
        string chainIdHex, string chainName, string rpcUrl, string blockExplorerUrl,
        string nativeSymbol, int nativeDecimals, string gameObjectName, string successCallbackMethod, string errorCallbackMethod
    )
    {
        Debug.LogWarning("MetaMask_ConnectAndSwitch called in Editor/Non-WebGL. Build WebGL to test.");
    }
#endif

    [Serializable]
    private class WalletPayload
    {
        public string address;
        public string chainId;
    }

    // --------------------------------------------------------------------
    // NO auto-generation on Play.
    // We only wire up the button if you've generated UI and saved the scene.
    // --------------------------------------------------------------------
    private void Start()
    {
        TryWireRuntimeUI();
        UpdateAvailabilityStatus();
    }

    private void TryWireRuntimeUI()
    {
        var root = GameObject.Find(UI_ROOT_NAME);
        if (root == null)
        {
            Debug.LogWarning("[KiteLoginSceneBuilder] UI root not found. Use the component context menu: Generate UI, then save the scene.");
            return;
        }

        var btnGo = FindDeepChild(root.transform, BTN_NAME);
        var statusGo = FindDeepChild(root.transform, STATUS_NAME);
        var walletGo = FindDeepChild(root.transform, WALLET_NAME);

        if (btnGo) _connectBtn = btnGo.GetComponent<Button>();
        if (statusGo) _status = statusGo.GetComponent<Text>();
        if (walletGo) _wallet = walletGo.GetComponent<Text>();

        if (_connectBtn != null)
        {
            _connectBtn.onClick.RemoveAllListeners();
            _connectBtn.onClick.AddListener(OnConnectClicked);
        }
    }

    private void UpdateAvailabilityStatus()
    {
        if (_status == null) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        if (MetaMask_IsAvailable() == 0)
            SetStatus("MetaMask not detected. Install extension, then refresh the page.");
        else
            SetStatus("Ready. Click Connect + Switch to enter.");
#else
        SetStatus("MetaMask works only in a WebGL build running in a browser (http/https).");
#endif
    }

    // ====================================================================
    // CONTEXT MENU ACTIONS (Inspector -> ⋮ / Right-click component header)
    // ====================================================================
    [ContextMenu("Generate UI")]
    public void GenerateUI()
    {
        BuildSpritesOnce();
        EnsureCanvasAndEventSystem();

        // Clear any old UI first
        ClearUI_Internal();

        var canvas = FindObjectOfType<Canvas>();
        if (canvas == null) canvas = CreateCanvas();

        // Root
        var root = CreateUI(UI_ROOT_NAME, canvas.transform);
        Stretch(root);

        // Background
        var bg = CreateUI("BG", root.transform);
        Stretch(bg);
        var bgImg = bg.AddComponent<Image>();
        bgImg.sprite = _bgSprite;
        bgImg.color = Color.white;

        // Card wrapper centered
        var cardWrap = CreateUI("CardWrap", root.transform);
        var cwrt = cardWrap.GetComponent<RectTransform>();
        cwrt.anchorMin = new Vector2(0.5f, 0.5f);
        cwrt.anchorMax = new Vector2(0.5f, 0.5f);
        cwrt.pivot = new Vector2(0.5f, 0.5f);
        cwrt.sizeDelta = new Vector2(1040, 580);
        cwrt.anchoredPosition = new Vector2(0, 10);

        // Shadow
        var shadow = CreateUI("Shadow", cardWrap.transform);
        Stretch(shadow);
        var shrt = shadow.GetComponent<RectTransform>();
        shrt.anchoredPosition = new Vector2(10, -12);
        var shImg = shadow.AddComponent<Image>();
        shImg.sprite = _cardShadowSprite;
        shImg.type = Image.Type.Sliced;
        shImg.color = new Color(0, 0, 0, 0.35f);

        // Card
        var card = CreateUI("Card", cardWrap.transform);
        Stretch(card);
        var cardImg = card.AddComponent<Image>();
        cardImg.sprite = _cardSprite;
        cardImg.type = Image.Type.Sliced;
        cardImg.color = Color.white;

        // Content area
        var content = CreateUI("Content", card.transform);
        var crt = content.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 0);
        crt.anchorMax = new Vector2(1, 1);
        crt.offsetMin = new Vector2(64, 56);
        crt.offsetMax = new Vector2(-64, -56);

        // Top pill: "KiteAI Testnet"
        var pill = CreateUI("TopPill", content.transform);
        var prt = pill.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0, 1);
        prt.anchorMax = new Vector2(0, 1);
        prt.pivot = new Vector2(0, 1);
        prt.sizeDelta = new Vector2(240, 44);
        prt.anchoredPosition = new Vector2(0, 0);

        var pillImg = pill.AddComponent<Image>();
        pillImg.sprite = _pillSprite;
        pillImg.type = Image.Type.Sliced;
        pillImg.color = new Color32(232, 245, 255, 255);

        var pillText = CreateUI("Text", pill.transform).AddComponent<Text>();
        SetFont(pillText);
        pillText.text = "KiteAI Testnet";
        pillText.fontStyle = FontStyle.Bold;
        pillText.fontSize = 18;
        pillText.color = new Color32(0, 98, 190, 255);
        pillText.alignment = TextAnchor.MiddleCenter;
        Stretch(pillText.gameObject);

        // Title: Connect Wallet
        var title = CreateUI("Title", content.transform).AddComponent<Text>();
        SetFont(title);
        title.text = "Connect Wallet";
        title.fontStyle = FontStyle.Bold;
        title.fontSize = 58;
        title.color = new Color32(10, 22, 40, 255);
        title.alignment = TextAnchor.UpperLeft;

        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 1);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0, 1);
        trt.sizeDelta = new Vector2(0, 70);
        trt.anchoredPosition = new Vector2(0, -62);

        // Subtitle: Enter the EthDenver Demo
        var subtitle = CreateUI("Subtitle", content.transform).AddComponent<Text>();
        SetFont(subtitle);
        subtitle.supportRichText = true;
        subtitle.text = "Enter the <color=#D9A441><b>EthDenver</b></color> Demo";
        subtitle.fontSize = 28;
        subtitle.color = new Color32(20, 40, 70, 255);
        subtitle.alignment = TextAnchor.UpperLeft;

        var sbrt = subtitle.GetComponent<RectTransform>();
        sbrt.anchorMin = new Vector2(0, 1);
        sbrt.anchorMax = new Vector2(1, 1);
        sbrt.pivot = new Vector2(0, 1);
        sbrt.sizeDelta = new Vector2(0, 40);
        sbrt.anchoredPosition = new Vector2(0, -132);

        // Divider
        var divider = CreateUI("Divider", content.transform).AddComponent<Image>();
        divider.sprite = _dividerSprite;
        divider.color = new Color32(190, 220, 255, 255);
        var drt = divider.GetComponent<RectTransform>();
        drt.anchorMin = new Vector2(0, 1);
        drt.anchorMax = new Vector2(1, 1);
        drt.pivot = new Vector2(0.5f, 1);
        drt.sizeDelta = new Vector2(0, 2);
        drt.anchoredPosition = new Vector2(0, -188);

        // Connect button
        var btn = CreateUI(BTN_NAME, content.transform);
        var brt = btn.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(0, 1);
        brt.anchorMax = new Vector2(0, 1);
        brt.pivot = new Vector2(0, 1);
        brt.sizeDelta = new Vector2(640, 96);
        brt.anchoredPosition = new Vector2(0, -238);

        var btnImg = btn.AddComponent<Image>();
        btnImg.sprite = _buttonSprite;
        btnImg.type = Image.Type.Sliced;
        btnImg.color = Color.white;

        var b = btn.AddComponent<Button>();
        b.transition = Selectable.Transition.SpriteSwap;
        b.spriteState = new SpriteState
        {
            highlightedSprite = _buttonSprite,
            selectedSprite = _buttonSprite,
            pressedSprite = _buttonPressedSprite,
            disabledSprite = _buttonPressedSprite
        };

        var btnText = CreateUI("Text", btn.transform).AddComponent<Text>();
        SetFont(btnText);
        btnText.text = "Connect + Switch to KiteAI";
        btnText.fontStyle = FontStyle.Bold;
        btnText.fontSize = 28;
        btnText.color = Color.white;
        btnText.alignment = TextAnchor.MiddleCenter;
        Stretch(btnText.gameObject);

        // Status label
        _status = CreateUI(STATUS_NAME, content.transform).AddComponent<Text>();
        SetFont(_status);
        _status.text = "Status: Ready";
        _status.fontStyle = FontStyle.Bold;
        _status.fontSize = 18;
        _status.color = new Color32(0, 110, 190, 255);
        _status.alignment = TextAnchor.UpperLeft;

        var strt = _status.GetComponent<RectTransform>();
        strt.anchorMin = new Vector2(0, 1);
        strt.anchorMax = new Vector2(1, 1);
        strt.pivot = new Vector2(0, 1);
        strt.sizeDelta = new Vector2(0, 26);
        strt.anchoredPosition = new Vector2(0, -356);

        // Wallet label
        _wallet = CreateUI(WALLET_NAME, content.transform).AddComponent<Text>();
        SetFont(_wallet);
        _wallet.text = "Wallet: —";
        _wallet.fontSize = 18;
        _wallet.color = new Color32(28, 52, 90, 255);
        _wallet.alignment = TextAnchor.UpperLeft;

        var wrt = _wallet.GetComponent<RectTransform>();
        wrt.anchorMin = new Vector2(0, 1);
        wrt.anchorMax = new Vector2(1, 1);
        wrt.pivot = new Vector2(0, 1);
        wrt.sizeDelta = new Vector2(0, 24);
        wrt.anchoredPosition = new Vector2(0, -386);

        // Footer
        var footer = CreateUI("Footer", content.transform).AddComponent<Text>();
        SetFont(footer);
        footer.supportRichText = true;
        footer.text = $"Network: <b>{CHAIN_NAME}</b>  •  ChainId: <b>{CHAIN_ID_HEX}</b>  •  RPC: <b>{RPC_URL}</b>";
        footer.fontSize = 14;
        footer.color = new Color32(55, 85, 125, 255);
        footer.alignment = TextAnchor.LowerLeft;

        var frt = footer.GetComponent<RectTransform>();
        frt.anchorMin = new Vector2(0, 0);
        frt.anchorMax = new Vector2(1, 0);
        frt.pivot = new Vector2(0, 0);
        frt.sizeDelta = new Vector2(0, 22);
        frt.anchoredPosition = new Vector2(0, 0);

        // Wire button for play mode (also works if you press play later)
        TryWireRuntimeUI();
        UpdateAvailabilityStatus();

        Debug.Log("[KiteLoginSceneBuilder] UI generated. Now SAVE the scene.");
    }

    [ContextMenu("Clear UI")]
    public void ClearUI()
    {
        ClearUI_Internal();
        _connectBtn = null;
        _status = null;
        _wallet = null;
        Debug.Log("[KiteLoginSceneBuilder] UI cleared. Now SAVE the scene.");
    }

    private void ClearUI_Internal()
    {
        var root = GameObject.Find(UI_ROOT_NAME);
        if (root == null) return;

        if (!Application.isPlaying) DestroyImmediate(root);
        else Destroy(root);
    }

    // ====================================================================
    // CONNECT BUTTON -> METAMASK
    // ====================================================================
    private void OnConnectClicked()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (MetaMask_IsAvailable() == 0)
        {
            SetStatus("MetaMask not detected. Install extension + refresh.");
            return;
        }

        if (_connectBtn) _connectBtn.interactable = false;
        SetStatus("Opening MetaMask… approve: add/switch network + connect wallet.");

        MetaMask_ConnectAndSwitch(
            CHAIN_ID_HEX,
            CHAIN_NAME,
            RPC_URL,
            EXPLORER_URL,
            NATIVE_SYMBOL,
            NATIVE_DECIMALS,
            gameObject.name,
            nameof(OnWalletConnected),
            nameof(OnWalletError)
        );
#else
        SetStatus("Build WebGL to test MetaMask (http/https).");
#endif
    }

    public void OnWalletConnected(string json)
    {
        if (_connectBtn) _connectBtn.interactable = true;

        try
        {
            var payload = JsonUtility.FromJson<WalletPayload>(json);

            if (payload == null || string.IsNullOrEmpty(payload.address))
            {
                SetStatus("Connected, but no wallet address returned.");
                return;
            }

            if (!string.IsNullOrEmpty(payload.chainId) &&
                !payload.chainId.Equals(CHAIN_ID_HEX, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Wrong network ({payload.chainId}). Please switch to {CHAIN_NAME}.");
                if (_wallet) _wallet.text = "Wallet: " + ShortAddr(payload.address);
                return;
            }

            if (_wallet) _wallet.text = "Wallet: " + ShortAddr(payload.address);
            SetStatus("Connected!");

            if (autoAdvanceOnSuccess)
            {
                SetStatus("Connected! Entering EthDenver…");
                SceneManager.LoadScene(string.IsNullOrWhiteSpace(nextSceneName) ? "EthDenver" : nextSceneName);
            }
        }
        catch (Exception e)
        {
            SetStatus("Error parsing wallet response: " + e.Message);
        }
    }

    public void OnWalletError(string message)
    {
        if (_connectBtn) _connectBtn.interactable = true;
        SetStatus("Wallet error: " + message);
    }

    private void SetStatus(string msg)
    {
        Debug.Log("[KiteLoginSceneBuilder] " + msg);
        if (_status) _status.text = "Status: " + msg;
    }

    // ====================================================================
    // Canvas / EventSystem
    // ====================================================================
    private void EnsureCanvasAndEventSystem()
    {
        if (FindObjectOfType<Canvas>() == null)
            CreateCanvas();

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();

            // Prefer new Input System UI module if available
            var inputSystemType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemType != null) es.AddComponent(inputSystemType);
            else es.AddComponent<StandaloneInputModule>();
        }
    }

    private static Canvas CreateCanvas()
    {
        var go = new GameObject("Canvas", typeof(RectTransform));
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        go.AddComponent<GraphicRaycaster>();

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    // ====================================================================
    // UI helpers
    // ====================================================================
    private static GameObject CreateUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void SetFont(Text t)
    {
        // Fixes Unity 6 built-in Arial not valid in some setups
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static string ShortAddr(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 10) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }

    private static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        for (int i = 0; i < parent.childCount; i++)
        {
            var c = parent.GetChild(i);
            if (c.name == name) return c;
            var r = FindDeepChild(c, name);
            if (r != null) return r;
        }
        return null;
    }

    // ====================================================================
    // Procedural textures -> sprites (internal only)
    // ====================================================================
    private void BuildSpritesOnce()
    {
        if (_bgSprite != null) return;

        _bgSprite = SpriteFromTex(GradientTex(64, 64,
            new Color32(3, 12, 36, 255),
            new Color32(0, 44, 98, 255)));

        // Card + shadow
        _cardSprite = SpriteFromTex(RoundedRectTex(512, 256, 46,
            new Color32(255, 255, 255, 248),
            new Color32(245, 250, 255, 248),
            borderPx: 3,
            border: new Color32(170, 215, 255, 255),
            glow: false), border: 46);

        _cardShadowSprite = SpriteFromTex(RoundedRectTex(512, 256, 46,
            new Color32(0, 0, 0, 120),
            new Color32(0, 0, 0, 120),
            borderPx: 0,
            border: new Color32(0, 0, 0, 0),
            glow: false), border: 46);

        // Button
        _buttonSprite = SpriteFromTex(RoundedRectTex(512, 128, 50,
            new Color32(0, 165, 255, 255),
            new Color32(0, 88, 235, 255),
            borderPx: 3,
            border: new Color32(205, 250, 255, 255),
            glow: true), border: 50);

        _buttonPressedSprite = SpriteFromTex(RoundedRectTex(512, 128, 50,
            new Color32(0, 125, 235, 255),
            new Color32(0, 70, 200, 255),
            borderPx: 3,
            border: new Color32(205, 250, 255, 255),
            glow: false), border: 50);

        _pillSprite = SpriteFromTex(RoundedRectTex(512, 128, 60,
            new Color32(240, 250, 255, 255),
            new Color32(225, 242, 255, 255),
            borderPx: 2,
            border: new Color32(190, 225, 255, 255),
            glow: false), border: 60);

        _dividerSprite = SpriteFromTex(SolidTex(2, 2, new Color32(200, 225, 255, 255)));
    }

    private static Sprite SpriteFromTex(Texture2D tex, int border = 0)
    {
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        var rect = new Rect(0, 0, tex.width, tex.height);
        var pivot = new Vector2(0.5f, 0.5f);
        var b = new Vector4(border, border, border, border);
        return Sprite.Create(tex, rect, pivot, 100f, 0, SpriteMeshType.FullRect, b);
    }

    private static Texture2D SolidTex(int w, int h, Color c)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    private static Texture2D GradientTex(int w, int h, Color top, Color bottom)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        for (int y = 0; y < h; y++)
        {
            float ty = (float)y / (h - 1);
            var c = Color.Lerp(bottom, top, ty);
            for (int x = 0; x < w; x++) t.SetPixel(x, y, c);
        }
        t.Apply();
        return t;
    }

    // Rounded rect with optional border and soft glow outside.
    private static Texture2D RoundedRectTex(int w, int h, float radius, Color top, Color bottom, int borderPx, Color border, bool glow)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        float aa = 1.25f;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float ty = (float)y / (h - 1);
            var fill = Color.Lerp(bottom, top, ty);

            float d = SdfRoundedRect(x + 0.5f, y + 0.5f, w, h, radius);

            float aIn = SmoothStep(0f, -aa, d);

            float aBorder = 0f;
            if (borderPx > 0)
                aBorder = SmoothStep(borderPx + aa, borderPx, Mathf.Abs(d));

            var c = fill;
            if (aBorder > 0.001f && d <= 0.5f)
                c = Color.Lerp(c, border, aBorder);

            if (glow && d > 0f && d < 16f)
            {
                float aGlow = Mathf.Exp(-d * 0.20f) * 0.55f;
                var g = new Color(0.20f, 0.95f, 1f, aGlow);
                c = Color.Lerp(g, c, aIn);
                c.a = Mathf.Max(c.a, aGlow);
            }
            else
            {
                c.a = aIn;
            }

            t.SetPixel(x, y, c);
        }

        t.Apply();
        return t;
    }

    private static float SdfRoundedRect(float px, float py, float w, float h, float r)
    {
        float cx = w * 0.5f;
        float cy = h * 0.5f;
        float x = Mathf.Abs(px - cx) - (w * 0.5f - r);
        float y = Mathf.Abs(py - cy) - (h * 0.5f - r);
        float ax = Mathf.Max(x, 0f);
        float ay = Mathf.Max(y, 0f);
        float outside = Mathf.Sqrt(ax * ax + ay * ay);
        float inside = Mathf.Min(Mathf.Max(x, y), 0f);
        return outside + inside - r;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
