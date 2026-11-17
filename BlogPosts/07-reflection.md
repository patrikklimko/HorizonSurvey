# Week 7 — Looking back

Seven weeks ago this was a question: can a phone measure the sky well enough to replace a $2,195 instrument? This post is about what I learned answering it, what I'd do differently, and where it could go next.

## What surprised me: how AR actually works

Before this course, I thought of AR as graphics: put a 3D object on a table and make it look like it belongs there. Building this app turned that around. AR is first a *measuring system*, and the graphics are the smallest part of it.

A few things I didn't know in September:

- **ARKit knows down, but not north.** The world frame is aligned with gravity, so elevation is reliable from the first frame. But the forward direction is wherever the phone happened to point when the session started. Every session has its own yaw, which is why my heading offsets were 54°, 237°, 29° and 302° in different sessions on the same afternoon. They're all correct, just for different frames.
- **The tracking is very good, and still not perfect.** Elevation came out a constant 0.31° low on my phone, and repeatable to a few hundredths of a degree. That's small enough to ignore in a game and large enough to matter in an instrument, so it got measured and corrected.
- **Things at infinity need different rules.** The sky doesn't move when you take a step. The sun paths sit on a dome that follows the camera's position but never its rotation. Getting that right was a short piece of code and a long piece of thinking.
- **The camera is not your eye.** Pointed at the sun, the camera sees a white blob several degrees wide. A thin white marker inside it is invisible. You only find that out by pointing the phone at the actual sun.

## The hardest part: finding north

The hardest moment was calibrating the heading. On the first morning, the compass was 13° out, a little more than iOS's own estimate of ±11°. By the afternoon, at the same window, it had been anywhere from −16° to +46°. A compass error that changes with where you stand and which way you face can't be calibrated away.

The outdoor surveys added a twist. Out in the open, the same compass was within a few degrees of the sun. At the window at home, half an hour earlier, it was 47 degrees out. The compass is worst exactly where people put small solar panels: on balconies and next to walls.

What got me past it was changing the question. Instead of asking how to make the compass better, I asked what else knows where north is, and the answer was the sun. Its position is known exactly from the time and place, so aiming at it gives a heading to about ±0.3°.

Then the sun sighting had its own hole. Aiming at a chimney that happened to sit at the sun's elevation passed my safety check. The fix was a second rule: two sightings must agree. It caught a real mistake on its first afternoon. The lesson I'll take from this is that a check is only as good as the mistakes you've imagined. The field finds the ones you haven't.

## What I'd do differently

**Go outside in week one, not week six.** Almost every important change came from field testing. The ring that vanished into the glare. The gate that a chimney could fool. Me tracing the sun's path instead of the skyline, because it was the brightest line on the screen. None of these were visible at a desk. If I started again, I'd test outdoors every week, even with half-finished features.

**Log to files from day one.** For the first field day, my data was screenshots, and I worked numbers out of pixels. The calibration log I added that afternoon gave me 23 attempts with every value, for free. Data you don't save is data you don't have.

**Skip the template.** The first week went on fighting a Unity project template that didn't match its own packages, behind a corporate network proxy. The working version was an empty scene with three objects. I'd start there.

**Decide the risky parts earlier.** Automatic sky detection was planned as a full week. I cut it late, for reasons I could have tested in an afternoon: point the camera at a low sun, and see what the image looks like.

## What I'd build next

The measurement works. The weak part is the experience of using it. Right now the screen is an engineering instrument panel: eleven lines of numbers and eight buttons. That's right for developing and validating it, and wrong for a homeowner.

A next version would have:

- **A guided flow instead of a dashboard.** "Stand where the panel will go" → "Point at the sun" → "Trace the rooftops" → result. One step on screen at a time, with the numbers hidden unless you ask for them.
- **Help with aiming.** Find the sun in the camera image automatically and snap the sighting to its centre, instead of relying on my aim at the glare.
- **Real energy, not just percentages.** Ask for the panel's tilt and direction, send the measured horizon to PVGIS, and show kilowatt-hours per year, the number people actually decide on.
- **A report to share**, a PDF with the sun chart and monthly figures for an installer or a neighbour.

With those, it could be a real app for real people. The core of it, a phone that can tell you where the sun will and won't reach, is already there and already validated.

## In one paragraph

I set out to find whether a phone can replace a dedicated solar survey instrument. The measurement side says yes: elevation to a few hundredths of a degree after correction, heading to a third of a degree from the sun, and an overlay that lands where the astronomy says it should. Two surveys of the same open spot gave 94.4 % and 94.7 %. Next to a wall a couple of metres away they differed by 2.4 points, which taught me that close obstructions need you to stand on the exact spot. The thing I'll remember most is how much of the work happened outside, pointing a phone at the sky and finding out what I'd got wrong.
