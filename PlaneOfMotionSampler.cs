using UnityEngine;

namespace FunnelGunSight
{
    internal static class PlaneOfMotionSampler
    {
        public static (Vector3 point, float range)[] Sample(
            Vector3   aircraftPosition,
            Vector3   gunWorldDir,
            float     muzzleVelocity,
            Vector3   angularVelocityWorld,
            int       pointCount,
            float     minRange,
            float     maxRange,
            float     minAngularRate,
            out Vector3 angAxisUsed)
        {
            pointCount     = Mathf.Max(pointCount, 2);
            muzzleVelocity = Mathf.Max(muzzleVelocity, 1f);
            minRange       = Mathf.Max(minRange, 1f);
            maxRange       = Mathf.Max(maxRange, minRange + 1f);

            float angSpeed = angularVelocityWorld.magnitude;
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

            for (int i = 0; i < pointCount; i++)
            {
                float range   = minRange + step * i;
                float tof     = range / muzzleVelocity;
                float leadDeg = angSpeed * tof * Mathf.Rad2Deg;

                Vector3 leadDir = Quaternion.AngleAxis(leadDeg, angAxis) * gunWorldDir;
                results[i] = (aircraftPosition + leadDir.normalized * range, range);
            }

            angAxisUsed = angAxis;
            return results;
        }
    }
}
