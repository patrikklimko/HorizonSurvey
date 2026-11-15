using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using HorizonSurvey.Solar;

/// <summary>
/// The survey instrument panel: live angles, heading calibration, skyline tracing,
/// and saving a result.
///
/// Deliberately IMGUI with no canvas or prefabs - one file, nothing to wire up in
/// the scene. The project is new-Input-System-only, so taps come from Touchscreen
/// and are hit-tested against the rects drawn here. Location and compass still come
/// from the legacy sensors, which is Unity's recommendation as the new system has
/// no equivalent.
/// </summary>
[RequireComponent(typeof(LocationProvider))]
[RequireComponent(typeof(HeadingCalibrator))]
[RequireComponent(typeof(SkylineCapture))]
[RequireComponent(typeof(SurveyRecorder))]
[RequireComponent(typeof(SunPathOverlay))]
public class AngleReadout : MonoBehaviour
{
    [SerializeField] ARCameraManager cameraManager;

    /// <summary>Elevation shown at the top of the skyline strip chart.</summary>
    const float StripMaxElevationDeg = 40f;

    Camera arCamera;
    LocationProvider location;
    HeadingCalibrator calibrator;
    SkylineCapture capture;
    SurveyRecorder recorder;
    SunPathOverlay overlay;
    readonly ResultsView results = new ResultsView();

    GUIStyle textStyle, buttonStyle, smallStyle, chipStyle;

    Rect compassButton, sunButton, resetHeadingButton;
    Rect traceButton, clearButton, saveButton;
    Rect stripRect;
    Rect overlayChip, resultChip;
    Rect panelArea;
    float warningY;
    float topInset;

    enum Pressed { None, Trace, Other }
    Pressed pressed = Pressed.None;
    float clearArmedUntil = -1f;

    HorizonProfile previewProfile = new HorizonProfile();
    float nextPreviewAt;

    void Awake()
    {
        location = Ensure<LocationProvider>();
        calibrator = Ensure<HeadingCalibrator>();
        capture = Ensure<SkylineCapture>();
        recorder = Ensure<SurveyRecorder>();
        overlay = Ensure<SunPathOverlay>();
    }

    T Ensure<T>() where T : Component
    {
        if (!TryGetComponent(out T c)) c = gameObject.AddComponent<T>();
        return c;
    }

    void Start()
    {
        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<ARCameraManager>();

        arCamera = cameraManager != null
            ? cameraManager.GetComponent<Camera>()
            : Camera.main;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        overlay.Init(arCamera, calibrator, location);
    }

    // ---------------------------------------------------------------- input

    void Update()
    {
        Layout();

        var touch = Touchscreen.current;
        if (touch != null)
        {
            var primary = touch.primaryTouch;

            if (primary.press.wasPressedThisFrame)
                OnPress(ToGui(primary.position.ReadValue()));

            if (primary.press.wasReleasedThisFrame)
            {
                if (pressed == Pressed.Trace) capture.EndTrace();
                pressed = Pressed.None;
            }
        }

        if (capture.IsTracing)
            capture.TryRecord(arCamera);

        if (Time.unscaledTime >= nextPreviewAt)
        {
            previewProfile = capture.BuildProfile(calibrator);
            overlay.SetHorizon(previewProfile);
            nextPreviewAt = Time.unscaledTime + 0.2f;
        }
    }

    // Input System reports Y up from the bottom; IMGUI rects use Y down from the top.
    static Vector2 ToGui(Vector2 p) => new Vector2(p.x, Screen.height - p.y);

    void OnPress(Vector2 p)
    {
        // The results panel sits over the camera view; a tap on it closes it.
        // The buttons below it keep working, so a new survey can start at once.
        if (results.IsOpen && results.PanelRect.Contains(p))
        {
            pressed = Pressed.Other;
            results.Hide();
            return;
        }

        if (traceButton.Contains(p))
        {
            pressed = Pressed.Trace;
            capture.BeginTrace(arCamera);
            return;
        }

        pressed = Pressed.Other;

        if (compassButton.Contains(p))
            calibrator.CalibrateFromCompass(arCamera, location);
        else if (sunButton.Contains(p))
            calibrator.CalibrateFromSun(arCamera, location);
        else if (resetHeadingButton.Contains(p))
            calibrator.Reset();
        else if (saveButton.Contains(p))
        {
            if (recorder.ComputeAndSave(capture, calibrator, location))
                results.Show(recorder.LastRecord);
        }
        else if (overlayChip.Contains(p))
            overlay.Visible = !overlay.Visible;
        else if (resultChip.Contains(p))
            results.Reopen();
        else if (clearButton.Contains(p))
        {
            // Two taps to clear: losing a careful sweep to a stray thumb is painful.
            if (Time.unscaledTime < clearArmedUntil)
            {
                capture.Clear();
                clearArmedUntil = -1f;
            }
            else
            {
                clearArmedUntil = Time.unscaledTime + 2.5f;
            }
        }
    }

    void Layout()
    {
        // Keep clear of the Dynamic Island and the home indicator.
        Rect safe = Screen.safeArea;
        topInset = Screen.height - safe.yMax;
        float bottomInset = safe.yMin;

        float h = Screen.height * 0.07f;
        float gap = Screen.height * 0.012f;
        float margin = Screen.width * 0.03f;
        float w = (Screen.width - margin * 4f) / 3f;

        float row2 = Screen.height - h - Mathf.Max(Screen.height * 0.03f, bottomInset + gap);
        float row1 = row2 - gap - h;

        compassButton = new Rect(margin, row1, w, h);
        sunButton = new Rect(margin * 2f + w, row1, w, h);
        resetHeadingButton = new Rect(margin * 3f + w * 2f, row1, w, h);

        traceButton = new Rect(margin, row2, w, h);
        clearButton = new Rect(margin * 2f + w, row2, w, h);
        saveButton = new Rect(margin * 3f + w * 2f, row2, w, h);

        warningY = row1 - Screen.height * 0.03f;
        float stripH = Screen.height * 0.085f;
        stripRect = new Rect(margin, warningY - gap * 0.5f - stripH, Screen.width - margin * 2f, stripH);

        float chipH = Screen.height * 0.04f;
        float chipW = (Screen.width - margin * 3f) / 2f;
        float chipY = stripRect.y - gap * 0.6f - chipH;
        overlayChip = new Rect(margin, chipY, chipW, chipH);
        resultChip = new Rect(margin * 2f + chipW, chipY, chipW, chipH);

        float panelTop = topInset + gap;
        panelArea = new Rect(margin, panelTop, Screen.width - margin * 2f, chipY - gap - panelTop);
    }

    // ---------------------------------------------------------------- drawing

    void OnGUI()
    {
        EnsureStyles();

        if (arCamera == null)
        {
            GUI.Label(new Rect(24, topInset + 24, Screen.width - 48, 200), "No AR camera found", textStyle);
            return;
        }

        if (results.IsOpen)
        {
            results.Draw(panelArea);
        }
        else
        {
            DrawReadout();
            DrawCrosshair();
        }

        DrawStrip();

        DrawButton(overlayChip, overlay.Visible ? "Sun paths: ON" : "Sun paths: OFF", false, chipStyle);
        DrawButton(resultChip, results.HasRecord ? "Last result" : "No result yet", false, chipStyle);

        GUI.Label(new Rect(0, warningY, Screen.width, smallStyle.fontSize * 1.6f),
            "   Aim the phone at the sun - never look at it directly", smallStyle);

        DrawButton(compassButton, "Calibrate\ncompass", false, buttonStyle);
        DrawButton(sunButton, "Sight\nsun", false, buttonStyle);
        DrawButton(resetHeadingButton, "Reset\nheading", false, buttonStyle);

        DrawButton(traceButton, capture.IsTracing ? "TRACING\n(release)" : "HOLD\nto trace", capture.IsTracing, buttonStyle);
        DrawButton(clearButton,
            Time.unscaledTime < clearArmedUntil ? "Tap again\nto clear" : "Clear\ntrace",
            Time.unscaledTime < clearArmedUntil, buttonStyle);
        DrawButton(saveButton, "Save +\nresult", false, buttonStyle);
    }

    void DrawReadout()
    {
        Vector3 forward = arCamera.transform.forward;
        float elevation = CameraAngles.Elevation(forward);
        float azimuthAr = CameraAngles.AzimuthAR(forward);

        string azimuthLine = calibrator.IsCalibrated
            ? $"azim   {calibrator.ToTrueAzimuth(azimuthAr),7:F2}°  " +
              $"({calibrator.Reference}, ±{calibrator.EstimatedUncertaintyDeg:F1})"
            : $"azim   {azimuthAr,7:F2}°  (UNCALIBRATED)";

        var arc = recorder.SunArc(location);
        double arcCoverage = previewProfile.CoverageFractionOfArc(arc.start, arc.end);
        string moved = capture.MaxTranslationM > SkylineCapture.TranslationWarningM
            ? $"moved {capture.MaxTranslationM:F1} m - STAND STILL"
            : $"moved {capture.MaxTranslationM:F1} m";

        var sb = new StringBuilder();
        sb.AppendLine($"elev   {elevation,7:F2}°");
        sb.AppendLine(azimuthLine);
        sb.AppendLine($"gps    {location.ShortSummary()}");
        sb.AppendLine($"comp   {FormatCompass()}");
        float compassError = calibrator.LiveCompassErrorDeg(arCamera, location);
        if (!float.IsNaN(compassError))
            sb.AppendLine($"dev    compass {compassError:+0.0;-0.0}° vs sun");
        sb.AppendLine($"utc    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"sun    {FormatExpectedSun()}");
        sb.AppendLine($"cal    {calibrator.Message}");
        sb.AppendLine($"trace  {capture.Samples.Count} pts  arc {arcCoverage:P0}  {moved}");
        sb.AppendLine($"save   {(string.IsNullOrEmpty(recorder.Message) ? "-" : recorder.Message)}");
        sb.Append($"track  {ARSession.state}");

        // Long lines wrap rather than run off the edge, and the panel grows to fit.
        var content = new GUIContent(sb.ToString());
        float textWidth = Screen.width - 52f;
        float textHeight = textStyle.CalcHeight(content, textWidth);
        float top = topInset + 8f;
        Box(new Rect(12, top, Screen.width - 24, textHeight + 20f), 0.6f);
        GUI.Label(new Rect(26, top + 10f, textWidth, textHeight), content, textStyle);
    }

    /// <summary>
    /// Live skyline: bearing across, elevation up. Amber band = the bearings the sun
    /// can occupy at this site; that is the part that must be traced.
    /// </summary>
    void DrawStrip()
    {
        Rect r = stripRect;
        Box(r, 0.55f);

        var arc = recorder.SunArc(location);
        float x0 = r.x + r.width * (float)(arc.start / 360.0);
        float x1 = r.x + r.width * (float)(arc.end / 360.0);
        GUI.color = new Color(1f, 0.75f, 0.2f, 0.18f);
        GUI.DrawTexture(new Rect(x0, r.y, x1 - x0, r.height), Texture2D.whiteTexture);

        float binW = r.width / HorizonProfile.BinCount;
        GUI.color = Color.white;
        for (int i = 0; i < HorizonProfile.BinCount; i++)
        {
            if (!previewProfile.IsCovered(i)) continue;
            float el = Mathf.Clamp((float)previewProfile.RawBin(i), 0f, StripMaxElevationDeg);
            float bh = Mathf.Max(1f, r.height * el / StripMaxElevationDeg);
            GUI.DrawTexture(new Rect(r.x + i * binW, r.yMax - bh, Mathf.Max(1f, binW), bh),
                Texture2D.whiteTexture);
        }

        // where the crosshair is pointing right now
        float az = CameraAngles.AzimuthAR(arCamera.transform.forward);
        if (calibrator.IsCalibrated) az = calibrator.ToTrueAzimuth(az);
        az = CameraAngles.Mod360(az);
        GUI.color = new Color(1f, 0.3f, 0.3f, 0.95f);
        GUI.DrawTexture(new Rect(r.x + r.width * az / 360f - 1f, r.y, 2f, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float labelH = smallStyle.fontSize * 1.5f;
        string[] labels = { "N", "E", "S", "W" };
        for (int k = 0; k < 4; k++)
            GUI.Label(new Rect(r.x + r.width * k / 4f + 4f, r.y, labelH * 2f, labelH), labels[k], smallStyle);

        if (!calibrator.IsCalibrated)
            GUI.Label(new Rect(r.x + r.width * 0.3f, r.y, r.width, labelH), "session frame - calibrate heading", smallStyle);
    }

    string FormatCompass()
    {
        if (!location.CompassEnabled) return "not enabled";
        float accuracy = location.CompassHeadingAccuracy;
        if (accuracy < 0f) return "reading invalid";
        return $"{location.CompassTrueHeading:F1}° true  (±{accuracy:F0}°)";
    }

    string FormatExpectedSun()
    {
        if (!location.HasFix) return "needs a GPS fix";
        SolarPosition sun = SolarEngine.Position(location.Latitude, location.Longitude, DateTime.UtcNow);
        string state = sun.ElevationDeg < HeadingCalibrator.MinimumSunElevationDeg ? "  TOO LOW" : "";
        return $"az {sun.AzimuthDeg:F2}°  el {sun.ElevationDeg:F2}°{state}";
    }

    void DrawCrosshair()
    {
        float c = Screen.height * 0.012f;
        GUI.DrawTexture(new Rect(Screen.width / 2f - c, Screen.height / 2f - 1, c * 2, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(Screen.width / 2f - 1, Screen.height / 2f - c, 2, c * 2), Texture2D.whiteTexture);
    }

    static void DrawButton(Rect r, string label, bool active, GUIStyle style)
    {
        GUI.color = active ? new Color(0.75f, 0.15f, 0.1f, 0.85f) : new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(r, label, style);
    }

    static void Box(Rect r, float alpha)
    {
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void EnsureStyles()
    {
        if (textStyle != null) return;

        textStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.019f),
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        textStyle.normal.textColor = Color.white;

        buttonStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.019f),
            alignment = TextAnchor.MiddleCenter
        };
        buttonStyle.normal.textColor = Color.white;

        smallStyle = new GUIStyle(textStyle) { fontSize = Mathf.RoundToInt(Screen.height * 0.016f), wordWrap = false };
        smallStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);

        chipStyle = new GUIStyle(buttonStyle) { fontSize = Mathf.RoundToInt(Screen.height * 0.016f) };
    }
}
