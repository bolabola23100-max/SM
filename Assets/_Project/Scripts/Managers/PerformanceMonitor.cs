using UnityEngine;

/// <summary>Optional lightweight runtime diagnostic overlay. Add to a scene object and enable only while profiling.</summary>
public class PerformanceMonitor : MonoBehaviour
{
    [SerializeField] private bool showOverlay = true;
    [SerializeField, Min(0.1f)] private float refreshInterval = 0.5f;
    private float timer;
    private int frames;
    private float fps;
    private GUIStyle style;

    private void Update()
    {
        if (!showOverlay) return;
        frames++;
        timer += Time.unscaledDeltaTime;
        if (timer >= refreshInterval)
        {
            fps = frames / timer;
            frames = 0;
            timer = 0f;
        }
    }

    private void OnGUI()
    {
        if (!showOverlay) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label);
            style.fontSize = 18;
            style.normal.textColor = Color.white;
        }
        GUI.color = Color.black;
        GUI.Box(new Rect(8, 8, 240, 88), GUIContent.none);
        GUI.color = Color.white;
        GUI.Label(new Rect(16, 12, 230, 28), $"FPS: {fps:0}", style);
        GUI.Label(new Rect(16, 40, 230, 28), $"Memory: {System.GC.GetTotalMemory(false) / 1048576f:0.0} MB", style);
        GUI.Label(new Rect(16, 66, 230, 28), $"Frame: {Time.unscaledDeltaTime * 1000f:0.0} ms", style);
    }
}