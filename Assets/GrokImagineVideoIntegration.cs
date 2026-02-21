using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Adds a "GROK VIDEO" button (new row) to ModernChatManager's TopRightModelPanel and lets you
/// generate/edit videos using xAI's grok-imagine-video model via deferred requests (request_id + polling).
///
/// Endpoints used (per xAI REST API reference):
/// POST  https://api.x.ai/v1/videos/generations
/// POST  https://api.x.ai/v1/videos/edits
/// GET   https://api.x.ai/v1/videos/{request_id}
///
/// Auth header: Authorization: Bearer {xAI API key}
/// </summary>
public class GrokImagineVideoIntegration : MonoBehaviour
{
    [Header("References (optional)")]
    public ModernChatManager manager;               // Will be auto-found if left null.
    public Canvas targetCanvasOverride;             // Optional if your manager doesn't expose targetCanvas in scene.

    [Header("xAI Video Model")]
    public string videoModelId = "grok-imagine-video";
    [Tooltip("If set, overrides manager.grokApiKey. Otherwise uses the Grok key from ModernChatManager.")]
    public string apiKeyOverride = "";

    [Header("Polling")]
    [Tooltip("Seconds between polling attempts for deferred video results.")]
    public float pollIntervalSeconds = 2.0f;
    [Tooltip("Max total time to poll (seconds) before giving up.")]
    public float maxPollSeconds = 240.0f;

    [Header("Defaults")]
    [Range(1, 15)] public int defaultDurationSeconds = 6;
    public string defaultAspectRatio = "16:9";   // Supported: 16:9,4:3,1:1,9:16,3:4,3:2,2:3
    public string defaultResolution = "720p";    // Supported: 720p,480p

    [Header("UI")]
    public bool autoAddButtonToTopRightPanel = true;
    [Tooltip("How long to wait for ModernChatManager to create the model buttons overlay before giving up (seconds).")]
    public float uiAttachTimeoutSeconds = 10f;
    public string buttonLabel = "GROK VIDEO";
    public Vector2 buttonSize = new Vector2(170, 44);
    public Sprite buttonSpriteOverride;

    // --- Internal state ---
    private Canvas _canvas;
    private GameObject _popupRoot;
    private Mode _mode = Mode.TextToVideo;

    // Popup widgets
    private InputField _promptField;
    private InputField _urlField;
    private Text _urlLabel;
    private InputField _durationField;
    private InputField _aspectField;
    private InputField _resolutionField;
    private Text _statusText;
    private Button _previewButton;
    private Button _uploadImageButton;
    private Button _clearImageButton;
    private string _uploadedImageDataUrl = "";

    private string _lastVideoUrl = "";

    private enum Mode
    {
        TextToVideo,
        ImageToVideo,
        EditVideo
    }

    private void Awake()
    {
        if (manager == null) manager = FindObjectOfType<ModernChatManager>();
        _canvas = targetCanvasOverride != null ? targetCanvasOverride : (manager != null ? manager.targetCanvas : FindObjectOfType<Canvas>());
    }

    private void Start()
    {
        if (_canvas == null)
        {
            Debug.LogError("[GrokVideo] No Canvas found. Assign targetCanvasOverride or ensure ModernChatManager.targetCanvas is set.");
            return;
        }

        if (autoAddButtonToTopRightPanel)
        {
            StartCoroutine(WaitForModelPanelAndAddButton());
        }
    }

    private IEnumerator WaitForModelPanelAndAddButton()
    {
        // ModernChatManager and this component may have an unpredictable Start order.
        // Wait a bit for the UI to be created, then attach our button.
        float elapsed = 0f;

        while (elapsed < uiAttachTimeoutSeconds)
        {
            if (TryAddButtonRow())
                yield break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Debug.LogWarning("[GrokVideo] Could not find TopRightModelPanel after waiting. Button not added. (UI may be named differently or not created yet.)");
    }


    // -----------------------------
    // UI: Add new "Video" row + button
    // -----------------------------
    private bool TryAddButtonRow()
    {
        Transform topRightPanel = FindTopRightModelPanel();
        if (topRightPanel == null)
        {
            return false;
        }

        Transform row = topRightPanel.Find("Row_Video_Toggles");
        if (row == null)
        {
            row = CreateRow(topRightPanel, "Row_Video_Toggles");
        }

        // Avoid duplicates
        if (row.Find(buttonLabel + "_Button") != null)
        {
            return true;
        }

        Sprite sprite = buttonSpriteOverride;
        if (sprite == null && manager != null && manager.modelFullButtonSprite != null) sprite = manager.modelFullButtonSprite;
        if (sprite == null && manager != null && manager.buttonSprite != null) sprite = manager.buttonSprite;

        Button btn = CreateButton(row, buttonLabel, sprite, buttonSize);
        btn.onClick.AddListener(OpenPopup);
        Debug.Log("[GrokVideo] Grok-imagine-video button added.");

        return true;
    }

    private Transform FindTopRightModelPanel()
    {
        // Primary: look under our known canvas
        if (_canvas != null)
        {
            Transform t = FindDeepChild(_canvas.transform, "TopRightModelPanel");
            if (t != null) return t;
        }

        // Fallback: search all canvases (including inactive), in case targetCanvasOverride/manager.targetCanvas is different
        Canvas[] allCanvases = Resources.FindObjectsOfTypeAll<Canvas>();
        for (int i = 0; i < allCanvases.Length; i++)
        {
            Transform t = FindDeepChild(allCanvases[i].transform, "TopRightModelPanel");
            if (t != null)
            {
                _canvas = allCanvases[i];
                return t;
            }
        }

        return null;
    }


    private Transform CreateRow(Transform parent, string name)
    {
        GameObject rowGO = new GameObject(name, typeof(RectTransform));
        rowGO.transform.SetParent(parent, false);

        RectTransform rt = rowGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.sizeDelta = new Vector2(0, 52);

        HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(8, 8, 6, 6);
        hlg.spacing = 8;
        hlg.childAlignment = TextAnchor.MiddleRight;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        ContentSizeFitter csf = rowGO.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return rowGO.transform;
    }

    private Button CreateButton(Transform parent, string label, Sprite sprite, Vector2 size)
    {
        GameObject go = new GameObject(label + "_Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
        img.color = new Color(1f, 1f, 1f, 0.92f);

        Button b = go.GetComponent<Button>();
        b.transition = Selectable.Transition.ColorTint;

        // Text
        GameObject txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(go.transform, false);
        RectTransform trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Text t = txtGO.GetComponent<Text>();
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 16;
        t.color = Color.black;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = 18;

        return b;
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform found = FindDeepChild(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // -----------------------------
    // Popup UI
    // -----------------------------
    private void OpenPopup()
    {
        if (_popupRoot == null) BuildPopup();
        _popupRoot.SetActive(true);
        _statusText.text = "Ready.";
        _lastVideoUrl = "";
        _uploadedImageDataUrl = "";
        if (_previewButton != null) _previewButton.gameObject.SetActive(false);
    }

    private void ClosePopup()
    {
        if (_popupRoot != null) _popupRoot.SetActive(false);
    }
    private void BuildPopup()
    {
        _popupRoot = new GameObject("GrokVideoPopup", typeof(RectTransform), typeof(CanvasGroup));
        _popupRoot.transform.SetParent(_canvas.transform, false);

        RectTransform rootRT = _popupRoot.GetComponent<RectTransform>();
        rootRT.anchorMin = Vector2.zero;
        rootRT.anchorMax = Vector2.one;
        rootRT.offsetMin = Vector2.zero;
        rootRT.offsetMax = Vector2.zero;

        // Dim background
        GameObject dim = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
        dim.transform.SetParent(_popupRoot.transform, false);
        RectTransform dimRT = dim.GetComponent<RectTransform>();
        dimRT.anchorMin = Vector2.zero;
        dimRT.anchorMax = Vector2.one;
        dimRT.offsetMin = Vector2.zero;
        dimRT.offsetMax = Vector2.zero;
        Image dimImg = dim.GetComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.72f);
        dim.GetComponent<Button>().onClick.AddListener(ClosePopup);

        // Panel (pro dark theme to match Spur UI)
        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(Outline));
        panel.transform.SetParent(_popupRoot.transform, false);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);

        // Bigger default modal (your screenshot showed this is too small)
        prt.sizeDelta = new Vector2(980, 650);

        Image pimg = panel.GetComponent<Image>();
        pimg.color = new Color(0.07f, 0.07f, 0.07f, 0.98f);
        pimg.raycastTarget = true;

        Outline outline = panel.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.55f);
        outline.effectDistance = new Vector2(1.2f, -1.2f);

        VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 18, 18);
        vlg.spacing = 14;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;

        // Title row
        GameObject titleRow = new GameObject("TitleRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        titleRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup th = titleRow.GetComponent<HorizontalLayoutGroup>();
        th.spacing = 10;
        th.childAlignment = TextAnchor.MiddleLeft;
        th.childControlHeight = false;
        th.childControlWidth = true;
        th.childForceExpandWidth = true;

        Text title = CreateLabel(titleRow.transform, "xAI • grok-imagine-video", 18, true);
        title.color = Color.white;

        var spacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        spacer.transform.SetParent(titleRow.transform, false);
        spacer.GetComponent<LayoutElement>().flexibleWidth = 1;

        Button closeBtn = CreateSmallButton(titleRow.transform, "X", new Vector2(42, 32), true);
        closeBtn.onClick.AddListener(ClosePopup);

        // Mode row
        GameObject modeRow = new GameObject("ModeRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        modeRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup mh = modeRow.GetComponent<HorizontalLayoutGroup>();
        mh.spacing = 10;
        mh.childAlignment = TextAnchor.MiddleLeft;
        mh.childControlHeight = false;
        mh.childControlWidth = false;

        var modeLabel = CreateLabel(modeRow.transform, "Mode:", 14, true);
        modeLabel.color = new Color(1f, 1f, 1f, 0.9f);

        Button textMode = CreateSmallButton(modeRow.transform, "Text → Video", new Vector2(150, 34), false);
        Button imgMode  = CreateSmallButton(modeRow.transform, "Image → Video", new Vector2(150, 34), false);
        Button editMode = CreateSmallButton(modeRow.transform, "Edit Video", new Vector2(140, 34), false);

        textMode.onClick.AddListener(() => SetMode(Mode.TextToVideo));
        imgMode.onClick.AddListener(() => SetMode(Mode.ImageToVideo));
        editMode.onClick.AddListener(() => SetMode(Mode.EditVideo));

        // Prompt
        CreateLabel(panel.transform, "Prompt:", 14, true).color = new Color(1f, 1f, 1f, 0.9f);
        _promptField = CreateInput(panel.transform, "Describe the video you want…", 120, true);
        _promptField.lineType = InputField.LineType.MultiLineNewline;

        // URL (image/video) optional
        _urlLabel = CreateLabel(panel.transform, "Image (URL or Upload):", 14, true);
        _urlLabel.color = new Color(1f, 1f, 1f, 0.9f);
        _urlField = CreateInput(panel.transform, "Paste a URL or upload an image…", 44, false);

        // Upload row (WebGL file picker)
        GameObject uploadRow = new GameObject("UploadRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        uploadRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup uh = uploadRow.GetComponent<HorizontalLayoutGroup>();
        uh.spacing = 10;
        uh.childAlignment = TextAnchor.MiddleLeft;
        uh.childControlWidth = false;
        uh.childControlHeight = false;
        uh.childForceExpandWidth = false;
        uh.childForceExpandHeight = false;

            _uploadImageButton = CreateSmallButton(uploadRow.transform, "Upload Image", new Vector2(150, 34), false);
    _uploadImageButton.onClick.AddListener(() =>
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SpuricWebGLMediaBridge.OpenFilePicker(gameObject.name, "OnGrokImagePicked", "image/*");
#elif UNITY_EDITOR
        try
        {
            string path = EditorUtility.OpenFilePanelWithFilters(
                "Select image",
                Application.dataPath,
                new string[] { "Image files", "png,jpg,jpeg,webp,gif", "All files", "*" }
            );

            if (string.IsNullOrEmpty(path))
            {
                SetStatus("No image selected.");
                return;
            }

            byte[] bytes = File.ReadAllBytes(path);
            string mime = GuessImageMime(path);
            string dataUrl = "data:" + mime + ";base64," + Convert.ToBase64String(bytes);
            string fileName = Path.GetFileName(path);

            // Reuse the same payload parser used in WebGL callbacks.
            string payload = fileName + "|||" + mime + "|||" + dataUrl;
            OnGrokImagePicked(payload);
        }
        catch (Exception ex)
        {
            SetStatus("Upload failed: " + ex.Message);
        }
#else
        SetStatus("Upload is supported in WebGL builds or in the Unity Editor.");
#endif
    });_clearImageButton = CreateSmallButton(uploadRow.transform, "Clear", new Vector2(100, 34), false);
        _clearImageButton.onClick.AddListener(() =>
        {
            _uploadedImageDataUrl = "";
            if (_urlField != null) _urlField.text = "";
            SetStatus("Cleared image input.");
        });

        Text uploadHint = CreateLabel(uploadRow.transform, "Tip: Upload is best for WebGL (no external hosting needed).", 12, false);
        uploadHint.color = new Color(1f, 1f, 1f, 0.65f);
        uploadHint.alignment = TextAnchor.MiddleLeft;

        // Output settings row (THIS fixes the 3 unwritable inputs)
        GameObject outRow = new GameObject("OutputRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        outRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup oh = outRow.GetComponent<HorizontalLayoutGroup>();
        oh.spacing = 14;
        oh.childAlignment = TextAnchor.UpperLeft;
        oh.childControlHeight = true;
        oh.childControlWidth = true;          // IMPORTANT: let layout drive widths
        oh.childForceExpandWidth = true;      // IMPORTANT: expand columns

        // Duration
        GameObject durCol = CreateColumn(outRow.transform, "Duration (1–15):", 270);
        _durationField = CreateInput(durCol.transform, defaultDurationSeconds.ToString(), 44, false);
        _durationField.contentType = InputField.ContentType.IntegerNumber;

        // Aspect
        GameObject aspCol = CreateColumn(outRow.transform, "Aspect (e.g. 16:9):", 270);
        _aspectField = CreateInput(aspCol.transform, defaultAspectRatio, 44, false);

        // Resolution
        GameObject resCol = CreateColumn(outRow.transform, "Resolution (720p/480p):", 270);
        _resolutionField = CreateInput(resCol.transform, defaultResolution, 44, false);

        // Status
        CreateLabel(panel.transform, "Status:", 14, true).color = new Color(1f, 1f, 1f, 0.9f);
        _statusText = CreateLabel(panel.transform, "Ready.", 12, false);
        _statusText.color = new Color(1f, 1f, 1f, 0.8f);
        _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _statusText.verticalOverflow = VerticalWrapMode.Overflow;

        // Action row
        GameObject actionRow = new GameObject("ActionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        actionRow.transform.SetParent(panel.transform, false);
        HorizontalLayoutGroup ah = actionRow.GetComponent<HorizontalLayoutGroup>();
        ah.spacing = 10;
        ah.childAlignment = TextAnchor.MiddleRight;
        ah.childControlHeight = false;
        ah.childControlWidth = false;

        var actionSpacer = new GameObject("Spacer", typeof(RectTransform), typeof(LayoutElement));
        actionSpacer.transform.SetParent(actionRow.transform, false);
        actionSpacer.GetComponent<LayoutElement>().flexibleWidth = 1;

        Button startBtn = CreateSmallButton(actionRow.transform, "Start", new Vector2(140, 38), false);
        startBtn.onClick.AddListener(() => StartCoroutine(StartAndPoll()));

        _previewButton = CreateSmallButton(actionRow.transform, "Preview", new Vector2(140, 38), false);
        _previewButton.gameObject.SetActive(false);
        _previewButton.onClick.AddListener(() =>
        {
            if (!string.IsNullOrEmpty(_lastVideoUrl))
            {
                SpuricWebGLMediaBridge.ShowVideoOverlayFromUrl(_lastVideoUrl);
            }
        });

        Button cancelBtn = CreateSmallButton(actionRow.transform, "Close", new Vector2(140, 38), true);
        cancelBtn.onClick.AddListener(ClosePopup);

        // Default mode
        SetMode(Mode.TextToVideo);
    }

    private GameObject CreateColumn(Transform parent, string label, float preferredWidth)
    {
        GameObject col = new GameObject(label.Replace(" ", "_"), typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        col.transform.SetParent(parent, false);

        var le = col.GetComponent<LayoutElement>();
        le.preferredWidth = preferredWidth;
        le.flexibleWidth = 1;

        VerticalLayoutGroup v = col.GetComponent<VerticalLayoutGroup>();
        v.spacing = 6;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = false;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;

        var lab = CreateLabel(col.transform, label, 12, true);
        lab.color = new Color(1f, 1f, 1f, 0.85f);
        return col;
    }

    private Text CreateLabel(Transform parent, string text, int fontSize, bool bold)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = fontSize;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.color = Color.white;
        t.alignment = TextAnchor.MiddleLeft;
        t.supportRichText = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, fontSize + 12);

        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = fontSize + 12;
        le.flexibleWidth = 1;

        return t;
    }

    private Button CreateSmallButton(Transform parent, string label, Vector2 size, bool isSecondary)
    {
        GameObject go = new GameObject(label + "_Btn", typeof(RectTransform), typeof(Image), typeof(Button), typeof(Outline));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = isSecondary ? new Color(0.12f, 0.12f, 0.12f, 1f) : new Color(0.20f, 0.20f, 0.20f, 1f);

        Outline outline = go.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.35f);
        outline.effectDistance = new Vector2(1f, -1f);

        GameObject tg = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tg.transform.SetParent(go.transform, false);
        RectTransform tr = tg.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = Vector2.zero;
        tr.offsetMax = Vector2.zero;

        Text txt = tg.GetComponent<Text>();
        txt.text = label;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;

        return go.GetComponent<Button>();
    }

    private InputField CreateInput(Transform parent, string placeholder, int height, bool multiline)
    {
        GameObject root = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
        root.transform.SetParent(parent, false);

        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);

        var le = root.GetComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1;
        le.minWidth = multiline ? 600 : 160;

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(0.11f, 0.11f, 0.11f, 1f);

        InputField input = root.GetComponent<InputField>();
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.characterValidation = InputField.CharacterValidation.None;

        // Text
        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(root.transform, false);
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(12, 8);
        trt.offsetMax = new Vector2(-12, -8);

        Text txt = textGO.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.color = Color.white;
        txt.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        // Placeholder
        GameObject phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(root.transform, false);
        RectTransform phrt = phGO.GetComponent<RectTransform>();
        phrt.anchorMin = new Vector2(0, 0);
        phrt.anchorMax = new Vector2(1, 1);
        phrt.offsetMin = new Vector2(12, 8);
        phrt.offsetMax = new Vector2(-12, -8);

        Text ph = phGO.GetComponent<Text>();
        ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ph.fontSize = 14;
        ph.color = new Color(1f, 1f, 1f, 0.35f);
        ph.text = placeholder;
        ph.alignment = multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;

        input.textComponent = txt;
        input.placeholder = ph;

        return input;
    }


    private GameObject CreateColumn(Transform parent, string label)
    {
        GameObject col = new GameObject(label.Replace(" ", "_"), typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(parent, false);
        VerticalLayoutGroup v = col.GetComponent<VerticalLayoutGroup>();
        v.spacing = 6;
        v.childAlignment = TextAnchor.UpperLeft;
        v.childControlWidth = true;
        v.childControlHeight = false;
        v.childForceExpandWidth = false;
        v.childForceExpandHeight = false;

        CreateLabel(col.transform, label, 14, true);
        return col;
    }

    


    private Button CreateSmallButton(Transform parent, string label, Vector2 size)
    {
        GameObject go = new GameObject(label + "_Btn", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;

        Image img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 1f);

        Button b = go.GetComponent<Button>();

        GameObject txtGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        txtGO.transform.SetParent(go.transform, false);

        RectTransform trt = txtGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Text t = txtGO.GetComponent<Text>();
        t.text = label;
        t.alignment = TextAnchor.MiddleCenter;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 14;
        t.color = Color.black;
        t.resizeTextForBestFit = true;
        t.resizeTextMinSize = 10;
        t.resizeTextMaxSize = 16;

        return b;
    }

    private InputField CreateInput(Transform parent, string placeholder, int height)
    {
        GameObject root = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        root.transform.SetParent(parent, false);
        RectTransform rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, height);

        Image bg = root.GetComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 1f);

        InputField input = root.GetComponent<InputField>();

        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(root.transform, false);
        RectTransform trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.offsetMin = new Vector2(10, 6);
        trt.offsetMax = new Vector2(-10, -6);

        Text txt = textGO.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.fontSize = 14;
        txt.color = Color.black;
        txt.alignment = TextAnchor.UpperLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Wrap;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        GameObject phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(root.transform, false);
        RectTransform phrt = phGO.GetComponent<RectTransform>();
        phrt.anchorMin = new Vector2(0, 0);
        phrt.anchorMax = new Vector2(1, 1);
        phrt.offsetMin = new Vector2(10, 6);
        phrt.offsetMax = new Vector2(-10, -6);

        Text ph = phGO.GetComponent<Text>();
        ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ph.fontSize = 14;
        ph.color = new Color(0.4f, 0.4f, 0.4f, 1f);
        ph.text = placeholder;
        ph.alignment = TextAnchor.UpperLeft;

        input.textComponent = txt;
        input.placeholder = ph;

        return input;
    }

    private void SetMode(Mode mode)
    {
        _mode = mode;

        if (_urlLabel == null || _urlField == null || _durationField == null) return;

        if (_mode == Mode.TextToVideo)
        {
            _urlLabel.text = "Image URL (optional):";
            _urlField.gameObject.SetActive(true);
            _durationField.interactable = true;
        }
        else if (_mode == Mode.ImageToVideo)
        {
            _urlLabel.text = "Image URL (required):";
            _urlField.gameObject.SetActive(true);
            _durationField.interactable = true;
        }
        else // EditVideo
        {
            _urlLabel.text = "Video URL (required):";
            _urlField.gameObject.SetActive(true);
            _durationField.interactable = false; // per docs, edits don't support duration
        }
    }

    // -----------------------------
    // Networking: Start + Poll
    // -----------------------------
    private IEnumerator StartAndPoll()
    {
        string key = ResolveApiKey();
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("Missing xAI API key. Set apiKeyOverride or ModernChatManager.grokApiKey.");
            yield break;
        }

        string prompt = _promptField != null ? _promptField.text : "";
        if (string.IsNullOrEmpty(prompt))
        {
            SetStatus("Prompt is empty.");
            yield break;
        }

        string url = _urlField != null ? _urlField.text : "";

        int duration = defaultDurationSeconds;
        if (_durationField != null && !string.IsNullOrEmpty(_durationField.text))
        {
            int.TryParse(_durationField.text, out duration);
            duration = Mathf.Clamp(duration, 1, 15);
        }

        string aspect = _aspectField != null && !string.IsNullOrEmpty(_aspectField.text) ? _aspectField.text.Trim() : defaultAspectRatio;
        string res = _resolutionField != null && !string.IsNullOrEmpty(_resolutionField.text) ? _resolutionField.text.Trim() : defaultResolution;

        // Validate required URL fields
        if (_mode == Mode.ImageToVideo && string.IsNullOrEmpty(url))
        {
            SetStatus("Image URL is required for Image → Video.");
            yield break;
        }
        if (_mode == Mode.EditVideo && string.IsNullOrEmpty(url))
        {
            SetStatus("Video URL is required for Edit Video.");
            yield break;
        }

        string endpoint = _mode == Mode.EditVideo ? "/v1/videos/edits" : "/v1/videos/generations";
        string requestJson = BuildRequestJson(prompt, duration, aspect, res, url);

        SetStatus("Starting request…");
        string requestId = null;
        string startErr = null;
        yield return StartCoroutine(PostJson("https://api.x.ai" + endpoint, key, requestJson,
            (resp) => {
                requestId = ExtractJsonString(resp, "request_id");
                if (string.IsNullOrEmpty(requestId)) requestId = ExtractJsonString(resp, "id");
            },
            (err) => { startErr = err; }));

        if (!string.IsNullOrEmpty(startErr))
        {
            SetStatus("Start failed: " + startErr);
            yield break;
        }
        if (string.IsNullOrEmpty(requestId))
        {
            SetStatus("Start returned no request_id. Raw response may differ; check Console logs.");
            yield break;
        }

        SetStatus("Request started. request_id=" + requestId + " (polling…)");
        float elapsed = 0f;

        while (elapsed < maxPollSeconds)
        {
            yield return new WaitForSeconds(pollIntervalSeconds);
            elapsed += pollIntervalSeconds;

            string pollResp = null;
            string pollErr = null;

            yield return StartCoroutine(GetJson("https://api.x.ai/v1/videos/" + EscapeUrlSegment(requestId), key,
                (resp) => { pollResp = resp; },
                (err) => { pollErr = err; }));

            if (!string.IsNullOrEmpty(pollErr))
            {
                // Keep polling; transient failures can happen
                SetStatus("Polling… (transient error) " + pollErr);
                continue;
            }
            if (string.IsNullOrEmpty(pollResp))
            {
                SetStatus("Polling… (empty response)");
                continue;
            }

            string status = ExtractJsonString(pollResp, "status");
            if (string.IsNullOrEmpty(status)) status = ExtractJsonString(pollResp, "state");
            string videoUrl = ExtractJsonString(pollResp, "url");
            if (string.IsNullOrEmpty(videoUrl)) videoUrl = ExtractJsonString(pollResp, "video_url");

            if (!string.IsNullOrEmpty(videoUrl))
            {
                _lastVideoUrl = videoUrl;
                SetStatus("✅ Video ready.");
                if (_previewButton != null) _previewButton.gameObject.SetActive(true);
                TryInsertVideoIntoChat(videoUrl);
                yield break;
            }

            // If status indicates failure, stop
            if (!string.IsNullOrEmpty(status) && status.ToLowerInvariant().Contains("fail"))
            {
                string msg = ExtractJsonString(pollResp, "message");
                if (string.IsNullOrEmpty(msg)) msg = ExtractJsonString(pollResp, "error");
                SetStatus("❌ Failed: " + (string.IsNullOrEmpty(msg) ? pollResp : msg));
                yield break;
            }

            SetStatus("Polling… " + (string.IsNullOrEmpty(status) ? "(pending)" : status) + " (" + Mathf.RoundToInt(elapsed) + "s)");
        }

        SetStatus("Timed out after " + maxPollSeconds + "s. Try increasing maxPollSeconds.");
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrEmpty(apiKeyOverride)) return apiKeyOverride.Trim();
        if (manager != null && !string.IsNullOrEmpty(manager.grokApiKey)) return manager.grokApiKey.Trim();
        return "";
    }

    private void SetStatus(string s)
    {
        if (_statusText != null) _statusText.text = s;
        Debug.Log("[GrokVideo] " + s);
    }

    private string BuildRequestJson(string prompt, int duration, string aspect, string resolution, string url)
    {
        // Request body fields (per xAI guide):
        // prompt, model, [duration], [aspect_ratio], [resolution], [image_url] OR [video_url]
        StringBuilder sb = new StringBuilder();
        sb.Append("{");
        sb.Append("\"prompt\":\"").Append(EscapeJson(prompt)).Append("\",");
        sb.Append("\"model\":\"").Append(EscapeJson(videoModelId)).Append("\"");

        if (_mode != Mode.EditVideo)
        {
            sb.Append(",\"duration\":").Append(duration);
        }

        if (!string.IsNullOrEmpty(aspect))
        {
            sb.Append(",\"aspect_ratio\":\"").Append(EscapeJson(aspect)).Append("\"");
        }

        if (!string.IsNullOrEmpty(resolution))
        {
            sb.Append(",\"resolution\":\"").Append(EscapeJson(resolution)).Append("\"");
        }

        if (!string.IsNullOrEmpty(url))
        {
            if (_mode == Mode.EditVideo)
                sb.Append(",\"video_url\":\"").Append(EscapeJson(url)).Append("\"");
            else
                sb.Append(",\"image_url\":\"").Append(EscapeJson(url)).Append("\"");
        }

        sb.Append("}");
        return sb.ToString();
    }

    private IEnumerator PostJson(string url, string apiKey, string json,
        Action<string> onOk,
        Action<string> onErr)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using (UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);

#if UNITY_WEBGL
            req.SetRequestHeader("Accept", "application/json");
#endif

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif

            if (!ok)
            {
                string msg = BuildUnityWebRequestError(req);
                onErr?.Invoke(msg);
                yield break;
            }

            string resp = req.downloadHandler != null ? req.downloadHandler.text : "";
            onOk?.Invoke(resp);
        }
    }

    private IEnumerator GetJson(string url, string apiKey,
        Action<string> onOk,
        Action<string> onErr)
    {
        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
            bool ok = !req.isNetworkError && !req.isHttpError;
#endif

            if (!ok)
            {
                string msg = BuildUnityWebRequestError(req);
                onErr?.Invoke(msg);
                yield break;
            }

            string resp = req.downloadHandler != null ? req.downloadHandler.text : "";
            onOk?.Invoke(resp);
        }
    }

    private string BuildUnityWebRequestError(UnityWebRequest req)
    {
        string body = "";
        try
        {
            if (req.downloadHandler != null) body = req.downloadHandler.text;
        }
        catch { }

        string msg = string.IsNullOrEmpty(req.error) ? "Request failed." : req.error;
        long code = req.responseCode;

        if (!string.IsNullOrEmpty(body))
        {
            // Try to extract error message field
            string em = ExtractJsonString(body, "message");
            if (string.IsNullOrEmpty(em)) em = ExtractJsonString(body, "error");
            if (!string.IsNullOrEmpty(em)) msg += " • " + em;
        }

        return msg + " • HTTP " + code;
    }

    // -----------------------------
    // Tiny JSON helpers (robust enough for simple responses)


    // ------------------------------------------------
    // WebGL Upload callback (JS -> Unity)
    // ------------------------------------------------
    public void OnGrokImagePicked(string payload)
    {
        try
        {
            string fileName, mime, dataUrl;
            if (!SpuricWebGLMediaBridge.TryParseFilePayload(payload, out fileName, out mime, out dataUrl))
            {
                SetStatus("Upload failed (bad payload).");
                return;
            }

            _uploadedImageDataUrl = dataUrl ?? "";
            if (_urlField != null) _urlField.text = _uploadedImageDataUrl;

            SetStatus(string.IsNullOrEmpty(_uploadedImageDataUrl)
                ? "Upload failed (empty data)."
                : ("✅ Image uploaded: " + fileName));
        }
        catch (Exception e)
        {
            SetStatus("Upload failed: " + e.Message);
        }
    }

    private void TryInsertVideoIntoChat(string videoUrl)
    {
        if (manager == null) return;
        try
        {
            // This adds a playable + savable video bubble INSIDE the chat (no /* Application.OpenURL disabled for WebGL in-app UX */ SpuricWebGLMediaBridge.ShowVideoOverlayFromUrl).
            manager.AddAssistantVideoFromUrl(videoUrl, "✅ GROK VIDEO", MakeDefaultVideoFileName(), "video/mp4");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GrokVideo] Failed to insert video into chat: " + e.Message);
        }
    }

    private string MakeDefaultVideoFileName()
    {
        return "grok_video_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".mp4";
    }

    // -----------------------------
    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    
    /// <summary>
    /// Best-effort MIME guess for an image filename or data URL.
    /// Used in Editor testing (file picker) and when constructing data: URLs.
    /// </summary>
    private static string GuessImageMime(string filenameOrDataUrl)
    {
        if (string.IsNullOrEmpty(filenameOrDataUrl)) return "image/png";

        // If already a data URL, parse "data:<mime>;"
        if (filenameOrDataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            int semi = filenameOrDataUrl.IndexOf(';');
            int comma = filenameOrDataUrl.IndexOf(',');
            int end = (semi >= 0) ? semi : comma;
            if (end > 5)
            {
                string mime = filenameOrDataUrl.Substring(5, end - 5);
                if (!string.IsNullOrEmpty(mime)) return mime;
            }
            return "image/png";
        }

        string ext = "";
        try { ext = Path.GetExtension(filenameOrDataUrl) ?? ""; } catch { ext = ""; }
        ext = ext.ToLowerInvariant();

        switch (ext)
        {
            case ".jpg":
            case ".jpeg": return "image/jpeg";
            case ".png":  return "image/png";
            case ".webp": return "image/webp";
            case ".gif":  return "image/gif";
            case ".bmp":  return "image/bmp";
            case ".tif":
            case ".tiff": return "image/tiff";
            default:      return "image/png";
        }
    }

private static string EscapeUrlSegment(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return UnityWebRequest.EscapeURL(s).Replace("+", "%20");
    }

    /// <summary>
    /// Extracts a simple string property value from JSON: "key":"value"
    /// Works even if JSON has extra spacing/newlines; not a full JSON parser.
    /// </summary>
    private static string ExtractJsonString(string json, string key)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return "";
        // Match "key" : "value"
        string pattern = "\""+Regex.Escape(key)+"\"\\s*:\\s*\"(?<v>.*?)\"";
        Match m = Regex.Match(json, pattern, RegexOptions.Singleline);
        if (!m.Success) return "";
        string v = m.Groups["v"].Value;
        // Unescape minimal sequences
        v = v.Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t").Replace("\\\"", "\"").Replace("\\\\", "\\");
        return v;
    }
}