using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using HorizonSurvey.Solar;

/// <summary>
/// Records the skyline as the user traces it with the crosshair.
///
/// Manual tracing rather than automatic segmentation, deliberately. The user holds
/// the crosshair on the top edge of whatever blocks the sky - roofline, treeline,
/// chimney - and turns. Each frame becomes one (bearing, elevation) sample.
///
/// Samples are stored in the AR session's own frame, not as compass bearings.
/// That means the heading can be calibrated before tracing, after tracing, or
/// recalibrated later, and the same trace is simply re-projected - no need to redo
/// the sweep because the sun came out halfway through.
/// </summary>
public class SkylineCapture : MonoBehaviour
{
    [Serializable]
    public struct Sample
    {
        public float azimuthAr;
        public float elevation;
    }

    /// <summary>Only record a new sample once the crosshair has moved at least this far.</summary>
    [SerializeField] float minSampleSpacingDeg = 0.2f;

    /// <summary>
    /// Moving the phone shifts the vantage point, which shifts every angle. An
    /// offset of d metres against an obstruction D metres away costs roughly
    /// atan(d / D): 0.5 m against a building 30 m off is about 1 degree.
    /// </summary>
    public const float TranslationWarningM = 1.0f;

    readonly List<Sample> _samples = new List<Sample>();
    public IReadOnlyList<Sample> Samples => _samples;

    public bool IsTracing { get; private set; }

    /// <summary>Furthest the camera has wandered from where tracing started, in metres.</summary>
    public float MaxTranslationM { get; private set; }

    Vector3 _origin;
    bool _hasOrigin;
    Sample _last;
    bool _hasLast;

    public void BeginTrace(Camera arCamera)
    {
        if (arCamera == null) return;
        IsTracing = true;
        _hasLast = false;
        if (!_hasOrigin)
        {
            _origin = arCamera.transform.position;
            _hasOrigin = true;
        }
    }

    public void EndTrace() => IsTracing = false;

    public void Clear()
    {
        _samples.Clear();
        IsTracing = false;
        _hasOrigin = false;
        _hasLast = false;
        MaxTranslationM = 0f;
    }

    /// <summary>Call every frame while the trace button is held.</summary>
    public bool TryRecord(Camera arCamera)
    {
        if (!IsTracing || arCamera == null) return false;

        // Samples taken while tracking is degraded carry a bad pose. Skip them.
        if (ARSession.state != ARSessionState.SessionTracking) return false;

        Vector3 forward = arCamera.transform.forward;
        var sample = new Sample
        {
            azimuthAr = CameraAngles.AzimuthAR(forward),
            elevation = CameraAngles.Elevation(forward)
        };

        float moved = Vector3.Distance(arCamera.transform.position, _origin);
        if (moved > MaxTranslationM) MaxTranslationM = moved;

        if (_hasLast)
        {
            float dAz = Mathf.Abs(CameraAngles.AngleDifference(sample.azimuthAr, _last.azimuthAr));
            float dEl = Mathf.Abs(sample.elevation - _last.elevation);
            if (dAz < minSampleSpacingDeg && dEl < minSampleSpacingDeg) return false;
        }

        _samples.Add(sample);
        _last = sample;
        _hasLast = true;
        return true;
    }

    /// <summary>
    /// Project the samples through the current heading calibration into a 360-bin
    /// horizon profile.
    ///
    /// Each bin takes the median of its samples, not the maximum. A hand trace
    /// wobbles, and a single upward twitch would otherwise become a phantom wall.
    /// The median also lets a second pass over the same bearings quietly improve
    /// the result rather than corrupt it.
    /// </summary>
    public HorizonProfile BuildProfile(HeadingCalibrator calibrator)
    {
        var bins = new List<float>[HorizonProfile.BinCount];

        foreach (Sample s in _samples)
        {
            float trueAzimuth = calibrator.ToTrueAzimuth(s.azimuthAr);
            int b = HorizonProfile.BinOf(trueAzimuth);
            (bins[b] ??= new List<float>()).Add(s.elevation);
        }

        var profile = new HorizonProfile();
        for (int i = 0; i < bins.Length; i++)
            if (bins[i] != null)
                profile.SetBin(i, Median(bins[i]));

        return profile;
    }

    static float Median(List<float> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1
            ? values[n / 2]
            : 0.5f * (values[n / 2 - 1] + values[n / 2]);
    }
}
