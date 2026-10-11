using System;

namespace HorizonSurvey.Solar
{
    /// <summary>
    /// The measured skyline: for each compass bearing, how high the highest
    /// obstruction reaches.
    ///
    /// 360 bins of one degree. Bin i covers azimuth [i, i+1) and is treated as
    /// sampled at its centre, i + 0.5.
    ///
    /// This is plain data with no AR dependency, which is deliberate: the whole
    /// results pipeline can be developed and tested against hand-authored
    /// profiles long before the camera capture works.
    /// </summary>
    public sealed class HorizonProfile
    {
        public const int BinCount = 360;

        readonly double[] _elevationDeg = new double[BinCount];
        readonly int[] _sampleCount = new int[BinCount];

        public HorizonProfile() { }

        /// <summary>A profile with a flat horizon at the given elevation, fully covered.</summary>
        public static HorizonProfile Flat(double elevationDeg)
        {
            var p = new HorizonProfile();
            for (int i = 0; i < BinCount; i++)
            {
                p._elevationDeg[i] = elevationDeg;
                p._sampleCount[i] = 1;
            }
            return p;
        }

        public void Clear()
        {
            Array.Clear(_elevationDeg, 0, BinCount);
            Array.Clear(_sampleCount, 0, BinCount);
        }

        public static int BinOf(double azimuthDeg)
        {
            int b = (int)Math.Floor(SolarEngine.Mod360(azimuthDeg));
            return b >= BinCount ? BinCount - 1 : b;
        }

        /// <summary>
        /// Record one observation. We keep the maximum per bearing, because an
        /// obstruction blocks the sun whenever the sun is below its top edge —
        /// so the tallest thing seen at that bearing is what matters.
        /// </summary>
        public void Observe(double azimuthDeg, double elevationDeg)
        {
            int b = BinOf(azimuthDeg);
            if (_sampleCount[b] == 0 || elevationDeg > _elevationDeg[b])
                _elevationDeg[b] = elevationDeg;
            _sampleCount[b]++;
        }

        /// <summary>Set a bin directly. For hand-authored profiles and tests.</summary>
        public void SetBin(int bin, double elevationDeg)
        {
            if (bin < 0 || bin >= BinCount)
                throw new ArgumentOutOfRangeException(nameof(bin));
            _elevationDeg[bin] = elevationDeg;
            _sampleCount[bin] = Math.Max(1, _sampleCount[bin]);
        }

        /// <summary>Set a contiguous arc of bins, wrapping across north if needed.</summary>
        public void SetArc(double fromAzimuthDeg, double toAzimuthDeg, double elevationDeg)
        {
            int from = BinOf(fromAzimuthDeg);
            int to = BinOf(toAzimuthDeg);
            int i = from;
            while (true)
            {
                SetBin(i, elevationDeg);
                if (i == to) break;
                i = (i + 1) % BinCount;
            }
        }

        public double RawBin(int bin) => _elevationDeg[bin];

        public bool IsCovered(int bin) => _sampleCount[bin] > 0;

        public int SampleCount(int bin) => _sampleCount[bin];

        /// <summary>Fraction of the 360 bearings that have at least one observation.</summary>
        public double CoverageFraction
        {
            get
            {
                int covered = 0;
                for (int i = 0; i < BinCount; i++)
                    if (_sampleCount[i] > 0) covered++;
                return covered / (double)BinCount;
            }
        }

        /// <summary>
        /// Obstruction elevation at an arbitrary bearing, linearly interpolated
        /// between adjacent bin centres. Interpolating rather than snapping avoids
        /// a sawtooth artefact in the shading integral at low sun elevations.
        /// </summary>
        public double ElevationAt(double azimuthDeg)
        {
            double x = SolarEngine.Mod360(azimuthDeg) - 0.5;
            if (x < 0.0) x += 360.0;

            int i0 = (int)Math.Floor(x);
            double frac = x - i0;
            int i1 = (i0 + 1) % BinCount;
            i0 %= BinCount;

            return _elevationDeg[i0] * (1.0 - frac) + _elevationDeg[i1] * frac;
        }

        /// <summary>Highest obstruction anywhere in the profile.</summary>
        public double MaxElevationDeg
        {
            get
            {
                double max = double.NegativeInfinity;
                for (int i = 0; i < BinCount; i++)
                    if (_elevationDeg[i] > max) max = _elevationDeg[i];
                return max;
            }
        }
    }
}
