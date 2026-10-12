using System.Collections;
using UnityEngine;

public enum LocationStatus
{
    Idle,
    Starting,
    Ready,
    DeniedByUser,
    TimedOut,
    Failed
}

/// <summary>
/// GPS fix and magnetometer heading.
///
/// Unity's new Input System has no location or compass support, so these still come
/// from the legacy UnityEngine.Input sensors. That is Unity's own recommendation,
/// not an oversight, and those members remain available even with the project set
/// to new-input-only.
///
/// Latitude and longitude feed the solar calculations. Horizontal accuracy is
/// surfaced and gated: a bad fix produces a plausible-looking but wrong sun, which
/// is the worst failure mode this app has.
/// </summary>
public class LocationProvider : MonoBehaviour
{
    [SerializeField] float desiredAccuracyMeters = 5f;
    [SerializeField] float updateDistanceMeters = 1f;
    [SerializeField] float startTimeoutSeconds = 20f;

    /// <summary>Refuse to survey above this horizontal uncertainty.</summary>
    public const float UsableAccuracyMeters = 20f;

    public LocationStatus Status { get; private set; } = LocationStatus.Idle;
    public string Message { get; private set; } = "location not started";

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public float HorizontalAccuracyMeters { get; private set; } = float.NaN;

    public bool CompassEnabled { get; private set; }
    public float CompassTrueHeading => Input.compass.trueHeading;

    /// <summary>
    /// Apple reports this as the magnetometer's own confidence, in degrees.
    /// Negative means the reading is invalid.
    /// </summary>
    public float CompassHeadingAccuracy => Input.compass.headingAccuracy;

    public bool HasFix =>
        Status == LocationStatus.Ready &&
        Input.location.status == LocationServiceStatus.Running;

    public bool HasUsableFix =>
        HasFix && HorizontalAccuracyMeters > 0f &&
        HorizontalAccuracyMeters <= UsableAccuracyMeters;

    IEnumerator Start()
    {
        Status = LocationStatus.Starting;
        Message = "starting location service";

        if (!Input.location.isEnabledByUser)
        {
            Status = LocationStatus.DeniedByUser;
            Message = "location is off for this app - enable it in Settings";
            yield break;
        }

        Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        // trueHeading needs location services running, so enable the compass after.
        Input.compass.enabled = true;
        CompassEnabled = true;

        float waited = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing &&
               waited < startTimeoutSeconds)
        {
            yield return new WaitForSeconds(0.5f);
            waited += 0.5f;
            Message = $"acquiring fix... {waited:F0}s";
        }

        if (Input.location.status == LocationServiceStatus.Initializing)
        {
            Status = LocationStatus.TimedOut;
            Message = $"no fix after {startTimeoutSeconds:F0}s - try outdoors";
            yield break;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Status = LocationStatus.Failed;
            Message = "location service failed";
            yield break;
        }

        Status = LocationStatus.Ready;
        Message = "location ready";
    }

    void Update()
    {
        if (Status != LocationStatus.Ready) return;
        if (Input.location.status != LocationServiceStatus.Running) return;

        LocationInfo info = Input.location.lastData;
        Latitude = info.latitude;
        Longitude = info.longitude;
        HorizontalAccuracyMeters = info.horizontalAccuracy;
    }

    void OnDisable()
    {
        if (Input.location.status == LocationServiceStatus.Running)
            Input.location.Stop();
        Input.compass.enabled = false;
        CompassEnabled = false;
    }

    public string ShortSummary()
    {
        if (!HasFix) return Message;
        string quality = HasUsableFix ? "ok" : "too coarse";
        return $"{Latitude:F5}, {Longitude:F5}  +/-{HorizontalAccuracyMeters:F0}m ({quality})";
    }
}
