using NUnit.Framework;
using HorizonSurvey.Solar;

namespace HorizonSurvey.Tests
{
    /// <summary>
    /// Expected values were derived from an independent Python implementation of
    /// the same equations and cross-checked against the physical geometry of the
    /// site, not taken from this code's own output.
    /// </summary>
    public class SolarAccessTests
    {
        const double Lat = 55.8607;   // Horsens
        const double Lon = 9.8503;
        const int Year = 2026;

        static double Annual(HorizonProfile h) =>
            SolarAccessCalculator.Compute(Lat, Lon, Year, h).AnnualPercent;

        [Test]
        public void OpenHorizon_GivesFullAccess()
        {
            Assert.That(Annual(HorizonProfile.Flat(0.0)), Is.EqualTo(100.0).Within(0.001));
        }

        [Test]
        public void FullyEnclosed_GivesNoAccess()
        {
            Assert.That(Annual(HorizonProfile.Flat(90.0)), Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void RaisingTheSkyline_MonotonicallyReducesAccess()
        {
            double previous = double.MaxValue;
            foreach (double ring in new[] { 0.0, 5.0, 10.0, 15.0, 20.0, 30.0, 45.0 })
            {
                double access = Annual(HorizonProfile.Flat(ring));
                Assert.That(access, Is.LessThan(previous),
                    $"access did not fall when the skyline rose to {ring} deg");
                previous = access;
            }
        }

        [Test]
        public void LowSkyline_CostsLittleAnnually()
        {
            // The air mass weighting is steep near the horizon, so a 10 degree
            // skyline removes only a few percent of the year's energy.
            Assert.That(Annual(HorizonProfile.Flat(10.0)), Is.EqualTo(97.5).Within(1.0));
        }

        [Test]
        public void LowSkyline_HitsWinterFarHarderThanSummer()
        {
            var r = SolarAccessCalculator.Compute(Lat, Lon, Year, HorizonProfile.Flat(10.0));
            Assert.That(r.MonthlyPercent[5], Is.EqualTo(98.9).Within(1.0), "June");
            Assert.That(r.MonthlyPercent[11], Is.EqualTo(53.9).Within(3.0), "December");
        }

        [Test]
        public void FifteenDegreeSkyline_EliminatesDecemberEntirely()
        {
            // At 56 N the midwinter sun peaks near 12 degrees, so it never clears
            // a 15 degree skyline at all.
            var r = SolarAccessCalculator.Compute(Lat, Lon, Year, HorizonProfile.Flat(15.0));
            Assert.That(r.MonthlyPercent[11], Is.EqualTo(0.0).Within(0.001));
            Assert.That(r.MonthlyPercent[5], Is.GreaterThan(90.0), "June should barely notice");
        }

        [Test]
        public void SouthernObstruction_CostsFarMoreThanNorthern()
        {
            var south = new HorizonProfile();
            south.SetArc(150, 210, 30.0);

            var north = new HorizonProfile();
            north.SetArc(330, 30, 30.0);

            double southAccess = Annual(south);
            double northAccess = Annual(north);

            Assert.That(southAccess, Is.EqualTo(90.9).Within(1.5));
            Assert.That(northAccess, Is.EqualTo(100.0).Within(0.5));
            Assert.That(100.0 - southAccess,
                Is.GreaterThan(10.0 * (100.0 - northAccess + 0.01)),
                "a southern obstruction should cost an order of magnitude more");
        }

        [Test]
        public void MonthlyFigures_AreAllInRange()
        {
            var h = new HorizonProfile();
            h.SetArc(0, 359, 2.0);
            h.SetArc(108, 145, 15.0);
            h.SetArc(198, 224, 23.0);

            var r = SolarAccessCalculator.Compute(Lat, Lon, Year, h);
            Assert.That(r.MonthlyPercent.Length, Is.EqualTo(12));
            foreach (double m in r.MonthlyPercent)
                Assert.That(m, Is.InRange(0.0, 100.0));
            Assert.That(r.AnnualPercent, Is.InRange(0.0, 100.0));
        }

        [Test]
        public void IrradianceWeight_IsZeroBelowHorizon_AndRisesWithElevation()
        {
            Assert.That(SolarAccessCalculator.ClearSkyHorizontalIrradiance(-1.0), Is.EqualTo(0.0));
            Assert.That(SolarAccessCalculator.ClearSkyHorizontalIrradiance(0.0), Is.EqualTo(0.0));

            double previous = 0.0;
            foreach (double e in new[] { 1.0, 5.0, 10.0, 20.0, 45.0, 60.0 })
            {
                double w = SolarAccessCalculator.ClearSkyHorizontalIrradiance(e);
                Assert.That(w, Is.GreaterThan(previous));
                previous = w;
            }
        }

        [Test]
        public void StepSize_DoesNotMateriallyChangeTheAnswer()
        {
            var h = HorizonProfile.Flat(10.0);
            double fine = SolarAccessCalculator.Compute(Lat, Lon, Year, h, 2).AnnualPercent;
            double coarse = SolarAccessCalculator.Compute(Lat, Lon, Year, h, 10).AnnualPercent;
            Assert.That(coarse, Is.EqualTo(fine).Within(0.5));
        }
    }
}
