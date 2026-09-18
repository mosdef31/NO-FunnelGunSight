using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// The game's own bullet law, in one place.
    ///
    /// WHY THIS FILE EXISTS. Three things in this mod now integrate a bullet: the
    /// funnel's plane-of-motion sweep, the tracer-stream line, and the ground
    /// sight's impact solve. They have to agree to the last float, because the
    /// whole point of drawing the tracer line is that a player can SEE whether the
    /// model matches the rounds - and a line drawn from a second, slightly
    /// different integrator would be auditing itself. So the step is written once
    /// here and everything calls it.
    ///
    /// THE LAW IS COPIED FROM `BulletSim.Bullet.TrajectoryTrace`, which does, per
    /// physics tick:
    ///
    ///     velocity.y -= 9.81f * dt * info.gravMult;
    ///     velocity   -= velocity.magnitude * velocity
    ///                   * (info.dragCoef * dt / info.muzzleVelocity);
    ///     position   += velocity * dt;
    ///
    /// and retires the round when
    /// `velocity.sqrMagnitude &lt; info.muzzleVelocity^2 * 0.02f`, which is about 14%
    /// of the nominal muzzle velocity.
    /// </summary>
    internal static class Ballistics
    {
        /// <summary>
        /// The fraction of nominal muzzle velocity below which the game deletes the
        /// round. `BulletSim` compares squared speeds against
        /// `info.muzzleVelocity * info.muzzleVelocity * 0.02f`, so this is that
        /// 0.02 kept in the same squared form rather than rooted and re-squared.
        /// </summary>
        private const float CutoffSpeedFractionSq = 0.02f;

        /// <summary>
        /// The step the game's own bullets are integrated at. `BulletSim` steps
        /// every bullet once per FixedUpdate with `Time.fixedDeltaTime`, and
        /// explicit Euler at that step is NOT the exact solution of the drag ODE -
        /// it bleeds speed faster than the true curve, so the real round arrives
        /// LATE compared with a finely integrated one. Matching the game's step is
        /// therefore MORE accurate than integrating accurately: the job is to
        /// predict THIS game's bullet, not an ideal one.
        /// </summary>
        internal static float GameStep()
        {
            float dt = Time.fixedDeltaTime;
            return dt > 1e-5f ? dt : 0.02f;
        }

        /// <summary>
        /// Everything about a round that the trajectory depends on, resolved once
        /// per frame instead of per step.
        ///
        /// `LaunchVelocity` and `DragDenominator` ARE TWO DIFFERENT NUMBERS AND
        /// THAT IS NOT A MISTAKE. `Gun.SpawnBullet` launches at the gun's own
        /// private `muzzleVelocity` field; `TrajectoryTrace` divides the drag term
        /// by `info.muzzleVelocity` regardless. A gun fed from a `GunAmmo` mount,
        /// or one whose barrel has heated, uses two different numbers and so must
        /// anything predicting it. See `FunnelGunSight.ResolveMuzzleVelocity`.
        /// </summary>
        internal readonly struct Inputs
        {
            internal Inputs(float launchVelocity, WeaponInfo? info)
            {
                LaunchSpeed = Mathf.Max(launchVelocity, 1f);

                float nominal = info != null && info.muzzleVelocity > 1f
                    ? info.muzzleVelocity
                    : LaunchSpeed;

                DragDenominator = nominal;
                DragCoefficient = info != null ? info.dragCoef : 0f;
                GravityMultiple = info != null ? info.gravMult : 1f;
                CutoffSpeedSq   = nominal * nominal * CutoffSpeedFractionSq;
            }

            internal float LaunchSpeed     { get; }
            internal float DragDenominator { get; }
            internal float DragCoefficient { get; }
            internal float GravityMultiple { get; }

            /// <summary>
            /// Squared speed below which the game retires the round. Anything drawn
            /// past this point is a bullet that no longer exists.
            /// </summary>
            internal float CutoffSpeedSq { get; }
        }

        /// <summary>
        /// One physics tick of the game's bullet, in place. `pos` is an offset from
        /// the muzzle, not a world position, so the caller decides what frame it is
        /// working in.
        /// </summary>
        internal static void Step(ref Vector3 vel, ref Vector3 pos, float dt, in Inputs inp)
        {
            vel.y -= 9.81f * dt * inp.GravityMultiple;
            vel   -= vel.magnitude * vel * (inp.DragCoefficient * dt / inp.DragDenominator);
            pos   += vel * dt;
        }

        /// <summary>
        /// The velocity a round leaves this gun with. `Gun.SpawnBullet` passes
        /// `velocityInherit.velocity + muzzle.forward * muzzleVelocity`, so the
        /// aircraft's FULL velocity vector is added, not just the component along
        /// the gun axis - which is what carries the sideways drift from any angle
        /// of attack or sideslip.
        /// </summary>
        internal static Vector3 LaunchVelocity(
            Vector3 gunWorldDir, Vector3 aircraftVelocity, in Inputs inp) =>
            gunWorldDir.normalized * inp.LaunchSpeed + aircraftVelocity;

        /// <summary>
        /// How many steps to allow before giving up. Closed-form time to cover
        /// `targetRange` under pure quadratic drag:
        ///
        ///     dv/dt = -k v^2,  k = dragCoef / dragDenom
        ///     t(x)  = (e^(k x) - 1) / (v0 k)
        ///
        /// The estimate ignores gravity and inherited velocity, so the budget is
        /// three times it, floored at `floorSteps` so a very short range still gets
        /// a sane cap.
        /// </summary>
        internal static int IterationBudget(
            float speed0, in Inputs inp, float targetRange, int floorSteps, float dt)
        {
            float v0 = Mathf.Max(speed0, 1f);
            float k  = inp.DragCoefficient / Mathf.Max(inp.DragDenominator, 1f);

            float tEstimate = k > 1e-6f
                ? (Mathf.Exp(Mathf.Min(k * targetRange, 10f)) - 1f) / (v0 * k)
                : targetRange / v0;

            int budget = Mathf.CeilToInt(tEstimate / Mathf.Max(dt, 1e-5f)) * 3;
            return Mathf.Clamp(budget, floorSteps, 4000);
        }

        /// <summary>
        /// Walks the trajectory from the muzzle and writes a world point roughly
        /// every `spacingMeters`, stopping at `maxRange`, at the speed cutoff, or
        /// when `points` is full. Returns how many points were written.
        ///
        /// The FIRST point written is always the muzzle itself, so the caller can
        /// draw a line that starts on the gun rather than a step downrange of it.
        /// </summary>
        internal static int Trace(
            Vector3   muzzleWorld,
            Vector3   gunWorldDir,
            Vector3   aircraftVelocity,
            in Inputs inp,
            float     maxRange,
            float     spacingMeters,
            Vector3[] points,
            int       floorSteps)
        {
            if (points.Length == 0) return 0;

            maxRange      = Mathf.Max(maxRange, 1f);
            spacingMeters = Mathf.Max(spacingMeters, 0.5f);

            Vector3 vel = LaunchVelocity(gunWorldDir, aircraftVelocity, inp);
            Vector3 pos = Vector3.zero;

            points[0] = muzzleWorld;
            int count = 1;

            float dt       = GameStep();
            int   budget   = IterationBudget(vel.magnitude, inp, maxRange, floorSteps, dt);
            float nextMark = spacingMeters;

            for (int s = 0; s < budget && count < points.Length; s++)
            {
                Step(ref vel, ref pos, dt, in inp);

                if (vel.sqrMagnitude < inp.CutoffSpeedSq) break;

                float dist = pos.magnitude;
                if (dist >= nextMark)
                {
                    points[count++] = muzzleWorld + pos;
                    // Skip whole marks at once, so one long step at high speed does
                    // not leave the spacing counter behind the bullet.
                    nextMark = (Mathf.Floor(dist / spacingMeters) + 1f) * spacingMeters;
                }

                if (dist >= maxRange) break;
            }

            return count;
        }
    }
}
