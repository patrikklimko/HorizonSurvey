using System;
using System.Collections.Generic;
using UnityEngine;
using HorizonSurvey.Solar;

/// <summary>
/// Draws the sun's real path across the real sky, in the live camera view.
///
/// This is the part of the app that only makes sense in AR. The paths for the June
/// solstice, the equinox, the December solstice and today are placed on a sky dome
/// around the camera, so they line up with the actual roofs and trees in view.
/// Where a path passes behind the traced skyline it is drawn dim: that is the time
/// of day and year the spot is in shade, visible at a glance.
///
/// Geometry. The sky is effectively at infinity, so the dome follows the camera's
/// position but never its rotation; only its yaw is set, from the heading
/// calibration. Inside the dome, points are laid out by TRUE azimuth. Rotating the
/// dome by minus the heading offset turns true azimuth back into the AR session's
/// frame. Recalibrating therefore costs nothing - only the dome's yaw changes.
///
/// Nothing is drawn until the heading is calibrated: a sun path in an arbitrary
/// frame would be confidently wrong, which is worse than absent.
/// </summary>
public class SunPathOverlay : MonoBehaviour
{
    [SerializeField] float radiusM = 15f;
    [SerializeField] float litWidthDeg = 0.30f;
    [SerializeField] float dimWidthDeg = 0.14f;
    [SerializeField] int stepMinutes = 2;

    public bool Visible { get; set; } = true;

    enum Exposure { Lit, Shaded, Unknown }

    struct SunPoint
    {
        public float az, el;
        public bool hourMark;
    }

    class SunPath
    {
        public string name;
        public Color color;
        public List<SunPoint> points = new List<SunPoint>();
    }

    static readonly Color SkylineColor = new Color(0.25f, 0.9f, 1f, 0.95f);
    // Saturated magenta: a white ring vanished into the sun's glare in the first field test.
    static readonly Color NowColor = new Color(1f, 0.2f, 0.75f, 1f);

    Camera cam;
    HeadingCalibrator calibrator;
    LocationProvider location;

    Transform dome;
    Material material;
    readonly List<LineRenderer> pool = new List<LineRenderer>();
    int used;
    LineRenderer nowRing;

    readonly List<SunPath> paths = new List<SunPath>();
    double builtLat = double.NaN, builtLon = double.NaN;
    DateTime builtLocalDate;

    HorizonProfile measured;
    HorizonProfile filled;
    bool dirty = true;
    float nextNowUpdate;

    public void Init(Camera arCamera, HeadingCalibrator headingCalibrator, LocationProvider locationProvider)
    {
        cam = arCamera;
        calibrator = headingCalibrator;
        location = locationProvider;

        dome = new GameObject("SunPathDome").transform;

        // Sprites/Default is on Unity's always-included shader list, so it survives
        // the build, and URP draws it unlit with vertex colours - exactly right here.
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
        material = new Material(shader);

        nowRing = CreateLine("SunNow");
        nowRing.loop = true;

        if (cam != null && cam.farClipPlane < radiusM * 2f)
            cam.farClipPlane = radiusM * 2f;
    }

    /// <summary>The measured skyline, in true azimuth. Call whenever the trace changes.</summary>
    public void SetHorizon(HorizonProfile profile)
    {
        measured = profile;
        filled = profile != null && profile.CoverageFraction > 0 ? profile.WithGapsInterpolated() : null;
        dirty = true;
    }

    void LateUpdate()
    {
        if (dome == null || cam == null) return;

        bool show = Visible && calibrator.IsCalibrated && location.HasFix;
        if (dome.gameObject.activeSelf != show) dome.gameObject.SetActive(show);
        if (!show) return;

        dome.SetPositionAndRotation(
            cam.transform.position,
            Quaternion.Euler(0f, -calibrator.HeadingOffsetDeg, 0f));

        if (NeedsNewPaths())
        {
            BuildPaths();
            dirty = true;
        }

        if (dirty)
        {
            RebuildLines();
            dirty = false;
        }

        if (Time.unscaledTime >= nextNowUpdate)
        {
            UpdateNowRing();
            nextNowUpdate = Time.unscaledTime + 1f;
        }
    }

    // ------------------------------------------------------------ sun paths

    bool NeedsNewPaths() =>
        double.IsNaN(builtLat) ||
        Math.Abs(location.Latitude - builtLat) > 0.05 ||
        Math.Abs(location.Longitude - builtLon) > 0.05 ||
        DateTime.Now.Date != builtLocalDate;

    void BuildPaths()
    {
        builtLat = location.Latitude;
        builtLon = location.Longitude;
        builtLocalDate = DateTime.Now.Date;
        int year = builtLocalDate.Year;

        paths.Clear();
        paths.Add(MakePath("June solstice", new DateTime(year, 6, 21), new Color(1f, 0.55f, 0.1f, 1f)));
        paths.Add(MakePath("Equinox", new DateTime(year, 3, 20), new Color(1f, 0.88f, 0.25f, 1f)));
        paths.Add(MakePath("December solstice", new DateTime(year, 12, 21), new Color(0.45f, 0.75f, 1f, 1f)));
        paths.Add(MakePath("Today", builtLocalDate, new Color(1f, 1f, 1f, 1f)));
    }

    SunPath MakePath(string name, DateTime localDate, Color color)
    {
        var path = new SunPath { name = name, color = color };
        DateTime startUtc = LocalToUtc(localDate);

        for (int minute = 0; minute <= 1440; minute += stepMinutes)
        {
            DateTime utc = startUtc.AddMinutes(minute);
            SolarPosition sun = SolarEngine.Position(builtLat, builtLon, utc);
            if (sun.ElevationDeg <= 0.0) continue;

            DateTime local = UtcToLocal(utc);
            path.points.Add(new SunPoint
            {
                az = (float)sun.AzimuthDeg,
                el = (float)sun.ElevationDeg,
                hourMark = local.Minute == 0 && local.Second == 0
            });
        }
        return path;
    }

    static DateTime LocalToUtc(DateTime localDate)
    {
        try
        {
            var unspecified = new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        }
        catch
        {
            return new DateTime(localDate.Year, localDate.Month, localDate.Day, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    static DateTime UtcToLocal(DateTime utc)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.Local); }
        catch { return utc; }
    }

    Exposure ExposureAt(float az, float el)
    {
        if (measured == null || filled == null) return Exposure.Unknown;
        if (!measured.IsCovered(HorizonProfile.BinOf(az))) return Exposure.Unknown;
        return el > filled.ElevationAt(az) ? Exposure.Lit : Exposure.Shaded;
    }

    // ------------------------------------------------------------ geometry

    void RebuildLines()
    {
        used = 0;

        foreach (SunPath path in paths)
            DrawPath(path);

        DrawSkyline();

        for (int i = used; i < pool.Count; i++)
            pool[i].gameObject.SetActive(false);
    }

    void DrawPath(SunPath path)
    {
        var run = new List<Vector3>();
        Exposure runState = Exposure.Unknown;

        for (int i = 0; i < path.points.Count; i++)
        {
            SunPoint p = path.points[i];
            Exposure state = ExposureAt(p.az, p.el);

            if (run.Count > 0 && state != runState)
            {
                // The new run starts on the previous point, so consecutive runs
                // touch without a gap and the changeover segment takes the new style.
                Emit(run, path.color, runState);
                run.Clear();
                run.Add(Dir(path.points[i - 1].az, path.points[i - 1].el));
            }

            run.Add(Dir(p.az, p.el));
            runState = state;

            if (p.hourMark)
                DrawHourTick(p, path.color, state);
        }

        if (run.Count >= 2) Emit(run, path.color, runState);
    }

    void DrawHourTick(SunPoint p, Color color, Exposure state)
    {
        const float half = 0.8f;
        LineRenderer lr = Take();
        lr.loop = false;
        lr.positionCount = 2;
        lr.SetPosition(0, Dir(p.az, p.el - half));
        lr.SetPosition(1, Dir(p.az, p.el + half));
        Style(lr, color, state);
    }

    void DrawSkyline()
    {
        if (measured == null) return;

        int n = HorizonProfile.BinCount;
        int covered = 0;
        for (int i = 0; i < n; i++) if (measured.IsCovered(i)) covered++;
        if (covered == 0) return;

        var run = new List<Vector3>();

        if (covered == n)
        {
            for (int i = 0; i < n; i++) run.Add(Dir(i + 0.5f, (float)measured.RawBin(i)));
            LineRenderer loop = Take();
            SetPositions(loop, run);
            loop.loop = true;
            StyleSkyline(loop);
            return;
        }

        // Start just after an untraced bin so no run is split across north.
        int start = 0;
        while (measured.IsCovered(start)) start++;

        for (int k = 1; k <= n; k++)
        {
            int b = (start + k) % n;
            if (measured.IsCovered(b))
            {
                run.Add(Dir(b + 0.5f, (float)measured.RawBin(b)));
                continue;
            }
            FlushSkyline(run);
        }
        FlushSkyline(run);
    }

    void FlushSkyline(List<Vector3> run)
    {
        if (run.Count == 0) return;
        if (run.Count == 1)
        {
            // A single traced degree still deserves a visible mark.
            Vector3 p = run[0];
            run.Clear();
            run.Add(Quaternion.Euler(0f, -0.5f, 0f) * p);
            run.Add(Quaternion.Euler(0f, 0.5f, 0f) * p);
        }
        LineRenderer lr = Take();
        SetPositions(lr, run);
        lr.loop = false;
        StyleSkyline(lr);
        run.Clear();
    }

    void UpdateNowRing()
    {
        SolarPosition sun = SolarEngine.Position(location.Latitude, location.Longitude, DateTime.UtcNow);
        bool up = sun.ElevationDeg > 0.0;
        nowRing.gameObject.SetActive(up);
        if (!up) return;

        // A circle about eight sun-diameters across, centred where the sun should be.
        // If the heading calibration is right, the real sun sits in the middle of it.
        // Big enough to stay visible outside the camera's glare around the sun.
        const int segments = 40;
        const float radiusDeg = 2.0f;
        float az = (float)sun.AzimuthDeg, el = (float)sun.ElevationDeg;
        float cosEl = Mathf.Max(0.2f, Mathf.Cos(el * Mathf.Deg2Rad));

        nowRing.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float t = i * Mathf.PI * 2f / segments;
            nowRing.SetPosition(i, Dir(az + radiusDeg * Mathf.Cos(t) / cosEl, el + radiusDeg * Mathf.Sin(t)));
        }
        nowRing.widthMultiplier = WidthMetres(0.35f);
        nowRing.startColor = nowRing.endColor = NowColor;
    }

    Vector3 Dir(float trueAzimuthDeg, float elevationDeg) =>
        CameraAngles.DirectionAR(trueAzimuthDeg, elevationDeg) * radiusM;

    float WidthMetres(float widthDeg) => radiusM * widthDeg * Mathf.Deg2Rad;

    void Emit(List<Vector3> run, Color color, Exposure state)
    {
        if (run.Count < 2) return;
        LineRenderer lr = Take();
        lr.loop = false;
        SetPositions(lr, run);
        Style(lr, color, state);
    }

    void Style(LineRenderer lr, Color color, Exposure state)
    {
        Color c = color;
        float width = litWidthDeg;
        switch (state)
        {
            case Exposure.Shaded:
                c = Color.Lerp(color, new Color(0.35f, 0.35f, 0.4f), 0.6f);
                c.a = 0.45f;
                width = dimWidthDeg;
                break;
            case Exposure.Unknown:
                c.a = 0.55f;
                width = dimWidthDeg;
                break;
        }
        lr.startColor = lr.endColor = c;
        lr.widthMultiplier = WidthMetres(width);
    }

    void StyleSkyline(LineRenderer lr)
    {
        lr.startColor = lr.endColor = SkylineColor;
        lr.widthMultiplier = WidthMetres(0.22f);
    }

    static void SetPositions(LineRenderer lr, List<Vector3> pts)
    {
        lr.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++) lr.SetPosition(i, pts[i]);
    }

    LineRenderer Take()
    {
        LineRenderer lr;
        if (used < pool.Count)
        {
            lr = pool[used];
            lr.gameObject.SetActive(true);
        }
        else
        {
            lr = CreateLine("SunPathLine");
            pool.Add(lr);
        }
        used++;
        return lr;
    }

    LineRenderer CreateLine(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(dome, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.alignment = LineAlignment.View;
        lr.sharedMaterial = material;
        lr.numCapVertices = 2;
        lr.numCornerVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.textureMode = LineTextureMode.Stretch;
        return lr;
    }

    void OnDestroy()
    {
        if (dome != null) Destroy(dome.gameObject);
        if (material != null) Destroy(material);
    }
}
