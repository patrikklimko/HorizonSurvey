using System;

namespace HorizonSurvey.Solar
{
    public readonly struct SolarAccessResult
    {
        /// <summary>Percentage of the year's available solar energy that reaches this spot.</summary>
        public readonly double AnnualPercent;

        /// <summary>Same figure per calendar month, index 0 = January.</summary>
        public readonly double[] MonthlyPercent;

        /// <summary>
        /// Clear-sky energy proxy in Wh/m2 that would arrive with no obstructions.
        /// Useful for comparing months; not a calibrated yield figure.
        /// </summary>
        public readonly double AnnualAvailableProxyWhPerM2;

        public readonly double AnnualReceivedProxyWhPerM2;

        public SolarAccessResult(
            double annualPercent,
            double[] monthlyPercent,
            double annualAvailable,
            double annualReceived)
        {
            AnnualPercent = annualPercent;
            MonthlyPercent = monthlyPercent;
            AnnualAvailableProxyWhPerM2 = annualAvailable;
            AnnualReceivedProxyWhPerM2 = annualReceived;
        }
    }

    /// <summary>
    /// Integrates a year of sun positions against a measured horizon profile to
    /// produce solar access: the fraction of available solar energy that actually
    /// reaches the spot.
    ///
    /// Method: step through every day of the year at a fixed time resolution. At
    /// each step, if the sun is up, weight it by a clear-sky irradiance model and
    /// add it to the "available" total. If it also clears the measured horizon at
    /// its bearing, add it to the "received" total. Received over available is the
    /// answer.
    ///
    /// Because the result is a ratio, systematic error in the irradiance model
    /// largely cancels. What matters is that the weighting is roughly right in its
    /// *shape* — low winter sun must count for less than high summer sun, or the
    /// monthly figures would be meaningless.
    /// </summary>
    public static class SolarAccessCalculator
    {
        const double Deg2Rad = Math.PI / 180.0;

        /// <summary>Solar constant, W/m2.</summary>
        public const double SolarConstant = 1353.0;

        /// <summary>
        /// Clear-sky irradiance on a horizontal surface, W/m2, for a given solar
        /// elevation. Uses the Kasten-Young (1989) relative air mass, which stays
        /// well behaved near the horizon where the naive 1/sin(h) blows up. That
        /// matters here: at 56 degrees north the midwinter sun never gets above
        /// about 11 degrees.
        /// </summary>
        public static double ClearSkyHorizontalIrradiance(double elevationDeg)
        {
            if (elevationDeg <= 0.0) return 0.0;

            double sinH = Math.Sin(elevationDeg * Deg2Rad);
            double airMass = 1.0 / (sinH + 0.50572 * Math.Pow(elevationDeg + 6.07995, -1.6364));
            double directNormal = SolarConstant * Math.Pow(0.7, Math.Pow(airMass, 0.678));
            return directNormal * sinH;
        }

        /// <summary>
        /// Compute solar access for a location, year and measured horizon.
        /// stepMinutes trades accuracy for speed; 5 is plenty and runs in well
        /// under a second.
        /// </summary>
        public static SolarAccessResult Compute(
            double latitudeDeg,
            double longitudeDeg,
            int year,
            HorizonProfile horizon,
            int stepMinutes = 5)
        {
            if (horizon == null) throw new ArgumentNullException(nameof(horizon));
            if (stepMinutes <= 0 || stepMinutes > 60)
                throw new ArgumentOutOfRangeException(nameof(stepMinutes));

            var available = new double[12];
            var received = new double[12];

            double hoursPerStep = stepMinutes / 60.0;

            var day = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            while (day.Year == year)
            {
                int m = day.Month - 1;

                for (int minute = 0; minute < 1440; minute += stepMinutes)
                {
                    DateTime t = day.AddMinutes(minute);
                    SolarPosition sun = SolarEngine.Position(latitudeDeg, longitudeDeg, t);

                    if (sun.ElevationDeg <= 0.0) continue;

                    double wattsPerM2 = ClearSkyHorizontalIrradiance(sun.ElevationDeg);
                    if (wattsPerM2 <= 0.0) continue;

                    double wattHours = wattsPerM2 * hoursPerStep;
                    available[m] += wattHours;

                    if (sun.ElevationDeg > horizon.ElevationAt(sun.AzimuthDeg))
                        received[m] += wattHours;
                }

                day = day.AddDays(1);
            }

            var monthlyPercent = new double[12];
            double totalAvailable = 0.0;
            double totalReceived = 0.0;

            for (int m = 0; m < 12; m++)
            {
                totalAvailable += available[m];
                totalReceived += received[m];
                monthlyPercent[m] = available[m] > 0.0
                    ? 100.0 * received[m] / available[m]
                    : 0.0;
            }

            double annualPercent = totalAvailable > 0.0
                ? 100.0 * totalReceived / totalAvailable
                : 0.0;

            return new SolarAccessResult(
                annualPercent, monthlyPercent, totalAvailable, totalReceived);
        }
    }
}
