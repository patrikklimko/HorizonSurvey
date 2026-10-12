using System;
using UnityEngine;
using HorizonSurvey.Solar;

public enum HeadingReference
{
    None,
    Compass,
    Sun
}

/// <summary>
/// Establishes which way is north, so that bearings in the AR frame become real
/// compass bearings.
///
/// Two routes, deliberately:
///
///   Compass. Always available, works in any weather, and not very good. Phone
///   magnetometers are pulled by steel structures, wiring, vehicles and laptops.
///
///   Sun sighting. If the time and position are known, the sun's bearing is known
///   to a fraction of a degree. Aiming at it therefore gives a far better heading
///   reference than the magnetometer - the same trick a surveyor uses with a solar
///   compass. Needs the sun to be visible, which in a Danish autumn is the catch.
///
/// The elevation cross-check is what makes the sun route trustworthy. Elevation
/// needs no calibration at all, so if the computed and observed elevations disagree,
/// either the aim was off or the device clock is wrong - and a wrong clock would
/// otherwise poison every bearing silently.
/// </summary>
public class HeadingCalibrator : MonoBehaviour
{
    /// <summary>Reject a sun sighting if observed elevation is further than this from computed.</summary>
    public const float ElevationGateDeg = 2.0f;

    /// <summary>Below this the sun is too low to aim at reliably, and refraction is largest.</summary>
    public const float MinimumSunElevationDeg = 5.0f;

    public HeadingReference Reference { get; private set; } = HeadingReference.None;
    public float HeadingOffsetDeg { get; private set; }
    public string Message { get; private set; } = "not calibrated";

    /// <summary>Elevation residual from the last sun sighting. NaN if none taken.</summary>
    public float LastElevationResidualDeg { get; private set; } = float.NaN;

    public DateTime? CalibratedAtUtc { get; private set; }

    public bool IsCalibrated => Reference != HeadingReference.None;

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

        float cameraAzimuthAr = CameraAngles.AzimuthAR(arCamera.transform.forward);
        HeadingOffsetDeg = CameraAngles.Mod360(heading - cameraAzimuthAr);

        Reference = HeadingReference.Compass;
        CalibratedAtUtc = DateTime.UtcNow;
        LastElevationResidualDeg = float.NaN;
        Message = $"compass: offset {HeadingOffsetDeg:F1} deg (device accuracy +/-{accuracy:F0})";
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
        float residual = observedElevation - (float)sun.ElevationDeg;
        LastElevationResidualDeg = residual;

        // Elevation is independently reliable, so disagreement means a bad aim or a
        // wrong clock. Either way the azimuth from this sighting cannot be trusted.
        if (Mathf.Abs(residual) > ElevationGateDeg)
        {
            Message =
                $"sun: rejected, elevation off by {residual:+0.0;-0.0} deg " +
                $"(aimed {observedElevation:F1}, expected {sun.ElevationDeg:F1}). " +
                "Check your aim and the device clock.";
            return false;
        }

        float observedAzimuthAr = CameraAngles.AzimuthAR(forward);
        HeadingOffsetDeg = CameraAngles.Mod360((float)sun.AzimuthDeg - observedAzimuthAr);

        Reference = HeadingReference.Sun;
        CalibratedAtUtc = nowUtc;
        Message =
            $"sun: offset {HeadingOffsetDeg:F1} deg, elevation check {residual:+0.00;-0.00} deg";
        return true;
    }

    public void Reset()
    {
        Reference = HeadingReference.None;
        HeadingOffsetDeg = 0f;
        LastElevationResidualDeg = float.NaN;
        CalibratedAtUtc = null;
        Message = "not calibrated";
    }
}
