using NUnit.Framework;
using HorizonSurvey.Solar;

namespace HorizonSurvey.Tests
{
    public class HorizonProfileTests
    {
        [Test]
        public void NewProfile_IsEmptyAndUncovered()
        {
            var p = new HorizonProfile();
            Assert.That(p.CoverageFraction, Is.EqualTo(0.0));
            Assert.That(p.IsCovered(0), Is.False);
        }

        [Test]
        public void Observe_KeepsTheHighestElevationPerBearing()
        {
            var p = new HorizonProfile();
            p.Observe(90.5, 12.0);
            p.Observe(90.2, 18.0);   // same bin, taller
            p.Observe(90.8, 9.0);    // same bin, shorter
            Assert.That(p.RawBin(90), Is.EqualTo(18.0));
            Assert.That(p.SampleCount(90), Is.EqualTo(3));
        }

        [TestCase(0.0, 0)]
        [TestCase(0.9, 0)]
        [TestCase(1.0, 1)]
        [TestCase(359.9, 359)]
        [TestCase(360.0, 0)]
        [TestCase(-1.0, 359)]
        [TestCase(-0.5, 359)]
        public void BinOf_WrapsCorrectly(double azimuth, int expectedBin)
        {
            Assert.That(HorizonProfile.BinOf(azimuth), Is.EqualTo(expectedBin));
        }

        [Test]
        public void ElevationAt_InterpolatesBetweenBinCentres()
        {
            var p = new HorizonProfile();
            p.SetBin(10, 10.0);
            p.SetBin(11, 20.0);

            // Bin centres are at 10.5 and 11.5 degrees.
            Assert.That(p.ElevationAt(10.5), Is.EqualTo(10.0).Within(1e-9));
            Assert.That(p.ElevationAt(11.5), Is.EqualTo(20.0).Within(1e-9));
            Assert.That(p.ElevationAt(11.0), Is.EqualTo(15.0).Within(1e-9));
        }

        [Test]
        public void ElevationAt_InterpolatesAcrossNorth()
        {
            var p = new HorizonProfile();
            p.SetBin(359, 10.0);
            p.SetBin(0, 20.0);
            Assert.That(p.ElevationAt(0.0), Is.EqualTo(15.0).Within(1e-9));
        }

        [Test]
        public void SetArc_WrapsAcrossNorth()
        {
            var p = new HorizonProfile();
            p.SetArc(350, 10, 25.0);

            Assert.That(p.RawBin(350), Is.EqualTo(25.0));
            Assert.That(p.RawBin(0), Is.EqualTo(25.0));
            Assert.That(p.RawBin(10), Is.EqualTo(25.0));
            Assert.That(p.RawBin(11), Is.EqualTo(0.0));
            Assert.That(p.RawBin(349), Is.EqualTo(0.0));
        }

        [Test]
        public void Flat_CoversEveryBearing()
        {
            var p = HorizonProfile.Flat(7.5);
            Assert.That(p.CoverageFraction, Is.EqualTo(1.0));
            Assert.That(p.ElevationAt(123.4), Is.EqualTo(7.5).Within(1e-9));
            Assert.That(p.MaxElevationDeg, Is.EqualTo(7.5));
        }

        [Test]
        public void CoverageFraction_ReportsPartialSweeps()
        {
            var p = new HorizonProfile();
            for (int i = 0; i < 90; i++) p.Observe(i + 0.5, 5.0);
            Assert.That(p.CoverageFraction, Is.EqualTo(0.25).Within(1e-9));
        }
    }
}
