using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Updates the normal Text component placed in the MobileControls Canvas.
/// The displayed time is read directly from DayNightCycle.
/// </summary>
[DisallowMultipleComponent]
public class GameClockUI : MonoBehaviour
{
    [SerializeField] private DayNightCycle clock;
    [SerializeField] private Text clockText;

    private void Awake()
    {
        if (clock == null)
            clock = GetComponent<DayNightCycle>();

        if (clock == null)
            clock = FindObjectOfType<DayNightCycle>();

        if (clock == null || clockText == null)
        {
            Debug.LogError("GameClockUI: Assign a DayNightCycle and the MobileControls clock Text.");
            enabled = false;
            return;
        }

        RefreshDisplay();
    }

    private void OnEnable()
    {
        if (clock == null) return;
        clock.MinuteChanged += OnClockChanged;
        clock.DayStarted += OnDayChanged;
    }

    private void OnDisable()
    {
        if (clock == null) return;
        clock.MinuteChanged -= OnClockChanged;
        clock.DayStarted -= OnDayChanged;
    }

    private void OnClockChanged(int hour, int minute) => RefreshDisplay();
    private void OnDayChanged(int day) => RefreshDisplay();

    private void RefreshDisplay()
    {
        if (clock == null || clockText == null) return;

        string period = clock.TimeOfDay >= 19f ? "NIGHT" :
                        clock.TimeOfDay >= 17f ? "EVENING" : "DAY";

        clockText.text = "DAY " + clock.Day.ToString("00") +
                         "   |   " + clock.GetTimeText() +
                         "   |   " + period;
    }
}
