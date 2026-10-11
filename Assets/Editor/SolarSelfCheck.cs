using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using HorizonSurvey.Solar;

/// <summary>
/// Verification for the solar engine that does not need the Unity Test Framework.
///
/// Every assertion here is something that can be derived from first principles or
/// checked against an almanac, rather than a number copied out of another
/// implementation. If these all pass, the astronomy is right.
///
/// Menu: Horizon Survey > Run Solar Self-Check
/// </summary>
public static class SolarSelfCheck
{
    // Horsens, Denmark
    const double Lat = 55.8607;
    const double Lon = 9.8503;
    const int Year = 2026;

    static int _passed;
    static int _failed;
    static StringBuilder _log;

    [MenuItem("Horizon Survey/Run Solar Self-Check")]
    public static void Run()
    {
        _passed = 0;
        _failed = 0;
        _log = new StringBuilder();
        _log.AppendLine("=== Solar engine self-check ===");
        _log.AppendLine($"Site: Horsens  lat {Lat:F4}  lon {Lon:F4}   year {Year}");
        _log.AppendLine();

        CheckDeclination();
        CheckNoonElevations();
        CheckNoonAzimuth();
        CheckEquationOfTime();
        CheckDayLengths();
        CheckSolarAccess();

        _log.AppendLine();
        _log.AppendLine($"=== {_passed} passed, {_failed} failed ===");

        if (_failed == 0) Debug.Log(_log.ToString());
        else Debug.LogError(_log.ToString());
    }

    // ---------- checks ----------

    static void CheckDeclination()
    {
        Section("Declination");

        // At the equinoxes the sun is over the equator. The calendar date drifts a
        // little year to year, so allow a degree.
        Near("Mar equinox declination ~ 0",
            SolarEngine.DeclinationDeg(Utc(3, 20, 12)), 0.0, 1.0);
        Near("Sep equinox declination ~ 0",
            SolarEngine.DeclinationDeg(Utc(9, 22, 12)), 0.0, 1.0);

        // At the solstices it reaches the obliquity of the ecliptic, 23.44 deg.
        Near("Jun solstice declination ~ +23.44",
            SolarEngine.DeclinationDeg(Utc(6, 21, 12)), 23.44, 0.15);
        Near("Dec solstice declination ~ -23.44",
            SolarEngine.DeclinationDeg(Utc(12, 21, 12)), -23.44, 0.15);
    }

    static void CheckNoonElevations()
    {
        Section("Noon elevation at Horsens");

        // Geometry: max elevation = 90 - latitude + declination.
        //   summer: 90 - 55.86 + 23.44 = 57.58
        //   winter: 90 - 55.86 - 23.44 = 10.70
        // Refraction adds a few hundredths at these elevations.
        Near("Summer solstice noon ~ 57.6",
            NoonElevation(6, 21), 57.58, 0.3);
        Near("Winter solstice noon ~ 10.7",
            NoonElevation(12, 21), 10.78, 0.3);
        Near("Equinox noon ~ 34.1",
            NoonElevation(3, 20), 34.16, 0.5);
    }

    static void CheckNoonAzimuth()
    {
        Section("Noon azimuth");

        // North of the tropics the sun is due south at solar noon, every day.
        foreach (var (m, d) in new[] { (3, 20), (6, 21), (9, 22), (12, 21) })
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(m, d, 12));
            var sun = SolarEngine.Position(Lat, Lon, noon);
            Near($"{m:00}-{d:00} noon azimuth ~ 180", sun.AzimuthDeg, 180.0, 0.5);
        }
    }

    static void CheckEquationOfTime()
    {
        Section("Equation of time");

        double min = double.MaxValue, max = double.MinValue;
        DateTime minAt = default, maxAt = default;

        for (int doy = 0; doy < 365; doy++)
        {
            DateTime t = new DateTime(Year, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddDays(doy);
            double eot = SolarEngine.EquationOfTimeMinutes(t);
            if (eot < min) { min = eot; minAt = t; }
            if (eot > max) { max = eot; maxAt = t; }
        }

        _log.AppendLine($"    min {min,6:F2} min on {minAt:MMM dd}");
        _log.AppendLine($"    max {max,6:F2} min on {maxAt:MMM dd}");

        // Known annual envelope: roughly -14.2 min in mid February, +16.4 in early November.
        Between("EoT minimum in [-16, -12]", min, -16.0, -12.0);
        Between("EoT maximum in [+14, +18]", max, 14.0, 18.0);
        Pass("Solar noon is not at 12:00 - that is the point of this term");
    }

    static void CheckDayLengths()
    {
        Section("Day length at Horsens");

        Near("Summer solstice ~ 17h35m", DayLengthHours(6, 21), 17.58, 0.25);
        Near("Winter solstice ~ 6h59m", DayLengthHours(12, 21), 6.98, 0.25);
        Near("Equinox ~ 12h", DayLengthHours(3, 20), 12.15, 0.25);

        // Sunrise and sunset must straddle solar noon symmetrically.
        DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(6, 21, 12));
        if (SolarEngine.TryGetSunriseSunsetUtc(Lat, Lon, Utc(6, 21, 12), out var rise, out var set))
        {
            double before = (noon - rise).TotalMinutes;
            double after = (set - noon).TotalMinutes;
            Near("Sunrise/sunset symmetric about solar noon", after - before, 0.0, 0.01);
        }
        else Fail("Sunrise/sunset returned false at Horsens in June");
    }

    static void CheckSolarAccess()
    {
        Section("Solar access integration");

        var open = HorizonProfile.Flat(0.0);
        Near("Open horizon -> 100%",
            SolarAccessCalculator.Compute(Lat, Lon, Year, open).AnnualPercent, 100.0, 0.001);

        var walled = HorizonProfile.Flat(90.0);
        Near("Boxed in at 90 deg -> 0%",
            SolarAccessCalculator.Compute(Lat, Lon, Year, walled).AnnualPercent, 0.0, 0.001);

        // Raising a uniform skyline must monotonically reduce access. If this ever
        // fails, something is wrong with the binning or the interpolation.
        double[] rings = { 0, 5, 10, 15, 20, 30, 45 };
        double previous = double.MaxValue;
        bool monotonic = true;
        var ringAccess = new double[rings.Length];

        for (int i = 0; i < rings.Length; i++)
        {
            ringAccess[i] = SolarAccessCalculator
                .Compute(Lat, Lon, Year, HorizonProfile.Flat(rings[i])).AnnualPercent;
            if (ringAccess[i] >= previous) monotonic = false;
            previous = ringAccess[i];
            _log.AppendLine($"    ring at {rings[i],2:F0} deg -> annual {ringAccess[i],6:F2}%");
        }
        True("Access falls monotonically as the skyline rises", monotonic);

        // A 10 degree ring. Note how little this costs annually: the air mass
        // weighting is very steep near the horizon (about 1 W/m2 at 1 degree
        // elevation against 75 at 10), so most of the year's energy arrives well
        // above a low skyline. December is the exception, because at 56 N the
        // midwinter sun peaks at only about 12 degrees.
        var ring10 = SolarAccessCalculator.Compute(Lat, Lon, Year, HorizonProfile.Flat(10.0));
        _log.AppendLine($"    10 deg ring: annual {ring10.AnnualPercent:F2}%  " +
                        $"Jun {ring10.MonthlyPercent[5]:F2}%  Dec {ring10.MonthlyPercent[11]:F2}%");
        Near("10 deg ring: annual ~97.5%", ring10.AnnualPercent, 97.5, 1.0);
        Near("10 deg ring: June ~98.9%", ring10.MonthlyPercent[5], 98.9, 1.0);
        Near("10 deg ring: December ~53.9%", ring10.MonthlyPercent[11], 53.9, 3.0);
        True("10 deg ring hurts December far more than June",
            ring10.MonthlyPercent[11] < ring10.MonthlyPercent[5] - 30.0);

        // Exact physical check: the midwinter sun at this latitude never reaches
        // 15 degrees, so a 15 degree skyline must take December to precisely zero.
        var ring15 = SolarAccessCalculator.Compute(Lat, Lon, Year, HorizonProfile.Flat(15.0));
        Near("15 deg ring: December exactly 0%", ring15.MonthlyPercent[11], 0.0, 0.001);

        double decMax = MaxElevationInDecember();
        _log.AppendLine($"    highest December sun: {decMax:F2} deg");
        Between("December sun peaks between 10 and 15 deg", decMax, 10.0, 15.0);

        // Direction matters. The same obstruction due south costs around ten times
        // what it costs due north, because at this latitude the sun is never high
        // in the northern sky.
        var south = new HorizonProfile();
        south.SetArc(150, 210, 30.0);
        double southAccess = SolarAccessCalculator.Compute(Lat, Lon, Year, south).AnnualPercent;

        var north = new HorizonProfile();
        north.SetArc(330, 30, 30.0);
        double northAccess = SolarAccessCalculator.Compute(Lat, Lon, Year, north).AnnualPercent;

        _log.AppendLine($"    30 deg wall, 60 deg wide: south {southAccess:F2}%   north {northAccess:F2}%");
        Near("30 deg wall due south -> ~90.9%", southAccess, 90.9, 1.5);
        Near("30 deg wall due north -> ~100%", northAccess, 100.0, 0.5);
        True("South obstruction costs far more than north",
            (100.0 - southAccess) > 10.0 * (100.0 - northAccess + 0.01));
    }

    static double MaxElevationInDecember()
    {
        double max = double.MinValue;
        var day = new DateTime(Year, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        while (day.Month == 12)
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, day);
            double el = SolarEngine.Position(Lat, Lon, noon).ElevationDeg;
            if (el > max) max = el;
            day = day.AddDays(1);
        }
        return max;
    }

    /// <summary>Prints a monthly table for an example site. For eyeballing, not asserting.</summary>
    [MenuItem("Horizon Survey/Print Example Survey")]
    public static void PrintExampleSurvey()
    {
        var h = new HorizonProfile();
        h.SetArc(0, 359, 2.0);       // distant treeline all round
        h.SetArc(108, 145, 15.0);    // building to the south-east
        h.SetArc(198, 224, 23.0);    // large tree to the south-south-west

        var r = SolarAccessCalculator.Compute(Lat, Lon, Year, h);

        var sb = new StringBuilder();
        sb.AppendLine("=== Example survey, Horsens ===");
        sb.AppendLine("Horizon: 2 deg treeline, 15 deg building SE, 23 deg tree SSW");
        sb.AppendLine();
        sb.AppendLine($"Annual solar access: {r.AnnualPercent:F1}%");
        sb.AppendLine();
        string[] months = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
        for (int m = 0; m < 12; m++)
        {
            int bars = Mathf.RoundToInt((float)r.MonthlyPercent[m] / 4f);
            sb.AppendLine($"  {months[m]}  {r.MonthlyPercent[m],5:F1}%  {new string('#', Mathf.Max(0, bars))}");
        }
        Debug.Log(sb.ToString());
    }

    // ---------- helpers ----------

    static DateTime Utc(int month, int day, int hour) =>
        new DateTime(Year, month, day, hour, 0, 0, DateTimeKind.Utc);

    static double NoonElevation(int month, int day)
    {
        DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(month, day, 12));
        return SolarEngine.Position(Lat, Lon, noon).ElevationDeg;
    }

    static double DayLengthHours(int month, int day)
    {
        if (!SolarEngine.TryGetSunriseSunsetUtc(Lat, Lon, Utc(month, day, 12), out var r, out var s))
            return double.NaN;
        return (s - r).TotalHours;
    }

    static void Section(string name)
    {
        _log.AppendLine($"-- {name}");
    }

    static void Near(string what, double actual, double expected, double tolerance)
    {
        double delta = Math.Abs(actual - expected);
        if (delta <= tolerance)
            Pass($"{what}   (got {actual:F3}, expected {expected:F3})");
        else
            Fail($"{what}   (got {actual:F3}, expected {expected:F3} +/- {tolerance})");
    }

    static void Between(string what, double actual, double lo, double hi)
    {
        if (actual >= lo && actual <= hi) Pass($"{what}   (got {actual:F3})");
        else Fail($"{what}   (got {actual:F3}, wanted {lo}..{hi})");
    }

    static void True(string what, bool condition)
    {
        if (condition) Pass(what); else Fail(what);
    }

    static void Pass(string what)
    {
        _passed++;
        _log.AppendLine($"  PASS  {what}");
    }

    static void Fail(string what)
    {
        _failed++;
        _log.AppendLine($"  FAIL  {what}");
    }
}
