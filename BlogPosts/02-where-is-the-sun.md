# Week 2 — Where is the sun, and how do I know I'm right?

Last week I proved the phone can measure an angle. This week is the other half of the problem, and it needs no phone at all: given a place and a moment, where is the sun?

Those two halves meet in week six. Until then they are completely independent, which turns out to be the most useful decision I've made on this project.

## The engine has no idea Unity exists

`Assets/Scripts/Solar/` has an assembly definition with `noEngineReferences: true`. The solar code cannot reference `UnityEngine` even by accident — the compiler refuses. Everything is `System.Math` in double precision.

That sounds like pedantry. It isn't. It means:

- I can develop and test the entire results pipeline on the laptop, with no device deploy in the loop
- I can port the same equations to another language and compare, which is exactly what I did below
- The expensive, fragile, phone-dependent part of the project is now the smallest part

Three files:

| File | Depends on AR? | Job |
|---|---|---|
| `SolarEngine.cs` | no | Sun position from latitude, longitude and a UTC instant |
| `HorizonProfile.cs` | no | 360 one-degree bins of obstruction elevation |
| `SolarAccessCalculator.cs` | no | Integrate a year of sun against a horizon |

## Solar noon is not at twelve

This is the part I'd have got wrong if I'd written it from memory.

Clock time and solar time disagree for two separate reasons. One is boring: your longitude within your time zone. Horsens sits at 9.85 degrees east, so the sun crosses the meridian about 39 minutes earlier than it does at the zone's nominal centre.

The other is not boring. Earth's orbit is slightly elliptical and its axis is tilted, so the sun runs ahead of and behind mean time over the year, by up to a quarter of an hour either way. This is the **equation of time**, and my implementation reproduces the classic envelope:

```
minimum  -14.23 minutes  on 11 February
maximum  +16.49 minutes  on  3 November
```

Those dates and magnitudes are textbook, which is a decent sign the implementation is right. Get this term wrong and nothing crashes — your sun just sits in a slightly wrong place, all year, consistently. That is a much nastier kind of bug than a crash, and it is why this week's work is mostly about verification rather than code.

## How I checked it

The temptation with astronomy code is to copy a reference implementation and then "test" it against itself. That proves nothing.

So instead I wrote every assertion as something derivable from first principles or checkable against an almanac, and separately ported the same equations to Python to catch transcription errors.

Geometry first. At solar noon the sun's elevation must be `90 - latitude + declination`. At Horsens, latitude 55.86:

| Date | Predicted | Computed |
|---|---|---|
| Summer solstice | 57.58 | 57.588 |
| Equinox | 34.14 | 34.111 |
| Winter solstice | 10.70 | 10.785 |

Declination hits +-23.438 at the solstices, which is the obliquity of the ecliptic. Azimuth at solar noon comes out at 180.000 degrees — due south — on every date tested.

Then the almanac. Midsummer day length at Horsens: 17 hours 35 minutes, sunrise 04:34 and sunset 22:09 local. Midwinter: 6 hours 58 minutes, 08:49 to 15:47. Anyone who has spent a Danish December will recognise those numbers.

## Two things I predicted wrong

Before running anything, I wrote down what I expected the shading integration to produce. Two of those predictions were badly wrong, and finding out why taught me more than the code did.

**Prediction: a uniform 10-degree skyline would destroy December.** I guessed under 10 per cent of the available light would get through.

**Actual: 53.9 per cent.**

**Prediction: a 30-degree wall filling 60 degrees of the southern sky would cost more than 15 per cent of the year.**

**Actual: 9.1 per cent.**

The cause is the same in both cases, and it's the air mass. Sunlight at low elevation travels through far more atmosphere, and the loss is not gentle:

```
 1 degree elevation ->    0.9 W/m2
 5 degrees          ->   20.8
10 degrees          ->   74.8
20 degrees          ->  222.0
45 degrees          ->  609.5
57 degrees          ->  759.4
```

A factor of eighty between one degree and ten. So the year's energy is overwhelmingly concentrated in the hours when the sun is well up, and a low skyline removes mostly worthless light. My intuition had been working in "fraction of sky blocked", which is simply the wrong measure.

This matters for the product, not just the code. It means a tool that reports "40 per cent of your sky is obstructed" would be actively misleading. The thing worth reporting is energy, and energy cares about elevation much more than it cares about area.

## The check I replaced them with

Instead of my two wrong guesses, I found an exact one.

At 56 degrees north the midwinter sun peaks at about 12.4 degrees. So a uniform 15-degree skyline must take December to **precisely zero** — not approximately, exactly, because the sun never clears it at any moment in the month.

```
ring at  0 deg -> annual 100.00%   Dec 100.00%
ring at  5 deg -> annual  99.70%   Dec  95.86%
ring at 10 deg -> annual  97.52%   Dec  53.91%
ring at 15 deg -> annual  92.22%   Dec   0.00%
ring at 20 deg -> annual  85.36%   Dec   0.00%
ring at 30 deg -> annual  66.84%   Dec   0.00%
```

A test with an exact expected value, derived from the site's latitude, that would catch almost any error in the integration. Much better than a tolerance I made up.

One more, because it's the whole premise of the product: the same 30-degree wall costs **9.1 per cent** due south and **0.0 per cent** due north. Direction is everything, and that is precisely what a satellite estimate cannot tell you.

## On not having a test framework

The Unity Test Framework went out of this project in week one along with the feature set that was dragging in a package my work network couldn't fetch. Rather than block on that, the verification lives in an editor menu item — `Horizon Survey > Run Solar Self-Check` — that runs the same assertions and prints a pass/fail report.

The proper NUnit suite is written and sitting in `Assets/Tests~`. The trailing tilde makes Unity ignore the folder entirely. Install the package, drop the tilde, done.

Slightly inelegant, but it meant the engine got verified this week rather than whenever the network cooperates.

## Next week

Finding north. Elevation came free from gravity, as week one showed. Azimuth is the opposite: nothing in the AR world frame knows which way is north, and a few degrees of error there smears the entire horizon profile sideways.

The plan is to sight the sun. If I know the time and position, I know exactly where the sun should be — which means aiming at it gives me a heading reference far better than a magnetometer sitting next to a laptop and a steel desk.

The maths for that is already in `SolarEngine.Position()`. I just didn't write it for this purpose.
