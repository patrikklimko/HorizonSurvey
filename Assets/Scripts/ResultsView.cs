using System;
using System.Globalization;
using UnityEngine;
using HorizonSurvey.Solar;

/// <summary>
/// The answer screen, shown after a survey is saved.
///
/// Three layers, in the order a homeowner asks the questions:
///   1. One number - how much of the year's clear-sky sunlight this spot keeps.
///   2. A sun chart - the measured skyline with the sun's path for each month
///      drawn over it, so you can see WHICH obstruction costs WHEN. This is the
///      same diagram a Solmetric SunEye prints.
///   3. Monthly bars - where the losses fall across the year.
///
/// Drawn with IMGUI like the rest of the HUD. The sun chart is rendered once into
/// a texture when a survey is shown, rather than as a thousand IMGUI calls a frame.
/// Everything comes from the SurveyRecord alone, so a saved survey can be shown
/// again later without the trace that produced it.
/// </summary>
public class ResultsView
{
    // Chart window: the bearings the sun can occupy at Horsens, elevations up to the
    // June noon sun with a little headroom.
    const float ChartMaxElevationDeg = 62f;
    const int TexW = 900, TexH = 420;

    static readonly string[] MonthLetters = { "J", "F", "M", "A", "M", "J", "J", "A", "S", "O", "N", "D" };

    static readonly Color32 Background = new Color32(18, 22, 30, 235);
    static readonly Color32 Grid = new Color32(70, 78, 92, 255);
    static readonly Color32 HorizonTraced = new Color32(120, 112, 100, 255);
    static readonly Color32 HorizonGuessed = new Color32(85, 80, 74, 255);
    static readonly Color32 SunLit = new Color32(255, 200, 60, 255);
    static readonly Color32 SunShaded = new Color32(105, 115, 140, 255);

    SurveyRecord record;
    Texture2D chart;
    float azFrom, azTo;

    GUIStyle big, body, small, label, labelLeft;

    public bool IsOpen { get; private set; }
    public bool HasRecord => record != null;
    public Rect PanelRect { get; private set; }

    public void Show(SurveyRecord r)
    {
        if (r == null) return;
        if (r != record)
        {
            record = r;
            BuildChart();
        }
        IsOpen = true;
    }

    public void Reopen()
    {
        if (record != null) IsOpen = true;
    }

    public void Hide() => IsOpen = false;

    // ------------------------------------------------------------ chart texture

    void BuildChart()
    {
        azFrom = record.sunArcFromDeg > 0 ? record.sunArcFromDeg : 44f;
        azTo = record.sunArcToDeg > azFrom ? record.sunArcToDeg : 316f;

        if (chart == null)
        {
            chart = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        var px = new Color32[TexW * TexH];
        for (int i = 0; i < px.Length; i++) px[i] = Background;

        // grid: every 15 deg of elevation, every 45 deg of bearing
        for (float el = 15f; el < ChartMaxElevationDeg; el += 15f)
            HLine(px, Y(el), Grid);
        for (float az = 45f; az <= 315f; az += 45f)
            if (az > azFrom && az < azTo) VLine(px, X(az), Grid);

        // measured skyline: solid where traced, darker where filled in between
        var horizon = new HorizonProfile();
        for (int b = 0; b < HorizonProfile.BinCount; b++)
            horizon.SetBin(b, record.horizonDeg[b]);

        for (int x = 0; x < TexW; x++)
        {
            float az = Mathf.Lerp(azFrom, azTo, (x + 0.5f) / TexW);
            int bin = HorizonProfile.BinOf(az);
            float h = (float)horizon.ElevationAt(az);
            int top = Mathf.Clamp(Y(h), 0, TexH - 1);
            bool traced = record.binTraced != null && record.binTraced[bin];
            Color32 c = traced ? HorizonTraced : HorizonGuessed;
            for (int y = 0; y <= top; y++)
            {
                // hatch the guessed parts so they read as "inferred, not measured"
                if (!traced && ((x + y) / 6) % 2 == 0) continue;
                px[y * TexW + x] = c;
            }
        }

        // the sun on the 21st of each month; Jul-Nov retrace Jan-May closely,
        // so seven curves cover the year without clutter
        int year = DateTime.UtcNow.Year;
        int[] months = { 12, 1, 2, 3, 4, 5, 6 };
        foreach (int m in months)
        {
            var day = new DateTime(year, m, 21, 0, 0, 0, DateTimeKind.Utc);
            for (int minute = 0; minute < 1440; minute += 3)
            {
                SolarPosition sun = SolarEngine.Position(record.latitude, record.longitude, day.AddMinutes(minute));
                if (sun.ElevationDeg <= 0.0) continue;
                float az = (float)sun.AzimuthDeg, el = (float)sun.ElevationDeg;
                if (az < azFrom || az > azTo) continue;

                bool lit = el > horizon.ElevationAt(az);
                Dot(px, X(az), Y(el), lit ? 3 : 2, lit ? SunLit : SunShaded);
            }
        }

        chart.SetPixels32(px);
        chart.Apply(false);
    }

    int X(float az) => Mathf.RoundToInt((az - azFrom) / (azTo - azFrom) * (TexW - 1));
    static int Y(float el) => Mathf.RoundToInt(Mathf.Clamp(el, 0f, ChartMaxElevationDeg) / ChartMaxElevationDeg * (TexH - 1));

    static void HLine(Color32[] px, int y, Color32 c)
    {
        if (y < 0 || y >= TexH) return;
        for (int x = 0; x < TexW; x++) px[y * TexW + x] = c;
    }

    static void VLine(Color32[] px, int x, Color32 c)
    {
        if (x < 0 || x >= TexW) return;
        for (int y = 0; y < TexH; y++) px[y * TexW + x] = c;
    }

    static void Dot(Color32[] px, int cx, int cy, int r, Color32 c)
    {
        for (int dy = -r; dy <= r; dy++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dy * dy > r * r) continue;
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= TexW || y >= TexH) continue;
            px[y * TexW + x] = c;
        }
    }

    // ------------------------------------------------------------ drawing

    public void Draw(Rect area)
    {
        if (!IsOpen || record == null) return;
        EnsureStyles();
        PanelRect = area;

        Fill(area, new Color(0.04f, 0.05f, 0.07f, 0.92f));

        float pad = area.width * 0.04f;
        float x = area.x + pad, w = area.width - pad * 2f;
        float y = area.y + pad * 0.6f;

        // 1. the number
        GUI.Label(new Rect(x, y, w, big.fontSize * 1.3f),
            $"{record.annualAccessPct:F1} %", big);
        y += big.fontSize * 1.25f;
        GUI.Label(new Rect(x, y, w, body.fontSize * 2.6f),
            "of the year's clear-sky sunlight reaches this spot\n(flat panel, measured skyline)", body);
        y += body.fontSize * 2.8f;

        string heading = record.headingReference == "Sun"
            ? $"heading: sun sighting, ±{record.headingUncertaintyDeg:F1}°"
            : $"heading: {record.headingReference.ToLowerInvariant()}, ±{record.headingUncertaintyDeg:F0}° - sight the sun to improve";
        GUI.Label(new Rect(x, y, w, small.fontSize * 1.4f), heading, small);
        y += small.fontSize * 1.45f;
        GUI.Label(new Rect(x, y, w, small.fontSize * 1.4f),
            $"sun's arc traced {record.sunArcCoverage:P0}   ·   moved {record.maxTranslationM:F1} m while tracing", small);
        y += small.fontSize * 1.9f;

        // 2. sun chart
        float chartH = Mathf.Min(area.height * 0.38f, w * TexH / TexW);
        var chartRect = new Rect(x, y, w, chartH);
        GUI.color = Color.white;
        GUI.DrawTexture(chartRect, chart, ScaleMode.StretchToFill, true);

        // axis labels under the chart
        float ly = chartRect.yMax + 2f;
        (float az, string name)[] ticks = { (90f, "E"), (135f, "SE"), (180f, "S"), (225f, "SW"), (270f, "W") };
        foreach (var t in ticks)
        {
            float tx = chartRect.x + (t.az - azFrom) / (azTo - azFrom) * chartRect.width;
            GUI.Label(new Rect(tx - 40f, ly, 80f, label.fontSize * 1.5f), t.name, label);
        }
        for (float el = 15f; el < ChartMaxElevationDeg; el += 15f)
        {
            float ty = chartRect.yMax - el / ChartMaxElevationDeg * chartRect.height;
            GUI.Label(new Rect(chartRect.x + 4f, ty - label.fontSize * 1.2f, 80f, label.fontSize * 1.4f),
                $"{el:F0}°", labelLeft);
        }
        y = ly + label.fontSize * 1.5f;
        GUI.Label(new Rect(x, y, w, small.fontSize * 1.4f),
            "sun on the 21st of each month:  yellow = sunlit,  grey = shaded", small);
        y += small.fontSize * 2.0f;

        // 3. monthly bars
        float barsBottom = area.yMax - pad - small.fontSize * 3.2f;
        float barsTop = y + label.fontSize * 1.4f;
        float barsH = Mathf.Max(40f, barsBottom - barsTop);
        float slot = w / 12f;
        for (int m = 0; m < 12; m++)
        {
            float pct = record.monthlyAccessPct != null && record.monthlyAccessPct.Length == 12
                ? Mathf.Clamp(record.monthlyAccessPct[m], 0f, 100f) : 0f;
            float bh = barsH * pct / 100f;
            float bx = x + slot * m + slot * 0.15f;
            float bw = slot * 0.7f;

            Fill(new Rect(bx, barsTop, bw, barsH), new Color(1f, 1f, 1f, 0.07f));
            Fill(new Rect(bx, barsTop + barsH - bh, bw, bh), Color.Lerp(
                new Color(0.55f, 0.55f, 0.6f), new Color(1f, 0.78f, 0.22f), pct / 100f));

            GUI.Label(new Rect(x + slot * m, barsTop - label.fontSize * 1.35f, slot, label.fontSize * 1.4f),
                pct.ToString("F0", CultureInfo.InvariantCulture), label);
            GUI.Label(new Rect(x + slot * m, barsTop + barsH + 2f, slot, label.fontSize * 1.4f),
                MonthLetters[m], label);
        }

        GUI.Label(new Rect(x, area.yMax - pad - small.fontSize * 1.3f, w, small.fontSize * 1.4f),
            $"survey-{record.id}.json   ·   tap here to close", small);
    }

    static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    void EnsureStyles()
    {
        if (big != null) return;

        big = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.05f),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft
        };
        big.normal.textColor = new Color(1f, 0.82f, 0.3f);

        body = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(Screen.height * 0.017f),
            alignment = TextAnchor.UpperLeft,
            wordWrap = true
        };
        body.normal.textColor = Color.white;

        small = new GUIStyle(body) { fontSize = Mathf.RoundToInt(Screen.height * 0.0135f), wordWrap = false };
        small.normal.textColor = new Color(0.8f, 0.83f, 0.88f);

        label = new GUIStyle(small) { alignment = TextAnchor.UpperCenter };
        labelLeft = new GUIStyle(small) { alignment = TextAnchor.UpperLeft };
    }
}
