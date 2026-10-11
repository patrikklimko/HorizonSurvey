using System;
using NUnit.Framework;
using HorizonSurvey.Solar;

namespace HorizonSurvey.Tests
{
    /// <summary>
    /// Every expectation here is derivable from first principles or checkable
    /// against a published almanac. Nothing is copied from another implementation,
    /// so these tests can actually catch a wrong implementation.
    /// </summary>
    public class SolarEngineTests
    {
        const double Lat = 55.8607;   // Horsens
        const double Lon = 9.8503;
        const int Year = 2026;

        static DateTime Utc(int m, int d, int h = 12) =>
            new DateTime(Year, m, d, h, 0, 0, DateTimeKind.Utc);

        // ---- time ----

        [Test]
        public void JulianDay_J2000_Is2451545()
        {
            var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(SolarEngine.JulianDay(j2000), Is.EqualTo(2451545.0).Within(1e-6));
        }

        // ---- declination ----

        [TestCase(3, 20)]
        [TestCase(9, 22)]
        public void Declination_AtEquinox_IsNearZero(int month, int day)
        {
            Assert.That(SolarEngine.DeclinationDeg(Utc(month, day)),
                Is.EqualTo(0.0).Within(1.0));
        }

        [Test]
        public void Declination_AtJuneSolstice_IsObliquityOfEcliptic()
        {
            Assert.That(SolarEngine.DeclinationDeg(Utc(6, 21)),
                Is.EqualTo(23.44).Within(0.15));
        }

        [Test]
        public void Declination_AtDecemberSolstice_IsNegativeObliquity()
        {
            Assert.That(SolarEngine.DeclinationDeg(Utc(12, 21)),
                Is.EqualTo(-23.44).Within(0.15));
        }

        // ---- noon geometry: elevation = 90 - latitude + declination ----

        [TestCase(6, 21, 57.59)]   // 90 - 55.86 + 23.44
        [TestCase(12, 21, 10.79)]  // 90 - 55.86 - 23.44
        [TestCase(3, 20, 34.11)]   // 90 - 55.86 + 0
        public void NoonElevation_MatchesGeometry(int month, int day, double expected)
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(month, day));
            Assert.That(SolarEngine.Position(Lat, Lon, noon).ElevationDeg,
                Is.EqualTo(expected).Within(0.3));
        }

        [TestCase(3, 20)]
        [TestCase(6, 21)]
        [TestCase(9, 22)]
        [TestCase(12, 21)]
        public void NoonAzimuth_IsDueSouth(int month, int day)
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(month, day));
            Assert.That(SolarEngine.Position(Lat, Lon, noon).AzimuthDeg,
                Is.EqualTo(180.0).Within(0.5));
        }

        [Test]
        public void Azimuth_IsEastInMorning_AndWestInAfternoon()
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(6, 21));
            double morning = SolarEngine.Position(Lat, Lon, noon.AddHours(-4)).AzimuthDeg;
            double afternoon = SolarEngine.Position(Lat, Lon, noon.AddHours(4)).AzimuthDeg;

            Assert.That(morning, Is.LessThan(180.0), "morning sun should be east of south");
            Assert.That(afternoon, Is.GreaterThan(180.0), "afternoon sun should be west of south");
        }

        [Test]
        public void Elevation_IsSymmetricAboutSolarNoon()
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(6, 21));
            double before = SolarEngine.Position(Lat, Lon, noon.AddHours(-3)).ElevationDeg;
            double after = SolarEngine.Position(Lat, Lon, noon.AddHours(3)).ElevationDeg;
            Assert.That(after, Is.EqualTo(before).Within(0.05));
        }

        // ---- equation of time ----

        [Test]
        public void EquationOfTime_AnnualMinimum_IsAboutMinus14MinutesInFebruary()
        {
            double min = double.MaxValue;
            DateTime at = default;
            for (int i = 0; i < 365; i++)
            {
                DateTime t = Utc(1, 1).AddDays(i);
                double e = SolarEngine.EquationOfTimeMinutes(t);
                if (e < min) { min = e; at = t; }
            }
            Assert.That(min, Is.EqualTo(-14.2).Within(0.5));
            Assert.That(at.Month, Is.EqualTo(2));
        }

        [Test]
        public void EquationOfTime_AnnualMaximum_IsAboutPlus16MinutesInEarlyNovember()
        {
            double max = double.MinValue;
            DateTime at = default;
            for (int i = 0; i < 365; i++)
            {
                DateTime t = Utc(1, 1).AddDays(i);
                double e = SolarEngine.EquationOfTimeMinutes(t);
                if (e > max) { max = e; at = t; }
            }
            Assert.That(max, Is.EqualTo(16.5).Within(0.5));
            Assert.That(at.Month, Is.EqualTo(11));
        }

        [Test]
        public void SolarNoon_IsNotAtTwelve()
        {
            // Horsens sits east of the Greenwich meridian, so solar noon in UTC is
            // before 12:00 all year. The exact offset moves with the equation of time.
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(6, 21));
            Assert.That(noon.TimeOfDay.TotalMinutes, Is.LessThan(720.0));
            Assert.That(noon.TimeOfDay.TotalHours, Is.EqualTo(11.37).Within(0.2));
        }

        // ---- day length ----

        [TestCase(6, 21, 17.58)]   // Danish midsummer: about 17h35m
        [TestCase(12, 21, 6.98)]   // Danish midwinter: about 6h59m
        [TestCase(3, 20, 12.19)]   // equinox, slightly over 12h thanks to refraction
        public void DayLength_MatchesAlmanac(int month, int day, double expectedHours)
        {
            Assert.That(
                SolarEngine.TryGetSunriseSunsetUtc(Lat, Lon, Utc(month, day), out var rise, out var set),
                Is.True);
            Assert.That((set - rise).TotalHours, Is.EqualTo(expectedHours).Within(0.25));
        }

        [Test]
        public void SunriseAndSunset_StraddleSolarNoonSymmetrically()
        {
            DateTime noon = SolarEngine.SolarNoonUtc(Lon, Utc(6, 21));
            Assert.That(
                SolarEngine.TryGetSunriseSunsetUtc(Lat, Lon, Utc(6, 21), out var rise, out var set),
                Is.True);
            Assert.That((set - noon).TotalMinutes,
                Is.EqualTo((noon - rise).TotalMinutes).Within(0.01));
        }

        [Test]
        public void PolarNight_ReturnsFalse()
        {
            // Longyearbyen in midwinter: the sun does not rise at all.
            Assert.That(
                SolarEngine.TryGetSunriseSunsetUtc(78.22, 15.63, Utc(12, 21), out _, out _),
                Is.False);
        }

        // ---- refraction ----

        [Test]
        public void Refraction_AtHorizon_IsAboutHalfADegree()
        {
            Assert.That(SolarEngine.RefractionDeg(0.0), Is.EqualTo(0.48).Within(0.1));
        }

        [Test]
        public void Refraction_HighInTheSky_IsNegligible()
        {
            Assert.That(SolarEngine.RefractionDeg(60.0), Is.LessThan(0.02));
        }
    }
}
