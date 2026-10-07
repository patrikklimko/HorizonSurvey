using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class AngleReadout : MonoBehaviour
{
    [SerializeField] ARCameraManager cameraManager;

    // Calibrated in week 3. Until then azimuth is relative to session start.
    [SerializeField] float headingOffset = 0f;

    Camera arCamera;
    GUIStyle style;

    void Start()
    {
        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<ARCameraManager>();

        arCamera = cameraManager != null
            ? cameraManager.GetComponent<Camera>()
            : Camera.main;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.028f),
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = Color.white;
        }

        if (arCamera == null)
        {
            GUI.Label(new Rect(24, 24, Screen.width - 48, 200), "No AR camera found", style);
            return;
        }

        Vector3 fwd = arCamera.transform.forward;

        float elevation = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
        float azimuthAR = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        float azimuth   = Mod360(azimuthAR + headingOffset);

        string text =
            $"elev   {elevation,7:F2}\u00B0\n" +
            $"azim   {azimuth,7:F2}\u00B0  (uncalibrated)\n";

        if (cameraManager != null && cameraManager.TryGetIntrinsics(out XRCameraIntrinsics k))
        {
            text += $"focal  {k.focalLength.x:F1}, {k.focalLength.y:F1}\n" +
                    $"princ  {k.principalPoint.x:F1}, {k.principalPoint.y:F1}\n" +
                    $"res    {k.resolution.x}x{k.resolution.y}\n";
        }
        else
        {
            text += "intrinsics unavailable\n";
        }

        text += $"\ntracking  {ARSession.state}";

        // dark backing so it reads against bright sky
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(16, 16, Screen.width * 0.68f, style.fontSize * 8.5f),
                        Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(32, 26, Screen.width - 48, Screen.height), text, style);

        // crosshair at screen centre — this is the direction being measured
        float c = Screen.height * 0.012f;
        GUI.DrawTexture(new Rect(Screen.width / 2f - c, Screen.height / 2f - 1, c * 2, 2),
                        Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(Screen.width / 2f - 1, Screen.height / 2f - c, 2, c * 2),
                        Texture2D.whiteTexture);
    }

    static float Mod360(float a) => (a % 360f + 360f) % 360f;
}