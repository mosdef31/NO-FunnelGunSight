using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// The funnel's spine: for each range along the sight, where a target would have
    /// to be for a round fired now to meet it, given how hard the aircraft is
    /// turning.
    ///
    /// THE BULLET LAW ITSELF LIVES IN <see cref="Ballistics"/> AND IS NOT REPEATED
    /// HERE. This file used to carry its own copy of the step, its own
    /// `GameStep()` and its own iteration budget. Once the tracer line and the
    /// ground sight started integrating the same round, two copies of the law were
    /// two things that could drift, and the tracer line's whole value is that it
    /// can be checked against the real tracers - which it cannot do if it is
    /// integrating a different bullet from the walls beside it.
    /// </summary>
    internal static class PlaneOfMotionSampler
    {
        public static (Vector3 point, float range)[] Sample(
            Vector3              aircraftPosition,
            Vector3              gunWorldDir,
            in Ballistics.Inputs inp,
            Vector3              angularVelocityWorld,
            Vector3              aircraftVelocity,
            int                  ballisticSteps,
            int                  pointCount,
            float                minRange,
            float                maxRange,
            float                minAngularRate,
            out Vector3          angAxisUsed)
        {
            pointCount     = Mathf.Max(pointCount, 2);
            minRange       = Mathf.Max(minRange, 1f);
            maxRange       = Mathf.Max(maxRange, minRange + 1f);
            ballisticSteps = Mathf.Max(ballisticSteps, 1);

            Vector3 initialVelocity =
                Ballistics.LaunchVelocity(gunWorldDir, aircraftVelocity, in inp);

            float   angSpeed = angularVelocityWorld.magnitude;
            Vector3 angAxis;

            if (angSpeed < minAngularRate)
            {
                angAxis  = Vector3.Cross(gunWorldDir, Vector3.up);
                angAxis  = angAxis.sqrMagnitude > 0.001f ? angAxis.normalized : Vector3.left;
                angSpeed = minAngularRate;
            }
            else
            {
                angAxis = angularVelocityWorld.normalized;
            }

            var   results   = new (Vector3 point, float range)[pointCount];
            float rangeSpan = maxRange - minRange;
            float step      = rangeSpan / Mathf.Max(pointCount - 1, 1);

            Vector3 gunDirNorm = gunWorldDir.normalized;

            // Pass 1: one sweep down the gun line records the time of flight to every
            // spine range at once. The ranges ascend, so a single integration passes
            // through all of them in order - running `pointCount` separate simulations
            // of the same trajectory was pure waste.
            var tof = new float[pointCount];
            SweepTimesOfFlight(
                initialVelocity, in inp, minRange, step, pointCount, ballisticSteps, tof);

            for (int i = 0; i < pointCount; i++)
            {
                float range     = minRange + step * i;
                float actualTof = tof[i];

                // The round arriving at this range now was fired `actualTof` ago,
                // when the gun pointed `angSpeed * actualTof` back along the turn.
                // Rotate the *firing conditions* rather than the finished
                // trajectory: gravity is world-vertical and does not turn with the
                // aircraft, so rotating a drooped vector swings the droop sideways
                // by the lead angle and pushes the spine off the tracer stream -
                // a few milliradians at 1200 m, which is the far end's whole wall
                // half-width. The inherited velocity *does* rotate with the
                // airframe through a turn, so it is carried along.
                Quaternion lead       = Quaternion.AngleAxis(angSpeed * actualTof * Mathf.Rad2Deg, angAxis);
                Vector3    leadGunDir = lead * gunDirNorm;
                Vector3    leadVel    = lead * aircraftVelocity;

                Vector3 bulletPos = SimulateBullet(
                    leadGunDir * inp.LaunchSpeed + leadVel,
                    in inp,
                    range,
                    ballisticSteps,
                    out float _);

                // Gravity droop is now world-vertical, applied after the rotation.
                Vector3 baseDir = bulletPos.sqrMagnitude > 0.001f
                    ? bulletPos.normalized
                    : leadGunDir;

                results[i] = (aircraftPosition + baseDir * range, range);
            }

            angAxisUsed = angAxis;
            return results;
        }

        // Integrates once along the gun line and fills `tof` with the time of flight
        // to each of `count` ranges starting at `minRange` and spaced `step` apart.
        private static void SweepTimesOfFlight(
            Vector3              initialVelocity,
            in Ballistics.Inputs inp,
            float                minRange,
            float                step,
            int                  count,
            int                  steps,
            float[]              tof)
        {
            Vector3 vel = initialVelocity;
            Vector3 pos = Vector3.zero;
            float   t   = 0f;
            float   dt  = Ballistics.GameStep();

            float maxRange = minRange + step * (count - 1);
            int   budget   = Ballistics.IterationBudget(
                initialVelocity.magnitude, in inp, maxRange, steps, dt);

            int next = 0;

            for (int s = 0; s < budget && next < count; s++)
            {
                float distPrev = pos.magnitude;

                Ballistics.Step(ref vel, ref pos, dt, in inp);

                float dist = pos.magnitude;
                float span = dist - distPrev;

                // One step can cross several spine ranges when they are closely spaced.
                while (next < count)
                {
                    float wanted = minRange + step * next;
                    if (dist < wanted) break;

                    float f = span > 1e-6f ? Mathf.Clamp01((wanted - distPrev) / span) : 1f;
                    tof[next] = t + dt * f;
                    next++;
                }

                t += dt;
            }

            // Anything the integration never reached - a very short-ranged round, or
            // the budget running out - keeps the last solved time rather than zero,
            // which would collapse the far end of the funnel onto the boresight.
            float last = next > 0 ? tof[next - 1] : t;
            for (; next < count; next++) tof[next] = last;
        }

        // Returns the bullet's position relative to the muzzle at exactly
        // `targetRange` metres, plus the time of flight to get there.
        private static Vector3 SimulateBullet(
            Vector3              initialVelocity,
            in Ballistics.Inputs inp,
            float                targetRange,
            int                  steps,
            out float            actualTof)
        {
            Vector3 vel = initialVelocity;
            Vector3 pos = Vector3.zero;
            actualTof   = 0f;

            float dt     = Ballistics.GameStep();
            int   budget = Ballistics.IterationBudget(
                vel.magnitude, in inp, targetRange, steps, dt);

            for (int s = 0; s < budget; s++)
            {
                Vector3 posPrev  = pos;
                float   distPrev = posPrev.magnitude;

                Ballistics.Step(ref vel, ref pos, dt, in inp);

                float dist = pos.magnitude;

                if (dist >= targetRange)
                {
                    // Interpolate within the final step so the returned point sits
                    // on the requested range rather than just past it.
                    float span = dist - distPrev;
                    float f    = span > 1e-6f
                        ? Mathf.Clamp01((targetRange - distPrev) / span)
                        : 1f;

                    pos        = Vector3.Lerp(posPrev, pos, f);
                    actualTof += dt * f;
                    return pos;
                }

                actualTof += dt;
            }

            return pos; // world-space offset from muzzle, along the trajectory
        }
    }
}
