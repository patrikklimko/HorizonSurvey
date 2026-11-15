using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using HorizonSurvey.Solar;

public enum HeadingReference
{
    None,
    Compass,
    Sun
}

/// <summary>
/// One calibration attempt, accepted or not. Every attempt is appended to
/// surveys/calibrations.csv the moment it happens, so field data never depends on
/// remembering to take a screenshot.
/// </summary>
[Serializable]
public struct CalibrationEvent
{
    public string utc;
    public string kind;                 // "sun" or "compass"
    public bool accepted;
    public float offsetDeg;             // AR-frame -> true bearing offset this attempt produced
    public float cameraAzimuthArDeg;
    public float cameraElevationDeg;
    public float sunAzimuthDeg;         // -999 for compass events
    public float sunElevationDeg;       // -999 for compass events
    public float elevationResidualDeg;  // -999 for compass events
    public float compassTrueHeadingDeg; // live compass reading, -999 if unavailable
    public float compassAccuracyDeg;    // -999 if unavailable
    public float compassErrorDeg;       // compass minus truth, -999 if no sun reference yet
    public double latitude;
    public double longitude;
    public float gpsAccuracyM;
}

/// <summary>
/// Establishes which way is north, so that bearings in the AR frame become real
/// compass bearings.
///
/// Two routes, deliberately:
///
///   Compass. Always available, works in any weather, and not very good. Phone
///   magnetometers are pulled by steel structures, wiring, vehicles and laptops.
///   Measured on 15 Nov 2026 at a window in Horsens: 13 degrees out, beyond the
///   +/-11 degrees iOS itself claimed.
///
///   Sun sighting. If the time and position are known, the sun's bearing is known
///   to a fraction of a degree. Aiming at it therefore gives a far better heading
///   reference than the magnetometer - the same trick a surveyor uses with a solar
///   compass. Three sightings on 15 Nov agreed to within 0.7 degrees.
///
/// The elevation cross-check is what makes the sun route trustworthy. Elevation
/// needs no calibration at all, so if the computed and observed elevations disagree,
/// either the aim was off or the device clock is wrong - and a wrong clock would
/// otherwise poison every bearing silently.
///
/// Because every sun sighting also reads the compass, each one measures the
/// compass error for free. That is the data behind the "how bad is the compass"
/// part of the write-up.
/// </summary>
public class HeadingCalibrator : MonoBehaviour
{
    /// <summary>Reject a sun sighting if observed elevation is further than this from computed.</summary>
    public const float ElevationGateDeg = 2.0f;

    /// <summary>Below this the sun is too low to aim at reliably, and refraction is largest.</summary>
    public const float MinimumSunElevationDeg = 5.0f;

    /// <summary>
    /// A new sun sighting further than this from the previous accepted one is not
    /// trusted on its own. Found in the field on 15 Nov 2026: a chimney top that
    /// happened to stand at the sun's elevation, 10 degrees to one side of it, passed
    /// the elevation gate. Elevation cannot see a sideways mistake; a second sighting can.
    /// </summary>
    public const float SightingAgreementDeg = 2.0f;

    const float Missing = -999f;

    public HeadingReference Reference { get; private set; } = HeadingReference.None;
    public float HeadingOffsetDeg { get; private set; }
    public string Message { get; private set; } = "not calibrated";

    /// <summary>Elevation residual from the last sun sighting. NaN if none taken.</summary>
    public float LastElevationResidualDeg { get; private set; } = float.NaN;

    public DateTime? CalibratedAtUtc { get; private set; }

    public bool IsCalibrated => Reference != HeadingReference.None;

    /// <summary>
    /// Offset from the most recent accepted sun sighting in this AR session, kept even
    /// after a compass calibration replaces it, so the compass can be judged against it.
    /// </summary>
    public float LastSunOffsetDeg { get; private set; } = float.NaN;
    public bool HasSunReference => !float.IsNaN(LastSunOffsetDeg);

    readonly List<CalibrationEvent> _events = new List<CalibrationEvent>();

    /// <summary>Every attempt this session, oldest first.</summary>
    public IReadOnlyList<CalibrationEvent> Events => _events;

    /// <summary>Convert a bearing in the AR frame into a true compass bearing.</summary>
    public float ToTrueAzimuth(float azimuthArDeg) =>
        CameraAngles.Mod360(azimuthArDeg + HeadingOffsetDeg);

    /// <summary>
    /// Rough estimate of how far out the heading could be, in degrees.
    /// Honest uncertainty beats a precise-looking number nobody can justify.
    /// </summary>
    public float EstimatedUncertaintyDeg => Reference switch
    {
        HeadingReference.Sun => 0.5f,
        HeadingReference.Compass => 5.0f,
        _ => float.NaN
    };

    /// <summary>
    /// Live compass error: what the compass says minus the sun-derived truth for the
    /// direction the camera points right now. NaN without a sun sighting or a compass.
    /// </summary>
    public float LiveCompassErrorDeg(Camera arCamera, LocationProvider location)
    {
        if (!HasSunReference || arCamera == null || location == null) return float.NaN;
        if (!location.CompassEnabled || location.CompassHeadingAccuracy < 0f) return float.NaN;

        float truth = CameraAngles.Mod360(
            CameraAngles.AzimuthAR(arCamera.transform.forward) + LastSunOffsetDeg);
        return CameraAngles.AngleDifference(location.CompassTrueHeading, truth);
    }

    public bool CalibrateFromCompass(Camera arCamera, LocationProvider location)
    {
        if (arCamera == null || location == null)
        {
            Message = "compass: missing camera or location";
            return false;
        }

        if (!location.CompassEnabled)
        {
            Message = "compass: not enabled";
            return false;
        }

        float heading = location.CompassTrueHeading;
        float accuracy = location.CompassHeadingAccuracy;

        if (accuracy < 0f)
        {
            Message = "compass: reading invalid - move away from metal and try again";
            return false;
        }

        Vector3 forward = arCamera.transform.forward;
        float cameraAzimuthAr = CameraAngles.AzimuthAR(forward);
        float offset = CameraAngles.Mod360(heading - cameraAzimuthAr);

        float compassError = HasSunReference
            ? CameraAngles.AngleDifference(offset, LastSunOffsetDeg)
            : Missing;

        HeadingOffsetDeg = offset;
        Reference = HeadingReference.Compass;
        CalibratedAtUtc = DateTime.UtcNow;
        LastElevationResidualDeg = float.NaN;

        Message = HasSunReference
            ? $"compass: offset {offset:F1} (+/-{accuracy:F0}), {compassError:+0.0;-0.0} vs sun"
            : $"compass: offset {offset:F1} deg (device +/-{accuracy:F0})";

        Record(new CalibrationEvent
        {
            utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            kind = "compass",
            accepted = true,
            offsetDeg = offset,
            cameraAzimuthArDeg = cameraAzimuthAr,
            cameraElevationDeg = CameraAngles.Elevation(forward),
            sunAzimuthDeg = Missing,
            sunElevationDeg = Missing,
            elevationResidualDeg = Missing,
            compassTrueHeadingDeg = heading,
            compassAccuracyDeg = accuracy,
            compassErrorDeg = compassError
        }, location);
        return true;
    }

    /// <summary>
    /// Aim the crosshair at the sun and call this. Never look at the sun directly -
    /// point the phone and watch the screen.
    /// </summary>
    public bool CalibrateFromSun(Camera arCamera, LocationProvider location)
    {
        if (arCamera == null || location == null)
        {
            Message = "sun: missing camera or location";
            return false;
        }

        if (!location.HasUsableFix)
        {
            Message = $"sun: need a GPS fix better than {LocationProvider.UsableAccuracyMeters:F0} m";
            return false;
        }

        DateTime nowUtc = DateTime.UtcNow;
        SolarPosition sun = SolarEngine.Position(location.Latitude, location.Longitude, nowUtc);

        if (sun.ElevationDeg < MinimumSunElevationDeg)
        {
            Message = $"sun: computed elevation {sun.ElevationDeg:F1} deg - too low to sight";
            return false;
        }

        Vector3 forward = arCamera.transform.forward;
        float observedElevation = CameraAngles.Elevation(forward);
        float observedAzimuthAr = CameraAngles.AzimuthAR(forward);
        float residual = observedElevation - (float)sun.ElevationDeg;
        float offset = CameraAngles.Mod360((float)sun.AzimuthDeg - observedAzimuthAr);
        bool elevationOk = Mathf.Abs(residual) <= ElevationGateDeg;
        float change = HasSunReference ? CameraAngles.AngleDifference(offset, LastSunOffsetDeg) : float.NaN;
        bool agrees = float.IsNaN(change) || Mathf.Abs(change) <= SightingAgreementDeg;
        bool accepted = elevationOk && agrees;

        bool compassValid = location.CompassEnabled && location.CompassHeadingAccuracy >= 0f;
        var ev = new CalibrationEvent
        {
            utc = nowUtc.ToString("o", CultureInfo.InvariantCulture),
            kind = "sun",
            accepted = accepted,
            offsetDeg = offset,
            cameraAzimuthArDeg = observedAzimuthAr,
            cameraElevationDeg = observedElevation,
            sunAzimuthDeg = (float)sun.AzimuthDeg,
            sunElevationDeg = (float)sun.ElevationDeg,
            elevationResidualDeg = residual,
            compassTrueHeadingDeg = compassValid ? location.CompassTrueHeading : Missing,
            compassAccuracyDeg = compassValid ? location.CompassHeadingAccuracy : Missing,
            // The camera is on the sun, so the compass should read the sun's azimuth.
            compassErrorDeg = compassValid && accepted
                ? CameraAngles.AngleDifference(location.CompassTrueHeading, (float)sun.AzimuthDeg)
                : Missing
        };
        Record(ev, location);

        // Elevation is independently reliable, so disagreement means a bad aim or a
        // wrong clock. Either way the azimuth from this sighting cannot be trusted.
        if (!elevationOk)
        {
            Message =
                $"sun: rejected, elevation off by {residual:+0.0;-0.0} deg " +
                $"(aimed {observedElevation:F1}, expected {sun.ElevationDeg:F1}). " +
                "Check your aim and the device clock.";
            return false;
        }

        if (!agrees)
        {
            // Keep the existing heading. If the EARLIER sighting was the bad one,
            // Reset heading and sight twice more.
            Message =
                $"sun: rejected, {change:+0.0;-0.0} deg from the last sighting. " +
                "Is the crosshair on the sun? Sight again, or Reset heading to start over.";
            return false;
        }

        LastElevationResidualDeg = residual;
        LastSunOffsetDeg = offset;
        HeadingOffsetDeg = offset;
        Reference = HeadingReference.Sun;
        CalibratedAtUtc = nowUtc;

        Message = float.IsNaN(change)
            ? $"sun: offset {offset:F1}, elev check {residual:+0.00;-0.00}"
            : $"sun: offset {offset:F1}, elev check {residual:+0.00;-0.00}, {change:+0.0;-0.0} vs last";
        return true;
    }

    public void Reset()
    {
        Reference = HeadingReference.None;
        HeadingOffsetDeg = 0f;
        LastElevationResidualDeg = float.NaN;
        LastSunOffsetDeg = float.NaN;
        CalibratedAtUtc = null;
        Message = "not calibrated";
    }

    // ------------------------------------------------------------ logging

    public static string LogPath => Path.Combine(SurveyRecorder.Folder, "calibrations.csv");

    const string CsvHeader =
        "utc,kind,accepted,offset_deg,camera_azimuth_ar_deg,camera_elevation_deg," +
        "sun_azimuth_deg,sun_elevation_deg,elevation_residual_deg," +
        "compass_true_heading_deg,compass_accuracy_deg,compass_error_deg," +
        "latitude,longitude,gps_accuracy_m";

    void Record(CalibrationEvent ev, LocationProvider location)
    {
        ev.latitude = location.Latitude;
        ev.longitude = location.Longitude;
        ev.gpsAccuracyM = location.HorizontalAccuracyMeters;
        _events.Add(ev);

        try
        {
            Directory.CreateDirectory(SurveyRecorder.Folder);
            if (!File.Exists(LogPath))
                File.WriteAllText(LogPath, CsvHeader + "\n");

            File.AppendAllText(LogPath, string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F2},{10:F1},{11:F2},{12:F6},{13:F6},{14:F1}\n",
                ev.utc, ev.kind, ev.accepted ? 1 : 0, ev.offsetDeg,
                ev.cameraAzimuthArDeg, ev.cameraElevationDeg,
                ev.sunAzimuthDeg, ev.sunElevationDeg, ev.elevationResidualDeg,
                ev.compassTrueHeadingDeg, ev.compassAccuracyDeg, ev.compassErrorDeg,
                ev.latitude, ev.longitude, ev.gpsAccuracyM));
        }
        catch (Exception e)
        {
            // Logging must never break calibration in the field.
            Debug.LogWarning($"[HorizonSurvey] could not write calibration log: {e.Message}");
        }
    }
}
