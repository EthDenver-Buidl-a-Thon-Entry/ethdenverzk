// KiteBalanceModelPanelInjector.cs
// OVERWRITE: Assets/KiteBalanceModelPanelInjector.cs
//
// Fixes:
// - Brings back the BIG "KITE Wallet: <balance>" line (your total wallet balance)
// - Moves the lock/credits block OUT of the chat area by placing it in its own overlay panel,
//   positioned just BELOW the existing TopRightModelPanel (model buttons).
// - Keeps everything self-contained (no ModernChatManager edits).

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

using BigInteger = System.Numerics.BigInteger;

public class KiteBalanceModelPanelInjector : MonoBehaviour
{
    [Header("Refresh")]
    public float refreshSeconds = 4f;

    [Header("Network Gate")]
    public bool requireExpectedChain = true;
    public string expectedChainIdHex = "0x940"; // KiteAI Testnet (2368)

    [Header("Demo Params")]
    public float lockAmountKite = 0.25f;
    public float costPerMessageKite = 0.05f; // fast demo

    [Header("Settlement TX")]
    [Tooltip("Where settlement sends. Empty -> SELF (real tx, minimal faucet burn).")]
    public string recipientAddress = "";

    [Header("Optional")]
    public bool showShortAddress = false;

    // ModernChatManager object names
    private const string MODEL_PANEL = "TopRightModelPanel";
    private const string MODEL_OVERLAY = "ModelButtonsOverlay";
    private const string SEND_BUTTON = "SendButton";
    private const string INPUT_CONTAINER = "InputFieldContainer";

    // Our overlay names
    private const string HUD_ROOT = "KiteHUDOverlay";
    private const string HUD_PANEL = "KiteHUDPanel";

    // UI refs
    private Text _walletText;
    private Button _lockButton;
    private Text _statusText;
    private Text _creditsText;
    private Text _txText;

    // chat gating refs
    private Button _sendButton;
    private InputField _inputField;

    // state
    private bool _lockActive = false;
    private bool _awaitingSignature = false;
    private bool _awaitingTx = false;
    private float _credits = 0f;

    private string _address = "";
    private string _chainId = "";
    private float _walletBalance = -1f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void MetaMask_GetNativeBalance(string go, string ok, string err);
    [DllImport("__Internal")] private static extern void MetaMask_SignMessage(string message, string go, string ok, string err);
    [DllImport("__Internal")] private static extern void MetaMask_SendNativeTransaction(string toAddress, string valueWeiHex, string go, string ok, string err);
#else
    private static void MetaMask_GetNativeBalance(string go, string ok, string err) { }
    private static void MetaMask_SignMessage(string message, string go, string ok, string err) { }
    private static void MetaMask_SendNativeTransaction(string toAddress, string valueWeiHex, string go, string ok, string err) { }
#endif

    [Serializable] private class BalancePayload { public string address; public string chainId; public string balanceHex; }
    [Serializable] private class SignPayload { public string address; public string signature; }
    [Serializable] private class TxPayload { public string from; public string to; public string valueWeiHex; public string txHash; }

    private void Start()
    {
        if (refreshSeconds < 0.5f) refreshSeconds = 2f;
        StartCoroutine(Init());
    }

    private System.Collections.IEnumerator Init()
    {
        // Wait for ModernChatManager to build UI (model panel + overlay)
        RectTransform modelPanelRt = null;
        RectTransform overlayRt = null;

        for (int i = 0; i < 900; i++)
        {
            var mp = GameObject.Find(MODEL_PANEL);
            if (mp != null) modelPanelRt = mp.GetComponent<RectTransform>();

            var ov = GameObject.Find(MODEL_OVERLAY);
            if (ov != null) overlayRt = ov.GetComponent<RectTransform>();

            if (modelPanelRt != null && overlayRt != null) break;
            yield return null;
        }

        // Find Send + input for gating
        var sb = GameObject.Find(SEND_BUTTON);
        if (sb != null) _sendButton = sb.GetComponent<Button>();

        var ic = GameObject.Find(INPUT_CONTAINER);
        if (ic != null) _inputField = ic.GetComponent<InputField>();

        // Build our HUD (under model panel, not inside it)
        BuildHud(overlayRt != null ? overlayRt.transform : (modelPanelRt != null ? modelPanelRt.transform.parent : null), modelPanelRt);

        // Start locked
        SetChatEnabled(false);
        SetCredits(0f);
        SetStatus($"ZK Lock {lockAmountKite:0.##} KITE to enable chat");
        SetTxLine("Settlement TX: —");

        if (_sendButton != null)
        {
            _sendButton.onClick.RemoveListener(OnSendClicked);
            _sendButton.onClick.AddListener(OnSendClicked);
        }

        InvokeRepeating(nameof(RequestBalance), 0.25f, refreshSeconds);
        UpdateButtonState();
    }

    // --------------------------------------------------------------------
    // HUD placement + visuals
    // --------------------------------------------------------------------
    private void BuildHud(Transform overlayParent, RectTransform modelPanelRt)
    {
        if (overlayParent == null)
        {
            Debug.LogWarning("[KiteBalanceModelPanelInjector] No overlay parent found to attach HUD.");
            return;
        }

        // Root
        var root = GameObject.Find(HUD_ROOT);
        if (root == null)
        {
            root = new GameObject(HUD_ROOT, typeof(RectTransform));
            root.transform.SetParent(overlayParent, false);
        }

        var rootRt = root.GetComponent<RectTransform>();
        rootRt.anchorMin = new UnityEngine.Vector2(1, 1);
        rootRt.anchorMax = new UnityEngine.Vector2(1, 1);
        rootRt.pivot = new UnityEngine.Vector2(1, 1);

        // Position: just below model panel
        float y = -410f;
        if (modelPanelRt != null) y = -(modelPanelRt.sizeDelta.y + 14f);
        rootRt.anchoredPosition = new UnityEngine.Vector2(-10f, y);
        rootRt.sizeDelta = new UnityEngine.Vector2(360f, 240f);

        // Panel
        Transform panelT = root.transform.Find(HUD_PANEL);
        GameObject panel;
        if (panelT == null)
        {
            panel = new GameObject(HUD_PANEL, typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
        }
        else panel = panelT.gameObject;

        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = UnityEngine.Vector2.zero;
        prt.anchorMax = UnityEngine.Vector2.one;
        prt.offsetMin = UnityEngine.Vector2.zero;
        prt.offsetMax = UnityEngine.Vector2.zero;

        var img = panel.GetComponent<Image>();
        if (img == null) img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.72f);

        var outline = panel.GetComponent<Outline>();
        if (outline == null) outline = panel.AddComponent<Outline>();
        outline.effectColor = new Color(0.45f, 0.9f, 1f, 0.55f);
        outline.effectDistance = new UnityEngine.Vector2(2f, -2f);

        var v = panel.GetComponent<VerticalLayoutGroup>();
        if (v == null) v = panel.AddComponent<VerticalLayoutGroup>();
        v.childAlignment = TextAnchor.UpperRight;
        v.childControlWidth = true;
        v.childControlHeight = false;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;
        v.spacing = 6f;
        v.padding = new RectOffset(10, 10, 10, 10);

        // Rows
        var walletRow = EnsureRow(panel.transform, "Row_Wallet", 56);
        _walletText = EnsureText(walletRow, "WalletText", 30, true, new Color32(180, 235, 255, 255), 340);
        _walletText.alignment = TextAnchor.MiddleRight;

        var buttonRow = EnsureRow(panel.transform, "Row_Button", 56);
        _lockButton = EnsureButton(buttonRow, "LockButton", $"ZK Lock {lockAmountKite:0.##} KITE", 22, 340, 46);
        _lockButton.onClick.RemoveAllListeners();
        _lockButton.onClick.AddListener(OnLockClicked);

        var creditsRow = EnsureRow(panel.transform, "Row_Credits", 44);
        _creditsText = EnsureText(creditsRow, "CreditsText", 22, true, Color.white, 340);
        _creditsText.alignment = TextAnchor.MiddleRight;

        var statusRow = EnsureRow(panel.transform, "Row_Status", 54);
        _statusText = EnsureText(statusRow, "StatusText", 16, false, new Color32(210, 230, 245, 255), 340);
        _statusText.alignment = TextAnchor.UpperRight;

        var txRow = EnsureRow(panel.transform, "Row_Tx", 64);
        _txText = EnsureText(txRow, "TxText", 16, false, new Color32(200, 210, 225, 255), 340);
        _txText.alignment = TextAnchor.UpperRight;

        // Defaults
        _walletText.text = "KITE Wallet: —";
        _creditsText.text = "<color=#FFCC66>Chat Locked</color> • Credits: 0";
        _statusText.text = "";
        _txText.text = "";
    }

    private static GameObject EnsureRow(Transform parent, string name, float height)
    {
        var t = parent.Find(name);
        GameObject row;
        if (t == null)
        {
            row = new GameObject(name, typeof(RectTransform));
            row.transform.SetParent(parent, false);
            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.spacing = 0f;
            h.padding = new RectOffset(0, 0, 0, 0);
        }
        else row = t.gameObject;

        var rt = row.GetComponent<RectTransform>();
        rt.sizeDelta = new UnityEngine.Vector2(0, height);
        return row;
    }

    private static Text EnsureText(GameObject row, string name, int fontSize, bool bold, Color color, float width)
    {
        var t = row.transform.Find(name)?.GetComponent<Text>();
        if (t != null) return t;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(row.transform, false);

        t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.supportRichText = true;
        t.raycastTarget = false;
        t.fontSize = fontSize;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.color = color;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new UnityEngine.Vector2(width, 40);
        return t;
    }

    private static Button EnsureButton(GameObject row, string name, string label, int fontSize, float width, float height)
    {
        var existing = row.transform.Find(name)?.GetComponent<Button>();
        if (existing != null) return existing;

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(row.transform, false);

        var img = go.AddComponent<Image>();
        img.color = new Color32(0, 140, 230, 255);

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0.6f, 0.95f, 1f, 0.6f);
        outline.effectDistance = new UnityEngine.Vector2(2f, -2f);

        var btn = go.AddComponent<Button>();

        var tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(go.transform, false);

        var trt = tgo.GetComponent<RectTransform>();
        trt.anchorMin = UnityEngine.Vector2.zero;
        trt.anchorMax = UnityEngine.Vector2.one;
        trt.offsetMin = UnityEngine.Vector2.zero;
        trt.offsetMax = UnityEngine.Vector2.zero;

        var t = tgo.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = label;
        t.fontSize = fontSize;
        t.fontStyle = FontStyle.Bold;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleCenter;
        t.supportRichText = true;
        t.raycastTarget = false;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new UnityEngine.Vector2(width, height);

        return btn;
    }

    // --------------------------------------------------------------------
    // Button states / text setters
    // --------------------------------------------------------------------
    private void UpdateButtonState()
    {
        if (_lockButton == null) return;
        var t = _lockButton.GetComponentInChildren<Text>();
        if (t == null) return;

        if (_awaitingSignature) { t.text = "Confirm Lock…"; _lockButton.interactable = false; return; }
        if (_awaitingTx)        { t.text = "Confirm TX…";   _lockButton.interactable = false; return; }

        if (!_lockActive) { t.text = $"ZK Lock {lockAmountKite:0.##} KITE"; _lockButton.interactable = true; return; }

        t.text = "Locked";
        _lockButton.interactable = false;
    }

    private void SetStatus(string s) { if (_statusText) _statusText.text = s; }
    private void SetTxLine(string s) { if (_txText) _txText.text = s; }

    private void SetCredits(float c)
    {
        _credits = Mathf.Max(0f, c);
        if (_creditsText == null) return;

        if (!_lockActive) _creditsText.text = "<color=#FFCC66>Chat Locked</color> • Credits: 0";
        else _creditsText.text = $"Credits Remaining: {_credits:0.####} KITE";
    }

    private void SetChatEnabled(bool enabled)
    {
        if (_sendButton) _sendButton.interactable = enabled;
        if (_inputField) _inputField.interactable = enabled;
    }

    // --------------------------------------------------------------------
    // Lock flow (signature) + settle flow (one tx)
    // --------------------------------------------------------------------
    private void OnLockClicked()
    {
        if (_awaitingSignature || _awaitingTx) return;

        if (requireExpectedChain && !string.IsNullOrEmpty(_chainId) &&
            !string.Equals(_chainId, expectedChainIdHex, StringComparison.OrdinalIgnoreCase))
        {
            SetStatus($"Wrong network ({_chainId}). Switch to KiteAI.");
            return;
        }

        if (_walletBalance >= 0 && _walletBalance < lockAmountKite)
        {
            SetStatus($"Need {lockAmountKite:0.##} KITE (wallet has {_walletBalance:0.####}).");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        _awaitingSignature = true;
        UpdateButtonState();
        SetStatus("Confirm ZK custody lock (signature) in MetaMask…");

        var session = Guid.NewGuid().ToString("N").Substring(0, 10);
        var msg = $"KiteAI ZK Custody Lock Commitment\nLock={lockAmountKite:0.##} KITE\nSession={session}\nTimeUTC={DateTime.UtcNow:O}";
        MetaMask_SignMessage(msg, gameObject.name, nameof(OnSignSuccess), nameof(OnSignError));
#else
        ActivateLock("editor_receipt");
#endif
    }

    public void OnSignSuccess(string json)
    {
        _awaitingSignature = false;
        try
        {
            var p = JsonUtility.FromJson<SignPayload>(json);
            if (p == null || string.IsNullOrEmpty(p.signature))
            {
                SetStatus("Lock failed: no signature.");
                UpdateButtonState();
                return;
            }
            var receipt = ShortReceipt(p.signature);
            ActivateLock(receipt);
        }
        catch (Exception e)
        {
            SetStatus("Lock parse error: " + e.Message);
            UpdateButtonState();
        }
    }

    public void OnSignError(string msg)
    {
        _awaitingSignature = false;
        SetStatus("Lock cancelled: " + msg);
        UpdateButtonState();
    }

    private void ActivateLock(string receipt)
    {
        _lockActive = true;
        SetChatEnabled(true);
        SetCredits(lockAmountKite);
        SetStatus($"<color=#8CFFB3>ZK Lock Active</color> • Receipt {receipt}");
        UpdateButtonState();
    }

    private void OnSendClicked()
    {
        if (!_lockActive || _awaitingTx) return;

        _credits -= Mathf.Max(0.000001f, costPerMessageKite);
        if (_credits < 0f) _credits = 0f;
        SetCredits(_credits);

        if (_credits <= 0.000001f)
        {
            SetChatEnabled(false);
            SetStatus("<color=#FFCC66>Credits depleted.</color> Settling on-chain…");
            StartSettlementTx();
        }
    }

    private void StartSettlementTx()
    {
        if (_awaitingTx) return;

        if (string.IsNullOrEmpty(_address))
        {
            SetStatus("No wallet connected.");
            return;
        }

        var to = string.IsNullOrWhiteSpace(recipientAddress) ? _address : recipientAddress.Trim();
        var valueWeiHex = ToWeiHex((decimal)lockAmountKite, 18);

#if UNITY_WEBGL && !UNITY_EDITOR
        _awaitingTx = true;
        UpdateButtonState();
        SetTxLine("Settlement TX: confirm in MetaMask…");
        MetaMask_SendNativeTransaction(to, valueWeiHex, gameObject.name, nameof(OnTxSuccess), nameof(OnTxError));
#else
        OnTxSuccess("{\"txHash\":\"editor_tx\"}");
#endif
    }

    public void OnTxSuccess(string json)
    {
        _awaitingTx = false;

        string tx = "";
        try
        {
            var p = JsonUtility.FromJson<TxPayload>(json);
            tx = p != null ? (p.txHash ?? "") : "";
        }
        catch { }

        if (!string.IsNullOrEmpty(tx))
        {
            SetTxLine($"Settlement TX: <color=#8CFFB3>{ShortHash(tx)}</color> (full in console)");
            Debug.Log("[KiteBalanceModelPanelInjector] Settlement TX hash: " + tx);
        }
        else
        {
            SetTxLine("Settlement TX: submitted");
        }

        _lockActive = false;
        SetCredits(0f);
        SetChatEnabled(false);
        SetStatus("<color=#8CFFB3>Settled.</color> Lock again to continue.");
        UpdateButtonState();
    }

    public void OnTxError(string msg)
    {
        _awaitingTx = false;

        SetTxLine("Settlement TX: cancelled");
        SetStatus("Settlement cancelled: " + msg + " • Lock again to retry.");
        _lockActive = false;
        SetCredits(0f);
        SetChatEnabled(false);
        UpdateButtonState();
    }

    // --------------------------------------------------------------------
    // Wallet balance polling
    // --------------------------------------------------------------------
    private void RequestBalance()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MetaMask_GetNativeBalance(gameObject.name, nameof(OnBalanceOk), nameof(OnBalanceErr));
#else
        // Editor note
        if (_walletText) _walletText.text = "KITE Wallet: (WebGL only)";
#endif
    }

    public void OnBalanceOk(string json)
    {
        try
        {
            var p = JsonUtility.FromJson<BalancePayload>(json);
            if (p == null || string.IsNullOrEmpty(p.address))
            {
                if (_walletText) _walletText.text = "KITE Wallet: (no wallet)";
                return;
            }

            _address = p.address;
            _chainId = p.chainId;

            if (requireExpectedChain && !string.IsNullOrEmpty(_chainId) &&
                !string.Equals(_chainId, expectedChainIdHex, StringComparison.OrdinalIgnoreCase))
            {
                if (_walletText) _walletText.text = $"<color=#FFCC66>Wrong network</color> ({_chainId})";
                SetChatEnabled(false);
                return;
            }

            var wei = HexToBigInteger(p.balanceHex);
            _walletBalance = WeiToFloat(wei, 18);

            if (_walletText)
            {
                if (showShortAddress)
                    _walletText.text = $"KITE Wallet: {_walletBalance:0.####}  <color=#7FBFE6>{ShortAddr(_address)}</color>";
                else
                    _walletText.text = $"KITE Wallet: {_walletBalance:0.####}";
            }
        }
        catch (Exception e)
        {
            if (_walletText) _walletText.text = "KITE Wallet: (parse error)";
            Debug.LogWarning("[KiteBalanceModelPanelInjector] Balance parse error: " + e.Message);
        }
    }

    public void OnBalanceErr(string msg)
    {
        if (_walletText) _walletText.text = "KITE Wallet: (error)";
        Debug.LogWarning("[KiteBalanceModelPanelInjector] " + msg);
    }

    // --------------------------------------------------------------------
    // Utils
    // --------------------------------------------------------------------
    private static string ShortAddr(string addr)
    {
        if (string.IsNullOrEmpty(addr) || addr.Length < 10) return addr ?? "";
        return addr.Substring(0, 6) + "…" + addr.Substring(addr.Length - 4);
    }

    private static string ShortHash(string h)
    {
        if (string.IsNullOrEmpty(h) || h.Length < 12) return h ?? "";
        return h.Substring(0, 10) + "…" + h.Substring(h.Length - 6);
    }

    private static string ShortReceipt(string s)
    {
        try
        {
            using var sha = SHA256.Create();
            var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? ""));
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(b[i].ToString("x2"));
            return sb.ToString();
        }
        catch { return "receipt"; }
    }

    private static BigInteger HexToBigInteger(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return BigInteger.Zero;
        hex = hex.Trim();
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
        if (hex.Length == 0) return BigInteger.Zero;
        return BigInteger.Parse("0" + hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static float WeiToFloat(BigInteger value, int decimals)
    {
        if (value <= 0) return 0f;

        var s = value.ToString(CultureInfo.InvariantCulture);
        if (s.Length <= decimals) s = s.PadLeft(decimals + 1, '0');

        int split = s.Length - decimals;
        var intPart = s.Substring(0, split);
        var fracPart = s.Substring(split);

        if (fracPart.Length > 6) fracPart = fracPart.Substring(0, 6);
        fracPart = fracPart.TrimEnd('0');
        var combined = fracPart.Length == 0 ? intPart : (intPart + "." + fracPart);

        if (float.TryParse(combined, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) return f;
        return 0f;
    }

    private static string ToWeiHex(decimal amount, int decimals)
    {
        if (amount <= 0m) return "0x0";

        decimal scale = 1m;
        for (int i = 0; i < decimals; i++) scale *= 10m;

        decimal weiDec = decimal.Floor(amount * scale);
        var weiStr = weiDec.ToString("0", CultureInfo.InvariantCulture);
        var wei = BigInteger.Parse(weiStr, CultureInfo.InvariantCulture);

        return "0x" + wei.ToString("x");
    }
}
