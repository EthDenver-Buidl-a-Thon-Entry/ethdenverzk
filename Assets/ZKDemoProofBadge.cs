// ZKDemoProofBadge.cs
// Add this to the SAME GameObject as your existing HUD/panel injector (in EthDenver scene).
// It will:
//  - Create a small "ZK Verified" line under your balance/lockup area (no inspector sprites).
//  - Run a REAL Groth16 verify in-browser using snarkjs (Option 2).
//
// NOTE: This verifies a demo circuit proof (multiplier c=56) to prove real ZK verification runs.
// Next step (after hackathon) is to bind the proof to the session commitment (signature hash / address / amount).

using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public class ZKDemoProofBadge : MonoBehaviour
{
    [Header("UI Placement")]
    [Tooltip("If you leave this null, we try to find the panel that already contains the wallet balance text.")]
    public RectTransform preferredParent;
    public int fontSize = 18;

    private Text _status;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ZK_VerifyDemoProof(string go, string ok, string err);
#else
    private static void ZK_VerifyDemoProof(string go, string ok, string err) { }
#endif

    private void Start()
    {
        EnsureBadge();
        // Kick off verification once per scene load (you can also call TriggerVerify() manually if you prefer)
        TriggerVerify();
    }

    public void TriggerVerify()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (_status != null) _status.text = "ZK: verifying…";
        ZK_VerifyDemoProof(gameObject.name, nameof(OnZkOk), nameof(OnZkErr));
#else
        if (_status != null) _status.text = "ZK: WebGL-only";
#endif
    }

    public void OnZkOk(string payloadJson)
    {
        EnsureBadge();
        // payloadJson: {"verified":true,"meta":{...}}
        if (payloadJson != null && payloadJson.Contains("\"verified\":true"))
            _status.text = "ZK: Verified ✅ (Groth16)";
        else
            _status.text = "ZK: INVALID ❌";
    }

    public void OnZkErr(string err)
    {
        EnsureBadge();
        _status.text = "ZK: error (" + (string.IsNullOrEmpty(err) ? "unknown" : err) + ")";
    }

    private void EnsureBadge()
    {
        if (_status != null) return;

        RectTransform parent = preferredParent;
        if (parent == null)
        {
            // Best-effort: find a Text containing "Wallet" or "Balance" and use its parent panel.
            foreach (var t in GameObject.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                var s = (t.text ?? "").ToLowerInvariant();
                if (s.Contains("wallet") || s.Contains("balance"))
                {
                    parent = t.transform.parent as RectTransform;
                    if (parent != null) break;
                }
            }
        }

        if (parent == null)
        {
            // Fallback: create a canvas if none found (rare, but safe).
            var canvasGo = new GameObject("ZKDemoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = canvasGo.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            parent = canvasGo.transform as RectTransform;
        }

        var go = new GameObject("ZKDemoStatus", typeof(RectTransform), typeof(Text));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);

        // place at bottom with small margin
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot = new Vector2(0.5f, 0);
        rt.sizeDelta = new Vector2(0, 26);
        rt.anchoredPosition = new Vector2(0, -28);

        var txt = go.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        txt.fontSize = fontSize;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.color = Color.white;
        txt.text = "ZK: pending…";

        _status = txt;
    }
}
