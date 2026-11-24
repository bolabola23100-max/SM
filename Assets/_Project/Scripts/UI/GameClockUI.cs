using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays the current in-game day and time from the existing DayNightCycle.
/// No independent timer is created, so the display stays synchronized with lighting.
/// </summary>
[DisallowMultipleComponent]
public class GameClockUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 panelSize = new Vector2(230f, 104f);
    [SerializeField] private Vector2 screenPadding = new Vector2(24f, 24f);

    private DayNightCycle clock;
    private Text dayText;
    private Text timeText;
    private Text periodText;

    private void Awake()
    {
        clock = GetComponent<DayNightCycle>();
        if (clock == null)
            clock = FindObjectOfType<DayNightCycle>();

        if (clock == null)
        {
            Debug.LogError("GameClockUI: No DayNightCycle was found in the scene.");
            enabled = false;
            return;
        }

        BuildClockUI();
        RefreshDisplay();
    }

    private void OnEnable()
    {
        if (clock == null) return;
        clock.MinuteChanged += OnMinuteChanged;
        clock.DayStarted += OnDayStarted;
    }

    private void OnDisable()
    {
        if (clock == null) return;
        clock.MinuteChanged -= OnMinuteChanged;
        clock.DayStarted -= OnDayStarted;
    }

    private void OnMinuteChanged(int hour, int minute) => RefreshDisplay();
    private void OnDayStarted(int day) => RefreshDisplay();

    private void RefreshDisplay()
    {
        if (clock == null || dayText == null) return;

        dayText.text = "DAY " + clock.Day.ToString("00");
        timeText.text = clock.GetTimeText();

        float hour = clock.TimeOfDay;
        if (hour >= 19f)
            periodText.text = "NIGHT";
        else if (hour >= 17f)
            periodText.text = "EVENING";
        else
            periodText.text = "DAY";
    }

    private void BuildClockUI()
    {
        GameObject canvasObject = new GameObject("GameClockCanvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panel = new GameObject("TimePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1f, 1f);
        panelRect.anchorMax = new Vector2(1f, 1f);
        panelRect.pivot = new Vector2(1f, 1f);
        panelRect.anchoredPosition = new Vector2(-screenPadding.x, -screenPadding.y);
        panelRect.sizeDelta = panelSize;

        Image background = panel.GetComponent<Image>();
        background.color = new Color(0.035f, 0.055f, 0.085f, 0.88f);
        background.raycastTarget = false;

        dayText = CreateText("DayText", panel.transform, new Vector2(18f, -12f),
            new Vector2(194f, 24f), 18, FontStyle.Bold,
            new Color(0.72f, 0.84f, 0.94f), TextAnchor.MiddleLeft);

        timeText = CreateText("TimeText", panel.transform, new Vector2(18f, -37f),
            new Vector2(194f, 40f), 30, FontStyle.Bold,
            Color.white, TextAnchor.MiddleLeft);

        periodText = CreateText("PeriodText", panel.transform, new Vector2(18f, -77f),
            new Vector2(194f, 20f), 15, FontStyle.Bold,
            new Color(0.35f, 0.82f, 1f), TextAnchor.MiddleLeft);
    }

    private Text CreateText(string objectName, Transform parent, Vector2 position,
        Vector2 size, int fontSize, FontStyle fontStyle, Color color, TextAnchor alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        Text text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }
}
