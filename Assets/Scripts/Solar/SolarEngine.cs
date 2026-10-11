using System;

namespace HorizonSurvey.Solar
{
    /// <summary>Where the sun is, as seen from a point on the ground.</summary>
    public readonly struct SolarPosition
    {
        /// <summary>Degrees clockwise from true north, 0..360.</summary>
        public readonly double AzimuthDeg;

        /// <summary>Degrees above the horizon. Negative when the sun is down.</summary>
        public readonly double ElevationDeg;

        public SolarPosition(double azimuthDeg, double elevationDeg)
        {
            AzimuthDeg = azimuthDeg;
            ElevationDeg = elevationDeg;
        }

        public bool IsUp => ElevationDeg > 0.0;

        public override string ToString() =>
            $"az {AzimuthDeg:F2} deg, el {ElevationDeg:F2} deg";
    }

    /// <summary>
    /// Solar position from latitude, longitude and a UTC instant.
    ///
    /// Implements the NOAA solar position equations (the ones behind NOAA's
    /// published solar calculator). Accuracy is around a hundredth of a degree
    /// for dates near the present, which is far finer than the horizon profile
    /// this gets compared against, so this part of the pipeline is effectively
    /// error-free.
    ///
    /// Deliberately has no Unity dependency: it is plain .NET and runs anywhere.
    /// Everything is UTC internally. Convert for display only.
    /// </summary>
    public static class SolarEngine
    {
        const double Deg2Rad = Math.PI / 180.0;
        const double Rad2Deg = 180.0 / Math.PI;

        static readonly DateTime MjdEpoch =
            new DateTime(1858, 11, 17, 0, 0, 0, DateTimeKind.Utc);

        static DateTime AsUtc(DateTime t) =>
            t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();

        public static double Mod360(double deg)
        {
            double m = deg % 360.0;
            return m < 0.0 ? m + 360.0 : m;
        }

        // ---------- time ----------

        /// <summary>Julian Day number for a UTC instant.</summary>
        public static double JulianDay(DateTime utc) =>
            (AsUtc(utc) - MjdEpoch).TotalDays + 2400000.5;

        /// <summary>Julian centuries since J2000.0.</summary>
        public static double JulianCentury(DateTime utc) =>
            (JulianDay(utc) - 2451545.0) / 36525.0;

        // ---------- orbital terms (NOAA) ----------

        static double GeomMeanLongSunDeg(double t) =>
            Mod360(280.46646 + t * (36000.76983 + t * 0.0003032));

        static double GeomMeanAnomalySunDeg(double t) =>
            357.52911 + t * (35999.05029 - 0.0001537 * t);

        static double EccentricityEarthOrbit(double t) =>
            0.016708634 - t * (0.000042037 + 0.0000001267 * t);

        static double SunEquationOfCentreDeg(double t)
        {
            double m = GeomMeanAnomalySunDeg(t) * Deg2Rad;
            return Math.Sin(m)       * (1.914602 - t * (0.004817 + 0.000014 * t))
                 + Math.Sin(2.0 * m) * (0.019993 - 0.000101 * t)
                 + Math.Sin(3.0 * m) * 0.000289;
        }

        static double SunTrueLongDeg(double t) =>
            GeomMeanLongSunDeg(t) + SunEquationOfCentreDeg(t);

        static double SunApparentLongDeg(double t) =>
            SunTrueLongDeg(t)
            - 0.00569
            - 0.00478 * Math.Sin((125.04 - 1934.136 * t) * Deg2Rad);

        static double MeanObliquityOfEclipticDeg(double t) =>
            23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;

        static double ObliquityCorrectedDeg(double t) =>
            MeanObliquityOfEclipticDeg(t)
            + 0.00256 * Math.Cos((125.04 - 1934.136 * t) * Deg2Rad);

        // ---------- public solar quantities ----------

        /// <summary>Solar declination in degrees: how far north or south of the equator the sun is overhead.</summary>
        public static double DeclinationDeg(DateTime utc)
        {
            double t = JulianCentury(utc);
            double eps = ObliquityCorrectedDeg(t) * Deg2Rad;
            double lambda = SunApparentLongDeg(t) * Deg2Rad;
            return Math.Asin(Math.Sin(eps) * Math.Sin(lambda)) * Rad2Deg;
        }

        /// <summary>
        /// Equation of time in minutes: how far apparent solar time runs ahead of
        /// mean solar time. Swings between roughly -14 and +16 minutes across the
        /// year. This is why solar noon is not at 12:00, and forgetting it is the
        /// classic way to get sun positions subtly and consistently wrong.
        /// </summary>
        public static double EquationOfTimeMinutes(DateTime utc)
        {
            double t = JulianCentury(utc);
            double eps = ObliquityCorrectedDeg(t) * Deg2Rad;
            double y = Math.Tan(eps / 2.0);
            y *= y;

            double l0 = GeomMeanLongSunDeg(t) * Deg2Rad;
            double m = GeomMeanAnomalySunDeg(t) * Deg2Rad;
            double e = EccentricityEarthOrbit(t);

            double eq = y * Math.Sin(2.0 * l0)
                      - 2.0 * e * Math.Sin(m)
                      + 4.0 * e * y * Math.Sin(m) * Math.Cos(2.0 * l0)
                      - 0.5 * y * y * Math.Sin(4.0 * l0)
                      - 1.25 * e * e * Math.Sin(2.0 * m);

            return 4.0 * eq * Rad2Deg;
        }

        /// <summary>
        /// Atmospheric refraction in degrees, to be added to the geometric elevation.
        /// About +0.57 deg at the horizon, negligible above ~20 deg.
        ///
        /// We apply it because the horizon profile is measured optically by the
        /// camera, so comparing it against the sun's *apparent* position is the
        /// consistent choice.
        /// </summary>
        public static double RefractionDeg(double elevationDeg)
        {
            if (elevationDeg > 85.0) return 0.0;

            double te = Math.Tan(elevationDeg * Deg2Rad);
            double arcSeconds;

            if (elevationDeg > 5.0)
                arcSeconds = 58.1 / te - 0.07 / (te * te * te) + 0.000086 / (te * te * te * te * te);
            else if (elevationDeg > -0.575)
                arcSeconds = 1735.0 + elevationDeg *
                             (-518.2 + elevationDeg *
                             (103.4 + elevationDeg *
                             (-12.79 + elevationDeg * 0.711)));
            else
                arcSeconds = -20.772 / te;

            return arcSeconds / 3600.0;
        }

        /// <summary>
        /// Sun position for a location and UTC instant.
        /// Longitude is positive east. Azimuth is clockwise from true north.
        /// </summary>
        public static SolarPosition Position(
            double latitudeDeg,
            double longitudeDeg,
            DateTime utc,
            bool applyRefraction = true)
        {
            DateTime u = AsUtc(utc);

            double declRad = DeclinationDeg(u) * Deg2Rad;
            double eotMin = EquationOfTimeMinutes(u);

            // Apparent solar time at this longitude, in minutes past local solar midnight.
            double trueSolarTime = u.TimeOfDay.TotalMinutes + eotMin + 4.0 * longitudeDeg;
            trueSolarTime = ((trueSolarTime % 1440.0) + 1440.0) % 1440.0;

            // Hour angle: 0 at solar noon, negative before, positive after. 15 deg per hour.
            double hourAngleDeg = trueSolarTime / 4.0 - 180.0;
            double hourAngleRad = hourAngleDeg * Deg2Rad;

            double latRad = latitudeDeg * Deg2Rad;

            double cosZenith = Math.Sin(latRad) * Math.Sin(declRad)
                             + Math.Cos(latRad) * Math.Cos(declRad) * Math.Cos(hourAngleRad);
            cosZenith = Clamp(cosZenith, -1.0, 1.0);

            double zenithRad = Math.Acos(cosZenith);
            double elevationDeg = 90.0 - zenithRad * Rad2Deg;

            double azimuthDeg;
            double denominator = Math.Cos(latRad) * Math.Sin(zenithRad);

            if (Math.Abs(denominator) < 1e-12)
            {
                // Degenerate: at a pole, or sun exactly overhead.
                azimuthDeg = hourAngleDeg > 0.0 ? 180.0 : 0.0;
            }
            else
            {
                double c = (Math.Sin(latRad) * cosZenith - Math.Sin(declRad)) / denominator;
                c = Clamp(c, -1.0, 1.0);
                double acz = Math.Acos(c) * Rad2Deg;
                azimuthDeg = hourAngleDeg > 0.0
                    ? Mod360(acz + 180.0)
                    : Mod360(540.0 - acz);
            }

            if (applyRefraction)
                elevationDeg += RefractionDeg(elevationDeg);

            return new SolarPosition(azimuthDeg, elevationDeg);
        }

        /// <summary>UTC instant of solar noon for a longitude on a given UTC date.</summary>
        public static DateTime SolarNoonUtc(double longitudeDeg, DateTime utcDate)
        {
            DateTime midnight = new DateTime(
                utcDate.Year, utcDate.Month, utcDate.Day, 0, 0, 0, DateTimeKind.Utc);

            double minutes = 720.0;
            // Two passes: the equation of time depends on the instant, but only weakly.
            for (int i = 0; i < 2; i++)
            {
                double eot = EquationOfTimeMinutes(midnight.AddMinutes(minutes));
                minutes = 720.0 - 4.0 * longitudeDeg - eot;
            }

            return midnight.AddMinutes(minutes);
        }

        /// <summary>
        /// Sunrise and sunset in UTC, using the conventional -0.833 deg centre
        /// elevation (solar radius plus horizon refraction).
        /// Returns false during polar day or polar night, when the sun never
        /// crosses the horizon.
        /// </summary>
        public static bool TryGetSunriseSunsetUtc(
            double latitudeDeg,
            double longitudeDeg,
            DateTime utcDate,
            out DateTime sunriseUtc,
            out DateTime sunsetUtc)
        {
            DateTime noon = SolarNoonUtc(longitudeDeg, utcDate);

            double declRad = DeclinationDeg(noon) * Deg2Rad;
            double latRad = latitudeDeg * Deg2Rad;

            double cosH = Math.Cos(90.833 * Deg2Rad) / (Math.Cos(latRad) * Math.Cos(declRad))
                        - Math.Tan(latRad) * Math.Tan(declRad);

            if (cosH > 1.0 || cosH < -1.0)
            {
                sunriseUtc = default;
                sunsetUtc = default;
                return false;
            }

            double halfDayMinutes = Math.Acos(cosH) * Rad2Deg * 4.0;
            sunriseUtc = noon.AddMinutes(-halfDayMinutes);
            sunsetUtc = noon.AddMinutes(halfDayMinutes);
            return true;
        }

        static double Clamp(double v, double lo, double hi) =>
            v < lo ? lo : (v > hi ? hi : v);
    }
}
