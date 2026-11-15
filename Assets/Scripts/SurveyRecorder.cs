using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using HorizonSurvey.Solar;

/// <summary>Everything about one survey, as written to disk.</summary>
[Serializable]
public class SurveyRecord
{
    public string id;
    public string utc;
    public string device;

    public double latitude;
    public double longitude;
    public float gpsAccuracyM;

    public string headingReference;
    public float headingOffsetDeg;
    public float headingUncertaintyDeg;
    public float sunElevationCheckDeg;       // -999 when not a sun calibration
    public float compassHeadingAccuracyDeg;  // -999 when unavailable

    public float elevationOffsetDeg;
    public float maxTranslationM;

    public float sunArcFromDeg;
    public float sunArcToDeg;
    public float sunArcCoverage;   // 0..1, fraction of the sun's arc actually traced
    public float totalCoverage;    // 0..1, fraction of all 360 bearings traced

    public float[] horizonDeg;     // 360 bins, gaps interpolated
    public bool[] binTraced;       // 360 bins, true where measured rather than inferred

    public float annualAccessPct;  // -1 when not computed
    public float[] monthlyAccessPct;

    public SkylineCapture.Sample[] samples;

    /// <summary>Every heading calibration attempt in this AR session, accepted or not.</summary>
    public CalibrationEvent[] calibrations;
}

/// <summary>
/// Turns the current trace into a result and saves it.
///
/// Surveys go to Documents/surveys on the phone. File sharing is switched on by
/// the iOS post-build step, so they are visible in the Files app and in Finder
/// when the phone is plugged in - which is how the raw data gets into the report.
/// </summary>
public class SurveyRecorder : MonoBehaviour
{
    /// <summary>Below this, refuse to report a result. The missing sky could hide anything.</summary>
    public const float MinimumSunArcCoverage = 0.80f;

    public SurveyRecord LastRecord { get; private set; }
    public string LastPath { get; private set; }
    public string Message { get; private set; } = "";

    public static string Folder => Path.Combine(Application.persistentDataPath, "surveys");

    (double start, double end) _arc = (44, 316);
    double _arcLat = double.NaN;

    /// <summary>The sun's arc for this site, recomputed only when the position changes meaningfully.</summary>
    public (double start, double end) SunArc(LocationProvider location)
    {
        if (location != null && location.HasFix &&
            (double.IsNaN(_arcLat) || Math.Abs(location.Latitude - _arcLat) > 0.05))
        {
            _arc = SolarAccessCalculator.SunAzimuthArc(
                location.Latitude, location.Longitude, DateTime.UtcNow.Year);
            _arcLat = location.Latitude;
        }
        return _arc;
    }

    public bool ComputeAndSave(
        SkylineCapture capture,
        HeadingCalibrator calibrator,
        LocationProvider location)
    {
        if (capture.Samples.Count == 0)
        {
            Message = "nothing traced yet";
            return false;
        }
        if (!calibrator.IsCalibrated)
        {
            Message = "calibrate heading first (compass or sun)";
            return false;
        }
        if (!location.HasUsableFix)
        {
            Message = $"need a GPS fix better than {LocationProvider.UsableAccuracyMeters:F0} m";
            return false;
        }

        HorizonProfile measured = capture.BuildProfile(calibrator);
        var arc = SunArc(location);
        double arcCoverage = measured.CoverageFractionOfArc(arc.start, arc.end);

        if (arcCoverage < MinimumSunArcCoverage)
        {
            Message = $"only {arcCoverage:P0} of the sun's arc ({arc.start:F0}-{arc.end:F0} deg) traced - " +
                      $"need {MinimumSunArcCoverage:P0}";
            return false;
        }

        HorizonProfile filled = measured.WithGapsInterpolated();
        DateTime now = DateTime.UtcNow;
        SolarAccessResult result = SolarAccessCalculator.Compute(
            location.Latitude, location.Longitude, now.Year, filled);

        var record = new SurveyRecord
        {
            id = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture),
            utc = now.ToString("o", CultureInfo.InvariantCulture),
            device = SystemInfo.deviceModel,

            latitude = location.Latitude,
            longitude = location.Longitude,
            gpsAccuracyM = location.HorizontalAccuracyMeters,

            headingReference = calibrator.Reference.ToString(),
            headingOffsetDeg = calibrator.HeadingOffsetDeg,
            headingUncertaintyDeg = calibrator.EstimatedUncertaintyDeg,
            sunElevationCheckDeg = Safe(calibrator.LastElevationResidualDeg),
            compassHeadingAccuracyDeg = location.CompassEnabled
                ? location.CompassHeadingAccuracy
                : -999f,

            elevationOffsetDeg = CameraAngles.ElevationOffsetDeg,
            maxTranslationM = capture.MaxTranslationM,

            sunArcFromDeg = (float)arc.start,
            sunArcToDeg = (float)arc.end,
            sunArcCoverage = (float)arcCoverage,
            totalCoverage = (float)measured.CoverageFraction,

            horizonDeg = new float[HorizonProfile.BinCount],
            binTraced = new bool[HorizonProfile.BinCount],

            annualAccessPct = (float)result.AnnualPercent,
            monthlyAccessPct = new float[12],

            samples = new SkylineCapture.Sample[capture.Samples.Count],
            calibrations = new CalibrationEvent[calibrator.Events.Count]
        };

        for (int i = 0; i < HorizonProfile.BinCount; i++)
        {
            record.horizonDeg[i] = (float)filled.RawBin(i);
            record.binTraced[i] = measured.IsCovered(i);
        }
        for (int m = 0; m < 12; m++)
            record.monthlyAccessPct[m] = (float)result.MonthlyPercent[m];
        for (int i = 0; i < capture.Samples.Count; i++)
            record.samples[i] = capture.Samples[i];
        for (int i = 0; i < calibrator.Events.Count; i++)
            record.calibrations[i] = calibrator.Events[i];

        Directory.CreateDirectory(Folder);
        string basePath = Path.Combine(Folder, $"survey-{record.id}");
        File.WriteAllText(basePath + ".json", JsonUtility.ToJson(record, true));
        File.WriteAllText(basePath + "-horizon.csv", HorizonCsv(record));

        LastRecord = record;
        LastPath = basePath + ".json";
        Message = $"saved survey-{record.id}  annual {record.annualAccessPct:F1}%";
        Debug.Log($"[HorizonSurvey] {Message}\n{LastPath}");
        return true;
    }

    static string HorizonCsv(SurveyRecord r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("azimuth_deg,elevation_deg,traced");
        for (int i = 0; i < r.horizonDeg.Length; i++)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F1},{1:F2},{2}", i + 0.5, r.horizonDeg[i], r.binTraced[i] ? 1 : 0));
        return sb.ToString();
    }

    static float Safe(float v) => float.IsNaN(v) || float.IsInfinity(v) ? -999f : v;
}
