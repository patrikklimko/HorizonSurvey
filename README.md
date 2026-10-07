# Horizon Survey

An iOS augmented reality app that measures how much sunlight a specific spot actually receives across the year — so you can tell whether it is worth putting solar panels there.

You stand where the panel would go, sweep the phone across the sky, and the app builds a profile of everything blocking it: buildings, trees, chimneys. It then overlays the sun's real path for every day of the year and reports how much solar energy is lost to shading, month by month.

The professional instrument that does this — a Solmetric SunEye 210 — costs around $2,195. Most homeowners therefore guess, and sometimes guess badly.

**XRD course project, VIA University College, autumn 2026.** Individual project.

---

## Why AR

The measurement is the angular position of real physical objects relative to a real vantage point. You have to stand where the panel will go, and the answer changes if you move three metres to the left. The phone's camera pose *is* the instrument.

A 2D app cannot do this.

## How it works

1. **Establish true north.** Anchor the AR session's world frame to a geographic reference. This is the accuracy bottleneck — every bearing downstream inherits its error.
2. **Sweep the sky.** The user pans the phone across the sky. Each frame carries a camera pose and camera intrinsics, so any pixel maps to a known azimuth and elevation.
3. **Segment sky from obstruction.** Per frame, separate bright sky from dark obstruction and find the boundary for each image column. Accumulate into a horizon profile: 360 bins of one degree, each holding the highest obstruction seen at that bearing.
4. **Compute the sun's track.** From latitude, longitude and date, calculate solar azimuth and elevation at five-minute intervals for all 365 days.
5. **Integrate.** For each sun sample, compare its elevation against the measured horizon at that azimuth. Weight by clear-sky irradiance and sum. Unshaded over total is the solar access figure.

## Architecture

Three subsystems, deliberately separated — two of them never touch AR at all, which means they can be developed and unit-tested on a laptop without deploying to a device.

| Subsystem | Depends on AR? | What it does |
|---|---|---|
| Solar engine | No | Pure C#. Solar position from lat/lon/UTC; year integration against a horizon profile. |
| Horizon model | No | Plain data: 360 floats of obstruction elevation, plus a coverage array. |
| AR capture | Yes | Turns camera frames plus pose into entries in the horizon model. |

### The asymmetry that shapes everything

The two measured angles are not equally trustworthy.

**Elevation is nearly free.** AR Foundation's world frame is gravity-aligned, so "up" is accurate to a fraction of a degree, indoors or out, always.

**Azimuth is the whole problem.** Nothing in the AR frame knows where north is, so it has to be established by calibration, and every bearing inherits that error.

So: trust elevation, calibrate azimuth, and report azimuth uncertainty in the results.

## Stack

- Unity 6.6 (6000.6.2f1), Universal Render Pipeline
- AR Foundation 6.6.2 + Apple ARKit XR Plugin 6.6.2
- C#, iOS 16+, built on macOS via Xcode
- Target device: iPhone 15 Pro (LiDAR is **not** used — the sky has no depth — so this runs on much older iPhones too)

## Validation

Unusually for a student AR project, this one has an objective ground truth, and obtaining it costs nothing:

- **Trigonometric check.** For an object of known height at known distance, the elevation angle to its top is `atan(height / distance)`. Compare against what the app reports, across several bearings, to get an error distribution.
- **Shadow check.** Note the time a real shadow reaches a marked point. The model predicts it. Agreement is unambiguous.
- **Independent comparison.** PVGIS, the European Commission's PV tool, accepts a user-supplied horizon profile via its API.
- **Repeatability.** Survey the same spot five times and report the spread.

## Progress

| Week | Focus | Status |
|---|---|---|
| 1 | Prove the phone can measure an angle | Done |
| 2 | Solar engine, unit-tested | |
| 3 | Heading: compass baseline + sun-sighting calibration | |
| 4 | Guided sweep and horizon capture | |
| 5 | Automatic sky segmentation | |
| 6 | Sun path overlay and results screen | |
| 7 | Validation and write-up | |

Weekly write-ups are in [`BlogPosts/`](BlogPosts/).

## References

- Solmetric SunEye 210 — the instrument this replaces
- PVGIS non-interactive API, European Commission Joint Research Centre
- NOAA solar position equations
- NREL Solar Position Algorithm (Reda & Andreas)
