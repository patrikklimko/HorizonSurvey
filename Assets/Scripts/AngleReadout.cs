using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using HorizonSurvey.Solar;

/// <summary>
/// Development readout: live elevation and azimuth from the AR camera pose, plus
/// the two heading calibration routes.
///
/// Deliberately IMGUI with no canvas or prefabs. This is an instrument panel for
/// development, not the product UI, and keeping it in one file means it costs
/// nothing to carry alongside the real interface later.
///
/// Note the project is set to new-input-only, so taps come from the Input System
/// rather than IMGUI events. Location and compass still come from the legacy
/// sensors, which is Unity's own recommendation since the new system has no
/// equivalent.
/// </summary>
[RequireComponent(typeof(LocationProvider))]
[RequireComponent(typeof(HeadingCalibrator))]
public class AngleReadout : MonoBehaviour
{
    [SerializeField] ARCameraManager cameraManager;

    Camera arCamera;
    LocationProvider location;
    HeadingCalibrator calibrator;
    GUIStyle textStyle;
    GUIStyle buttonStyle;

    Rect compassButton, sunButton, resetButton;

    void Awake()
    {
        location = GetComponent<LocationProvider>();
        calibrator = GetComponent<HeadingCalibrator>();
    }

    void Start()
    {
        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<ARCameraManager>();

        arCamera = cameraManager != null
            ? cameraManager.GetComponent<Camera>()
            : Camera.main;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
    }

    void LayoutButtons()
    {
        float h = Screen.height * 0.075f;
        float margin = Screen.width * 0.03f;
        float bottom = Screen.height - h - Screen.height * 0.05f;
        float w = (Screen.width - margin * 4f) / 3f;

        compassButton = new Rect(margin, bottom, w, h);
        sunButton = new Rect(margin * 2f + w, bottom, w, h);
        resetButton = new Rect(margin * 3f + w * 2f, bottom, w, h);
    }

    void Update()
    {
        LayoutButtons();

        var touch = Touchscreen.current;
        if (touch == null) return;
        if (!touch.primaryTouch.press.wasPressedThisFrame) return;

        // Input System reports screen space with Y up from the bottom.
        // IMGUI rects use Y down from the top.
        Vector2 p = touch.primaryTouch.position.ReadValue();
        Vector2 guiPoint = new Vector2(p.x, Screen.height - p.y);

        if (compassButton.Contains(guiPoint))
            calibrator.CalibrateFromCompass(arCamera, location);
        else if (sunButton.Contains(guiPoint))
            calibrator.CalibrateFromSun(arCamera, location);
        else if (resetButton.Contains(guiPoint))
            calibrator.Reset();
    }

    void OnGUI()
    {
        EnsureStyles();

        if (arCamera == null)
        {
            GUI.Label(new Rect(24, 24, Screen.width - 48, 200), "No AR camera found", textStyle);
            return;
        }

        Vector3 forward = arCamera.transform.forward;
        float elevation = CameraAngles.Elevation(forward);
        float azimuthAr = CameraAngles.AzimuthAR(forward);

        string azimuthLine = calibrator.IsCalibrated
            ? $"azim   {calibrator.ToTrueAzimuth(azimuthAr),7:F2}°  " +
              $"({calibrator.Reference}, +/-{calibrator.EstimatedUncertaintyDeg:F1})"
            : $"azim   {azimuthAr,7:F2}°  (UNCALIBRATED - session frame)";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"elev   {elevation,7:F2}°");
        sb.AppendLine(azimuthLine);
        sb.AppendLine();
        sb.AppendLine($"gps    {location.ShortSummary()}");
        sb.AppendLine($"comp   {FormatCompass()}");
        sb.AppendLine($"utc    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"sun    {FormatExpectedSun()}");
        sb.AppendLine();
        sb.AppendLine($"cal    {calibrator.Message}");
        sb.AppendLine($"track  {ARSession.state}");

        string text = sb.ToString();

        float panelHeight = textStyle.fontSize * 12.5f;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(12, 12, Screen.width - 24, panelHeight), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(26, 22, Screen.width - 40, Screen.height), text, textStyle);

        DrawCrosshair();

        DrawButton(compassButton, "Calibrate\ncompass");
        DrawButton(sunButton, "Sight\nsun");
        DrawButton(resetButton, "Reset");

        // Safety: the user aims the phone and watches the screen. Never the sun directly.
        var warn = new GUIStyle(textStyle) { fontSize = Mathf.RoundToInt(Screen.height * 0.018f) };
        warn.normal.textColor = new Color(1f, 0.85f, 0.4f);
        GUI.Label(new Rect(0, compassButton.y - Screen.height * 0.035f, Screen.width, 40f),
            "   Aim the phone at the sun - do not look at it directly", warn);
    }

    string FormatCompass()
    {
        if (!location.CompassEnabled) return "not enabled";
        float accuracy = location.CompassHeadingAccuracy;
        if (accuracy < 0f) return "reading invalid";
        return $"{location.CompassTrueHeading:F1}° true  (+/-{accuracy:F0}°)";
    }

    string FormatExpectedSun()
    {
        if (!location.HasFix) return "needs a GPS fix";

        SolarPosition sun = SolarEngine.Position(location.Latitude, location.Longitude, DateTime.UtcNow);
        string state = sun.ElevationDeg < HeadingCalibrator.MinimumSunElevationDeg
            ? "  TOO LOW to sight"
            : "";
        return $"az {sun.AzimuthDeg:F2}°  el {sun.ElevationDeg:F2}°{state}";
    }

    void DrawCrosshair()
    {
        float c = Screen.height * 0.012f;
        GUI.DrawTexture(new Rect(Screen.width / 2f - c, Screen.height / 2f - 1, c * 2, 2),
                        Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(Screen.width / 2f - 1, Screen.height / 2f - c, 2, c * 2),
                        Texture2D.whiteTexture);
    }

    void DrawButton(Rect r, string label)
    {
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(r, label, buttonStyle);
    }

    void EnsureStyles()
    {
        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.021f),
                alignment = TextAnchor.UpperLeft,
                wordWrap = false
            };
            textStyle.normal.textColor = Color.white;
        }

        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(Screen.height * 0.020f),
                alignment = TextAnchor.MiddleCenter
            };
            buttonStyle.normal.textColor = Color.white;
        }
    }
}
