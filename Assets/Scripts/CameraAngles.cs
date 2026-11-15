using UnityEngine;

/// <summary>
/// Turns an AR camera direction into the two angles this project cares about.
///
/// The two are not equally trustworthy, and the whole architecture follows from that:
///
///   Elevation is nearly free. AR Foundation's world frame is gravity-aligned, so
///   "up" is known to a fraction of a degree, always. Only a small fixed offset
///   needed correcting, measured in week 1.
///
///   Azimuth is the hard part. Nothing in the AR frame knows where north is, so a
///   heading offset has to be established by calibration before any bearing means
///   anything at all.
/// </summary>
public static class CameraAngles
{
    /// <summary>
    /// Calibrated 2026-10-10 against tape-measure ground truth at 1, 2, 3, 4 and 8 m.
    /// ARKit reported elevation a constant 0.31 deg low across 12.1-59.8 deg
    /// (mean -0.31, SD 0.03, n=5). Applying this takes worst-case error to 0.04 deg.
    /// Device- and camera-configuration-specific: re-verify if the focal length changes.
    /// </summary>
    public const float ElevationOffsetDeg = 0.31f;

    /// <summary>Degrees above the horizon, with the measured offset applied.</summary>
    public static float Elevation(Vector3 forward) =>
        Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg + ElevationOffsetDeg;

    /// <summary>
    /// Bearing in the AR session's own frame, which has an arbitrary yaw.
    /// Meaningless until a heading offset is applied.
    /// </summary>
    public static float AzimuthAR(Vector3 forward) =>
        Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

    /// <summary>
    /// Inverse of Elevation and AzimuthAR: the unit vector the crosshair would point
    /// along to read these two angles. This is how overlays get placed in the sky.
    /// The elevation offset is removed again, so an overlay drawn at the sun's true
    /// elevation lands exactly where the crosshair would read that elevation.
    /// </summary>
    public static Vector3 DirectionAR(float azimuthArDeg, float elevationDeg)
    {
        float el = (elevationDeg - ElevationOffsetDeg) * Mathf.Deg2Rad;
        float az = azimuthArDeg * Mathf.Deg2Rad;
        float c = Mathf.Cos(el);
        return new Vector3(Mathf.Sin(az) * c, Mathf.Sin(el), Mathf.Cos(az) * c);
    }

    public static float Mod360(float degrees) => (degrees % 360f + 360f) % 360f;

    /// <summary>Smallest signed difference a - b, in -180..180.</summary>
    public static float AngleDifference(float a, float b)
    {
        float d = Mod360(a - b);
        return d > 180f ? d - 360f : d;
    }
}
