# Week 1 — Measuring the sky with a phone

## The problem

Before you mount solar panels anywhere — a roof, a carport, a balcony, a garden frame — you need to know how much sunlight that exact spot actually gets across the year. Not the region, not the street. The spot. A chimney, a neighbour's gable, or one mature birch standing to the south can remove a quarter of the annual yield, and none of that shows up in a satellite estimate.

Professionals answer this with a dedicated instrument. The Solmetric SunEye 210 is the standard one, and it still sells for around $2,195. At that price, installers own one and homeowners don't. So domestic siting decisions get made by standing in the garden, squinting, and guessing.

The measurement itself isn't exotic. You need two things: the outline of everything blocking the sky as seen from that spot, and the sun's track across that sky for every day of the year. The second is pure astronomy. The first is an angular measurement of real objects from a real position — which is exactly what a phone with a tracked camera can do.

So that's the project. Stand where the panel would go, sweep the phone across the sky, and get back a number: how much of the available sunlight this spot actually receives, month by month.

## Why this has to be AR

I want to state this plainly, because I think it's the part that justifies the project existing in an XR course rather than as a regular app.

A 2D app cannot do this measurement. The quantity being measured is the angular position of physical objects relative to a specific vantage point. You have to be standing there, and the answer changes if you move three metres to the left. The camera's pose in space *is* the instrument — not a way of illustrating the result, but the mechanism that produces it.

That's a cleaner justification than most AR projects get, and it's the reason I picked this one.

## Working alone

The course description asks for groups. I asked to do this individually and Kasper agreed, so a word on why.

It's one long pipeline rather than a set of parallel features. Camera angles feed the horizon model, which feeds the solar calculations, which feed the visualisation. Each stage depends entirely on the one before it, so splitting it between two people would mostly add coordination rather than throughput.

I also work as a software engineer at Schneider Electric, which is an energy company, and the idea comes out of that world. I'd like to keep developing it after the course, and that gets awkward to share.

## Week 1: is the phone actually an instrument?

Everything in this project rests on one assumption: that an iPhone can measure an angle to the sky accurately enough to be useful. If that's false, there's no project. So week one exists to answer that single question before I build anything on top of it.

The test is small. Read the AR camera's pose each frame, convert the forward vector into elevation and azimuth, and display them live with a crosshair at the screen centre. Then point the crosshair at something whose true angle I can compute independently, and compare.

```csharp
Vector3 fwd = arCamera.transform.forward;

float elevation = Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg;
float azimuthAR = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
```

That's the whole measurement, for now.

## What the numbers said

Running on the iPhone 15 Pro, AR Foundation reports the camera intrinsics:

Three things worth noticing.

The focal length is identical in x and y, so pixels are square and I don't need to correct for anisotropy.

The principal point sits at (967.5, 721.3) against an image of 1920x1440 — almost exactly the geometric centre at (960, 720). The optical axis is centred, which keeps the pixel-to-ray maths clean when I get to the sweep in week four.

And those two together imply a horizontal field of view of about 68.5 degrees, which is right for this camera. Everything is internally consistent, which is the first real evidence that I'm reading the sensor correctly rather than reading plausible-looking garbage.

## The asymmetry I didn't expect

The useful discovery this week wasn't a number, it was a structural one.

My two measured angles are not equally trustworthy, and the gap is enormous.

**Elevation is essentially free.** AR Foundation's world frame is gravity-aligned, so "up" is known to a fraction of a degree, always, indoors or out, regardless of tracking quality. Lay the phone flat on a table and it reads +90. Flip it over and it reads -90. Gravity does not get confused.

**Azimuth is the entire problem.** Nothing in the AR coordinate frame knows where north is. The azimuth my app currently displays is relative to wherever the session happened to start — arbitrary, and useless. Every bearing in the final horizon profile will inherit whatever error I have in establishing north.

So the architecture follows from that: trust elevation, calibrate azimuth, and report azimuth uncertainty alongside the results instead of pretending it isn't there. Week three is entirely about finding north, and I now understand why it deserves a whole week.

## The setup tax

Honest accounting: almost none of this week went into the code above. The script is about sixty lines and took two hours.

The rest went to:

- Unity's package manager refusing to fetch a package because this machine sits behind a corporate TLS proxy. curl trusted the certificate; Unity's own certificate bundle didn't. My first fix made things worse — pointing Unity at a custom CA file replaced its trust store instead of extending it, and then *nothing* resolved.
- The AR Mobile template shipping sample assets from XR Interaction Toolkit 3.5.1 while the project installed 3.6.0. 112 compile errors, all in demo content I didn't want.
- Deleting those samples, which orphaned the template's own prefabs — including the XR Origin rig the scene was built on.

In the end I stopped trying to repair the template and built the scene from scratch. It contains three objects: a directional light, an XR Origin, and an AR Session. That's all this project needs, and the minimal version works where the half-gutted template didn't.

Lesson, filed for later: a project template is a convenience, not a foundation. The moment it fights you, delete it.

## Next week

The solar engine. Declination, equation of time, hour angle, altitude and azimuth — then integrating clear-sky irradiance across 365 days against a horizon profile.

All of it is pure C# with published reference values, so it's unit-testable on the laptop with no device involved. After a week of fighting toolchains, writing code I can actually assert against sounds restful.

The test I'll hold it to: my computed sunrise and solar-noon times for Horsens should match a published almanac to within a minute. And solar noon is not at twelve — the equation of time moves it by up to sixteen minutes across the year, plus a fixed offset for longitude within the time zone. I expect that to be where the bugs are.
