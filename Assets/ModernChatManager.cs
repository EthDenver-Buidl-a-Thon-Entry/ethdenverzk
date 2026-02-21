using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(Canvas))]
public class ModernChatManager : MonoBehaviour
{
    // ------------------------------------
    // EVENTS ADDED TO SUPPORT EXTRA LOGIC
    // ------------------------------------
    public event Action<GameObject, bool, string, Texture2D> OnBubbleCreated;
    public event Action<string> OnUserMessageSent;


    // --- Inline video playback (Editor/Standalone) helpers ---
    private readonly Dictionary<int, string> _vpOriginalUrl = new Dictionary<int, string>();
    private readonly HashSet<int> _vpTriedLocalFallback = new HashSet<int>();
    private readonly Dictionary<string, string> _videoTempCache = new Dictionary<string, string>();

    // ------------------------------------
    // NESTED static class to store all model constants
    // ------------------------------------
    public static class OpenAIModels
    {
        // -----------------------------
        // OpenAI (Chat)
        // -----------------------------
        public const string GPT_5_2            = "gpt-5.2-2025-12-11";
        public const string GPT_5_MINI         = "gpt-5-mini-2025-08-07";
        public const string GPT_5_NANO         = "gpt-5-nano-2025-08-07";

        // Legacy (kept for backward compatibility / existing features)
        public const string GPT35_TURBO        = "gpt-3.5-turbo";
        public const string GPT35_TURBO_16K    = "gpt-3.5-turbo-16k";
        public const string GPT4O_MINI         = "gpt-4o-mini";
        public const string GPT4O              = "gpt-4o";
        public const string GPT45_PREVIEW      = "gpt-4.5-preview-2025-02-27";
        public const string O1                 = "o1-2024-12-17";
        public const string O1_MINI            = "o1-mini-2024-09-12";
        public const string O3_MINI            = "o3-mini-2025-01-31";
        public const string O3_MINI_HIGH       = "o3-mini-high";
        public const string GPT4O_MINI_SEARCH  = "gpt-4o-mini-search-preview-2025-03-11";
        public const string GPT4O_SEARCH       = "gpt-4o-search-preview-2025-03-11";

        // -----------------------------
        // OpenAI (Images) – NO DALL·E
        // -----------------------------
        public const string GPT_IMAGE_1_5      = "gpt-image-1.5-2025-12-16";

        // -----------------------------
        // xAI Grok (Chat)
        // -----------------------------
        public const string GROK_4_1_FAST_REASONING      = "grok-4-1-fast-reasoning";
        public const string GROK_4_1_FAST_NON_REASONING  = "grok-4-1-fast-non-reasoning";
        public const string GROK_3_MINI                  = "grok-3-mini";

        // xAI Grok (Images)
        public const string GROK_2_IMAGE_1212            = "grok-2-image-1212";

        // Legacy Grok (kept)
        public const string GROK_2_1212                  = "grok-2-1212";         // text
        public const string GROK_2_VISION_1212           = "grok-2-vision-1212";  // vision
        public const string GROK_2_IMAGE                 = "grok-2-image";        // image generation (legacy id)

        // -----------------------------
        // Gemini (Chat)
        // -----------------------------
        public const string GEMINI_3_PRO_PREVIEW         = "gemini-3-pro-preview";
        public const string GEMINI_3_FLASH_PREVIEW       = "gemini-3-flash-preview";
        public const string GEMINI_25_FLASH_LITE         = "gemini-2.5-flash-lite";

        // Gemini (Images)
        public const string GEMINI_3_PRO_IMAGE_PREVIEW   = "gemini-3-pro-image-preview";
        public const string GEMINI_25_FLASH_IMAGE         = "gemini-2.5-flash-image";

        // Legacy Gemini (kept)
        public const string GEMINI_25_PRO                = "gemini-2.5-pro-exp-03-25";
        public const string GEMINI_20_FLASH              = "gemini-2.0-flash";
        public const string GEMINI_20_FLASH_LITE         = "gemini-2.0-flash-lite";
        public const string GEMINI_20_IMAGE              = "imagen-3.0-generate-002";

        // Legacy DALL·E constant kept (not used)
        public const string DALLE_3                      = "dall-e-3";
    }

    // ------------------------------------
    // PUBLIC FIELDS (CONFIG)
    // ------------------------------------
    [Header("Canvas & Sprites")]
    public Canvas targetCanvas;
    public Sprite buttonSprite;

    [Header("Branding")]
    [Tooltip("Shown on the floating header / overlay. Change this per hackathon or chain demo.")]
    public string appTitle = "ETHDenver";

    [Header("Button Sprites (Optional Overrides)")]
    public Sprite togglePanelButtonSprite;
    public Sprite newChatButtonSprite;
    public Sprite renameButtonSprite;
    public Sprite deleteButtonSprite;
    public Sprite modelMiniButtonSprite;
    public Sprite modelFullButtonSprite;
    public Sprite modelO1ButtonSprite;
    public Sprite modelO1miniButtonSprite;
    public Sprite uploadButtonSprite;
    public Sprite searchButtonSprite;
    public Sprite sendButtonSprite;
    public Sprite dalle3ButtonSprite;

    [Header("Canvas Background/Other Sprites")]
    public Sprite conversationSprite;
    public Sprite userBubbleSprite;
    public Sprite assistantBubbleSprite;
    public Sprite backgroundSprite;

    [Header("Logo (Optional)")]
    public Sprite topBarLogoSprite;

    [Header("Colors (All Black + White Theme]")]
    public Color topBarColor        = Color.black;
    public Color leftPanelColor     = Color.black;
    public Color mainAreaColor      = Color.black;
    public Color bottomBarColor     = Color.black;
    public Color inputFieldColor    = Color.black;
    public Color userTextColor      = Color.white;
    public Color assistantTextColor = Color.white;

    [Header("Text Settings")]
    public int titleFontSize  = 32;
    public int buttonFontSize = 24;
    public int chatFontSize   = 28;
    public int inputFontSize  = 24;

    [Header("OpenAI Settings")]
    public string openAiApiKey = "YOUR_API_KEY_HERE";
    public string geminiApiKey = "YOUR_GEMINI_KEY_HERE";  // If Gemini uses a separate API key

    [Header("Grok Settings")]
    public string grokApiKey = "YOUR_GROK_KEY_HERE";
    public int maxTokensForVision = 1500;

    [TextArea(4, 10)]
    public string globalSuperPrompt =
        "Enter your extremely long super prompt or system instructions here...";

    [Header("Conversation Mode Settings")]
    [TextArea(4, 10)]
    public string conversationModePrompt =
        "You are now in conversation mode. Please consider all previous messages in the conversation when replying. Respond sequentially and coherently.";

    public bool downscaleLargeImages = true;
    public int maxImageSize = 768;

    // ------------------------------------
    // ADDITIONAL ENHANCEMENTS (#1, #2, #4, #5, #6, #7, #8)
    // (IGNORING #3)
    // ------------------------------------
    [Header("Enhancements (Ignoring #3)")]
    public int maxChatMessages = 100;
    public bool fadeInAssistantBubbles = true;
    public float fadeInDuration = 0.6f;
    public int maxBubbleWidth = 600;
    [Header("Save/Load Sessions (Enhancement #1)")]
    public string sessionsSaveFilename = "ChatSessions.json";

    // ------------------------------------
    // NEW: LLM-Conversation Mode Settings
    // ------------------------------------
    [Header("LLM-Conversation Mode Settings")]
    public bool llmConversationModeActive = false;
    public List<LLMConversationSetting> llmConversationSettings = new List<LLMConversationSetting>();
    [HideInInspector] public Button initiateConversationButton;
    [HideInInspector] public Button stopConversationButton;

    // -- New memory-based conversation fields
    [Header("LLM Conversation Memory")]
    [Tooltip("When true, multi-LLM conversations will be summarized if they get too large.")]
    public bool conversationMemoryEnabled = true;

    [Tooltip("Approximate max tokens before summarizing older messages.")]
    public int memoryTokenThreshold = 3000;

    [Tooltip("Model used for conversation summarization. Default set to GPT4O_MINI.")]
    public string memorySummaryModel = OpenAIModels.GPT4O_MINI;

    [Tooltip("Max tokens used for the summary generation call.")]
    public int memorySummaryMaxTokens = 600;

    // ------------------------------------
    // PRIVATE FIELDS
    // ------------------------------------
    private Dictionary<string, Button> allModelButtons = new Dictionary<string, Button>();

    // In conversation mode: a single session
    // In non-conversation mode: each model has its own sub-session
    private Dictionary<string, ChatSession> subSessions = new Dictionary<string, ChatSession>();
    private List<string> selectedModels = new List<string>();  // preserve order

    // For dynamic re-roll approach (Stop/Re-roll if repetitive)
    // We store each model's recent assistant responses:
    private Dictionary<string, Queue<string>> recentModelResponses = new Dictionary<string, Queue<string>>();

    private Button btnDalle3;
    private bool dalle3Active = true;

    private Button btnGrokImage;
    private bool grokImageActive = false;

    private Button btnGeminiImage;
    private bool geminiImageActive = false;

    private GameObject topBarGO, leftPanelGO, chatAreaGO, bottomBarGO;
    private GameObject topRightModelPanel;
    private GameObject modelButtonsOverlayGO;
    private bool showModelButtonsPanel = true;
    private GameObject togglePanelButtonGO, newChatButtonGO;
    private Button renameButton, deleteButton;
    private RectTransform chatContentRect;
    private ScrollRect chatScrollRect;

    private InputField inputField, renameInputField;

    private Button sendButton, uploadButton, webSearchButton;
    private bool panelVisible = true;

    private List<ChatSession> chatSessions = new List<ChatSession>();
    private ChatSession currentSession = null;
    private RectTransform sessionListContainer;

    private string selectedFilePath = null;
    private byte[] selectedFileBytes = null;
    private string selectedFileName = null;
    private Text filePreviewText = null;

    private bool webSearchActive = false;
    private bool pendingImageConfirmation = false;
    private string pendingImagePrompt = null;

    private GameObject imagePopupOverlayGO;
    private Image popupImage;

    private bool conversationModeActive = false;
    private Button conversationModeButton;

    private bool llmConversationRunning = false;
    private Coroutine llmConversationCoroutine;
    private Button llmConvToggleButton;

    // Single conversation memory for multi-LLM usage
    private List<ChatMessageVision> multiLLMConversation = new List<ChatMessageVision>();

    // For extension => MIME
    private static readonly Dictionary<string, string> extensionToMime = new Dictionary<string, string>()
    {
        {".txt","text/plain"}, {".csv","text/csv"}, {".json","application/json"},
        {".png","image/png"},  {".jpg","image/jpeg"}, {".jpeg","image/jpeg"},
        {".gif","image/gif"},  {".bmp","image/bmp"},  {".tif","image/tiff"},
        {".tiff","image/tiff"},{".webp","image/webp"},
        {".pdf","application/pdf"}, {".doc","application/msword"},
        {".docx","application/vnd.openxmlformats-officedocument.wordprocessingml.document"},
        {".xls","application/vnd.ms-excel"},
        {".xlsx","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"},
        {".ppt","application/vnd.ms-powerpoint"},
        {".pptx","application/vnd.openxmlformats-officedocument.presentationml.presentation"},
        {".mp3","audio/mpeg"}, {".wav","audio/wav"}, {".ogg","audio/ogg"},
        {".mp4","video/mp4"}, {".mov","video/quicktime"}, {".avi","video/x-msvideo"}, {".webm","video/webm"},
        {".zip","application/zip"}, {".rar","application/x-rar-compressed"},
        {".7z","application/x-7z-compressed"}, {".tar","application/x-tar"},
        {".gz","application/gzip"},
    };

    // Input field resizing limits
    private float minInputFieldHeight = 60f;
    private float maxInputFieldHeight = 300f;
    private bool needsResizing = false;
    private Coroutine autoScrollCoroutine;

    // Sample dynamic continuation prompts to reduce repetitiveness
    private static readonly string[] possibleContinuationPrompts = new string[]
    {
        "Continue the story from the last assistant’s final sentence.",
        "Take the preceding response and build upon it with fresh ideas.",
        "Carry on where the last speaker left off, adding new detail or twists.",
        "Expand on the last assistant’s ideas, moving the narrative forward."
    };

    private void Awake()
    {
        // Auto-resolve canvas if not assigned (common in quick hackathon scenes).
        if (targetCanvas == null)
        {
            targetCanvas = GetComponent<Canvas>();
            if (targetCanvas == null) targetCanvas = FindObjectOfType<Canvas>();
        }

        if (targetCanvas == null)
        {
            Debug.LogError("[ModernChatManager] No Canvas assigned/found. Please assign targetCanvas in the Inspector.");
            return;
        }

        EnsureUnityUIRuntime(targetCanvas);

        var scaler = targetCanvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = targetCanvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private void Start()
    {
        if (targetCanvas == null)
        {
            Debug.LogError("No Canvas assigned to ModernChatManager!");
            return;
        }

        // Default selection
        selectedModels.Clear();
        selectedModels.Add(OpenAIModels.GPT_5_MINI);

        // Single "shared" session for conversation mode
        currentSession = new ChatSession
        {
            sessionName = "Ephemeral Chat",
            chosenModels = new List<string>(selectedModels),
            messages = new List<ChatBubbleData>()
        };

        // Also create sub-sessions for each model
        foreach (string model in selectedModels)
        {
            if (!subSessions.ContainsKey(model))
            {
                subSessions[model] = new ChatSession
                {
                    sessionName = "SubSession:" + model,
                    chosenModels = new List<string>() { model },
                    messages = new List<ChatBubbleData>()
                };
            }
        }

        // Prepare for repetition checks
        recentModelResponses.Clear();
        foreach (var setting in llmConversationSettings)
        {
            recentModelResponses[setting.modelId] = new Queue<string>();
        }

        InitializeUI();
        CreateImagePopupOverlay();
        CreateTopRightModelButtons();
    }

    private void Update()
    {
        // Enter to send (Shift+Enter for newline) like ChatGPT
        if (inputField != null && inputField.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

                if (!shiftHeld)
                {
                    OnSendClicked();
                    // Keep focus so you can keep typing quickly
                    inputField.ActivateInputField();
                }
                // If Shift is held, we let InputField handle the newline.
            }
        }
    }


    private void LateUpdate()
    {
        // If user typed or changed text, recalc dynamic size
        if (needsResizing)
        {
            needsResizing = false;
            AdjustInputFieldSize();
            if (autoScrollCoroutine != null) StopCoroutine(autoScrollCoroutine);
            autoScrollCoroutine = StartCoroutine(AutoScrollInputField());
        }
    }

    private IEnumerator AutoScrollInputField()
    {
        // Force UI updates so the scrollbar matches text changes
        yield return null;
        Canvas.ForceUpdateCanvases();
        yield return null;
        Canvas.ForceUpdateCanvases();
    }

    private void MuteAudio()
    {
        Debug.Log("[MuteAudio] Starting audio mute process...");
        AudioSource[] sources = FindObjectsOfType<AudioSource>();
        Debug.Log("[MuteAudio] Found " + sources.Length + " AudioSource(s).");
        foreach (AudioSource source in sources)
        {
            source.Stop();
            source.mute = true;
        }
    }

    // ------------------------------------------------
    // Overlays, UI creation, left panel, top bar, etc.
    // ------------------------------------------------

    private void CreateImagePopupOverlay()
    {
        imagePopupOverlayGO = new GameObject("ImagePopupOverlay");
        imagePopupOverlayGO.transform.SetParent(targetCanvas.transform, false);

        var overlayRect = imagePopupOverlayGO.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        var overlayImg = imagePopupOverlayGO.AddComponent<Image>();
        overlayImg.color = new Color(0, 0, 0, 0.65f);
        overlayImg.maskable = false;

        var overlayButton = imagePopupOverlayGO.AddComponent<Button>();
        overlayButton.onClick.AddListener(() => imagePopupOverlayGO.SetActive(false));

        var popupImgGO = new GameObject("PopupImage");
        popupImgGO.transform.SetParent(imagePopupOverlayGO.transform, false);

        var popupRect = popupImgGO.AddComponent<RectTransform>();
        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = new Vector2(900, 900);

        popupImage = popupImgGO.AddComponent<Image>();
        popupImage.maskable = false;
        popupImage.color = Color.white;
        popupImage.preserveAspect = true;

        imagePopupOverlayGO.SetActive(false);
    }

    public void CreateUI()
    {
        if (targetCanvas == null)
        {
            Debug.LogError("No Canvas assigned!");
            return;
        }
        CreateTopBar();
        CreateLeftPanel();
        CreateChatArea();
        CreateBottomBar();
        CreateFilePreviewUI();
        topBarGO.transform.SetAsLastSibling();
    }

    private void InitializeUI()
    {
        topBarGO = targetCanvas.transform.Find("TopBar")?.gameObject;
        leftPanelGO = targetCanvas.transform.Find("LeftPanel")?.gameObject;
        chatAreaGO = targetCanvas.transform.Find("ChatArea")?.gameObject;
        bottomBarGO = targetCanvas.transform.Find("BottomBar")?.gameObject;

        if (topBarGO == null) CreateTopBar();
        if (leftPanelGO == null) CreateLeftPanel();
        if (chatAreaGO == null) CreateChatArea();
        if (bottomBarGO == null) CreateBottomBar();

        CreateFilePreviewUI();
    }

    // Creates a simple button with text
    private GameObject CreateButton(
        string label, int fontSize, Color bgColor,
        float width, float height, Sprite overrideSprite = null)
    {
        var buttonGO = new GameObject(label + "Button");
        var rt = buttonGO.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);

        var btn = buttonGO.AddComponent<Button>();
        var img = buttonGO.AddComponent<Image>();
        img.maskable = false;

        Sprite finalSprite = overrideSprite ? overrideSprite : buttonSprite;
        if (finalSprite != null)
        {
            img.sprite = finalSprite;
            img.type = Image.Type.Sliced;
        }
        img.color = bgColor;

        var outline = buttonGO.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, 2f);

        btn.targetGraphic = img;

        var textGO = new GameObject("Text");
        textGO.transform.SetParent(buttonGO.transform, false);

        var txtRect = textGO.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        var textComp = textGO.AddComponent<Text>();
        textComp.text = label;
        textComp.fontSize = fontSize;
        textComp.color = Color.white;
        textComp.alignment = TextAnchor.MiddleCenter;
        // Use "LegacyRuntime.ttf" to avoid TMP
        textComp.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        return buttonGO;
    }

    // Quick style for InputField-like backgrounds
    private void StyleAsInputField(Image bgImg)
    {
        if (bgImg == null) return;
        bgImg.color = inputFieldColor;
        if (buttonSprite != null)
        {
            bgImg.sprite = buttonSprite;
            bgImg.type = Image.Type.Sliced;
        }
        var outline = bgImg.gameObject.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, 2f);
    }

    private void CreateFilePreviewUI()
    {
        if (bottomBarGO == null) return;

        var previewGO = new GameObject("FilePreview");
        previewGO.transform.SetParent(bottomBarGO.transform, false);

        var previewRect = previewGO.AddComponent<RectTransform>();
        previewRect.anchorMin = new Vector2(0, 1);
        previewRect.anchorMax = new Vector2(1, 1);
        previewRect.pivot = new Vector2(0.5f, 0);
        previewRect.anchoredPosition = new Vector2(0, 10);
        previewRect.sizeDelta = new Vector2(0, 30);

        filePreviewText = previewGO.AddComponent<Text>();
        filePreviewText.fontSize = 18;
        filePreviewText.alignment = TextAnchor.MiddleCenter;
        filePreviewText.color = Color.white;
        filePreviewText.text = "";
        filePreviewText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private void UpdateFilePreviewLabel()
    {
        if (filePreviewText == null) return;
        filePreviewText.text = !string.IsNullOrEmpty(selectedFileName)
            ? $"Selected File: <i>{selectedFileName}</i>" : "";
    }

    // -------------------
    // Top Bar
    // -------------------
    private void CreateTopBar()
    {
        topBarGO = new GameObject("TopBar");
        topBarGO.transform.SetParent(targetCanvas.transform, false);

        var rt = topBarGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0, 70);

        var topBG = topBarGO.AddComponent<Image>();
        topBG.color = topBarColor;
        topBG.maskable = false;

        if (topBarLogoSprite != null)
        {
            var logoGO = new GameObject("TopBarLogo");
            logoGO.transform.SetParent(topBarGO.transform, false);

            var logoRect = logoGO.AddComponent<RectTransform>();
            logoRect.anchorMin = new Vector2(0.5f, 0.5f);
            logoRect.anchorMax = new Vector2(0.5f, 0.5f);
            logoRect.pivot = new Vector2(0.5f, 0.5f);
            logoRect.sizeDelta = new Vector2(200, 50);

            var logoImg = logoGO.AddComponent<Image>();
            logoImg.sprite = topBarLogoSprite;
            logoImg.color = Color.white;
            logoImg.preserveAspect = true;
            logoImg.maskable = false;
        }
        else
        {
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(topBarGO.transform, false);

            var titleRect = titleGO.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.sizeDelta = new Vector2(300, 50);

            var titleText = titleGO.AddComponent<Text>();
            titleText.text = string.IsNullOrEmpty(appTitle) ? "ETHDenver" : appTitle;
            titleText.fontSize = titleFontSize;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        var toggleColor = Color.black;
        togglePanelButtonGO = CreateButton("≡", buttonFontSize, toggleColor, 50, 50, togglePanelButtonSprite);
        togglePanelButtonGO.name = "TogglePanelButton";
        togglePanelButtonGO.transform.SetParent(topBarGO.transform, false);

        var toggleRect = togglePanelButtonGO.GetComponent<RectTransform>();
        toggleRect.anchorMin = new Vector2(0, 0.5f);
        toggleRect.anchorMax = new Vector2(0, 0.5f);
        toggleRect.pivot = new Vector2(0, 0.5f);
        toggleRect.anchoredPosition = Vector2.zero;
        togglePanelButtonGO.GetComponent<Button>().onClick.AddListener(OnTogglePanelClicked);
        // Toggle model buttons overlay (top-right)
        var modelsToggleGO = CreateButton("Models", buttonFontSize, Color.black, 110, 50, togglePanelButtonSprite);
        modelsToggleGO.name = "ToggleModelsButton";
        modelsToggleGO.transform.SetParent(topBarGO.transform, false);
        var modelsRect = modelsToggleGO.GetComponent<RectTransform>();
        modelsRect.anchorMin = new Vector2(1, 0.5f);
        modelsRect.anchorMax = new Vector2(1, 0.5f);
        modelsRect.pivot = new Vector2(1, 0.5f);
        modelsRect.anchoredPosition = new Vector2(-10, 0);
        modelsToggleGO.GetComponent<Button>().onClick.AddListener(ToggleModelButtonsPanel);
    }

    private void OnTogglePanelClicked()
    {
        panelVisible = !panelVisible;
        if (leftPanelGO != null)
        {
            var rect = leftPanelGO.GetComponent<RectTransform>();
            rect.sizeDelta = panelVisible ? new Vector2(250, 0) : new Vector2(0, 0);
        }
    }

    private void ToggleModelButtonsPanel()
    {
        showModelButtonsPanel = !showModelButtonsPanel;
        if (modelButtonsOverlayGO != null) modelButtonsOverlayGO.SetActive(showModelButtonsPanel);
    }


    // -------------------
    // Left Panel
    // -------------------
    private void CreateLeftPanel()
    {
        leftPanelGO = new GameObject("LeftPanel");
        leftPanelGO.transform.SetParent(targetCanvas.transform, false);

        var lr = leftPanelGO.AddComponent<RectTransform>();
        lr.anchorMin = new Vector2(0, 0);
        lr.anchorMax = new Vector2(0, 1);
        lr.pivot = new Vector2(0, 0.5f);
        lr.sizeDelta = new Vector2(250, 0);

        var img = leftPanelGO.AddComponent<Image>();
        img.color = leftPanelColor;
        img.maskable = false;

        var sessionListGO = new GameObject("SessionList");
        sessionListGO.transform.SetParent(leftPanelGO.transform, false);

        var listRect = sessionListGO.AddComponent<RectTransform>();
        listRect.anchorMin = new Vector2(0, 0);
        listRect.anchorMax = new Vector2(1, 1);
        listRect.offsetMin = new Vector2(0, 180);
        listRect.offsetMax = Vector2.zero;
        listRect.pivot = new Vector2(0.5f, 0.5f);

        var vLayout = sessionListGO.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.UpperCenter;
        vLayout.childControlWidth = false;
        vLayout.childControlHeight = false;
        vLayout.childForceExpandWidth = false;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = 5f;

        var cSF = sessionListGO.AddComponent<ContentSizeFitter>();
        cSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        cSF.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        sessionListContainer = listRect;

        var newChatButtonColor = Color.black;
        var newChatButtonGO = CreateButton("New Chat", buttonFontSize, newChatButtonColor, 210, 50, newChatButtonSprite);
        newChatButtonGO.name = "NewChatButton";
        newChatButtonGO.transform.SetParent(leftPanelGO.transform, false);

        var newChatRect = newChatButtonGO.GetComponent<RectTransform>();
        newChatRect.anchorMin = new Vector2(0.5f, 1f);
        newChatRect.anchorMax = new Vector2(0.5f, 1f);
        newChatRect.pivot = new Vector2(0.5f, 1f);
        newChatRect.anchoredPosition = new Vector2(0, -10);
        newChatButtonGO.GetComponent<Button>().onClick.AddListener(OnNewChatClicked);

        // Rename Input
        var renameInputGO = new GameObject("RenameInputField");
        renameInputGO.transform.SetParent(leftPanelGO.transform, false);

        var renameRect = renameInputGO.AddComponent<RectTransform>();
        renameRect.anchorMin = new Vector2(0.5f, 1f);
        renameRect.anchorMax = new Vector2(0.5f, 1f);
        renameRect.pivot = new Vector2(0.5f, 1f);
        renameRect.sizeDelta = new Vector2(210, 40);
        renameRect.anchoredPosition = new Vector2(0, -70);

        var renameInputImage = renameInputGO.AddComponent<Image>();
        StyleAsInputField(renameInputImage);

        renameInputField = renameInputGO.AddComponent<InputField>();
        renameInputField.lineType = InputField.LineType.SingleLine;
        renameInputField.interactable = true;
        renameInputField.textComponent = null;

        // Placeholder
        var renamePlaceholderGO = new GameObject("Placeholder");
        renamePlaceholderGO.transform.SetParent(renameInputGO.transform, false);

        var renamePlaceholderRect = renamePlaceholderGO.AddComponent<RectTransform>();
        renamePlaceholderRect.anchorMin = Vector2.zero;
        renamePlaceholderRect.anchorMax = Vector2.one;
        renamePlaceholderRect.offsetMin = new Vector2(10, 0);
        renamePlaceholderRect.offsetMax = new Vector2(-10, 0);

        var renamePlaceholderText = renamePlaceholderGO.AddComponent<Text>();
        renamePlaceholderText.text = "Enter new name...";
        renamePlaceholderText.fontSize = 18;
        renamePlaceholderText.color = new Color(1, 1, 1, 0.5f);
        renamePlaceholderText.alignment = TextAnchor.MiddleLeft;
        renamePlaceholderText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        renameInputField.placeholder = renamePlaceholderText;

        // Actual text
        var renameTextGO = new GameObject("RenameText");
        renameTextGO.transform.SetParent(renameInputGO.transform, false);

        var renameTextRect = renameTextGO.AddComponent<RectTransform>();
        renameTextRect.anchorMin = Vector2.zero;
        renameTextRect.anchorMax = Vector2.one;
        renameTextRect.offsetMin = new Vector2(10, 0);
        renameTextRect.offsetMax = new Vector2(-10, 0);

        var renameText = renameTextGO.AddComponent<Text>();
        renameText.fontSize = 18;
        renameText.color = Color.white;
        renameText.alignment = TextAnchor.MiddleLeft;
        renameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        renameInputField.textComponent = renameText;

        var renameBtnColor = Color.black;
        var renameGO = CreateButton("Rename", buttonFontSize - 8, renameBtnColor, 100, 40, renameButtonSprite);
        renameGO.name = "RenameButton";
        renameGO.transform.SetParent(leftPanelGO.transform, false);

        var renameBtnRect = renameGO.GetComponent<RectTransform>();
        renameBtnRect.anchorMin = new Vector2(0.5f, 1f);
        renameBtnRect.anchorMax = new Vector2(0.5f, 1f);
        renameBtnRect.pivot = new Vector2(0.5f, 1f);
        renameBtnRect.anchoredPosition = new Vector2(-55, -120);
        renameButton = renameGO.GetComponent<Button>();
        renameButton.onClick.AddListener(OnRenameClicked);

        var deleteBtnColor = Color.black;
        var deleteGO = CreateButton("Delete", buttonFontSize - 8, deleteBtnColor, 100, 40, deleteButtonSprite);
        deleteGO.name = "DeleteButton";
        deleteGO.transform.SetParent(leftPanelGO.transform, false);

        var deleteRect = deleteGO.GetComponent<RectTransform>();
        deleteRect.anchorMin = new Vector2(0.5f, 1f);
        deleteRect.anchorMax = new Vector2(0.5f, 1f);
        deleteRect.pivot = new Vector2(0.5f, 1f);
        deleteRect.anchoredPosition = new Vector2(55, -120);
        deleteButton = deleteGO.GetComponent<Button>();
        deleteButton.onClick.AddListener(OnDeleteClicked);
    }

    private void OnNewChatClicked()
    {
        if (currentSession != null && currentSession.messages.Count > 0)
            SaveCurrentSessionToLeftPanel();

        currentSession = new ChatSession
        {
            sessionName = "Ephemeral Chat",
            chosenModels = new List<string>(selectedModels),
            messages = new List<ChatBubbleData>()
        };

        // Reset sub-sessions
        subSessions.Clear();
        foreach (string model in selectedModels)
        {
            subSessions[model] = new ChatSession
            {
                sessionName = "SubSession:" + model,
                chosenModels = new List<string>() { model },
                messages = new List<ChatBubbleData>()
            };
        }

        if (chatContentRect != null)
        {
            foreach (Transform child in chatContentRect)
                Destroy(child.gameObject);
        }

        // Clear file selection
        selectedFilePath = null;
        selectedFileBytes = null;
        selectedFileName = null;
        UpdateFilePreviewLabel();

        // Clear pending image confirmations
        pendingImageConfirmation = false;
        pendingImagePrompt = null;
    }

    private void SaveCurrentSessionToLeftPanel()
    {
        if (!chatSessions.Contains(currentSession))
            chatSessions.Add(currentSession);

        if (currentSession.sessionName == "Ephemeral Chat")
            currentSession.sessionName = "Session " + chatSessions.Count;

        var sessionBtnGO = new GameObject("SessionButton");
        sessionBtnGO.transform.SetParent(sessionListContainer, false);

        var r = sessionBtnGO.AddComponent<RectTransform>();
        r.sizeDelta = new Vector2(220, 40);

        var le = sessionBtnGO.AddComponent<LayoutElement>();
        le.minHeight = 40;
        le.preferredHeight = 40;

        var b = sessionBtnGO.AddComponent<Button>();
        var i = sessionBtnGO.AddComponent<Image>();
        i.maskable = false;
        if (buttonSprite != null)
        {
            i.sprite = buttonSprite;
            i.type = Image.Type.Sliced;
        }
        i.color = Color.black;

        var outline = sessionBtnGO.AddComponent<Outline>();
        outline.effectColor = Color.white;
        outline.effectDistance = new Vector2(2f, 2f);

        var txtGO = new GameObject("Text");
        txtGO.transform.SetParent(sessionBtnGO.transform, false);

        var txtRect = txtGO.AddComponent<RectTransform>();
        txtRect.anchorMin = Vector2.zero;
        txtRect.anchorMax = Vector2.one;
        txtRect.offsetMin = Vector2.zero;
        txtRect.offsetMax = Vector2.zero;

        var labelText = txtGO.AddComponent<Text>();
        labelText.text = currentSession.sessionName;
        labelText.fontSize = buttonFontSize;
        labelText.alignment = TextAnchor.MiddleCenter;
        labelText.color = Color.white;
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        ChatSession thisSession = currentSession;
        b.onClick.AddListener(() =>
        {
            currentSession = thisSession;
            renameInputField.text = currentSession.sessionName;
            LoadSession(currentSession);
        });
    }

    private void LoadSession(ChatSession session)
    {
        if (chatContentRect != null)
        {
            foreach (Transform child in chatContentRect)
                Destroy(child.gameObject);
        }

        currentSession = session;
        selectedModels.Clear();
        if (session.chosenModels != null)
        {
            foreach (string m in session.chosenModels)
                selectedModels.Add(m);
        }
        UpdateModelButtonsVisual();

        foreach (var bubble in session.messages)
        {
            if (!string.IsNullOrEmpty(bubble.videoUrl))
            {
                var vid = new VideoBubbleData
                {
                    url = bubble.videoUrl,
                    fileName = string.IsNullOrEmpty(bubble.videoFileName) ? "generated.mp4" : bubble.videoFileName,
                    mimeType = string.IsNullOrEmpty(bubble.videoMime) ? "video/mp4" : bubble.videoMime
                };
                CreateChatBubble(bubble.text, bubble.isUser, true, null, vid);
            }
            else if (!string.IsNullOrEmpty(bubble.imageBase64))
            {
                var tex = DecodeBase64ToTexture(bubble.imageBase64);
                CreateChatBubble(bubble.text, bubble.isUser, true, tex);
            }
            else
            {
                CreateChatBubble(bubble.text, bubble.isUser, true);
            }
        }
    }

    private void OnRenameClicked()
    {
        if (currentSession == null || !chatSessions.Contains(currentSession))
        {
            Debug.LogWarning("No valid session selected to rename.");
            return;
        }

        string newName = renameInputField.text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            renameInputField.Select();
            renameInputField.ActivateInputField();
            return;
        }
        currentSession.sessionName = newName;
        renameInputField.text = "";
        RebuildSessionList();
    }

    private void OnDeleteClicked()
    {
        if (currentSession == null || !chatSessions.Contains(currentSession))
            return;

        chatSessions.Remove(currentSession);
        currentSession = null;

        if (chatContentRect != null)
        {
            foreach (Transform child in chatContentRect)
                Destroy(child.gameObject);
        }
        RebuildSessionList();
    }

    private void RebuildSessionList()
    {
        foreach (Transform child in sessionListContainer)
            Destroy(child.gameObject);

        foreach (var s in chatSessions)
        {
            currentSession = s;
            SaveCurrentSessionToLeftPanel();
        }
        currentSession = null;
    }

    // --------------------
    // Chat area
    // --------------------
    private void CreateChatArea()
    {
        chatAreaGO = new GameObject("ChatArea");
        chatAreaGO.transform.SetParent(targetCanvas.transform, false);

        var rect = chatAreaGO.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(1, 1);
        rect.offsetMin = new Vector2(250, 70);
        rect.offsetMax = new Vector2(0, -70);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var areaImg = chatAreaGO.AddComponent<Image>();
        areaImg.color = mainAreaColor;
        areaImg.maskable = true;
        areaImg.raycastTarget = true;

        var scrollGO = new GameObject("ChatScrollView");
        scrollGO.transform.SetParent(chatAreaGO.transform, false);

        var scrollRectRT = scrollGO.AddComponent<RectTransform>();
        scrollRectRT.anchorMin = Vector2.zero;
        scrollRectRT.anchorMax = Vector2.one;
        scrollRectRT.offsetMin = Vector2.zero;
        scrollRectRT.offsetMax = Vector2.zero;

        var sr = scrollGO.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;
        sr.scrollSensitivity = 20f;
        chatScrollRect = sr;
        sr.onValueChanged.AddListener(_ => OnChatScrolled());

        var viewportGO = new GameObject("Viewport");
        viewportGO.transform.SetParent(scrollGO.transform, false);
        var viewportRT = viewportGO.AddComponent<RectTransform>();
        viewportRT.anchorMin = Vector2.zero;
        viewportRT.anchorMax = Vector2.one;
        viewportRT.offsetMin = Vector2.zero;
        viewportRT.offsetMax = Vector2.zero;
        sr.viewport = viewportRT;
        viewportGO.AddComponent<RectMask2D>();

        var contentGO = new GameObject("ChatContent");
        contentGO.transform.SetParent(viewportGO.transform, false);

        chatContentRect = contentGO.AddComponent<RectTransform>();
        chatContentRect.anchorMin = new Vector2(0, 1);
        chatContentRect.anchorMax = new Vector2(1, 1);
        chatContentRect.pivot = new Vector2(0.5f, 1f);

        var vLayout = contentGO.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.UpperLeft;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = true;
        vLayout.childForceExpandWidth = false;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = 10f;
        vLayout.padding = new RectOffset(20, 20, 20, 20);

        var cSF = contentGO.AddComponent<ContentSizeFitter>();
        cSF.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        sr.content = chatContentRect;

        // Add a vertical scrollbar on the right
        var scrollbarGO = new GameObject("Scrollbar");
        scrollbarGO.transform.SetParent(scrollGO.transform, false);

        var sbRect = scrollbarGO.AddComponent<RectTransform>();
        sbRect.anchorMin = new Vector2(1, 0);
        sbRect.anchorMax = new Vector2(1, 1);
        sbRect.pivot = new Vector2(1, 1);
        sbRect.sizeDelta = new Vector2(20, 0);
        sbRect.offsetMin = new Vector2(-20, 0);

        var scrollbar = scrollbarGO.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        var sbBgImage = scrollbarGO.AddComponent<Image>();
        sbBgImage.color = new Color(1, 1, 1, 0f);
        sbBgImage.type = Image.Type.Sliced;
        if (buttonSprite != null)
            sbBgImage.sprite = buttonSprite;

        var sbOutline = scrollbarGO.AddComponent<Outline>();
        sbOutline.effectColor = Color.white;
        sbOutline.effectDistance = new Vector2(2f, 2f);

        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(scrollbarGO.transform, false);

        var handleRect = handleGO.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        var handleImage = handleGO.AddComponent<Image>();
        handleImage.color = Color.white;
        handleImage.type = Image.Type.Sliced;
        if (buttonSprite != null)
            handleImage.sprite = buttonSprite;

        scrollbar.handleRect = handleRect;
        scrollbar.transition = Selectable.Transition.None;
        var cb = scrollbar.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.pressedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(1, 1, 1, 0.3f);
        scrollbar.colors = cb;
        scrollbar.targetGraphic = handleImage;

        sr.verticalScrollbar = scrollbar;
        sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        sr.verticalScrollbarSpacing = -3f;
    }

    // Creates a bubble for user or assistant
    private class VideoBubbleData
    {
        public string url;
        public string fileName;
        public string mimeType;
    }

    // Creates a bubble for user or assistant
    private void CreateChatBubble(
        string text, bool isUser, bool skipSaving = false, Texture2D optionalImage = null, VideoBubbleData optionalVideo = null)
    {
        if (chatContentRect == null) return;

        // Timestamp label
        string timeStamp = DateTime.Now.ToString("HH:mm");
        string finalText = $"<i>{timeStamp}</i>  {text}";

        var bubbleGO = new GameObject(isUser ? "UserBubble" : "AssistantBubble");
        bubbleGO.transform.SetParent(chatContentRect, false);

        var rt = bubbleGO.AddComponent<RectTransform>();
        rt.pivot = new Vector2(0.5f, 1f);

        var horizontalGroup = bubbleGO.AddComponent<HorizontalLayoutGroup>();
        horizontalGroup.spacing = 10f;
        horizontalGroup.childControlWidth = false;
        horizontalGroup.childControlHeight = true;
        horizontalGroup.childForceExpandHeight = false;
        horizontalGroup.childForceExpandWidth = false;
        horizontalGroup.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

        // spacer to push bubble left or right
        var spacerGO = new GameObject("Spacer");
        spacerGO.transform.SetParent(bubbleGO.transform, false);
        var spacerLayout = spacerGO.AddComponent<LayoutElement>();
        spacerLayout.flexibleWidth = 1;

        var contentContainer = new GameObject("BubbleContainer");
        contentContainer.transform.SetParent(bubbleGO.transform, false);

        var contentRect = contentContainer.AddComponent<RectTransform>();
        contentRect.pivot = new Vector2(0.5f, 0.5f);

        var bubbleVLayout = contentContainer.AddComponent<VerticalLayoutGroup>();
        bubbleVLayout.childControlWidth = true;
        bubbleVLayout.childControlHeight = true;
        bubbleVLayout.childForceExpandWidth = false;
        bubbleVLayout.childForceExpandHeight = false;
        bubbleVLayout.spacing = 5f;
        bubbleVLayout.padding = new RectOffset(18, 18, 14, 14);
        bubbleVLayout.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

        var bubbleFitter = contentContainer.AddComponent<ContentSizeFitter>();
        bubbleFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        bubbleFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var bubbleImage = contentContainer.AddComponent<Image>();
        bubbleImage.maskable = false;

        // High-contrast bubbles for readability (assistant = light, user = saturated blue)
        Color assistantBg = new Color(0.93f, 0.96f, 1.00f, 1f);
        Color userBg      = new Color(0.12f, 0.52f, 0.95f, 0.95f);
        bubbleImage.color = isUser ? userBg : assistantBg;
        if (optionalVideo != null && !string.IsNullOrEmpty(optionalVideo.url))
{
    // There's a video (WebGL: HTML5 video element positioned over this bubble region)
    var videoGO = new GameObject("BubbleVideo");
    videoGO.transform.SetParent(contentContainer.transform, false);

    var vLayout = videoGO.AddComponent<VerticalLayoutGroup>();
    vLayout.childControlWidth = true;
    vLayout.childControlHeight = false;
    vLayout.childForceExpandWidth = false;
    vLayout.childForceExpandHeight = false;
    vLayout.spacing = 6f;
    vLayout.padding = new RectOffset(10, 10, 10, 10);
    vLayout.childAlignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;

    var vBg = videoGO.AddComponent<Image>();
    vBg.color = new Color(0f, 0f, 0f, 0.18f);
    vBg.maskable = false;

    var vLe = videoGO.AddComponent<LayoutElement>();
    vLe.preferredWidth = maxBubbleWidth;
    vLe.preferredHeight = 280;

    var titleGO = new GameObject("VideoTitle");
    titleGO.transform.SetParent(videoGO.transform, false);
    var titleTxt = titleGO.AddComponent<Text>();
    titleTxt.text = "VIDEO";
    titleTxt.fontSize = chatFontSize - 2;
    titleTxt.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
    titleTxt.color = Color.white;
    titleTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    var metaGO = new GameObject("VideoMeta");
    metaGO.transform.SetParent(videoGO.transform, false);
    var metaTxt = metaGO.AddComponent<Text>();
    metaTxt.text = string.IsNullOrEmpty(optionalVideo.fileName) ? "generated.mp4" : optionalVideo.fileName;
    metaTxt.fontSize = chatFontSize - 6;
    metaTxt.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
    metaTxt.color = new Color(1f, 1f, 1f, 0.75f);
    metaTxt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

    // Video viewport (in Editor/Standalone we render via Unity VideoPlayer into this RawImage)
    var viewportGO = new GameObject("VideoViewport");
    viewportGO.transform.SetParent(videoGO.transform, false);

    var viewportRaw = viewportGO.AddComponent<RawImage>();
    viewportRaw.color = new Color(0f, 0f, 0f, 0.55f);
    viewportRaw.raycastTarget = false;

    var viewportOutline = viewportGO.AddComponent<Outline>();
    viewportOutline.effectColor = new Color(1f, 1f, 1f, 0.35f);
    viewportOutline.effectDistance = new Vector2(1f, -1f);

    var viewportLE = viewportGO.AddComponent<LayoutElement>();
    viewportLE.preferredWidth = maxBubbleWidth;
    viewportLE.preferredHeight = 200;

    var viewportRT = viewportGO.GetComponent<RectTransform>();

    var btnRow = new GameObject("VideoBottomRow");
    btnRow.transform.SetParent(videoGO.transform, false);

    var rowLayout = btnRow.AddComponent<HorizontalLayoutGroup>();
    rowLayout.spacing = 6f;
    rowLayout.childControlWidth = false;
    rowLayout.childControlHeight = false;
    rowLayout.childForceExpandHeight = false;
    rowLayout.childForceExpandWidth = false;
    rowLayout.childAlignment = isUser ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

    var rowFitter = btnRow.AddComponent<ContentSizeFitter>();
    rowFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    rowFitter.verticalFit = ContentSizeFitter.FitMode.MinSize;

    var urlToUse = optionalVideo.url;

    var playBtnGO = CreateButton("Play", chatFontSize - 4, Color.black, 80, 40);
    playBtnGO.name = "PlayVideoButton";
    playBtnGO.transform.SetParent(btnRow.transform, false);
    playBtnGO.GetComponent<Button>().onClick.AddListener(() => PlayVideoInChat(urlToUse, viewportRT, viewportRaw));

    var saveBtnGO = CreateButton("Save", chatFontSize - 4, Color.black, 80, 40);
    saveBtnGO.name = "SaveVideoButton";
    saveBtnGO.transform.SetParent(btnRow.transform, false);
    saveBtnGO.GetComponent<Button>().onClick.AddListener(() => SaveVideoFromUrl(urlToUse, optionalVideo.fileName));

    var copyBtnGO = CreateButton("Copy URL", chatFontSize - 6, Color.black, 110, 40);
    copyBtnGO.name = "CopyVideoUrlButton";
    copyBtnGO.transform.SetParent(btnRow.transform, false);
    copyBtnGO.GetComponent<Button>().onClick.AddListener(() => CopyToClipboard(urlToUse));
}

        else if (optionalImage == null)
        {
            var textGO = new GameObject("BubbleText");
            textGO.transform.SetParent(contentContainer.transform, false);

            var txt = textGO.AddComponent<Text>();
            txt.text = finalText;
            txt.fontSize = chatFontSize;
            txt.alignment = isUser ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
            txt.color = isUser ? Color.white : new Color(0.07f, 0.10f, 0.16f, 1f);
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.lineSpacing = 1.08f;

            // subtle shadow for readability
            var shadow = textGO.AddComponent<Shadow>();
            shadow.effectDistance = new Vector2(1f, -1f);
            shadow.effectColor = isUser ? new Color(0f, 0f, 0f, 0.45f) : new Color(0f, 0f, 0f, 0.15f);

            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.supportRichText = true;

            var textLayoutElem = textGO.AddComponent<LayoutElement>();
            textLayoutElem.preferredWidth = maxBubbleWidth;
            textLayoutElem.flexibleWidth = 0;

            txt.text = AdvancedLinkHelper.SanitizeAndConvertLinks(finalText);

            // optional link-click placeholder
            textGO.AddComponent<AdvancedHyperlinkHandler>();
        }
        else
        {
            // There's an image
            var imageGO = new GameObject("BubbleImage");
            imageGO.transform.SetParent(contentContainer.transform, false);

            var uiImg = imageGO.AddComponent<Image>();
            uiImg.maskable = false;
            uiImg.preserveAspect = true;

            var le = imageGO.AddComponent<LayoutElement>();
            le.preferredWidth = maxBubbleWidth;
            le.preferredHeight = maxBubbleWidth;

            Sprite sp = SpriteFromTexture(optionalImage);
            uiImg.sprite = sp;

            var bottomRow = new GameObject("ImageBottomRow");
            bottomRow.transform.SetParent(contentContainer.transform, false);

            var bottomRowLayout = bottomRow.AddComponent<HorizontalLayoutGroup>();
            bottomRowLayout.spacing = 5f;
            bottomRowLayout.childControlWidth = false;
            bottomRowLayout.childControlHeight = false;
            bottomRowLayout.childForceExpandHeight = false;
            bottomRowLayout.childForceExpandWidth = false;
            bottomRowLayout.childAlignment = isUser ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;

            var bottomRowSizeFitter = bottomRow.AddComponent<ContentSizeFitter>();
            bottomRowSizeFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bottomRowSizeFitter.verticalFit = ContentSizeFitter.FitMode.MinSize;

            var saveBtnGO = CreateButton("Save", chatFontSize - 4, Color.black, 80, 40);
            saveBtnGO.name = "SaveImageButton";
            saveBtnGO.transform.SetParent(bottomRow.transform, false);

            var texToSave = optionalImage;
            saveBtnGO.GetComponent<Button>().onClick.AddListener(() => SaveImage(texToSave));

            var clickBtn = imageGO.AddComponent<Button>();
            clickBtn.onClick.AddListener(() => ShowImagePopup(texToSave));
        }
OnBubbleCreated?.Invoke(bubbleGO, isUser, text, optionalImage);
        StartCoroutine(ForceScrollAndPrune());
    }

    

    /// <summary>
    /// Adds a video bubble to the current chat (assistant side), playable + savable inside WebGL (no external tab).
    /// This stores the video URL in the session so it can be re-opened later.
    /// </summary>
    public void AddAssistantVideoFromUrl(string videoUrl, string caption = "✅ VIDEO", string fileName = null, string mimeType = "video/mp4")
    {
        if (string.IsNullOrEmpty(videoUrl)) return;
        if (string.IsNullOrEmpty(fileName)) fileName = "video_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".mp4";

        var vid = new VideoBubbleData { url = videoUrl, fileName = fileName, mimeType = mimeType };
        CreateChatBubble(caption, false, false, null, vid);

        if (currentSession != null)
        {
            if (currentSession.messages == null) currentSession.messages = new List<ChatBubbleData>();
            currentSession.messages.Add(new ChatBubbleData
            {
                isUser = false,
                text = caption,
                videoUrl = videoUrl,
                videoFileName = fileName,
                videoMime = mimeType
            });
        }
    }

        private void PlayVideoInChat(string url, RectTransform anchorRect, RawImage previewSurface)
    {
        if (string.IsNullOrEmpty(url)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // In WebGL we can't render video inside the canvas; instead we position an HTML5 <video>
        // directly over the bubble viewport so it *appears* to play inside the chat.
        if (anchorRect != null)
        {
            Rect r = GetScreenRect(anchorRect);
            if (r.width > 12f && r.height > 12f)
            {
                SpuricWebGLMediaBridge.ShowVideoInRect(url, (int)r.x, (int)r.y, (int)r.width, (int)r.height, Screen.width, Screen.height);
                return;
            }
        }

        // Fallback: full-screen overlay (still in-app)
        SpuricWebGLMediaBridge.ShowVideoOverlayFromUrl(url);
#else
        // Editor/Standalone: render directly into the bubble via Unity VideoPlayer (no browser tab)
        if (anchorRect == null || previewSurface == null)
        {
            Application.OpenURL(url);
            return;
        }

        TryPlayVideoIntoRawImage(url, anchorRect.gameObject, previewSurface);
#endif
    }

    private Rect GetScreenRect(RectTransform rt)
    {
        if (rt == null) return new Rect(0, 0, 0, 0);

        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        for (int i = 0; i < 4; i++)
        {
            Vector2 sp = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            if (sp.x < minX) minX = sp.x;
            if (sp.y < minY) minY = sp.y;
            if (sp.x > maxX) maxX = sp.x;
            if (sp.y > maxY) maxY = sp.y;
        }

        return new Rect(minX, minY, Mathf.Max(0, maxX - minX), Mathf.Max(0, maxY - minY));
    }

    private void TryPlayVideoIntoRawImage(string url, GameObject host, RawImage target)
    {
        if (host == null || target == null || string.IsNullOrEmpty(url)) return;

        var vp = host.GetComponent<VideoPlayer>();
        if (vp == null) vp = host.AddComponent<VideoPlayer>();

        // Reset state so repeated clicks work reliably.
        try { vp.Stop(); } catch { }

        vp.playOnAwake = false;
        vp.source = VideoSource.Url;
        vp.url = url;
        vp.isLooping = true;
        vp.waitForFirstFrame = true;
        vp.skipOnDrop = true;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.renderMode = VideoRenderMode.RenderTexture;

        // Ensure a render texture is available
        RenderTexture rt = target.texture as RenderTexture;
        if (rt == null || !rt.IsCreated() || rt.width < 256 || rt.height < 256)
        {
            rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32);
            rt.Create();
            target.texture = rt;
        }

        vp.targetTexture = rt;

        int id = vp.GetInstanceID();
        _vpOriginalUrl[id] = url;
        _vpTriedLocalFallback.Remove(id);

        vp.prepareCompleted -= OnInlineVideoPrepared;
        vp.prepareCompleted += OnInlineVideoPrepared;

        vp.errorReceived -= OnInlineVideoError;
        vp.errorReceived += OnInlineVideoError;

        vp.Prepare();

        // Safety timeout: if the platform can't decode (common on Linux editor without codecs), fall back to opening the URL.
        StartCoroutine(CoInlineVideoPrepareTimeout(vp, 8f));
    }

    private void OnInlineVideoPrepared(VideoPlayer vp)
    {
        try { vp.Play(); }
        catch { }
    }

    private void OnInlineVideoError(VideoPlayer vp, string message)
    {
        Debug.LogWarning("[Video] VideoPlayer error: " + message);

        if (vp == null) return;

        int id = vp.GetInstanceID();
        if (_vpTriedLocalFallback.Contains(id)) 
        {
            // Already tried fallback once; final fallback is to open in a browser.
            string original = _vpOriginalUrl.ContainsKey(id) ? _vpOriginalUrl[id] : "";
            if (!string.IsNullOrEmpty(original)) Application.OpenURL(original);
            return;
        }

        string url = _vpOriginalUrl.ContainsKey(id) ? _vpOriginalUrl[id] : "";
        if (string.IsNullOrEmpty(url) || !(url.StartsWith("http://") || url.StartsWith("https://")))
        {
            return;
        }

        _vpTriedLocalFallback.Add(id);

        // If direct URL playback fails, download to a temp .mp4 and play locally (much more reliable in Editor/Standalone).
        StartCoroutine(CoDownloadTempVideoAndPlay(vp, url));
    }

    private void ShowVideoOverlay(string url)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SpuricWebGLMediaBridge.ShowVideoOverlayFromUrl(url);
#else
        // Editor fallback
        if (!string.IsNullOrEmpty(url)) Application.OpenURL(url);
#endif
    }


    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Browser clipboard is restricted; this tries a few paths:
        // 1) GUIUtility.systemCopyBuffer (works in some Unity/WebGL versions)
        // 2) If SpuricWebGLMediaBridge implements a static clipboard method, invoke it (optional).
        try { GUIUtility.systemCopyBuffer = text; } catch { }

        try
        {
            MethodInfo mi =
                typeof(SpuricWebGLMediaBridge).GetMethod("CopyToClipboard", BindingFlags.Public | BindingFlags.Static) ??
                typeof(SpuricWebGLMediaBridge).GetMethod("CopyTextToClipboard", BindingFlags.Public | BindingFlags.Static);

            if (mi != null) mi.Invoke(null, new object[] { text });
        }
        catch { }
#else
        GUIUtility.systemCopyBuffer = text;
#endif

        Debug.Log("[Video] Copied URL to clipboard.");
    }

    private static string SanitizeFileName(string filename, string fallback, string forcedExt)
    {
        string name = string.IsNullOrEmpty(filename) ? fallback : filename;
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), "_");

        if (!string.IsNullOrEmpty(forcedExt))
        {
            if (!forcedExt.StartsWith(".")) forcedExt = "." + forcedExt;
            if (!name.EndsWith(forcedExt, StringComparison.OrdinalIgnoreCase))
                name += forcedExt;
        }

        return name;
    }

    private void SaveVideoFromUrl(string url, string filename)
    {
        if (string.IsNullOrEmpty(url)) return;
        filename = SanitizeFileName(filename, "video_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"), "mp4");

#if UNITY_WEBGL && !UNITY_EDITOR
        SpuricWebGLMediaBridge.DownloadUrlAsFile(url, filename);
#else
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel("Save Video", "", filename, "mp4");
        if (string.IsNullOrEmpty(path)) return;
        StartCoroutine(DownloadAndWriteFile(url, path));
#else
        string path = Path.Combine(Application.persistentDataPath, filename);
        StartCoroutine(DownloadAndWriteFile(url, path));
#endif
#endif
    }

    private IEnumerator DownloadAndWriteFile(string url, string path)
    {
        using (var req = UnityWebRequest.Get(url))
        {
            req.downloadHandler = new DownloadHandlerBuffer();
            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            if (req.result != UnityWebRequest.Result.Success)
#else
            if (req.isNetworkError || req.isHttpError)
#endif
            {
                Debug.LogWarning("[Video] Download failed: " + req.error);
                yield break;
            }

            try
            {
                File.WriteAllBytes(path, req.downloadHandler.data);
                Debug.Log("[Video] Saved => " + path);
#if UNITY_EDITOR
                // Open folder for convenience in Editor
                try { Application.OpenURL("file://" + Path.GetDirectoryName(path)); } catch { }
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Video] Save error: " + e.Message);
            }
        }
    }


    private void OnChatScrolled()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Inline HTML video doesn't move with the Unity scroll view; hide it when the user scrolls.
        SpuricWebGLMediaBridge.HideInlineVideo();
#endif

#if !UNITY_WEBGL || UNITY_EDITOR
        // Editor/Standalone: pause any inline VideoPlayers so they don't look detached while scrolling.
        if (chatContentRect != null)
        {
            var vps = chatContentRect.GetComponentsInChildren<VideoPlayer>(true);
            for (int i = 0; i < vps.Length; i++)
            {
                try { if (vps[i] != null && vps[i].isPlaying) vps[i].Pause(); }
                catch { }
            }
        }
#endif
    }

private IEnumerator ForceScrollAndPrune()
    {
        yield return null;
        if (chatScrollRect != null)
            chatScrollRect.verticalNormalizedPosition = 0f;

        while (chatContentRect.childCount > maxChatMessages)
            Destroy(chatContentRect.GetChild(0).gameObject);
    }

    private Sprite SpriteFromTexture(Texture2D tex)
    {
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f));
    }

    private void SaveImage(Texture2D tex)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("Simulating WebGL save. Implement JS-based download if needed!");
#else
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.SaveFilePanel("Save Image", "",
            "generated.png", "png");
        if (!string.IsNullOrEmpty(path))
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("Saved image => " + path);
        }
#else
        Debug.Log("Saving images at runtime for standalone is not fully implemented.");
#endif
#endif
    }

    private void ShowImagePopup(Texture2D tex)
    {
        if (tex == null || imagePopupOverlayGO == null || popupImage == null) return;
        popupImage.sprite = SpriteFromTexture(tex);
        imagePopupOverlayGO.SetActive(true);
    }

    // -------------------
    // Bottom Bar
    // -------------------
    private void CreateBottomBar()
    {
        bottomBarGO = new GameObject("BottomBar");
        bottomBarGO.transform.SetParent(targetCanvas.transform, false);

        var barRect = bottomBarGO.AddComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0, 0);
        barRect.anchorMax = new Vector2(1, 0);
        barRect.pivot = new Vector2(0.5f, 0);
        barRect.sizeDelta = new Vector2(0, 70);

        var barBG = bottomBarGO.AddComponent<Image>();
        barBG.color = bottomBarColor;
        barBG.maskable = false;

        var uploadGO = CreateButton("Upload", buttonFontSize, Color.black, 100, 60, uploadButtonSprite);
        uploadGO.name = "UploadButton";
        uploadGO.transform.SetParent(bottomBarGO.transform, false);

        var upRect = uploadGO.GetComponent<RectTransform>();
        upRect.anchorMin = new Vector2(0, 0.5f);
        upRect.anchorMax = new Vector2(0, 0.5f);
        upRect.pivot = new Vector2(0, 0.5f);
        upRect.anchoredPosition = new Vector2(20, 0);
        uploadButton = uploadGO.GetComponent<Button>();
        uploadButton.onClick.AddListener(OnUploadClicked);

        var searchGO = CreateButton("Web Search", buttonFontSize, Color.black, 120, 60, searchButtonSprite);
        searchGO.name = "SearchButton";
        searchGO.transform.SetParent(bottomBarGO.transform, false);

        var searchRect = searchGO.GetComponent<RectTransform>();
        searchRect.anchorMin = new Vector2(0, 0.5f);
        searchRect.anchorMax = new Vector2(0, 0.5f);
        searchRect.pivot = new Vector2(0, 0.5f);
        searchRect.anchoredPosition = new Vector2(130, 0);
        webSearchButton = searchGO.GetComponent<Button>();
        webSearchButton.onClick.AddListener(OnSearchButtonClicked);

        var inputFieldContainer = new GameObject("InputFieldContainer");
        inputFieldContainer.transform.SetParent(bottomBarGO.transform, false);

        var containerRect = inputFieldContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0, 0.5f);
        containerRect.anchorMax = new Vector2(1, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.offsetMin = new Vector2(281, -30);
        containerRect.offsetMax = new Vector2(-119, 30);

        var containerBg = inputFieldContainer.AddComponent<Image>();
        StyleAsInputField(containerBg);

        // Create the InputField
        inputField = inputFieldContainer.AddComponent<InputField>();
        inputField.text = "";
        inputField.lineType = InputField.LineType.MultiLineNewline;
        inputField.interactable = true;
        inputField.textComponent = null;
        inputField.placeholder = null;
        inputField.onValueChanged.AddListener(delegate { needsResizing = true; });

        // 'Viewport' child
        var viewportGO = new GameObject("InputViewport");
        viewportGO.transform.SetParent(inputFieldContainer.transform, false);
        var viewportRect = viewportGO.AddComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0, 0);
        viewportRect.anchorMax = new Vector2(1, 1);
        viewportRect.offsetMin = new Vector2(10, 10);
        viewportRect.offsetMax = new Vector2(-30, -10);
        viewportGO.AddComponent<RectMask2D>();

        // Placeholder text
        var placeholderGO = new GameObject("Placeholder");
        placeholderGO.transform.SetParent(viewportGO.transform, false);
        var phRect = placeholderGO.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(5, 0);
        phRect.offsetMax = new Vector2(-5, 0);

        var phText = placeholderGO.AddComponent<Text>();
        phText.text = "Type your message...";
        phText.fontSize = inputFontSize;
        phText.color = new Color(1f, 1f, 1f, 0.5f);
        phText.alignment = TextAnchor.UpperLeft;
        phText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        inputField.placeholder = phText;

        // Main text area
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(viewportGO.transform, false);
        var textRect = textGO.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(5, 0);
        textRect.offsetMax = new Vector2(-5, 0);

        var mainText = textGO.AddComponent<Text>();
        mainText.fontSize = inputFontSize;
        mainText.color = Color.white;
        mainText.alignment = TextAnchor.UpperLeft;
        mainText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        mainText.supportRichText = false;
        inputField.textComponent = mainText;

        // --- The following block is removed/fixed to avoid CS1061 error ---
        // Because legacy InputField doesn't have 'verticalScrollbar' property.
        /*
        // Vertical scrollbar for the input field (older Unity InputField doesn't have autoHide)
        var inputScrollbarGO = new GameObject("InputFieldScrollbar");
        inputScrollbarGO.transform.SetParent(inputFieldContainer.transform, false);

        var sbRect = inputScrollbarGO.AddComponent<RectTransform>();
        sbRect.anchorMin = new Vector2(1, 0);
        sbRect.anchorMax = new Vector2(1, 1);
        sbRect.pivot = new Vector2(1, 0.5f);
        sbRect.offsetMin = new Vector2(-20, 0);
        sbRect.offsetMax = new Vector2(0, 0);

        var inputScrollbar = inputScrollbarGO.AddComponent<Scrollbar>();
        inputScrollbar.direction = Scrollbar.Direction.BottomToTop;

        var sbImg = inputScrollbarGO.AddComponent<Image>();
        sbImg.color = new Color(1, 1, 1, 0.1f);
        sbImg.type = Image.Type.Sliced;
        if (buttonSprite != null)
        {
            sbImg.sprite = buttonSprite;
        }

        var handleGO = new GameObject("Handle");
        handleGO.transform.SetParent(inputScrollbarGO.transform, false);

        var handleRect = handleGO.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        var handleImg = handleGO.AddComponent<Image>();
        handleImg.color = Color.white;
        handleImg.type = Image.Type.Sliced;
        if (buttonSprite != null)
        {
            handleImg.sprite = buttonSprite;
        }
        inputScrollbar.handleRect = handleRect;
        inputScrollbar.targetGraphic = handleImg;
        inputScrollbar.transition = Selectable.Transition.None;

        // This next line causes the error in older Unity InputFields
        // inputField.verticalScrollbar = inputScrollbar;
        */

        // "Send" button on right side
        var sendGO = CreateButton("Send", buttonFontSize, Color.black, 100, 60, sendButtonSprite);
        sendGO.name = "SendButton";
        sendGO.transform.SetParent(bottomBarGO.transform, false);

        var sRect = sendGO.GetComponent<RectTransform>();
        sRect.anchorMin = new Vector2(1, 0.5f);
        sRect.anchorMax = new Vector2(1, 0.5f);
        sRect.pivot = new Vector2(1, 0.5f);
        sRect.anchoredPosition = new Vector2(-10, 0);
        sendButton = sendGO.GetComponent<Button>();
        sendButton.onClick.AddListener(OnSendClicked);
    }

    private void OnUploadClicked()
    {
#if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select a file to upload", Application.dataPath, "");
        if (!string.IsNullOrEmpty(path))
        {
            selectedFilePath = path;
            selectedFileBytes = File.ReadAllBytes(path);
            selectedFileName = Path.GetFileName(path);
            UpdateFilePreviewLabel();
            Debug.Log($"[Upload] File selected: {selectedFileName}, size {selectedFileBytes.Length} bytes");
        }
        else
        {
            Debug.Log("[Upload] No file selected.");
        }
#elif UNITY_WEBGL && !UNITY_EDITOR
        // WebGL-safe upload: browser file picker → DataURL → bytes
        SpuricWebGLMediaBridge.OpenFilePicker(gameObject.name, "OnWebGLUploadSelected", "*/*");
#else
        Debug.LogWarning("[Upload] File selection is not implemented on this platform!");
#endif
    }

    // JS sends: "{name}|||{mime}|||{dataUrl}"
    public void OnWebGLUploadSelected(string payload)
    {
        try
        {
            string fileName, mime, dataUrl;
            if (!SpuricWebGLMediaBridge.TryParseFilePayload(payload, out fileName, out mime, out dataUrl))
            {
                Debug.LogWarning("[Upload] Bad payload from WebGL file picker.");
                return;
            }

            string parsedMime, base64;
            if (!SpuricWebGLMediaBridge.TryExtractBase64FromDataUrl(dataUrl, out parsedMime, out base64))
            {
                Debug.LogWarning("[Upload] Could not extract base64 from DataURL.");
                return;
            }

            selectedFilePath = null;
            selectedFileName = string.IsNullOrEmpty(fileName) ? "upload.bin" : fileName;
            selectedFileBytes = Convert.FromBase64String(base64);

            UpdateFilePreviewLabel();
            Debug.Log($"[Upload] WebGL file selected: {selectedFileName}, size {selectedFileBytes.Length} bytes");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Upload] WebGL selection failed: " + e.Message);
        }
    }


        private void OnSearchButtonClicked()
    {
        webSearchActive = !webSearchActive;
        var btnImage = webSearchButton.GetComponent<Image>();
        var textObj = webSearchButton.GetComponentsInChildren<Text>(true).FirstOrDefault();

        if (webSearchActive)
        {
            btnImage.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
            Debug.Log("[WebSearch] Activated.");
            selectedModels.Add(OpenAIModels.GPT4O_MINI_SEARCH);
        }
        else
        {
            btnImage.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
            Debug.Log("[WebSearch] Deactivated.");
            selectedModels.Remove(OpenAIModels.GPT4O_MINI_SEARCH);
        }
        UpdateModelButtonsVisual();
    }

    // Called when user hits "Send"
    private void OnSendClicked()
    {
        if (inputField == null) return;
        var userText = inputField.text.Trim();
        if (string.IsNullOrEmpty(userText)) return;

        OnUserMessageSent?.Invoke(userText);
        CreateChatBubble(userText, true);
        inputField.text = "";
        needsResizing = true;

        // If we asked for confirmation to generate an image last turn
        if (pendingImageConfirmation)
        {
            string lower = userText.ToLowerInvariant();
            bool yes = (lower.Contains("yes") || lower.Contains("sure") || lower.Contains("please") || lower == "y");
            if (yes)
            {
                if (dalle3Active) StartCoroutine(RequestOpenAIImage_1_5(pendingImagePrompt)); // repurposed toggle => GPT Image 1.5
                if (grokImageActive) StartCoroutine(RequestGrokImage(pendingImagePrompt));
                if (geminiImageActive) StartCoroutine(RequestGeminiImage(pendingImagePrompt));

                if (!dalle3Active && !grokImageActive && !geminiImageActive)
                    CreateChatBubble("[No image model is enabled. Turn on IMG 1.5 / Grok Img / Gemini Img first.]", false);
            }
            else
            {
                CreateChatBubble("[No problem, I'll just chat normally.]", false);
                foreach (var mdl in selectedModels)
                    StartCoroutine(RequestLLMResponse_NonConversation(pendingImagePrompt, mdl));
            }

            pendingImageConfirmation = false;
            pendingImagePrompt = null;
            return;
        }

        if (conversationModeActive)
        {
            StartCoroutine(ConversationModeFlow(userText));
            return;
        }

        // If user uploaded a file, treat this as vision/chat request (no image generation here)
        if (HasUserUploadedImageFile())
        {
            foreach (var model in selectedModels)
                StartCoroutine(RequestLLMResponse_NonConversation(userText, model));
            return;
        }

        if (webSearchActive)
        {
            foreach (var model in selectedModels)
                StartCoroutine(GenerateSearchQueryThenSearch_NonConversation(userText, model));
            return;
        }

        // Normal: decide if user wants an image; if so, ask confirmation for the currently enabled image model(s).
        StartCoroutine(CheckIfUserWantsImage(userText, (imageWanted) =>
        {
            bool anyImageEnabled = (dalle3Active || grokImageActive || geminiImageActive);
            if (imageWanted && anyImageEnabled)
            {
                CreateChatBubble("Generate an image using the enabled image model(s)? Type YES or NO.", false);
                pendingImageConfirmation = true;
                pendingImagePrompt = userText;
                return;
            }

            foreach (var mdl in selectedModels)
                StartCoroutine(RequestLLMResponse_NonConversation(userText, mdl));
        }));
    }

    // -----------
    // Conversation Flow
    // -----------
    private IEnumerator ConversationModeFlow(string userText)
    {
        if (HasUserUploadedImageFile())
        {
            foreach (var model in selectedModels)
            {
                yield return StartCoroutine(RequestLLMResponse_Conversation(userText, model));
}
            yield break;
        }

        if (webSearchActive)
        {
            foreach (var model in selectedModels)
            {
                yield return StartCoroutine(GenerateSearchQueryThenSearch_Conversation(userText, model));
            }
            yield break;
        }

        bool doneImageCheck = false;
        yield return StartCoroutine(CheckIfUserWantsImage(userText, (imageWanted) =>
        {
            doneImageCheck = true;
            bool anyImageEnabled = (dalle3Active || grokImageActive || geminiImageActive);
            if (imageWanted && anyImageEnabled)
            {
                CreateChatBubble("Generate an image using the enabled image model(s)? Type YES or NO.", false);
                pendingImageConfirmation = true;
                pendingImagePrompt = userText;
            }
        }));
        if (doneImageCheck && pendingImageConfirmation)
        {
            yield break;
        }

        // Round-robin among selected chat models
        foreach (var model in selectedModels)
        {
            yield return StartCoroutine(RequestLLMResponse_Conversation(userText, model));
        }
    }

    private IEnumerator RequestLLMResponse_Conversation(string userInput, string model)
    {
        yield return StartCoroutine(RequestLLMResponse(userInput, model, useConversationMode: true));
    }

    private IEnumerator GenerateSearchQueryThenSearch_Conversation(string userInput, string model)
    {
        string shortQuery = null;
        yield return StartCoroutine(RequestShortGPTSearchQuery(userInput, model, (res) => shortQuery = res));
        if (string.IsNullOrEmpty(shortQuery))
        {
            shortQuery = userInput;
        }

        string searchPrompt =
            "You have internet search capabilities. The user wants info about:\n" +
            $"\"{shortQuery}\"\nPlease provide an answer with references if needed.";

        yield return StartCoroutine(RequestLLMResponse(searchPrompt, model, useConversationMode: true));
    }

    private IEnumerator RequestLLMResponse_NonConversation(string userInput, string model)
    {
        yield return StartCoroutine(RequestLLMResponse(userInput, model, false));
    }

    private IEnumerator GenerateSearchQueryThenSearch_NonConversation(string userInput, string model)
    {
        string shortQuery = null;
        yield return StartCoroutine(RequestShortGPTSearchQuery(userInput, model, (res) => shortQuery = res));
        if (string.IsNullOrEmpty(shortQuery))
        {
            shortQuery = userInput;
        }

        string searchPrompt =
            "You have internet search capabilities. The user wants info about:\n" +
            $"\"{shortQuery}\"\nPlease provide an answer with references if needed.";

        yield return StartCoroutine(RequestLLMResponse(searchPrompt, model, false));
    }

    // Actual method that sends request to LLM
    private IEnumerator RequestLLMResponse(string userInput, string modelForRequest, bool useConversationMode)
    {
        // pick which session
        ChatSession sessionToUse;
        if (useConversationMode)
        {
            sessionToUse = currentSession;
            sessionToUse.messages.Add(new ChatBubbleData { isUser = true, text = userInput });
        }
        else
        {
            if (!subSessions.ContainsKey(modelForRequest))
            {
                subSessions[modelForRequest] = new ChatSession
                {
                    sessionName = "SubSession:" + modelForRequest,
                    chosenModels = new List<string>() { modelForRequest },
                    messages = new List<ChatBubbleData>()
                };
            }
            sessionToUse = subSessions[modelForRequest];
            sessionToUse.messages.Add(new ChatBubbleData { isUser = true, text = userInput });
        }

        var finalMessages = BuildVisionContextFromSession(sessionToUse);

        // Clear selected file
        selectedFilePath = null;
        selectedFileBytes = null;
        selectedFileName = null;
        UpdateFilePreviewLabel();

        // Allow a single automatic fallback for Gemini 3 Pro (paid / tier-gated in many accounts)
        string requestedModel = modelForRequest;
        string effectiveModel = modelForRequest;
        bool triedGeminiProFallback = false;

        while (true)
        {
            string baseUrl = GetBaseUrlForModel(effectiveModel);
            string usedApiKey = GetApiKeyForModel(effectiveModel);

            bool useResponses = IsOpenAIResponsesModel(effectiveModel);
            string apiUrl;
            string jsonBody;

            if (useResponses)
            {
                var req = new OpenAIResponsesRequest
                {
                    model = effectiveModel,
                    input = ConvertVisionMessagesToResponsesInput(finalMessages.ToArray()),
                    max_output_tokens = maxTokensForVision,
                    store = false
                };
                jsonBody = JsonUtility.ToJson(req);
                apiUrl = baseUrl + "/responses";
            }
            else
            {
                var requestData = new ChatRequestVision
                {
                    model = effectiveModel,
                    messages = finalMessages.ToArray(),
                    max_tokens = maxTokensForVision
                };
                jsonBody = JsonUtility.ToJson(requestData);
                apiUrl = baseUrl + "/chat/completions";
            }

            Debug.Log($"[LLM] Sending to {apiUrl} with model={effectiveModel}");
            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
                request.timeout = 120;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    string errBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                    Debug.LogWarning($"[LLM] Error ({request.responseCode}) => {request.error} | Body => {errBody}");
                    string parsed = TryExtractOpenAIErrorMessage(errBody);

                    // Gemini 3 Pro is often tier/billing gated. If it fails, auto-fallback to Gemini 3 Flash once.
                    if (!triedGeminiProFallback &&
                        requestedModel == OpenAIModels.GEMINI_3_PRO_PREVIEW &&
                        effectiveModel == requestedModel)
                    {
                        string lcBody = (errBody ?? "").ToLowerInvariant();
                        string lcParsed = (parsed ?? "").ToLowerInvariant();
                        bool looksLikeTierOrModelIssue =
                            (request.responseCode == 400 || request.responseCode == 403 || request.responseCode == 404) &&
                            (lcBody.Contains("free") || lcBody.Contains("billing") || lcBody.Contains("payment") || lcBody.Contains("tier") ||
                             lcBody.Contains("permission") || lcBody.Contains("not authorized") || lcBody.Contains("not found") || lcBody.Contains("model")) ||
                            (lcParsed.Contains("free") || lcParsed.Contains("billing") || lcParsed.Contains("tier") ||
                             lcParsed.Contains("permission") || lcParsed.Contains("not found") || lcParsed.Contains("model"));

                        if (looksLikeTierOrModelIssue)
                        {
                            triedGeminiProFallback = true;
                            effectiveModel = OpenAIModels.GEMINI_3_FLASH_PREVIEW;
                            CreateChatBubble($"[Gemini 3 Pro unavailable. Falling back to {GetShortModelName(effectiveModel)}.]", false);
                            continue; // retry loop with fallback model
                        }
                    }

                    CreateChatBubble(!string.IsNullOrEmpty(parsed) ? parsed : $"Request failed ({request.responseCode}). Please try again.", false);
                    yield break;
                }
                else
                {
                    string resp = request.downloadHandler.text;
                    Debug.Log($"[LLM] Response => {resp}");

                    string assistantText = null;

                    if (useResponses)
                    {
                        assistantText = ExtractTextFromResponses(resp);
                    }
                    else
                    {
                        var data = JsonUtility.FromJson<ChatResponse>(resp);
                        if (data != null && data.choices != null && data.choices.Length > 0)
                            assistantText = data.choices[0].message.content;
                    }

                    if (!string.IsNullOrEmpty(assistantText))
                    {
                        // check repetition
                        bool wasRepetitive = CheckRepetitiveResponse(effectiveModel, assistantText);
                        if (wasRepetitive)
                        {
                            yield return StartCoroutine(RerollResponse(
                                effectiveModel,
                                sessionToUse,
                                "Please avoid repeating the same sentences or reusing identical phrasing. Provide fresh content.",
                                (rerolledText) =>
                                {
                                    if (!string.IsNullOrEmpty(rerolledText))
                                    {
                                        assistantText = rerolledText;
                                    }
                                }
                            ));
                        }

                        string modelLabel = GetShortModelName(requestedModel);
                        if (effectiveModel != requestedModel)
                            modelLabel = $"{GetShortModelName(requestedModel)} (using {GetShortModelName(effectiveModel)})";

                        string finalText = $"{modelLabel} {assistantText}";
                        CreateChatBubble(finalText, false);

                        // store in session
                        sessionToUse.messages.Add(new ChatBubbleData { isUser = false, text = finalText });
                        StoreRecentResponse(effectiveModel, assistantText);
                    }
                    else
                    {
                        CreateChatBubble($"Something went wrong. (Unable to parse {GetShortModelName(effectiveModel)} response.)", false);
                    }

                    yield break;
                }
            }
        }
    }

    // Track last 1-2 responses
    private void StoreRecentResponse(string modelId, string responseText)
    {
        if (!recentModelResponses.ContainsKey(modelId))
        {
            recentModelResponses[modelId] = new Queue<string>();
        }
        recentModelResponses[modelId].Enqueue(responseText);
        if (recentModelResponses[modelId].Count > 2)
        {
            recentModelResponses[modelId].Dequeue();
        }
    }

    private bool CheckRepetitiveResponse(string modelId, string newText)
    {
        if (!recentModelResponses.ContainsKey(modelId)) return false;

        foreach (var oldText in recentModelResponses[modelId])
        {
            if (IsTooSimilar(oldText, newText))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsTooSimilar(string oldText, string newText)
    {
        if (string.IsNullOrEmpty(oldText) || string.IsNullOrEmpty(newText)) return false;
        if (newText.Length < 40) return false;
        if (oldText.Length >= 50 && newText.Contains(oldText.Substring(0, 50)))
        {
            return true;
        }
        return false;
    }

    private IEnumerator RerollResponse(
        string modelId,
        ChatSession sessionToUse,
        string antiRepeatSystemNote,
        Action<string> onComplete)
    {
        var finalMessages = BuildVisionContextFromSession(sessionToUse).ToList();
        finalMessages.Add(new ChatMessageVision
        {
            role = "system",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent { type = "text", text = antiRepeatSystemNote }
            }
        });
        string baseUrl = GetBaseUrlForModel(modelId);
        string usedApiKey = GetApiKeyForModel(modelId);

        bool useResponses = IsOpenAIResponsesModel(modelId);
        string apiUrl;
        string jsonBody;

        if (useResponses)
        {
            var req = new OpenAIResponsesRequest
            {
                model = modelId,
                input = ConvertVisionMessagesToResponsesInput(finalMessages.ToArray()),
                max_output_tokens = maxTokensForVision,
                store = false
            };
            jsonBody = JsonUtility.ToJson(req);
            apiUrl = baseUrl + "/responses";
        }
        else
        {
            var requestData = new ChatRequestVision
            {
                model = modelId,
                messages = finalMessages.ToArray(),
                max_tokens = maxTokensForVision
            };
            jsonBody = JsonUtility.ToJson(requestData);
            apiUrl = baseUrl + "/chat/completions";
        }

Debug.Log($"[LLM] Re-roll => {apiUrl} model={modelId}");
        using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
            request.timeout = 120;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[Reroll] Error ({request.responseCode}) => {request.error} | Body => {errBody}");
                onComplete(null);
            }
            else
            {
                string resp = request.downloadHandler.text;
                Debug.Log($"[Reroll] Response => {resp}");
                string rerolledText = null;

                if (useResponses)
                {
                    rerolledText = ExtractTextFromResponses(resp);
                }
                else
                {
                    var data = JsonUtility.FromJson<ChatResponse>(resp);
                    if (data != null && data.choices != null && data.choices.Length > 0)
                        rerolledText = data.choices[0].message.content;
                }

                if (!string.IsNullOrEmpty(rerolledText))
                    onComplete(rerolledText);
                else
                    onComplete(null);
            }
        }
    }

    // Build conversation
    private List<ChatMessageVision> BuildVisionContextFromSession(ChatSession session)
    {
        var finalMessages = new List<ChatMessageVision>();

        if (!string.IsNullOrEmpty(globalSuperPrompt))
        {
            finalMessages.Add(new ChatMessageVision
            {
                role = "system",
                content = new VisionMessageContent[]
                {
                    new VisionMessageContent { type = "text", text = globalSuperPrompt }
                }
            });
        }

        if (conversationModeActive && !string.IsNullOrEmpty(conversationModePrompt))
        {
            finalMessages.Add(new ChatMessageVision
            {
                role = "system",
                content = new VisionMessageContent[]
                {
                    new VisionMessageContent { type = "text", text = conversationModePrompt }
                }
            });
        }

        if (session != null)
        {
            foreach (var bubble in session.messages)
            {
                var blocks = new List<VisionMessageContent>();
                blocks.Add(new VisionMessageContent { type = "text", text = bubble.text });

                string roleStr = bubble.isUser ? "user" : "assistant";
                finalMessages.Add(new ChatMessageVision
                {
                    role = roleStr,
                    content = blocks.ToArray()
                });
            }
        }
        return finalMessages;
    }

    // Checking if the user has an uploaded image
    private bool HasUserUploadedImageFile()
    {
        if (selectedFileBytes == null || selectedFileBytes.Length == 0) return false;
        if (string.IsNullOrEmpty(selectedFileName)) return false;
        string extension = Path.GetExtension(selectedFileName).ToLower();
        return extensionToMime.ContainsKey(extension) &&
               extensionToMime[extension].StartsWith("image/");
    }

    // Convert base64 to Texture2D
    private Texture2D DecodeBase64ToTexture(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        byte[] imageBytes = Convert.FromBase64String(base64);
        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(imageBytes))
        {
            Debug.LogWarning("DecodeBase64ToTexture: Failed to load image from base64");
            return null;
        }
        return tex;
    }

    private Texture2D DownscaleTexture(Texture2D source, int maxSize)
    {
        int width = source.width;
        int height = source.height;
        if (width <= maxSize && height <= maxSize)
        {
            return source;
        }

        float ratio = (float)width / height;
        int newW = width, newH = height;
        if (width >= height)
        {
            newW = maxSize;
            newH = Mathf.RoundToInt((float)maxSize / ratio);
        }
        else
        {
            newH = maxSize;
            newW = Mathf.RoundToInt((float)maxSize * ratio);
        }

        Texture2D result = new Texture2D(newW, newH, TextureFormat.RGBA32, false);
        Color[] pixels = result.GetPixels(0);
        float incX = 1.0f / newW;
        float incY = 1.0f / newH;
        for (int px = 0; px < pixels.Length; px++)
        {
            int x = px % newW;
            int y = px / newW;
            float u = x * incX;
            float v = y * incY;
            pixels[px] = source.GetPixelBilinear(u, v);
        }
        result.SetPixels(pixels);
        result.Apply();
        Debug.Log("[Downscale] Resized large image before embedding.");
        return result;
    }

    // Creates the row of model buttons in the top-right
    private void CreateTopRightModelButtons()
    {
        modelButtonsOverlayGO = new GameObject("ModelButtonsOverlay");
        modelButtonsOverlayGO.transform.SetParent(targetCanvas.transform, false);

        var overlayRect = modelButtonsOverlayGO.AddComponent<RectTransform>();
        overlayRect.anchorMin = new Vector2(1, 1);
        overlayRect.anchorMax = new Vector2(1, 1);
        overlayRect.pivot = new Vector2(1, 1);
        overlayRect.anchoredPosition = new Vector2(-10, -10);

        topRightModelPanel = new GameObject("TopRightModelPanel");
        topRightModelPanel.transform.SetParent(modelButtonsOverlayGO.transform, false);

        var panelRect = topRightModelPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(740, 390);

        var panelImg = topRightModelPanel.AddComponent<Image>();
        panelImg.color = new Color(0, 0, 0, 0.85f);

        var vLayout = topRightModelPanel.AddComponent<VerticalLayoutGroup>();
        vLayout.childAlignment = TextAnchor.UpperRight;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = false;
        vLayout.childForceExpandWidth = false;
        vLayout.childForceExpandHeight = false;
        vLayout.spacing = 6f;
        vLayout.padding = new RectOffset(8, 8, 8, 8);

        // Helper: create a row container
        GameObject CreateRow(string name)
        {
            var row = new GameObject(name);
            row.transform.SetParent(topRightModelPanel.transform, false);
            var rowRect = row.AddComponent<RectTransform>();
            rowRect.sizeDelta = new Vector2(0, 52);

            var h = row.AddComponent<HorizontalLayoutGroup>();
            h.childAlignment = TextAnchor.MiddleRight;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.spacing = 6f;
            h.padding = new RectOffset(0, 0, 0, 0);
            return row;
        }

        // -------------------------
        // Row 1: OpenAI chat models
        // -------------------------
        var row1 = CreateRow("Row_OpenAI_Chat");
        CreateModelButton(row1.transform, OpenAIModels.GPT_5_2, "GPT 5.2");
        CreateModelButton(row1.transform, OpenAIModels.GPT_5_MINI, "GPT 5 mini");
        CreateModelButton(row1.transform, OpenAIModels.GPT_5_NANO, "GPT 5 nano");

        // -------------------------
        // Row 2: Grok chat models
        // -------------------------
        var row2 = CreateRow("Row_Grok_Chat");
        CreateModelButton(row2.transform, OpenAIModels.GROK_4_1_FAST_REASONING, "Grok 4.1 R");
        CreateModelButton(row2.transform, OpenAIModels.GROK_4_1_FAST_NON_REASONING, "Grok 4.1 NR");
        CreateModelButton(row2.transform, OpenAIModels.GROK_3_MINI, "Grok 3 mini");

        // -------------------------
        // Row 3: Gemini chat models
        // -------------------------
        var row3 = CreateRow("Row_Gemini_Chat");
        CreateModelButton(row3.transform, OpenAIModels.GEMINI_3_PRO_PREVIEW, "Gemini 3 Pro");
        CreateModelButton(row3.transform, OpenAIModels.GEMINI_3_FLASH_PREVIEW, "Gemini 3 Flash");
        CreateModelButton(row3.transform, OpenAIModels.GEMINI_25_FLASH_LITE, "Gemini 2.5 Lite");

        // -------------------------
        // Row 4: Image toggles (base64 only; no URL downloads)
        // -------------------------
        var row4 = CreateRow("Row_Image_Toggles");

        var img1GO = CreateButton("IMG 1.5", buttonFontSize, Color.black, 130, 44, dalle3ButtonSprite);
        img1GO.transform.SetParent(row4.transform, false);
        btnDalle3 = img1GO.GetComponent<Button>();
        btnDalle3.onClick.RemoveAllListeners();
        btnDalle3.onClick.AddListener(OnDalle3ButtonClicked); // repurposed as OpenAI Image 1.5
        UpdateDalle3ButtonVisual();

        var grokImgGO = CreateButton("Grok Img", buttonFontSize, Color.black, 130, 44, modelFullButtonSprite);
        grokImgGO.transform.SetParent(row4.transform, false);
        btnGrokImage = grokImgGO.GetComponent<Button>();
        btnGrokImage.onClick.RemoveAllListeners();
        btnGrokImage.onClick.AddListener(OnGrokImageButtonClicked);
        UpdateGrokImageButtonVisual();

        var gemImgGO = CreateButton("Gemini Img", buttonFontSize, Color.black, 140, 44, modelFullButtonSprite);
        gemImgGO.transform.SetParent(row4.transform, false);
        btnGeminiImage = gemImgGO.GetComponent<Button>();
        btnGeminiImage.onClick.RemoveAllListeners();
        btnGeminiImage.onClick.AddListener(OnGeminiImageButtonClicked);
        UpdateGeminiImageButtonVisual();

        // -------------------------
        // Row 5: Mode buttons (restored)
        // -------------------------
        var row5 = CreateRow("Row_Mode");
        var convGO = CreateButton("Conversation Mode", buttonFontSize, Color.black, 200, 44, modelFullButtonSprite);
        convGO.transform.SetParent(row5.transform, false);
        conversationModeButton = convGO.GetComponent<Button>();
        conversationModeButton.onClick.RemoveAllListeners();
        conversationModeButton.onClick.AddListener(OnConversationModeButtonClicked);
        UpdateConversationModeButtonVisual();

        // -------------------------
        // Row 6: LLM-Conversation controls (restored)
        // -------------------------
        var row6 = CreateRow("Row_LLM_Conversation");
        var llmConvGO = CreateButton("LLM Conv", buttonFontSize, Color.black, 140, 44, modelFullButtonSprite);
        llmConvGO.transform.SetParent(row6.transform, false);
        llmConvToggleButton = llmConvGO.GetComponent<Button>();
        llmConvToggleButton.onClick.RemoveAllListeners();
        llmConvToggleButton.onClick.AddListener(OnLLMConversationToggleClicked);
        UpdateLLMConversationToggleVisual();

        var initGO = CreateButton("Initiate", buttonFontSize, Color.black, 120, 44, modelFullButtonSprite);
        initGO.transform.SetParent(row6.transform, false);
        initiateConversationButton = initGO.GetComponent<Button>();
        initiateConversationButton.onClick.RemoveAllListeners();
        initiateConversationButton.onClick.AddListener(OnInitiateConversationClicked);

        var stopGO = CreateButton("Stop", buttonFontSize, Color.black, 100, 44, modelFullButtonSprite);
        stopGO.transform.SetParent(row6.transform, false);
        stopConversationButton = stopGO.GetComponent<Button>();
        stopConversationButton.onClick.RemoveAllListeners();
        stopConversationButton.onClick.AddListener(OnStopConversationClicked);

        // Start hidden unless toggled by user (existing behavior)
        modelButtonsOverlayGO.SetActive(showModelButtonsPanel);
    }

    private void OnConversationModeButtonClicked()
    {
        conversationModeActive = !conversationModeActive;
        UpdateConversationModeButtonVisual();
        Debug.Log($"[ConversationMode] => {conversationModeActive}");
    }

    private void UpdateConversationModeButtonVisual()
    {
        if (conversationModeButton == null) return;
        var btnImage = conversationModeButton.GetComponent<Image>();
        var textObj = conversationModeButton.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (conversationModeActive)
        {
            btnImage.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
        }
        else
        {
            btnImage.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
        }
    }

    private void UpdateLLMConversationToggleVisual()
    {
        if (llmConvToggleButton == null) return;
        var img = llmConvToggleButton.GetComponent<Image>();
        var textObj = llmConvToggleButton.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (llmConversationModeActive)
        {
            img.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
        }
        else
        {
            img.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
        }
    }


    // ------------------------------------------------------------
    // OpenAI Responses API helpers (required for gpt-5 / o-series)
    // ------------------------------------------------------------
    [System.Serializable]
    private class OpenAIResponsesRequest
    {
        public string model;
        public ResponseInputMessage[] input;
        public int max_output_tokens;
        public bool store = false;
    }

    [System.Serializable]
    private class ResponseInputMessage
    {
        public string type = "message";
        public string role;
        public string content;
    }

    [System.Serializable]
    private class OpenAIResponsesResponse
    {
        public ResponseOutputItem[] output;
    }

    [System.Serializable]
    private class ResponseOutputItem
    {
        public string type;
        public string role;
        public ResponseOutputContent[] content;
    }

    [System.Serializable]
    private class ResponseOutputContent
    {
        public string type;
        public string text;
    }

    [System.Serializable]
    private class OpenAIErrorEnvelope
    {
        public OpenAIError error;
    }

    [System.Serializable]
    private class OpenAIError
    {
        public string message;
        public string type;
        public string code;
    }

    private bool IsOpenAIResponsesModel(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return false;
        string baseUrl = GetBaseUrlForModel(modelId);
        if (string.IsNullOrEmpty(baseUrl)) return false;

        // Only OpenAI's platform uses the Responses API for gpt-5 / o-series.
        if (!baseUrl.Contains("api.openai.com")) return false;

        string m = modelId.Trim().ToLowerInvariant();
        return m.StartsWith("gpt-") || m.StartsWith("o");
    }

    private ResponseInputMessage[] ConvertVisionMessagesToResponsesInput(ChatMessageVision[] msgs)
    {
        if (msgs == null) return new ResponseInputMessage[0];
        List<ResponseInputMessage> outMsgs = new List<ResponseInputMessage>(msgs.Length);
        for (int i = 0; i < msgs.Length; i++)
        {
            var msg = msgs[i];
            if (msg == null) continue;
            outMsgs.Add(new ResponseInputMessage
            {
                role = msg.role,
                content = FlattenVisionContent(msg.content)
            });
        }
        return outMsgs.ToArray();
    }

    private string FlattenVisionContent(VisionMessageContent[] parts)
    {
        if (parts == null || parts.Length == 0) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p == null) continue;
            if (string.Equals(p.type, "text", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(p.text))
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(p.text);
            }
        }
        return sb.ToString();
    }

    private string ExtractTextFromResponses(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        try
        {
            var parsed = JsonUtility.FromJson<OpenAIResponsesResponse>(json);
            if (parsed != null && parsed.output != null)
            {
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < parsed.output.Length; i++)
                {
                    var item = parsed.output[i];
                    if (item == null || item.content == null) continue;
                    for (int j = 0; j < item.content.Length; j++)
                    {
                        var c = item.content[j];
                        if (c == null) continue;
                        if (string.Equals(c.type, "output_text", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(c.text))
                        {
                            if (sb.Length > 0) sb.Append("\n");
                            sb.Append(c.text);
                        }
                    }
                }
                return sb.ToString();
            }
        }
        catch { }
        return "";
    }

    private string TryExtractOpenAIErrorMessage(string body)
    {
        if (string.IsNullOrEmpty(body)) return "";
        try
        {
            var env = JsonUtility.FromJson<OpenAIErrorEnvelope>(body);
            if (env != null && env.error != null && !string.IsNullOrEmpty(env.error.message))
                return env.error.message;
        }
        catch { }
        return body;
    }


    private void CreateModelButton(Transform parent, string modelId, string displayName)
    {
        var btnGO = CreateButton(displayName, buttonFontSize - 4, Color.black, 120, 50);
        btnGO.transform.SetParent(parent, false);

        var bBtn = btnGO.GetComponent<Button>();
        string idForClosure = modelId;
        bBtn.onClick.AddListener(() => ToggleModelSelection(idForClosure));
        allModelButtons[idForClosure] = bBtn;
    }

    private void ToggleModelSelection(string modelId)
    {
        if (selectedModels.Contains(modelId))
        {
            selectedModels.Remove(modelId);
        }
        else
        {
            selectedModels.Add(modelId);
            if (!conversationModeActive)
            {
                if (!subSessions.ContainsKey(modelId))
                {
                    subSessions[modelId] = new ChatSession
                    {
                        sessionName = "SubSession:" + modelId,
                        chosenModels = new List<string>() { modelId },
                        messages = new List<ChatBubbleData>()
                    };
                }
            }
        }
        if (currentSession != null)
        {
            currentSession.chosenModels = new List<string>(selectedModels);
        }

        if (llmConversationModeActive)
        {
            llmConversationSettings.RemoveAll(s => !selectedModels.Contains(s.modelId));
            foreach (var selId in selectedModels)
            {
                if (!llmConversationSettings.Any(s => s.modelId == selId))
                {
                    llmConversationSettings.Add(new LLMConversationSetting
                    {
                        modelId = selId,
                        conversationPrompt = "Default prompt for " + selId
                    });
                }
            }
        }

        UpdateModelButtonsVisual();
    }

    private void UpdateModelButtonsVisual()
    {
        foreach (var kvp in allModelButtons)
        {
            var btn = kvp.Value;
            var img = btn.GetComponent<Image>();
            var txt = btn.GetComponentInChildren<Text>();
            bool isSelected = selectedModels.Contains(kvp.Key);
            if (isSelected)
            {
                img.color = Color.white;
                if (txt != null) txt.color = Color.black;
            }
            else
            {
                img.color = Color.black;
                if (txt != null) txt.color = Color.white;
            }
        }
    }

    private void OnDalle3ButtonClicked()
    {
        dalle3Active = !dalle3Active;
        Debug.Log(dalle3Active ? "[GPT Image 1.5] Activated." : "[GPT Image 1.5] Deactivated.");
        UpdateDalle3ButtonVisual();
    }

    private void UpdateDalle3ButtonVisual()
    {
        if (btnDalle3 == null) return;
        var btnImage = btnDalle3.GetComponent<Image>();
        var textObj = btnDalle3.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (dalle3Active)
        {
            btnImage.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
        }
        else
        {
            btnImage.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
        }
    }

    private void OnGrokImageButtonClicked()
    {
        grokImageActive = !grokImageActive;
        Debug.Log(grokImageActive ? $"[{OpenAIModels.GROK_2_IMAGE_1212}] Activated." : $"[{OpenAIModels.GROK_2_IMAGE_1212}] Deactivated.");

        UpdateGrokImageButtonVisual();
    }

    private void UpdateGrokImageButtonVisual()
    {
        if (btnGrokImage == null) return;
        var btnImage = btnGrokImage.GetComponent<Image>();
        var textObj = btnGrokImage.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (grokImageActive)
        {
            btnImage.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
        }
        else
        {
            btnImage.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
        }
    }
    private void OnGeminiImageButtonClicked()
    {
        geminiImageActive = !geminiImageActive;
        Debug.Log(geminiImageActive ? $"[{OpenAIModels.GEMINI_3_PRO_IMAGE_PREVIEW}] Activated." : $"[{OpenAIModels.GEMINI_3_PRO_IMAGE_PREVIEW}] Deactivated.");

        UpdateGeminiImageButtonVisual();
    }

    private void UpdateGeminiImageButtonVisual()
    {
        if (btnGeminiImage == null) return;
        var btnImage = btnGeminiImage.GetComponent<Image>();
        var textObj = btnGeminiImage.GetComponentsInChildren<Text>(true).FirstOrDefault();
        if (geminiImageActive)
        {
            btnImage.color = Color.white;
            if (textObj != null) textObj.color = Color.black;
        }
        else
        {
            btnImage.color = Color.black;
            if (textObj != null) textObj.color = Color.white;
        }
    }


    // Minimal request object for grok-2 image
    [System.Serializable]
    private class XAiImageRequest
    {
        public string prompt;
        public string model;
        public int n;
        public string response_format;
    }

    private IEnumerator RequestGrokImage(string userText)
    {
        CreateChatBubble($"[Generating an image with {GetShortModelName(OpenAIModels.GROK_2_IMAGE_1212)} for: {userText}]", false);
        Debug.Log($"[GROK ImageGen] prompt => {userText}");

        string baseUrl = GetBaseUrlForModel(OpenAIModels.GROK_2_IMAGE_1212);
        string usedApiKey = GetApiKeyForModel(OpenAIModels.GROK_2_IMAGE_1212);

        var imageReqData = new XAiImageRequest
        {
            prompt = userText,
            model = OpenAIModels.GROK_2_IMAGE_1212,
            n = 1,
            response_format = "b64_json"
        };

        string jsonBody = JsonUtility.ToJson(imageReqData);
        string apiUrl = baseUrl + "/images/generations";

        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
                request.timeout = 90;

                yield return request.SendWebRequest();

                bool ok = (request.result == UnityWebRequest.Result.Success);
                long code = request.responseCode;

                if (!ok)
                {
                    bool shouldRetry = (code == 429 || code == 500 || code == 502 || code == 503 || code == 504);
                    Debug.LogWarning($"[GrokImage] Error => {request.error} (HTTP {code}) attempt {attempt + 1}/{maxAttempts}");
                    if (shouldRetry && attempt < maxAttempts - 1)
                    {
                        float wait = Mathf.Pow(2f, attempt); // 1s, 2s, 4s
                        yield return new WaitForSeconds(wait);
                        continue;
                    }
                    CreateChatBubble("Something went wrong generating a Grok image. Check your xAI API key and try again.", false);
                    yield break;
                }

                string resp = request.downloadHandler.text;
                Debug.Log($"[GrokImage] Resp => {resp}");
                var data = JsonUtility.FromJson<DalleImageResponse>(resp);
                if (data != null && data.data != null && data.data.Length > 0)
                {
                    string b64 = data.data[0].b64_json;
                    if (!string.IsNullOrEmpty(b64))
                    {
                        Texture2D tex = DecodeBase64ToTexture(b64);
                        if (tex != null) CreateChatBubble(userText, false, false, tex);
                        else CreateChatBubble("[Error => Failed to decode Grok image]", false);
                    }
                    else
                    {
                        CreateChatBubble("[Error => Grok image response had no base64]", false);
                    }
                }
                else
                {
                    CreateChatBubble("[Error => no Grok image returned]", false);
                }
                yield break;
            }
        }
    }


    [System.Serializable]
    private class GeminiImageRequest
    {
        public string prompt;
        public string model;
        public string size;
        public int n;
        public string response_format;
    }

    private IEnumerator RequestGeminiImage(string userText)
    {
        // Try Gemini 3 Pro Image first; fall back to Gemini Flash Image if the account/tier/model doesn't allow it.
        string requestedModel = OpenAIModels.GEMINI_3_PRO_IMAGE_PREVIEW;
        string modelToUse = requestedModel;
        bool triedFallback = false;

        CreateChatBubble($"[Generating an image with {GetShortModelName(modelToUse)} for: {userText}]", false);
        Debug.Log($"[Gemini ImageGen] prompt => {userText}");

        const int maxAttempts = 3;

        while (true)
        {
            string baseUrl = GetBaseUrlForModel(modelToUse);
            string usedApiKey = GetApiKeyForModel(modelToUse);
            string apiUrl = baseUrl + "/images/generations";

            var imageReqData = new GeminiImageRequest
            {
                prompt = userText,
                model = modelToUse,
                // Some providers in the OpenAI-compat layer may not accept size; omit it to maximize compatibility.
                size = null,
                n = 1,
                response_format = "b64_json"
            };

            string jsonBody = JsonUtility.ToJson(imageReqData);

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                using (var request = new UnityWebRequest(apiUrl, "POST"))
                {
                    byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
                    request.timeout = 90;

                    yield return request.SendWebRequest();

                    bool ok = (request.result == UnityWebRequest.Result.Success);
                    long httpCode = request.responseCode;

                    if (!ok)
                    {
                        string errBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                        string parsed = TryExtractOpenAIErrorMessage(errBody);

                        bool shouldRetry = (httpCode == 429 || httpCode == 500 || httpCode == 502 || httpCode == 503 || httpCode == 504);
                        Debug.LogWarning($"[GeminiImage] Error => {request.error} (HTTP {httpCode}) attempt {attempt + 1}/{maxAttempts} | Body => {errBody}");

                        if (shouldRetry && attempt < maxAttempts - 1)
                        {
                            float wait = Mathf.Pow(2f, attempt); // 1s, 2s, 4s
                            yield return new WaitForSeconds(wait);
                            continue;
                        }

                        // If Gemini 3 Pro Image isn't available (often billing/tier/model availability), fall back.
                        string lcBody = (errBody ?? "").ToLowerInvariant();
                        string lcParsed = (parsed ?? "").ToLowerInvariant();
                        bool looksLikeTierOrModelIssue =
                            (httpCode == 400 || httpCode == 403 || httpCode == 404) &&
                            (lcBody.Contains("free") || lcBody.Contains("billing") || lcBody.Contains("payment") || lcBody.Contains("tier") ||
                             lcBody.Contains("permission") || lcBody.Contains("not authorized") || lcBody.Contains("not found") || lcBody.Contains("model")) ||
                            (lcParsed.Contains("free") || lcParsed.Contains("billing") || lcParsed.Contains("tier") ||
                             lcParsed.Contains("permission") || lcParsed.Contains("not found") || lcParsed.Contains("model"));

                        if (!triedFallback && modelToUse == OpenAIModels.GEMINI_3_PRO_IMAGE_PREVIEW && looksLikeTierOrModelIssue)
                        {
                            triedFallback = true;
                            modelToUse = OpenAIModels.GEMINI_25_FLASH_IMAGE;
                            CreateChatBubble($"[Gemini 3 Pro Image unavailable. Falling back to {GetShortModelName(modelToUse)}.]", false);
                            // Restart outer while with the fallback model.
                            break;
                        }

                        CreateChatBubble(!string.IsNullOrEmpty(parsed) ? parsed : "Something went wrong generating a Gemini image. Check your Gemini API key and try again.", false);
                        yield break;
                    }

                    string resp = request.downloadHandler.text;
                    Debug.Log($"[GeminiImage] Resp => {resp}");
                    var data = JsonUtility.FromJson<DalleImageResponse>(resp);
                    if (data != null && data.data != null && data.data.Length > 0)
                    {
                        string b64 = data.data[0].b64_json;
                        if (!string.IsNullOrEmpty(b64))
                        {
                            Texture2D tex = DecodeBase64ToTexture(b64);
                            if (tex != null) CreateChatBubble(userText, false, false, tex);
                            else CreateChatBubble("[Error => Failed to decode Gemini image]", false);
                        }
                        else
                        {
                            CreateChatBubble("[Error => Gemini image response had no base64]", false);
                        }
                    }
                    else
                    {
                        CreateChatBubble("[Error => no Gemini image returned]", false);
                    }
                    yield break;
                }
            }

            // If we got here, it was because we broke out to retry with fallback.
            if (triedFallback && modelToUse == OpenAIModels.GEMINI_25_FLASH_IMAGE)
            {
                continue;
            }

            // Safety
            yield break;
        }
    }
    private IEnumerator DownloadAndEmbedImage(string url, string refinedPrompt)
    {
        Debug.Log($"[ImageGen] Downloading => {url}");
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(url))
        {
            www.timeout = 60;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[ImageGen] Download error => " + www.error);
                CreateChatBubble("[Error => Failed to download image]", false);
            }
            else
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(www);
                CreateChatBubble(refinedPrompt, false, false, tex);
            }
        }
    }

    // OpenAI image request (GPT Image 1.5) — base64 only (no URL downloads)
    [System.Serializable]
    private class OpenAIImageRequest
    {
        public string prompt;
        public string model;
        public string size;
        public string quality;
        public int n;
        public string response_format;
    }

    private IEnumerator RequestOpenAIImage_1_5(string originalUserText)
    {
        CreateChatBubble($"[Generating an image with {GetShortModelName(OpenAIModels.GPT_IMAGE_1_5)} for: {originalUserText}]", false);

        string baseUrl = "https://api.openai.com/v1";
        string usedApiKey = openAiApiKey;

        var imageReqData = new OpenAIImageRequest
        {
            prompt = originalUserText,
            model = OpenAIModels.GPT_IMAGE_1_5,
            size = "1024x1024",
            quality = "auto",
            n = 1,
            response_format = "b64_json"
        };

        string jsonBody = JsonUtility.ToJson(imageReqData);
        string apiUrl = baseUrl + "/images/generations";

        const int maxAttempts = 3;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            using (var request = new UnityWebRequest(apiUrl, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
                request.timeout = 90;

                yield return request.SendWebRequest();

                bool ok = (request.result == UnityWebRequest.Result.Success);
                long code = request.responseCode;

                if (!ok)
                {
                    bool shouldRetry = (code == 429 || code == 500 || code == 502 || code == 503 || code == 504);
                    Debug.LogWarning($"[GPT-Image] Error => {request.error} (HTTP {code}) attempt {attempt + 1}/{maxAttempts}");
                    if (shouldRetry && attempt < maxAttempts - 1)
                    {
                        float wait = Mathf.Pow(2f, attempt); // 1s, 2s, 4s
                        yield return new WaitForSeconds(wait);
                        continue;
                    }

                    CreateChatBubble("Something went wrong generating an image. Please try again or check your OpenAI API Key.", false);
                    yield break;
                }

                string resp = request.downloadHandler.text;
                Debug.Log("[GPT-Image] Resp => " + resp);
                var data = JsonUtility.FromJson<DalleImageResponse>(resp);
                if (data != null && data.data != null && data.data.Length > 0)
                {
                    // For GPT image models, base64 is returned by default.
                    string b64 = data.data[0].b64_json;
                    if (!string.IsNullOrEmpty(b64))
                    {
                        Texture2D tex = DecodeBase64ToTexture(b64);
                        if (tex != null) CreateChatBubble(originalUserText, false, false, tex);
                        else CreateChatBubble("[Error => Failed to decode image]", false);
                    }
                    else
                    {
                        CreateChatBubble("[Error => Image response had no base64 data]", false);
                    }
                }
                else
                {
                    CreateChatBubble("[Error => no image returned]", false);
                }

                yield break;
            }
        }
    }

    // Additional fade in
    private void OnEnable()
    {
        OnBubbleCreated += HandleBubbleCreatedEnhancements;
    }

    private void OnDisable()
    {
        OnBubbleCreated -= HandleBubbleCreatedEnhancements;
    }

    private void HandleBubbleCreatedEnhancements(GameObject bubbleGO, bool isUser, string text, Texture2D image)
    {
        if (!isUser && fadeInAssistantBubbles)
        {
            var cg = bubbleGO.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            StartCoroutine(FadeInBubble(cg));
        }
    }

    private IEnumerator FadeInBubble(CanvasGroup cg)
    {
        float t = 0f;
        while (t < fadeInDuration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Clamp01(t / fadeInDuration);
            yield return null;
        }
        cg.alpha = 1f;
    }

    // -------------
    // Save/Load
    // -------------
    public void SaveAllSessionsToDisk()
    {
        var wrapper = new SessionListWrapper { sessions = chatSessions };
        string data = JsonUtility.ToJson(wrapper, true);
        string fullPath = Path.Combine(Application.persistentDataPath, sessionsSaveFilename);
        File.WriteAllText(fullPath, data);
        Debug.Log("[ModernChatManager] Sessions saved => " + fullPath);
    }

    public void LoadAllSessionsFromDisk()
    {
        string fullPath = Path.Combine(Application.persistentDataPath, sessionsSaveFilename);
        if (!File.Exists(fullPath))
        {
            Debug.LogWarning("[ModernChatManager] No session file found => " + fullPath);
            return;
        }
        string json = File.ReadAllText(fullPath);
        var wrapper = JsonUtility.FromJson<SessionListWrapper>(json);
        if (wrapper == null || wrapper.sessions == null)
        {
            Debug.LogWarning("[ModernChatManager] Failed to parse session data.");
            return;
        }
        chatSessions.Clear();
        chatSessions.AddRange(wrapper.sessions);
        Debug.Log("[ModernChatManager] Loaded sessions => " + chatSessions.Count);
        RebuildSessionList();
    }

    // Decide if user wants an image
    private IEnumerator CheckIfUserWantsImage(string userText, Action<bool> onComplete)
    {
        string modelForRequest = selectedModels.Count > 0 ? selectedModels.First() : OpenAIModels.GPT4O_MINI;

        string systemPrompt =
            "You are a gatekeeper that decides if a user wants an AI-generated image.\n" +
            "User says:\n\"" + userText + "\"\n\n" +
            "Return JSON {\"imageWanted\":true} or {\"imageWanted\":false} only.";

        var shortMessages = new List<ChatMessageVision>();
        shortMessages.Add(new ChatMessageVision
        {
            role = "system",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent { type = "text", text = systemPrompt }
            }
        });
        string baseUrl = GetBaseUrlForModel(modelForRequest);
        string usedApiKey = GetApiKeyForModel(modelForRequest);

        bool useResponses = IsOpenAIResponsesModel(modelForRequest);
        string apiUrl;
        string jsonBody;

        if (useResponses)
        {
            var req = new OpenAIResponsesRequest
            {
                model = modelForRequest,
                input = ConvertVisionMessagesToResponsesInput(shortMessages.ToArray()),
                max_output_tokens = 30,
                store = false
            };
            jsonBody = JsonUtility.ToJson(req);
            apiUrl = baseUrl + "/responses";
        }
        else
        {
            var requestData = new ChatRequestVision
            {
                model = modelForRequest,
                messages = shortMessages.ToArray(),
                max_tokens = 30
            };
            jsonBody = JsonUtility.ToJson(requestData);
            apiUrl = baseUrl + "/chat/completions";
        }

using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
            request.timeout = 30;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[CheckIfUserWantsImage] Error => " + request.error);
                onComplete(false);
            }
            else
            {
                string resp = request.downloadHandler.text;
                Debug.Log("[CheckIfUserWantsImage] Resp => " + resp);
                string content = null;

                if (useResponses)
                    content = ExtractTextFromResponses(resp);
                else
                {
                    var data = JsonUtility.FromJson<ChatResponse>(resp);
                    if (data != null && data.choices != null && data.choices.Length > 0)
                        content = data.choices[0].message.content;
                }

                if (!string.IsNullOrEmpty(content))
                {
                    string lower = content.ToLower().Trim();
                    bool wantImage = lower.Contains("\"imagewanted\":true") || lower.Contains("\"image_wanted\":true");
                    onComplete(wantImage);
                }
                else
                {
                    onComplete(false);
                }
            }
        }
    }

    // Short GPT search query
    private IEnumerator RequestShortGPTSearchQuery(
        string userInput, string modelId, Action<string> onComplete)
    {
        var shortRequestMessages = new List<ChatMessageVision>();
        shortRequestMessages.Add(new ChatMessageVision
        {
            role = "system",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent
                {
                    type = "text",
                    text = "Rewrite the user request into a short search query (under 10 words). Return only that short query."
                }
            }
        });
        shortRequestMessages.Add(new ChatMessageVision
        {
            role = "user",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent { type = "text", text = userInput }
            }
        });
        string baseUrl = GetBaseUrlForModel(modelId);
        string usedApiKey = GetApiKeyForModel(modelId);

        bool useResponses = IsOpenAIResponsesModel(modelId);
        string apiUrl;
        string jsonBody;

        if (useResponses)
        {
            var req = new OpenAIResponsesRequest
            {
                model = modelId,
                input = ConvertVisionMessagesToResponsesInput(shortRequestMessages.ToArray()),
                max_output_tokens = 50,
                store = false
            };
            jsonBody = JsonUtility.ToJson(req);
            apiUrl = baseUrl + "/responses";
        }
        else
        {
            var requestData = new ChatRequestVision
            {
                model = modelId,
                messages = shortRequestMessages.ToArray(),
                max_tokens = 50
            };
            jsonBody = JsonUtility.ToJson(requestData);
            apiUrl = baseUrl + "/chat/completions";
        }

using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
            request.timeout = 60;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[GPT:ShortQuery] Error ({request.responseCode}) => {request.error} | Body => {errBody}");
                onComplete(null);
            }
            else
            {
                string resp = request.downloadHandler.text;
                Debug.Log($"[GPT:ShortQuery] Response => {resp}");
                string shortText = null;

                if (useResponses)
                    shortText = ExtractTextFromResponses(resp);
                else
                {
                    var data = JsonUtility.FromJson<ChatResponse>(resp);
                    if (data != null && data.choices != null && data.choices.Length > 0)
                        shortText = data.choices[0].message.content;
                }

                if (!string.IsNullOrEmpty(shortText))
                {
                    shortText = shortText.Replace("\n", " ").Trim();
                    if (shortText.Length > 80) shortText = shortText.Substring(0, 80);
                    onComplete(shortText);
                }
                else
                {
                    onComplete(null);
                }
            }
        }
    }

    // Return base URL for model ID
    private string GetBaseUrlForModel(string modelName)
    {
        if (modelName.StartsWith("grok-", StringComparison.OrdinalIgnoreCase))
            return "https://api.x.ai/v1";
        else if (modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ||
                 modelName.StartsWith("imagen-", StringComparison.OrdinalIgnoreCase))
            return "https://generativelanguage.googleapis.com/v1beta/openai";
        else
            return "https://api.openai.com/v1";
    }

    // Return API key for model ID
    private string GetApiKeyForModel(string modelName)
    {
        if (modelName.StartsWith("grok-", StringComparison.OrdinalIgnoreCase))
            return grokApiKey;
        else if (modelName.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ||
                 modelName.StartsWith("imagen-", StringComparison.OrdinalIgnoreCase))
            return geminiApiKey;
        return openAiApiKey;
    }

    // Return a short name for display
    private string GetShortModelName(string fullModelId)
    {
        // OpenAI (Chat)
        if (fullModelId == OpenAIModels.GPT_5_2) return "GPT‑5.2";
        if (fullModelId == OpenAIModels.GPT_5_MINI) return "GPT‑5 mini";
        if (fullModelId == OpenAIModels.GPT_5_NANO) return "GPT‑5 nano";

        // OpenAI (Images)
        if (fullModelId == OpenAIModels.GPT_IMAGE_1_5) return "GPT Image 1.5";

        // Grok (Chat)
        if (fullModelId == OpenAIModels.GROK_4_1_FAST_REASONING) return "Grok 4.1 R";
        if (fullModelId == OpenAIModels.GROK_4_1_FAST_NON_REASONING) return "Grok 4.1 NR";
        if (fullModelId == OpenAIModels.GROK_3_MINI) return "Grok 3 mini";

        // Grok (Images)
        if (fullModelId == OpenAIModels.GROK_2_IMAGE_1212) return "Grok Image";

        // Gemini (Chat)
        if (fullModelId == OpenAIModels.GEMINI_3_PRO_PREVIEW) return "Gemini 3 Pro";
        if (fullModelId == OpenAIModels.GEMINI_3_FLASH_PREVIEW) return "Gemini 3 Flash";
        if (fullModelId == OpenAIModels.GEMINI_25_FLASH_LITE) return "Gemini 2.5 Lite";

        // Gemini (Images)
        if (fullModelId == OpenAIModels.GEMINI_3_PRO_IMAGE_PREVIEW) return "Gemini Image";
        if (fullModelId == OpenAIModels.GEMINI_25_FLASH_IMAGE) return "Gemini Image (Flash)";

        // Legacy mapping (kept)
        if (fullModelId == OpenAIModels.GPT35_TURBO) return "GPT-3.5";
        if (fullModelId == OpenAIModels.GPT35_TURBO_16K) return "GPT-3.5-16K";
        if (fullModelId == OpenAIModels.GPT4O) return "GPT-4o";
        if (fullModelId == OpenAIModels.GPT4O_MINI) return "GPT-4o-mini";
        if (fullModelId == OpenAIModels.GPT4O_MINI_SEARCH) return "GPT-4o-mini-search";
        if (fullModelId == OpenAIModels.GPT4O_SEARCH) return "GPT-4o-search";
        if (fullModelId == OpenAIModels.O1) return "O1";
        if (fullModelId == OpenAIModels.O1_MINI) return "O1-mini";
        if (fullModelId == OpenAIModels.O3_MINI) return "O3-mini";
        if (fullModelId == OpenAIModels.O3_MINI_HIGH) return "O3-mini-high";
        if (fullModelId == OpenAIModels.GEMINI_25_PRO) return "G2.5-pro";
        if (fullModelId == OpenAIModels.GEMINI_20_FLASH) return "G2.0-flash";
        if (fullModelId == OpenAIModels.GEMINI_20_FLASH_LITE) return "G2.0-flash-lite";
        if (fullModelId == OpenAIModels.GEMINI_20_IMAGE) return "Imagen";
        if (fullModelId == OpenAIModels.GROK_2_1212) return "Grok2";
        if (fullModelId == OpenAIModels.GROK_2_VISION_1212) return "Grok2-vision";
        if (fullModelId == OpenAIModels.GROK_2_IMAGE) return "Grok2-image";
        if (fullModelId == OpenAIModels.GPT45_PREVIEW) return "GPT‑4.5 Prev";

        return fullModelId;
    }

    /// <summary>
    /// Dynamically resizes the InputField height up to max, or shrinks if lines removed.
    /// </summary>
    private void AdjustInputFieldSize()
    {
        float textHeight = CalculateTextHeight(inputField);

        float offset = 20f; // small extra padding
        float newHeight = textHeight + offset;
        newHeight = Mathf.Clamp(newHeight, minInputFieldHeight, maxInputFieldHeight);

        var inputFieldRT = inputField.GetComponent<RectTransform>();
        float currentWidth = inputFieldRT.sizeDelta.x;
        inputFieldRT.sizeDelta = new Vector2(currentWidth, newHeight);

        // Adjust bottom bar height to match
        var bottomBarRT = bottomBarGO.GetComponent<RectTransform>();
        float barNeededHeight = inputFieldRT.sizeDelta.y + 10f;
        bottomBarRT.sizeDelta =
            new Vector2(bottomBarRT.sizeDelta.x, Mathf.Max(barNeededHeight, 70f));

        // Move chat area up
        var chatRect = chatAreaGO.GetComponent<RectTransform>();
        chatRect.offsetMin =
            new Vector2(chatRect.offsetMin.x, bottomBarRT.sizeDelta.y);
    }

    /// <summary>
    /// Calculates text height via TextGenerator
    /// </summary>
    private float CalculateTextHeight(InputField input)
    {
        if (input == null || input.textComponent == null)
            return minInputFieldHeight;

        Text textObj = input.textComponent;
        var generator = new TextGenerator();
        Vector2 extents = new Vector2(textObj.rectTransform.rect.width, 99999f);
        var settings = textObj.GetGenerationSettings(extents);
        generator.Populate(input.text, settings);
        float preferred = generator.GetPreferredHeight(input.text, settings);
        float lineHeight = preferred / textObj.pixelsPerUnit;
        return lineHeight;
    }

    // -------------
    // LLM-Conversation Flow
    // -------------
    public void OnLLMConversationToggleClicked()
    {
        llmConversationModeActive = !llmConversationModeActive;
        Debug.Log("LLM Conversation Mode toggled: " + llmConversationModeActive);
        UpdateLLMConversationToggleVisual();
    }

    public void OnInitiateConversationClicked()
    {
        if (!llmConversationModeActive)
        {
            Debug.LogWarning("LLM Conversation mode is not active!");
            return;
        }
        if (llmConversationSettings == null || llmConversationSettings.Count < 2)
        {
            Debug.LogWarning("At least two LLM conversation settings are required!");
            return;
        }
        if (llmConversationRunning)
        {
            Debug.LogWarning("LLM Conversation is already running!");
            return;
        }
        llmConversationRunning = true;
        llmConversationCoroutine = StartCoroutine(LLMConversationFlow());
    }

    public void OnStopConversationClicked()
    {
        llmConversationRunning = false;
        if (llmConversationCoroutine != null)
        {
            StopCoroutine(llmConversationCoroutine);
            llmConversationCoroutine = null;
        }
        Debug.Log("LLM Conversation stopped.");
    }

    private IEnumerator LLMConversationFlow()
    {
        // Clear old memory
        multiLLMConversation.Clear();

        // Global guidance so each model "sees" the other model(s) as distinct speakers.
        // IMPORTANT: we store model labels directly in the assistant message text (e.g., "[GPT-5.2]: ...")
        // so that the next model can reliably read and respond to what the other model said.
        multiLLMConversation.Add(new ChatMessageVision
        {
            role = "system",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent
                {
                    type = "text",
                    text = "You are participating in a multi-LLM round-robin conversation. " +
                           "Each assistant message in the transcript is prefixed like [MODEL]:. " +
                           "Always read the most recent message from the other model(s) and respond directly to it. " +
                           "Do not ignore prior turns."
                }
            }
        });

        // Start with first LLM's prompt as user message (seed/topic/instructions)
        var firstSetting = llmConversationSettings[0];
        multiLLMConversation.Add(new ChatMessageVision
        {
            role = "user",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent
                {
                    type = "text",
                    text = firstSetting.conversationPrompt
                }
            }
        });

        // Request from first LLM (use a per-turn context to avoid polluting the shared transcript)
        {
            var ctxFirst = new List<ChatMessageVision>(multiLLMConversation);
            string firstSys = "You are " + GetShortModelName(firstSetting.modelId) + ". " +
                              "Respond as that model and begin the conversation based on the user's last message.";
            ctxFirst.Add(new ChatMessageVision
            {
                role = "system",
                content = new VisionMessageContent[]
                {
                    new VisionMessageContent { type = "text", text = firstSys }
                }
            });

            yield return StartCoroutine(RequestLLMResponseForConversationMode(
                firstSetting, ctxFirst,
                (response) =>
                {
                    string labeled = $"[{GetShortModelName(firstSetting.modelId)}]: {response}";
                    multiLLMConversation.Add(new ChatMessageVision
                    {
                        role = "assistant",
                        content = new VisionMessageContent[]
                        {
                            new VisionMessageContent
                            {
                                type = "text",
                                text = labeled
                            }
                        }
                    });
                    CreateChatBubble(labeled, false);
                }
            ));
        }

        // Round-robin among LLMs
        int currentIndex = 0;
        while (llmConversationRunning)
        {
            currentIndex = (currentIndex + 1) % llmConversationSettings.Count;
            var setting = llmConversationSettings[currentIndex];

            // Summarize older if too large
            if (conversationMemoryEnabled)
            {
                yield return StartCoroutine(SummarizeLLMConversationIfTooLarge(multiLLMConversation));
            }

            // Find the most recent assistant message to force the next model to "reply to" it.
            string lastAssistantText = "";
            for (int i = multiLLMConversation.Count - 1; i >= 0; i--)
            {
                if (multiLLMConversation[i] != null && multiLLMConversation[i].role == "assistant")
                {
                    if (multiLLMConversation[i].content != null && multiLLMConversation[i].content.Length > 0)
                        lastAssistantText = multiLLMConversation[i].content[0].text;
                    break;
                }
            }

            // Build a per-turn context so "who said what" is unambiguous for the current model.
            var ctx = new List<ChatMessageVision>(multiLLMConversation);

            string shortName = GetShortModelName(setting.modelId);
            string sysText = "You are " + shortName + ". You are in a multi-LLM round-robin conversation. " +
                             "Speak as " + shortName + " and respond directly to the other model's most recent message. " +
                             "Do not ignore it, and do not pretend you wrote the other model's lines.";
            // If the user configured a per-model prompt for this model, include it (but avoid duplicating the seed prompt for the first model).
            if (!string.IsNullOrWhiteSpace(setting.conversationPrompt) && setting != firstSetting)
            {
                sysText += "\n\nYour role instructions:\n" + setting.conversationPrompt.Trim();
            }

            ctx.Add(new ChatMessageVision
            {
                role = "system",
                content = new VisionMessageContent[]
                {
                    new VisionMessageContent { type = "text", text = sysText }
                }
            });

            // Force a "reply to last message" framing as a USER message so the next model actually engages.
            if (!string.IsNullOrEmpty(lastAssistantText))
            {
                ctx.Add(new ChatMessageVision
                {
                    role = "user",
                    content = new VisionMessageContent[]
                    {
                        new VisionMessageContent
                        {
                            type = "text",
                            text = "Reply directly to this most recent message from the other model:\n\n" + lastAssistantText + "\n\nYour reply:"
                        }
                    }
                });
            }

            // Next LLM response
            yield return StartCoroutine(RequestLLMResponseForConversationMode(
                setting, ctx,
                (response) =>
                {
                    string labeled = $"[{GetShortModelName(setting.modelId)}]: {response}";
                    multiLLMConversation.Add(new ChatMessageVision
                    {
                        role = "assistant",
                        content = new VisionMessageContent[]
                        {
                            new VisionMessageContent
                            {
                                type = "text",
                                text = labeled
                            }
                        }
                    });
                    CreateChatBubble(labeled, false);
                }
            ));

            yield return new WaitForSeconds(1f);
        }
    }

    private IEnumerator RequestLLMResponseForConversationMode(
        LLMConversationSetting setting,
        List<ChatMessageVision> entireConversationSoFar,
        Action<string> onComplete)
    {
        string baseUrl = GetBaseUrlForModel(setting.modelId);
        string usedApiKey = GetApiKeyForModel(setting.modelId);

        bool useResponses = IsOpenAIResponsesModel(setting.modelId);
        string apiUrl;
        string jsonBody;

        if (useResponses)
        {
            var req = new OpenAIResponsesRequest
            {
                model = setting.modelId,
                input = ConvertVisionMessagesToResponsesInput(entireConversationSoFar.ToArray()),
                max_output_tokens = maxTokensForVision,
                store = false
            };
            jsonBody = JsonUtility.ToJson(req);
            apiUrl = baseUrl + "/responses";
        }
        else
        {
            var requestData = new ChatRequestVision
            {
                model = setting.modelId,
                messages = entireConversationSoFar.ToArray(),
                max_tokens = maxTokensForVision
            };
            jsonBody = JsonUtility.ToJson(requestData);
            apiUrl = baseUrl + "/chat/completions";
        }

Debug.Log($"[LLM-Conv] Sending to {apiUrl} with model={setting.modelId}");
        using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
            request.timeout = 120;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[LLM-Conv] Error ({request.responseCode}) => {request.error} | Body => {errBody}");
                CreateChatBubble($"[{GetShortModelName(setting.modelId)} Error => {request.error}]", false);
                onComplete("");
            }
            else
            {
                string resp = request.downloadHandler.text;
                Debug.Log($"[LLM-Conv] Response => {resp}");
                string assistantText = null;

                if (useResponses)
                    assistantText = ExtractTextFromResponses(resp);
                else
                {
                    var data = JsonUtility.FromJson<ChatResponse>(resp);
                    if (data != null && data.choices != null && data.choices.Length > 0)
                        assistantText = data.choices[0].message.content;
                }

                if (!string.IsNullOrEmpty(assistantText))
                {
                    onComplete(assistantText);
                }
                else
                {
                    CreateChatBubble($"[{GetShortModelName(setting.modelId)} => Error parsing response]", false);
                    onComplete("");
                }
            }
        }
    }

    private IEnumerator SummarizeLLMConversationIfTooLarge(List<ChatMessageVision> conversation)
    {
        int approxTokens = EstimateTokens(conversation);
        if (approxTokens <= memoryTokenThreshold) yield break;

        int keepCount = 3;
        if (conversation.Count <= keepCount) yield break;

        var olderMessages = conversation.Take(conversation.Count - keepCount).ToList();
        string olderText = ConvertConversationToPlainText(olderMessages);
        string summaryPrompt =
            "You are a summarizing assistant. Summarize the following conversation in a concise way, " +
            "capturing all relevant details so the story or context can continue seamlessly. " +
            "Output only the summary text.\n\n---\n" + olderText + "\n---\nSummary:";

        Debug.Log("[LLM-Conv:Memory] Summarizing older messages. Approx tokens: " + approxTokens);

        var summaryRequest = new List<ChatMessageVision>();
        summaryRequest.Add(new ChatMessageVision
        {
            role = "system",
            content = new VisionMessageContent[]
            {
                new VisionMessageContent { type = "text", text = summaryPrompt }
            }
        });
        string baseUrl = GetBaseUrlForModel(memorySummaryModel);
        string usedApiKey = GetApiKeyForModel(memorySummaryModel);

        bool useResponses = IsOpenAIResponsesModel(memorySummaryModel);
        string apiUrl;
        string jsonBody;

        if (useResponses)
        {
            var req = new OpenAIResponsesRequest
            {
                model = memorySummaryModel,
                input = ConvertVisionMessagesToResponsesInput(summaryRequest.ToArray()),
                max_output_tokens = memorySummaryMaxTokens,
                store = false
            };
            jsonBody = JsonUtility.ToJson(req);
            apiUrl = baseUrl + "/responses";
        }
        else
        {
            var requestData = new ChatRequestVision
            {
                model = memorySummaryModel,
                messages = summaryRequest.ToArray(),
                max_tokens = memorySummaryMaxTokens
            };
            jsonBody = JsonUtility.ToJson(requestData);
            apiUrl = baseUrl + "/chat/completions";
        }

using (var request = new UnityWebRequest(apiUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + usedApiKey);
            request.timeout = 60;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[LLM-Conv:Memory] Summarization Error => " + request.error);
                yield break;
            }
            else
            {
                string resp = request.downloadHandler.text;
                Debug.Log("[LLM-Conv:Memory] Summarization => " + resp);
                string summaryText = null;

                if (useResponses)
                    summaryText = ExtractTextFromResponses(resp);
                else
                {
                    var data = JsonUtility.FromJson<ChatResponse>(resp);
                    if (data != null && data.choices != null && data.choices.Length > 0)
                        summaryText = data.choices[0].message.content.Trim();
                }

                if (!string.IsNullOrEmpty(summaryText))
                {
                    var summaryMessage = new ChatMessageVision
                    {
                        role = "system",
                        content = new VisionMessageContent[]
                        {
                            new VisionMessageContent { type = "text", text = "[CONVERSATION SUMMARY]\n" + summaryText }
                        }
                    };

                    var lastFew = conversation.Skip(conversation.Count - keepCount).ToList();
                    conversation.Clear();
                    conversation.Add(summaryMessage);
                    conversation.AddRange(lastFew);

                    Debug.Log("[LLM-Conv:Memory] Summarization complete. Conversation now has " +
                              conversation.Count + " messages.");
                }
            }
        }
    }

    private int EstimateTokens(List<ChatMessageVision> conversation)
    {
        int totalChars = 0;
        foreach (var msg in conversation)
        {
            if (msg.content == null) continue;
            foreach (var part in msg.content)
            {
                if (!string.IsNullOrEmpty(part.text))
                {
                    totalChars += part.text.Length;
                }
            }
        }
        // naive: ~4 chars per token
        return totalChars / 4;
    }

    private string ConvertConversationToPlainText(List<ChatMessageVision> messages)
    {
        var sb = new StringBuilder();
        foreach (var msg in messages)
        {
            string roleLabel = msg.role.ToUpperInvariant();
            sb.AppendLine($"{roleLabel}:");
            if (msg.content != null)
            {
                foreach (var c in msg.content)
                {
                    sb.AppendLine(c.text);
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
    // ---------------------------------------------------------
    // Runtime UI safety: ensure buttons can be clicked in Editor
    // (missing EventSystem / GraphicRaycaster is the #1 cause)
    // ---------------------------------------------------------
    private void EnsureUnityUIRuntime(Canvas canvas)
    {
        if (canvas == null) return;

        // Ensure this canvas can receive UI raycasts.
        if (canvas.GetComponent<GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        // Ensure there is an EventSystem in the scene.
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();

            // Prefer the new Input System module if the package is present, otherwise fall back.
            Type inputSystemUIModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemUIModuleType != null)
            {
                esGO.AddComponent(inputSystemUIModuleType);
            }
            else
            {
                esGO.AddComponent<StandaloneInputModule>();
            }
        }
    }

    // ---------------------------------------------------------
    // Inline video playback helpers (Editor/Standalone)
    // ---------------------------------------------------------
    private IEnumerator CoInlineVideoPrepareTimeout(VideoPlayer vp, float timeoutSeconds)
    {
        if (vp == null) yield break;

        float t = 0f;
        while (t < timeoutSeconds)
        {
            if (vp == null) yield break;
            if (vp.isPrepared) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Timed out preparing (often missing codecs) — open URL as the final UX fallback.
        int id = (vp != null) ? vp.GetInstanceID() : 0;
        if (id != 0 && _vpOriginalUrl.TryGetValue(id, out string original) && !string.IsNullOrEmpty(original))
        {
            Application.OpenURL(original);
        }
    }

    private IEnumerator CoDownloadTempVideoAndPlay(VideoPlayer vp, string url)
    {
        if (vp == null) yield break;
        if (string.IsNullOrEmpty(url)) yield break;
        if (!(url.StartsWith("http://") || url.StartsWith("https://"))) yield break;

        // Cache on disk to avoid repeated downloads when user replays.
        string localPath = null;
        if (!_videoTempCache.TryGetValue(url, out localPath) || string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
        {
            string safeName = "tmp_video_" + Mathf.Abs(url.GetHashCode()).ToString("X8") + ".mp4";
            localPath = Path.Combine(Application.temporaryCachePath, safeName);

            using (UnityWebRequest req = UnityWebRequest.Get(url))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
                bool ok = req.result == UnityWebRequest.Result.Success;
#else
                bool ok = !(req.isNetworkError || req.isHttpError);
#endif
                if (!ok)
                {
                    Debug.LogWarning("[Video] Temp download failed: " + req.error);
                    Application.OpenURL(url);
                    yield break;
                }

                try
                {
                    File.WriteAllBytes(localPath, req.downloadHandler.data);
                    _videoTempCache[url] = localPath;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Video] Couldn't write temp video: " + e.Message);
                    Application.OpenURL(url);
                    yield break;
                }
            }
        }

        if (vp == null) yield break;

        // Convert to a file:// URL (Unity VideoPlayer expects this on some platforms).
        string vpUrl = localPath.Replace("\\", "/");
        if (!vpUrl.StartsWith("file://"))
        {
            // Windows: file:///C:/...
            if (vpUrl.Length >= 2 && vpUrl[1] == ':')
                vpUrl = "file:///" + vpUrl;
            else
                vpUrl = "file://" + vpUrl; // Unix-style absolute paths start with /
        }

        try { vp.Stop(); } catch { }

        vp.source = VideoSource.Url;
        vp.url = vpUrl;

        vp.prepareCompleted -= OnInlineVideoPrepared;
        vp.prepareCompleted += OnInlineVideoPrepared;

        vp.errorReceived -= OnInlineVideoError;
        vp.errorReceived += OnInlineVideoError;

        vp.Prepare();

        // Short timeout for local playback as well; if it still can't play, OnInlineVideoError will open browser.
        yield return CoInlineVideoPrepareTimeout(vp, 6f);
    }
}

// --------------------
// Additional Classes
// --------------------

[System.Serializable]
public class SessionListWrapper
{
    public List<ChatSession> sessions;
}

[System.Serializable]
public class ChatSession
{
    public string sessionName;
    public List<string> chosenModels;
    public List<ChatBubbleData> messages;
}

[System.Serializable]
public class ChatBubbleData
{
    public bool isUser;
    public string text;
    public string imageBase64;

    // Video (WebGL in-chat playback via HTML5 overlay)
    public string videoUrl;
    public string videoFileName;
    public string videoMime;
}

[System.Serializable]
public class LLMConversationSetting
{
    public string modelId;
    [TextArea(3, 6)]
    public string conversationPrompt;
}

[System.Serializable]
public class ChatRequestVision
{
    public string model;
    public ChatMessageVision[] messages;
    public int max_tokens;
}

[System.Serializable]
public class ChatMessageVision
{
    public string role;  // "system", "user", or "assistant"
    public VisionMessageContent[] content;
}

[System.Serializable]
public class VisionMessageContent
{
    public string type; // "text" or "image"
    public string text; // if type == "text"
}

[System.Serializable]
public class ChatResponse
{
    public string id;
    public string @object;
    public long created;
    public Choice[] choices;
    public Usage usage;
}

[System.Serializable]
public class Choice
{
    public int index;
    public Message message;
    public string finish_reason;
}

[System.Serializable]
public class Message
{
    public string role;
    public string content;
}

[System.Serializable]
public class Usage
{
    public int prompt_tokens;
    public int completion_tokens;
    public int total_tokens;
}

[System.Serializable]
public class DalleImageResponse
{
    public DalleImageData[] data;
}

[System.Serializable]
public class DalleImageData
{
    public string url;
    public string b64_json;
}

public static class AdvancedLinkHelper
{
    public static string SanitizeAndConvertLinks(string text)
    {
        // Minimal/no-op for this example
        return text;
    }
}

public class AdvancedHyperlinkHandler : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        // Stub; implement link-click if needed
    }
}

public class InputFieldPasteHelper : MonoBehaviour
{
    // empty
}