using UnityEngine;

namespace FunnelGunSight
{
    internal static class PlaneOfMotionSampler
    {
        public static (Vector3 point, float range)[] Sample(
            Vector3    aircraftPosition,
            Vector3    gunWorldDir,
            float      muzzleVelocity,
            Vector3    angularVelocityWorld,
            Vector3    aircraftVelocity,
            WeaponInfo weaponInfo,
            int        ballisticSteps,
            int        pointCount,
            float      minRange,
            float      maxRange,
            float      minAngularRate,
            out Vector3 angAxisUsed)
        {
            pointCount     = Mathf.Max(pointCount, 2);
            muzzleVelocity = Mathf.Max(muzzleVelocity, 1f);
            minRange       = Mathf.Max(minRange, 1f);
            maxRange       = Mathf.Max(maxRange, minRange + 1f);
            ballisticSteps = Mathf.Max(ballisticSteps, 1);

            // A bullet leaves the muzzle with the aircraft's full velocity vector
            // added, not just the component along the gun axis — Gun.Fire() passes
            // `velocityInherit.velocity + muzzle.forward * muzzleVelocity` straight
            // into BulletSim. Modelling only the along-axis part would miss the
            // sideways drift produced by any AoA or sideslip.
            Vector3 initialVelocity = gunWorldDir.normalized * muzzleVelocity + aircraftVelocity;

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

            float dragCoef = weaponInfo?.dragCoef ?? 0f;
            float gravMult = weaponInfo?.gravMult  ?? 1f;

            for (int i = 0; i < pointCount; i++)
            {
                float range = minRange + step * i;

                // Simulate drag and gravity to get time-of-flight and aim direction.
                Vector3 bulletPos = SimulateBullet(
                    initialVelocity,
                    muzzleVelocity,   // raw, air-relative, for the drag denominator
                    dragCoef,
                    gravMult,
                    range,
                    ballisticSteps,
                    out float actualTof);

                float leadDeg = angSpeed * actualTof * Mathf.Rad2Deg;

                // bulletPos direction already includes gravity droop.
                Vector3 baseDir = bulletPos.sqrMagnitude > 0.001f
                    ? bulletPos.normalized
                    : gunWorldDir.normalized;

                Vector3 leadDir = Quaternion.AngleAxis(leadDeg, angAxis) * baseDir;
                results[i] = (aircraftPosition + leadDir.normalized * range, range);
            }

            angAxisUsed = angAxis;
            return results;
        }

        // Euler-integrates drag and gravity using the game's own bullet law
        // (BulletSim.Bullet.TrajectoryTrace), and returns the bullet's position
        // relative to the muzzle at exactly targetRange metres, plus the time of
        // flight to get there.
        //
        // The integration runs until the range is actually reached rather than for
        // a fixed number of steps. An earlier version sized `dt` from the drag-free
        // flight time and then ran exactly `steps` iterations, so the loop always
        // ran out of budget before covering targetRange and reported a time of
        // flight equal to targetRange / muzzleVelocity — the drag-free time. Since
        // lead angle is directly proportional to time of flight, that underestimate
        // became a systematic underlead, reaching 31% at 1200 m with the draggier
        // cannons.
        private static Vector3 SimulateBullet(
            Vector3 initialVelocity,
            float   rawMuzzleVelocity,  // for drag formula denominator (air-relative)
            float   dragCoef,
            float   gravMult,
            float   targetRange,
            int     steps,
            out float actualTof)
        {
            Vector3 vel = initialVelocity;
            Vector3 pos = Vector3.zero;
            actualTof   = 0f;

            float speed0 = Mathf.Max(vel.magnitude, 1f);

            // Closed-form time to cover targetRange under pure quadratic drag,
            // used only to size the step so `steps` stays a meaningful resolution
            // knob across weapons with very different drag coefficients:
            //   dv/dt = -k v^2, k = dragCoef / muzzleVelocity
            //   t(x)  = (e^(k x) - 1) / (v0 k)
            // Falls back to the straight-line time when drag is negligible.
            float k  = dragCoef / Mathf.Max(rawMuzzleVelocity, 1f);
            float tEstimate = k > 1e-6f
                ? (Mathf.Exp(Mathf.Min(k * targetRange, 10f)) - 1f) / (speed0 * k)
                : targetRange / speed0;

            float dt = Mathf.Max(tEstimate / steps, 1e-5f);

            // The estimate ignores gravity and any inherited velocity, so allow
            // headroom past `steps` before giving up.
            int maxIterations = steps * 3;

            for (int s = 0; s < maxIterations; s++)
            {
                Vector3 posPrev  = pos;
                float   distPrev = posPrev.magnitude;

                vel.y -= 9.81f * dt * gravMult;
                vel   -= vel.sqrMagnitude * dragCoef * dt * vel.normalized / rawMuzzleVelocity;
                pos   += vel * dt;

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
