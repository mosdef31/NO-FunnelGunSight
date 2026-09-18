using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// CCIP - the continuously computed impact point. Where the rounds meet the
    /// ground if you fire now.
    ///
    /// WHY IT IS A SEPARATE SIGHT AND NOT A SETTING ON THE FUNNEL. The funnel is an
    /// air-to-air construction: its walls are a target's wingspan and its curve is
    /// the plane of motion of a manoeuvring fighter. Against a stationary truck
    /// none of that means anything. This solves the SAME trajectory - the same
    /// `Ballistics.Step`, the same launch velocity, the same drag denominator - and
    /// asks a different question of it: not "where must the target be" but "where
    /// does the round land". Owner, 2026-09-16: "i want to give ground attack a
    /// dedicated gunsight."
    ///
    /// HOW THE IMPACT IS FOUND, AND IT IS THE GAME'S OWN TEST. `BulletSim` does
    /// exactly two things to decide a round has hit the world:
    ///
    ///     Physics.Linecast(a, b, out hit, ~(int)PhysicsLayers.ExclusionZonesMask)
    ///
    /// and, failing that, a check that the round's global Y has gone below zero -
    /// the sea. Both are reproduced here. Using the game's own mask matters: a
    /// narrower one would miss ships and statics, and a wider one would stop the
    /// solve on an exclusion-zone trigger the bullet flies straight through.
    ///
    /// THE COST IS THE REASON FOR EVERY OTHER DECISION IN THIS FILE. A gun run at
    /// 850 m/s onto ground 2 km away is about 125 physics steps, and a linecast per
    /// step is 125 casts per frame for one pipper. So the integration runs at the
    /// game's step, as it must to stay honest, but the CASTS are batched: the
    /// trajectory is accumulated into chords of roughly <see cref="ChordMeters"/>
    /// and one cast is fired per chord. A chord is a straight line and the
    /// trajectory inside it is not, but at 25 m the sag is under a centimetre and
    /// no terrain feature in this game is finer than that.
    /// </summary>
    internal static class GroundSight
    {
        /// <summary>
        /// How far apart the linecast chords are, in metres. See the class note:
        /// this trades cast count against the sag of a straight chord under a
        /// curving trajectory, and 25 m keeps that sag in the millimetres.
        /// </summary>
        private const float ChordMeters = 25f;

        /// <summary>
        /// Floor for the flight-time ceiling, in seconds. Beyond the ceiling the
        /// round has either hit something or bled below the game's own retirement
        /// speed, and a gun solution that far out is fantasy anyway.
        ///
        /// IT IS A FLOOR AND NOT THE WHOLE ANSWER, because the ceiling has to scale
        /// with the range being asked for. It was a flat 6 s, which covers the
        /// 2500 m default with room to spare and SILENTLY TRUNCATES anything much
        /// past 3 km - the solve would stop mid-trajectory and report no impact,
        /// which looks exactly like a pipper that does not work. Per-aircraft ranges
        /// made that reachable: the A-19 ships at 5000 m.
        /// </summary>
        private const float MinFlightSeconds = 6f;

        /// <summary>
        /// Metres per second used to turn a max range into a flight-time ceiling.
        /// Deliberately BELOW any gun's muzzle velocity: a round that has bled to
        /// 250 m/s is at the bottom of its useful life, so a ceiling sized at this
        /// speed always outlasts the trajectory rather than cutting it short. The
        /// budget is a safety stop, not the thing that ends the walk - the range
        /// test and the speed cutoff inside the loop do that.
        /// </summary>
        private const float CeilingSpeed = 250f;

        /// <summary>
        /// The muzzle sits INSIDE the firing aircraft's own colliders, so the first
        /// chord would hit the shooter every time. Casting is suppressed until the
        /// round is clear of the airframe by this margin, in metres, on top of the
        /// unit's own `maxRadius`. The game does not need the same guard because its
        /// first trace starts from the muzzle transform after the round has already
        /// been placed outside.
        /// </summary>
        private const float SelfClearanceMargin = 5f;

        /// <summary>
        /// WHAT THE ROUNDS WOULD ACTUALLY LAND ON. Owner, 2026-09-17: "i want it to
        /// differentiate ground targets (buildings, ground units and ships) from air
        /// targets."
        ///
        /// THE FOUR UNIT TYPES ARE THE GAME'S OWN and are not invented here -
        /// `Building`, `Ship`, `GroundVehicle` and `Aircraft` are the concrete
        /// subclasses of `Unit`, and `WingspanDatabase` already branches on exactly
        /// the first three. So this reads the engine's own taxonomy rather than
        /// guessing from a collider name or a layer.
        ///
        /// `Terrain` AND `Water` ARE NOT TARGETS, and keeping them in the same enum
        /// rather than off to one side is what lets the renderer ask one question -
        /// <see cref="IsTarget"/> - instead of three.
        /// </summary>
        internal enum TargetKind
        {
            /// <summary>No impact was found at all.</summary>
            None,
            /// <summary>A collider that carries no Unit: terrain, scenery, debris.</summary>
            Terrain,
            /// <summary>The sea.</summary>
            Water,
            Building,
            GroundVehicle,
            Ship,
            /// <summary>An aircraft, in the way of a ground solve. Deliberately distinct.</summary>
            Aircraft,
        }

        /// <summary>
        /// What the solve found. `Hit` false means the rounds reached the ceiling in
        /// <see cref="MaxFlightSeconds"/>, or died of drag, without meeting anything
        /// - which is the honest answer for a level pass over open water, and the
        /// pipper is not drawn.
        /// </summary>
        internal readonly struct Solution
        {
            internal Solution(bool hit, Vector3 point, float range, float timeOfFlight, bool water,
                              TargetKind kind = TargetKind.None, Unit? unit = null)
            {
                Hit = hit; Point = point; Range = range; TimeOfFlight = timeOfFlight; Water = water;
                Kind = kind; Unit = unit;
            }

            internal bool    Hit          { get; }
            internal Vector3 Point        { get; }

            /// <summary>What the impact point sits on. See <see cref="TargetKind"/>.</summary>
            internal TargetKind Kind      { get; }

            /// <summary>The unit that was hit, when one was. Null for terrain and sea.</summary>
            internal Unit?   Unit         { get; }

            /// <summary>
            /// TRUE WHEN THE ROUNDS WOULD HIT SOMETHING THAT CAN BE KILLED, which is
            /// the condition the pipper flashes on. Bare ground and open water are
            /// impacts but they are not hits, and flashing on them would mean
            /// flashing for the whole of every gun run.
            /// </summary>
            internal bool    IsTarget     =>
                Kind == TargetKind.Building || Kind == TargetKind.GroundVehicle ||
                Kind == TargetKind.Ship     || Kind == TargetKind.Aircraft;

            /// <summary>True for the three the owner called "ground targets".</summary>
            internal bool    IsGroundTarget =>
                Kind == TargetKind.Building || Kind == TargetKind.GroundVehicle ||
                Kind == TargetKind.Ship;

            /// <summary>Slant range from the muzzle to the impact, in metres.</summary>
            internal float   Range        { get; }
            internal float   TimeOfFlight { get; }

            /// <summary>True when the impact is the sea rather than a collider.</summary>
            internal bool    Water        { get; }

            internal static Solution None => new Solution(false, Vector3.zero, 0f, 0f, false);
        }

        /// <summary>
        /// Walks the round from the muzzle until it meets the world. `selfRadius` is
        /// the firing aircraft's own `maxRadius`; pass 0 if it is not known and the
        /// solve will simply start casting one chord later than it needs to.
        /// </summary>
        internal static Solution Solve(
            Vector3              muzzleWorld,
            Vector3              gunWorldDir,
            Vector3              aircraftVelocity,
            in Ballistics.Inputs inp,
            float                selfRadius,
            float                maxRange)
        {
            int  mask    = ~(int)PhysicsLayers.ExclusionZonesMask;
            float seaY   = SeaLevelY();
            float clearSq = (selfRadius + SelfClearanceMargin) * (selfRadius + SelfClearanceMargin);

            Vector3 vel = Ballistics.LaunchVelocity(gunWorldDir, aircraftVelocity, in inp);
            Vector3 pos = Vector3.zero;

            float dt      = Ballistics.GameStep();
            int   budget  = Mathf.Min(
                Ballistics.IterationBudget(vel.magnitude, in inp, maxRange, 10, dt),
                Mathf.CeilToInt(
                    Mathf.Max(MinFlightSeconds, maxRange / CeilingSpeed) /
                    Mathf.Max(dt, 1e-5f)));

            // The near end of the chord currently being accumulated.
            Vector3 chordStart     = pos;
            float   chordStartTime = 0f;
            float   t              = 0f;

            for (int s = 0; s < budget; s++)
            {
                Vector3 prev = pos;
                Ballistics.Step(ref vel, ref pos, dt, in inp);
                t += dt;

                // The sea is a plane, not a collider, and the game tests it as one.
                // Crossing it downwards ends the round wherever the crossing is,
                // which is finer than the chord and so is solved on its own.
                float prevY = muzzleWorld.y + prev.y;
                float nowY  = muzzleWorld.y + pos.y;
                if (nowY < seaY && prevY >= seaY)
                {
                    float f = Mathf.Approximately(prevY, nowY)
                        ? 1f
                        : Mathf.Clamp01((prevY - seaY) / (prevY - nowY));
                    Vector3 hitOffset = Vector3.Lerp(prev, pos, f);
                    return new Solution(
                        true, muzzleWorld + hitOffset, hitOffset.magnitude,
                        t - dt * (1f - f), water: true, kind: TargetKind.Water);
                }

                if (vel.sqrMagnitude < inp.CutoffSpeedSq) break;
                if (pos.magnitude > maxRange)             break;

                // Accumulate until the chord is long enough to be worth a cast.
                Vector3 chord = pos - chordStart;
                if (chord.magnitude < ChordMeters) continue;

                bool clearOfSelf = chordStart.sqrMagnitude >= clearSq;
                if (clearOfSelf &&
                    Physics.Linecast(
                        muzzleWorld + chordStart, muzzleWorld + pos, out RaycastHit hit, mask))
                {
                    Vector3 hitOffset = hit.point - muzzleWorld;

                    // Time of flight to the hit, interpolated along the chord. The
                    // chord is short enough that speed is near constant across it.
                    float along = chord.magnitude > 1e-4f
                        ? Mathf.Clamp01(Vector3.Dot(hitOffset - chordStart, chord) / chord.sqrMagnitude)
                        : 1f;

                    bool water = hit.point.y < seaY || IsWater(hit);

                    Unit? struck = UnitOf(hit);
                    TargetKind kind = Classify(struck, water);

                    return new Solution(
                        true, hit.point, hitOffset.magnitude,
                        Mathf.Lerp(chordStartTime, t, along), water, kind, struck);
                }

                chordStart     = pos;
                chordStartTime = t;
            }

            return Solution.None;
        }

        /// <summary>
        /// The unit a struck collider belongs to, or null.
        ///
        /// `GetComponentInParent` RATHER THAN `GetComponent` because the collider is
        /// almost never on the unit's root - a ship's hull, a building's wall and a
        /// vehicle's turret are all children - and a root-only lookup would report
        /// every one of them as bare terrain.
        ///
        /// IT INCLUDES INACTIVE PARENTS. A unit part-way through its death animation
        /// can have the root deactivated while the colliders are still in the world
        /// for a frame, and reporting that as terrain makes the pipper blink off at
        /// the exact moment the player is watching it.
        /// </summary>
        private static Unit? UnitOf(RaycastHit hit)
        {
            try
            {
                Collider c = hit.collider;
                if (c == null) return null;
                return c.GetComponentInParent<Unit>(includeInactive: true);
            }
            catch { return null; }
        }

        /// <summary>
        /// Put a struck unit into the game's own taxonomy. See <see cref="TargetKind"/>.
        ///
        /// THE WATER TEST LOSES TO THE UNIT TEST ON PURPOSE. A ship sits in the sea,
        /// so a hit on a hull can satisfy both; calling that water would hide the one
        /// target the owner most wants marked.
        /// </summary>
        private static TargetKind Classify(Unit? unit, bool water)
        {
            if (unit is Building)       return TargetKind.Building;
            if (unit is Ship)           return TargetKind.Ship;
            if (unit is GroundVehicle)  return TargetKind.GroundVehicle;
            if (unit is Aircraft)       return TargetKind.Aircraft;
            return water ? TargetKind.Water : TargetKind.Terrain;
        }

        /// <summary>
        /// Sea level in the local, floating-origin frame. `Datum.LocalSeaY` is the
        /// same property `BulletSim` compares a hit against; if the datum is not up
        /// yet, zero is the right answer because that is where the sea is before the
        /// origin has shifted.
        /// </summary>
        private static float SeaLevelY()
        {
            try   { return Datum.LocalSeaY; }
            catch { return 0f; }
        }

        /// <summary>
        /// The game identifies water by physics material rather than by layer, which
        /// is why this reads `GameAssets` rather than testing `hit.collider.gameObject.layer`.
        /// </summary>
        private static bool IsWater(RaycastHit hit)
        {
            try   { return hit.collider.sharedMaterial == GameAssets.i.WaterMaterial; }
            catch { return false; }
        }
    }
}
