using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Controls the store's day/night clock and lighting.
/// The clock advances only while the store is open.
/// UI is intentionally kept separate and can subscribe to the public events.
/// </summary>
public class DayNightCycle : MonoBehaviour
{
    [Serializable]
    public class SaveData
    {
        public int day = 1;
        public float time = 9f;
    }

    [Serializable]
    private class LightingKey
    {
        public float hour;
        public Color sunColor;
        public float sunEnergy;
        public Color ambientColor;
        public float ambientEnergy;
        public Color skyTop;
        public Color skyHorizon;
        public float moonEnergy;

        public LightingKey(float h, Color sun, float sunE, Color ambient, float ambientE,
            Color top, Color horizon, float moonE)
        {
            hour = h;
            sunColor = sun;
            sunEnergy = sunE;
            ambientColor = ambient;
            ambientEnergy = ambientE;
            skyTop = top;
            skyHorizon = horizon;
            moonEnergy = moonE;
        }
    }

    public event Action<int> HourChanged;
    public event Action<int, int> MinuteChanged;
    public event Action<int> DayStarted;
    public event Action<int> DayEnded;
    public event Action<int> DayTimeOver;
    public event Action<bool> StoreOpenChanged;

    [Header("Time")]
    [Min(0f)] public float startHour = 9f;
    [Range(0.1f, 24f)] public float endHour = 21f;
    [Min(0.1f)] public float dayLengthMinutes = 12f;
    [Min(0f)] public float timeScale = 1f;
    [Tooltip("If enabled, the store starts open when this scene loads.")]
    public bool startWithStoreOpen;

    [Header("Sun")]
    public Light sun;
    public float sunAzimuthStart = -70f;
    public float sunAzimuthEnd = 70f;
    [Range(1f, 89f)] public float sunMaxElevation = 70f;

    [Header("Night Lighting")]
    [Tooltip("Assign the shop lights that should fade in after sunset.")]
    public Light[] nightLights = Array.Empty<Light>();
    public Color moonColor = new Color(0.55f, 0.65f, 1f);

    [Header("Sky")]
    [Tooltip("Optional skybox material. If empty, RenderSettings.skybox is used.")]
    public Material skyboxMaterial;

    public float TimeOfDay { get; private set; } = 9f;
    public int Day { get; private set; } = 1;
    public bool IsDayOver { get; private set; }
    public bool IsStoreOpen { get; private set; }

    private bool running = true;
    private bool endDayReported;
    private int lastMinute = -1;
    private int lastHour = -1;
    private Light moon;
    private float[] nightLightBaseIntensities;
    private Material runtimeSkybox;
    private bool ownsRuntimeSkybox;

    private readonly LightingKey[] keys =
    {
        new LightingKey(9f,  new Color(1f, .93f, .82f), 1.1f, new Color(.62f, .68f, .78f), .9f, new Color(.35f, .55f, .85f), new Color(.65f, .78f, .92f), 0f),
        new LightingKey(12f, new Color(1f, .98f, .95f), 1.3f, new Color(.70f, .74f, .82f), 1f, new Color(.25f, .50f, .90f), new Color(.62f, .78f, .95f), 0f),
        new LightingKey(16f, new Color(1f, .95f, .85f), 1.15f, new Color(.68f, .70f, .78f), .95f, new Color(.30f, .52f, .85f), new Color(.70f, .78f, .90f), 0f),
        new LightingKey(18f, new Color(1f, .65f, .35f), .8f, new Color(.65f, .55f, .55f), .8f, new Color(.35f, .35f, .60f), new Color(.95f, .55f, .35f), 0f),
        new LightingKey(19f, new Color(1f, .45f, .25f), 0f, new Color(.35f, .30f, .45f), .55f, new Color(.12f, .14f, .32f), new Color(.55f, .30f, .35f), .05f),
        new LightingKey(20f, new Color(.6f, .3f, .3f), 0f, new Color(.15f, .17f, .30f), .40f, new Color(.03f, .04f, .12f), new Color(.10f, .10f, .22f), .20f),
        new LightingKey(21f, new Color(.6f, .3f, .3f), 0f, new Color(.12f, .14f, .28f), .38f, new Color(.02f, .03f, .09f), new Color(.06f, .07f, .16f), .25f)
    };

    private void Awake()
    {
        TimeOfDay = Mathf.Clamp(startHour, 0f, 23.99f);
        if (sun == null)
        {
            Light[] sceneLights = FindObjectsOfType<Light>();
            foreach (Light candidate in sceneLights)
            {
                if (candidate != null && candidate.type == LightType.Directional)
                {
                    sun = candidate;
                    break;
                }
            }
        }

        CreateMoon();
        CacheNightLights();
        SetupSkybox();
        IsStoreOpen = startWithStoreOpen;
        ApplyLighting();
    }

    private void Start()
    {
        DayStarted?.Invoke(Day);
        StoreOpenChanged?.Invoke(IsStoreOpen);
        EmitClockChanges(true);
        Debug.Log("DAY/NIGHT CYCLE READY — Day " + Day + ", " + GetTimeText());
    }

    private void Update()
    {
        if (!running || !IsStoreOpen)
            return;

        float hoursPerSecond = (endHour - startHour) / (Mathf.Max(0.1f, dayLengthMinutes) * 60f);
        TimeOfDay += Time.deltaTime * hoursPerSecond * timeScale;

        if (TimeOfDay >= endHour)
        {
            TimeOfDay = endHour;
            ApplyLighting();
            TimeOver();
            return;
        }

        ApplyLighting();
        EmitClockChanges(false);
    }

    private void OnDestroy()
    {
        if (ownsRuntimeSkybox && runtimeSkybox != null)
            Destroy(runtimeSkybox);
    }

    private void CreateMoon()
    {
        GameObject moonObject = new GameObject("Moon Light (DayNightCycle)");
        moonObject.transform.SetParent(transform, false);
        moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = moonColor;
        moon.intensity = 0f;
        moon.shadows = LightShadows.None;
        moon.renderMode = LightRenderMode.ForcePixel;
        moon.enabled = false;
    }

    private void CacheNightLights()
    {
        if (nightLights == null)
            nightLights = Array.Empty<Light>();

        nightLightBaseIntensities = new float[nightLights.Length];
        for (int i = 0; i < nightLights.Length; i++)
        {
            Light light = nightLights[i];
            nightLightBaseIntensities[i] = light != null ? light.intensity : 0f;
        }
    }

    private void SetupSkybox()
    {
        if (skyboxMaterial == null)
            skyboxMaterial = RenderSettings.skybox;

        if (skyboxMaterial != null)
        {
            runtimeSkybox = new Material(skyboxMaterial);
            ownsRuntimeSkybox = true;
            RenderSettings.skybox = runtimeSkybox;
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
    }

    private void ApplyLighting()
    {
        if (sun == null)
            return;

        LightingKey sample = Sample(TimeOfDay);
        float elevation = Mathf.Max(Mathf.Sin((TimeOfDay - 7f) / 12f * Mathf.PI) * sunMaxElevation, 0f);
        float azimuth = Mathf.Lerp(sunAzimuthStart, sunAzimuthEnd, Mathf.InverseLerp(7f, 19f, TimeOfDay));

        // In Unity a directional light at X=90 points along the horizon.
        sun.transform.rotation = Quaternion.Euler(90f - elevation, azimuth, 0f);
        sun.color = sample.sunColor;
        sun.intensity = sample.sunEnergy;
        sun.enabled = sample.sunEnergy > 0.01f;
        RenderSettings.sun = sun;

        if (moon != null)
        {
            moon.color = moonColor;
            moon.intensity = sample.moonEnergy;
            moon.transform.rotation = Quaternion.Euler(90f + elevation, azimuth + 180f, 0f);
            moon.enabled = sample.moonEnergy > 0.01f;
        }

        RenderSettings.ambientSkyColor = sample.ambientColor;
        RenderSettings.ambientEquatorColor = Color.Lerp(sample.ambientColor, sample.skyHorizon, 0.5f);
        RenderSettings.ambientGroundColor = sample.ambientColor * 0.45f;
        RenderSettings.ambientIntensity = sample.ambientEnergy;

        if (runtimeSkybox != null)
        {
            Color tint = Color.Lerp(sample.skyTop, sample.skyHorizon, 0.35f);
            if (runtimeSkybox.HasProperty("_SkyTint"))
                runtimeSkybox.SetColor("_SkyTint", tint);
            if (runtimeSkybox.HasProperty("_GroundColor"))
                runtimeSkybox.SetColor("_GroundColor", sample.skyHorizon);
            if (runtimeSkybox.HasProperty("_Exposure"))
                runtimeSkybox.SetFloat("_Exposure", Mathf.Lerp(0.35f, 1.3f, sample.sunEnergy));
        }

        float night = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(17.5f, 19.5f, TimeOfDay));
        for (int i = 0; i < nightLights.Length; i++)
        {
            Light light = nightLights[i];
            if (light == null)
                continue;

            light.intensity = nightLightBaseIntensities[i] * night;
            light.enabled = night > 0.01f;
        }

        EmitClockChanges(false);
    }

    private LightingKey Sample(float hour)
    {
        if (hour <= keys[0].hour)
            return keys[0];

        for (int i = 0; i < keys.Length - 1; i++)
        {
            LightingKey a = keys[i];
            LightingKey b = keys[i + 1];
            if (hour <= b.hour)
            {
                float t = Mathf.InverseLerp(a.hour, b.hour, hour);
                return new LightingKey(
                    hour,
                    Color.Lerp(a.sunColor, b.sunColor, t),
                    Mathf.Lerp(a.sunEnergy, b.sunEnergy, t),
                    Color.Lerp(a.ambientColor, b.ambientColor, t),
                    Mathf.Lerp(a.ambientEnergy, b.ambientEnergy, t),
                    Color.Lerp(a.skyTop, b.skyTop, t),
                    Color.Lerp(a.skyHorizon, b.skyHorizon, t),
                    Mathf.Lerp(a.moonEnergy, b.moonEnergy, t));
            }
        }

        return keys[keys.Length - 1];
    }

    private void EmitClockChanges(bool force)
    {
        int totalMinutes = Mathf.FloorToInt(TimeOfDay * 60f);
        if (force || totalMinutes != lastMinute)
        {
            lastMinute = totalMinutes;
            int hour = Mathf.FloorToInt(TimeOfDay) % 24;
            int minute = totalMinutes % 60;
            MinuteChanged?.Invoke(hour, minute);

            if (force || hour != lastHour)
            {
                lastHour = hour;
                HourChanged?.Invoke(hour);
            }
        }
    }

    private void TimeOver()
    {
        if (IsDayOver)
            return;

        running = false;
        IsDayOver = true;
        IsStoreOpen = false;
        StoreOpenChanged?.Invoke(false);

        Debug.Log("DAY " + Day + " TIME OVER — WAITING FOR END DAY BUTTON");
        DayTimeOver?.Invoke(Day);
    }

    /// <summary>Call this from the Open Store button. The clock starts advancing.</summary>
    public void OpenStore()
    {
        SetStoreOpen(true);
    }

    /// <summary>Closing the store pauses the clock for the rest of the current session.</summary>
    public void CloseStore()
    {
        SetStoreOpen(false);
    }

    public void SetStoreOpen(bool open)
    {
        if (open && (!running || IsDayOver))
            return;

        if (IsStoreOpen == open)
            return;

        IsStoreOpen = open;
        StoreOpenChanged?.Invoke(IsStoreOpen);
    }

    /// <summary>Call this from the End Day button after the clock reaches endHour.</summary>
    public void EndDay()
    {
        if (running || !IsDayOver || endDayReported)
            return;

        endDayReported = true;
        Debug.Log("DAY " + Day + " ENDED");
        DayEnded?.Invoke(Day);
    }

    /// <summary>Call this from the Continue button on the end-of-day report.</summary>
    public void StartNextDay()
    {
        if (!IsDayOver)
            return;

        Day++;
        TimeOfDay = startHour;
        lastMinute = -1;
        lastHour = -1;
        running = true;
        IsDayOver = false;
        endDayReported = false;
        IsStoreOpen = false;

        ApplyLighting();
        StoreOpenChanged?.Invoke(false);
        Debug.Log("DAY " + Day + " STARTED");
        DayStarted?.Invoke(Day);
        EmitClockChanges(true);
    }

    public bool IsOpen()
    {
        return running && IsStoreOpen && TimeOfDay >= startHour && TimeOfDay < endHour;
    }

    public string GetTimeText()
    {
        int hour24 = Mathf.FloorToInt(TimeOfDay) % 24;
        int minute = Mathf.FloorToInt((TimeOfDay - Mathf.Floor(TimeOfDay)) * 60f);
        string suffix = hour24 < 12 ? "AM" : "PM";
        int hour12 = hour24 % 12;
        if (hour12 == 0) hour12 = 12;
        return hour12.ToString("00") + ":" + minute.ToString("00") + " " + suffix;
    }

    public void SetTime(float hour)
    {
        TimeOfDay = Mathf.Clamp(hour, startHour, endHour);
        lastMinute = -1;
        lastHour = -1;
        ApplyLighting();
        EmitClockChanges(true);
    }

    public SaveData GetSaveData()
    {
        return new SaveData { day = Day, time = TimeOfDay };
    }

    public string GetSaveDataJson()
    {
        return JsonUtility.ToJson(GetSaveData());
    }

    public void LoadSaveData(SaveData data)
    {
        if (data == null)
            return;

        Day = Mathf.Max(1, data.day);
        TimeOfDay = Mathf.Clamp(data.time, startHour, endHour);
        lastMinute = -1;
        lastHour = -1;
        IsDayOver = TimeOfDay >= endHour;
        running = !IsDayOver;
        IsStoreOpen = false;
        endDayReported = false;
        ApplyLighting();
        EmitClockChanges(true);
    }

    public void LoadSaveDataJson(string json)
    {
        if (string.IsNullOrEmpty(json))
            return;

        LoadSaveData(JsonUtility.FromJson<SaveData>(json));
    }
}
