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

            // Bullet inherits aircraft velocity along the gun axis; raw
            // muzzleVelocity is kept separately for the drag denominator.
            float effectiveMuzzleVel = Mathf.Max(
                muzzleVelocity + Vector3.Dot(aircraftVelocity, gunWorldDir.normalized),
                1f);

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
                    gunWorldDir,
                    effectiveMuzzleVel,
                    muzzleVelocity,   // raw, for drag denominator (air-relative)
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

        // Euler-integrates drag and gravity, returns bullet position relative
        // to the muzzle at approximately targetRange metres, and time of flight.
        private static Vector3 SimulateBullet(
            Vector3 gunWorldDir,
            float   muzzleVelocity,     // effective (includes aircraft speed component)
            float   rawMuzzleVelocity,  // for drag formula denominator (air-relative)
            float   dragCoef,
            float   gravMult,
            float   targetRange,
            int     steps,
            out float actualTof)
        {
            Vector3 vel  = gunWorldDir.normalized * muzzleVelocity;
            Vector3 pos  = Vector3.zero;
            float   dt   = (targetRange / muzzleVelocity) / steps; // initial step estimate
            actualTof    = 0f;

            for (int s = 0; s < steps; s++)
            {
                vel.y -= 9.81f * dt * gravMult;
                vel   -= vel.sqrMagnitude * dragCoef * dt * vel.normalized / rawMuzzleVelocity;
                pos   += vel * dt;
                actualTof += dt;

                if (pos.magnitude >= targetRange) break;
            }

            return pos; // world-space offset from muzzle, along gunWorldDir with gravity droop
        }
    }
}
