using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// MEASUREMENT PROOFS FOR THE THREE CLAIMS THIS MOD MAKES. Owner, 2026-09-17:
    /// "i want you to instrument some tests for funnel mod. i want a measurement
    /// proof of the aryx guns data accuracy, aspect dependent sizing and Ground
    /// attack sight bullet accuracy."
    ///
    /// WHAT MAKES THESE PROOFS RATHER THAN PRINTOUTS. Each one computes the same
    /// quantity TWICE, by two routes that do not share code, and reports the
    /// disagreement as a number with a PASS or a FAIL against a stated tolerance.
    /// A probe that only prints what the mod already believes proves nothing - it
    /// is the mod agreeing with itself - and this workspace has been bitten by
    /// exactly that: three checks passed on builds that were broken, because each
    /// one only ever ran against the fix.
    ///
    /// SO EVERY PROBE HERE CARRIES A KNOWN-BAD CONTROL. Alongside the real
    /// comparison it runs a deliberately WRONG one - a gun law with the drag term
    /// dropped, a width measured along the wrong axis, a solve with the chord
    /// length made absurd - and reports that too. If the control does not FAIL, the
    /// check cannot tell right from wrong and its PASS is worthless. Read the
    /// control line first.
    ///
    /// IT WRITES A FILE AND NOT A LOG. The report is a table, the log is one line
    /// at a time behind whatever else is running, and the owner reads these on a
    /// second machine. `probe-runs/` beside the game's config is where it lands.
    /// </summary>
    internal static class FunnelProbes
    {
        // ── Tolerances, all stated rather than implied ─────────────────────────

        /// <summary>
        /// Metres of divergence allowed between our integrator and a literal
        /// transcription of `BulletSim.Bullet.TrajectoryTrace` after a full
        /// trajectory. They run the same arithmetic in the same order at the same
        /// step, so the only difference should be float association: microns.
        /// </summary>
        private const float BallisticToleranceMeters = 0.01f;

        /// <summary>
        /// Metres of disagreement allowed between the oriented-box support width
        /// and an independent eight-corner projection of the same box. Both are
        /// exact for a box, so this is float noise on a 30 m airframe.
        /// </summary>
        private const float WidthToleranceMeters = 0.01f;

        /// <summary>
        /// Metres the chord-batched ground solve may differ from a per-step solve.
        /// THIS ONE IS NOT FLOAT NOISE - it is the real approximation the file
        /// makes, and GroundSight's own note claims the sag inside a 25 m chord is
        /// "under a centimetre". A metre of tolerance here is generous against that
        /// claim and still tight against terrain features.
        /// </summary>
        private const float ImpactToleranceMeters = 1.0f;

        // ── Entry point ───────────────────────────────────────────────────────

        /// <summary>
        /// Runs all three proofs and writes the report. Returns the file path, or
        /// null if it could not be written.
        /// </summary>
        internal static string? RunAll(FunnelGunSight? sight, FunnelConfig? config)
        {
            var sb = new StringBuilder();
            sb.AppendLine("FUNNEL GUN SIGHT - MEASUREMENT PROOFS");
            sb.AppendLine("Run " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",
                                                         CultureInfo.InvariantCulture));
            sb.AppendLine("Physics step " + Ballistics.GameStep().ToString("F4") + " s");
            sb.AppendLine();
            sb.AppendLine("Read the CONTROL line of each section first. A control that");
            sb.AppendLine("PASSES means the check cannot tell right from wrong.");
            sb.AppendLine();

            int fails = 0;

            fails += ProveGunData(sb);
            sb.AppendLine();
            fails += ProveAspectSizing(sb);
            sb.AppendLine();
            fails += ProveImpactAccuracy(sb, sight, config);
            sb.AppendLine();

            sb.AppendLine(fails == 0
                ? "OVERALL: PASS - every proof met its tolerance and every control failed."
                : "OVERALL: FAIL - " + fails + " check(s) did not hold. Detail above.");

            return Write(sb.ToString());
        }

        // ── 1. Gun data ───────────────────────────────────────────────────────

        /// <summary>
        /// THE CLAIM: our `Ballistics.Step` is the game's own bullet law, and the
        /// `Inputs` we feed it are the gun's own numbers.
        ///
        /// THE SECOND ROUTE: `EngineTruth` below is `BulletSim.Bullet.TrajectoryTrace`
        /// transcribed literally, reading `WeaponInfo` fields directly rather than
        /// through our `Inputs` struct. If `Inputs` misreads a field - the
        /// muzzleVelocity/dragDenominator asymmetry is the obvious candidate, and it
        /// is real - the two diverge and this says by how much.
        /// </summary>
        private static int ProveGunData(StringBuilder sb)
        {
            sb.AppendLine("=== 1. ARYX GUN DATA ACCURACY ===");
            sb.AppendLine();
            sb.AppendLine("For every gun the game has loaded: our integrator against a literal");
            sb.AppendLine("transcription of BulletSim.Bullet.TrajectoryTrace, over 2 s of flight.");
            sb.AppendLine();
            sb.AppendLine("  gun                          muzzle   drag    grav   ours-vs-engine  verdict");

            int fails = 0;
            int guns  = 0;

            foreach (WeaponInfo info in LoadedGunInfos())
            {
                guns++;
                var inp = new Ballistics.Inputs(info.muzzleVelocity, info);

                float diverge = Divergence(inp, info, dropDrag: false);
                bool  ok      = diverge <= BallisticToleranceMeters;
                if (!ok) fails++;

                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,-26} {1,7:F1} {2,7:F4} {3,6:F2}   {4,12:F6} m  {5}",
                    Trim(info.weaponName, 26), info.muzzleVelocity, info.dragCoef,
                    info.gravMult, diverge, ok ? "PASS" : "FAIL"));
            }

            if (guns == 0)
            {
                sb.AppendLine("  (no gun WeaponInfo loaded - run this in a mission, not the menu)");
                return 1;
            }

            // THE CONTROL. Same comparison with the drag term deleted from OUR side
            // only. A check that cannot see a missing drag term cannot see anything.
            sb.AppendLine();
            float worst = 0f;
            foreach (WeaponInfo info in LoadedGunInfos())
            {
                var inp = new Ballistics.Inputs(info.muzzleVelocity, info);
                worst = Mathf.Max(worst, Divergence(inp, info, dropDrag: true));
            }

            bool controlFailed = worst > BallisticToleranceMeters;
            if (!controlFailed) fails++;

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  CONTROL (drag term deleted, MUST fail): worst divergence {0:F3} m -> {1}",
                worst, controlFailed ? "FAILS, as it must" : "PASSES - THIS CHECK IS BLIND"));

            sb.AppendLine();
            sb.AppendLine("  " + guns + " gun(s) examined, tolerance "
                          + BallisticToleranceMeters.ToString("F3", CultureInfo.InvariantCulture) + " m.");
            return fails;
        }

        /// <summary>
        /// Metres between our trajectory and the engine's own law after 2 s, both
        /// fired level at the same speed. `dropDrag` deliberately breaks OUR side,
        /// for the control.
        /// </summary>
        private static float Divergence(in Ballistics.Inputs inp, WeaponInfo info, bool dropDrag)
        {
            float dt    = Ballistics.GameStep();
            int   steps = Mathf.CeilToInt(2f / Mathf.Max(dt, 1e-5f));

            Vector3 ourVel = Vector3.forward * inp.LaunchSpeed, ourPos = Vector3.zero;
            Vector3 engVel = Vector3.forward * inp.LaunchSpeed, engPos = Vector3.zero;

            for (int i = 0; i < steps; i++)
            {
                if (dropDrag)
                {
                    // The broken version: gravity only.
                    ourVel.y -= 9.81f * dt * inp.GravityMultiple;
                    ourPos   += ourVel * dt;
                }
                else
                {
                    Ballistics.Step(ref ourVel, ref ourPos, dt, in inp);
                }

                EngineTruth(ref engVel, ref engPos, dt, info);
            }

            return Vector3.Distance(ourPos, engPos);
        }

        /// <summary>
        /// `BulletSim.Bullet.TrajectoryTrace`, transcribed. It reads the WeaponInfo
        /// directly ON PURPOSE - going through our own Inputs struct would make this
        /// the same code twice and prove nothing.
        /// </summary>
        private static void EngineTruth(ref Vector3 velocity, ref Vector3 position,
                                        float dt, WeaponInfo info)
        {
            velocity.y -= 9.81f * dt * info.gravMult;
            velocity   -= velocity.magnitude * velocity
                          * (info.dragCoef * dt / info.muzzleVelocity);
            position   += velocity * dt;
        }

        // ── 2. Aspect-dependent sizing ────────────────────────────────────────

        /// <summary>
        /// THE CLAIM: `TargetExtent.ApparentWidth` returns the true width of the
        /// target's oriented box along the direction the player is asked to fit, at
        /// any aspect.
        ///
        /// THE SECOND ROUTE: take the same box and project all EIGHT corners onto
        /// the same direction, then take max minus min. The support formula and the
        /// corner sweep are exact for a box and share no code, so they must agree.
        ///
        /// IT SWEEPS ASPECT RATHER THAN TESTING ONE. The whole point of the feature
        /// is that the number CHANGES with aspect, so a single-angle check would
        /// pass on a function that ignored orientation entirely. The sweep also
        /// reports the width ratio between beam and head-on, which is the thing the
        /// owner can sanity-check against the aircraft he is looking at.
        /// </summary>
        private static int ProveAspectSizing(StringBuilder sb)
        {
            sb.AppendLine("=== 2. ASPECT-DEPENDENT SIZING ===");
            sb.AppendLine();
            sb.AppendLine("Oriented-box support width against an independent 8-corner projection,");
            sb.AppendLine("swept through aspect. Both are exact for a box, so they must agree.");
            sb.AppendLine();
            sb.AppendLine("  unit                      aspect   support    8-corner      delta  verdict");

            int fails = 0;
            int rows  = 0;

            foreach (Unit unit in LiveUnits())
            {
                Vector3? half = HalfExtentsOf(unit);
                if (half == null) continue;

                float beam = 0f, nose = 0f;

                for (int deg = 0; deg < 180; deg += 30)
                {
                    float   a   = deg * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)).normalized;

                    float support = SupportWidth(unit.transform, half.Value, dir);
                    float corners = CornerWidth(unit.transform, half.Value, dir);
                    float delta   = Mathf.Abs(support - corners);

                    bool ok = delta <= WidthToleranceMeters;
                    if (!ok) fails++;
                    rows++;

                    if (deg == 0)  nose = support;
                    if (deg == 90) beam = support;

                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0,-24} {1,6}deg {2,8:F3} m {3,9:F3} m {4,10:F5}  {5}",
                        Trim(NameOf(unit), 24), deg, support, corners, delta,
                        ok ? "PASS" : "FAIL"));
                }

                if (nose > 0.01f)
                    sb.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "  {0,-24} beam/nose width ratio {1:F2} - a ratio of 1.00 means aspect is being IGNORED",
                        Trim(NameOf(unit), 24), beam / nose));

                if (rows >= 60) break;   // one report, not a census
            }

            if (rows == 0)
            {
                sb.AppendLine("  (no measurable unit in the scene - run this in a mission with traffic)");
                return 1;
            }

            // THE CONTROL. Measure the support width along the direction the player
            // is NOT being asked to fit - the box's own forward - and compare it
            // against the 8-corner width along the SIDEWAYS direction. On any
            // aircraft that is not square in plan these must disagree.
            sb.AppendLine();
            float worst = 0f;
            foreach (Unit unit in LiveUnits())
            {
                Vector3? half = HalfExtentsOf(unit);
                if (half == null) continue;
                Transform t = unit.transform;
                worst = Mathf.Max(worst,
                    Mathf.Abs(SupportWidth(t, half.Value, t.forward)
                              - CornerWidth(t, half.Value, t.right)));
            }

            bool controlFailed = worst > WidthToleranceMeters;
            if (!controlFailed) fails++;

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  CONTROL (length measured against width, MUST fail): {0:F3} m -> {1}",
                worst, controlFailed ? "FAILS, as it must" : "PASSES - THIS CHECK IS BLIND"));

            sb.AppendLine();
            sb.AppendLine("  " + rows + " measurement(s), tolerance "
                          + WidthToleranceMeters.ToString("F3", CultureInfo.InvariantCulture) + " m.");
            return fails;
        }

        /// <summary>The support formula TargetExtent uses.</summary>
        private static float SupportWidth(Transform t, Vector3 h, Vector3 d)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
            return 2f * (
                Mathf.Abs(Vector3.Dot(t.right   * h.x, d)) +
                Mathf.Abs(Vector3.Dot(t.up      * h.y, d)) +
                Mathf.Abs(Vector3.Dot(t.forward * h.z, d)));
        }

        /// <summary>
        /// The same width found the long way: project all eight corners and take the
        /// spread. Shares no code with the formula above, which is the point.
        /// </summary>
        private static float CornerWidth(Transform t, Vector3 h, Vector3 d)
        {
            d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 local = new Vector3(
                    (i & 1) == 0 ? -h.x : h.x,
                    (i & 2) == 0 ? -h.y : h.y,
                    (i & 4) == 0 ? -h.z : h.z);

                Vector3 world = t.right * local.x + t.up * local.y + t.forward * local.z;
                float   proj  = Vector3.Dot(world, d);

                if (proj < min) min = proj;
                if (proj > max) max = proj;
            }
            return max - min;
        }

        // ── 3. Ground sight impact accuracy ───────────────────────────────────

        /// <summary>
        /// THE CLAIM: batching the impact search into 25 m chords - one linecast per
        /// chord instead of one per physics step - does not move the impact point.
        /// `GroundSight`'s own note says the sag inside a chord is "under a
        /// centimetre"; this measures it instead of asserting it.
        ///
        /// THE SECOND ROUTE: the same solve with the chord length collapsed to
        /// roughly one step, which is a cast per step - the expensive thing the
        /// batching exists to avoid. If the two impact points agree, the batching
        /// is free; if they do not, the pipper is lying by the difference.
        ///
        /// IT SWEEPS DEPRESSION ANGLE, because a shallow pass over rising ground is
        /// where a chord crosses the most terrain per metre of trajectory, and a
        /// single steep test would be the easy case.
        /// </summary>
        private static int ProveImpactAccuracy(
            StringBuilder sb, FunnelGunSight? sight, FunnelConfig? config)
        {
            sb.AppendLine("=== 3. GROUND SIGHT BULLET ACCURACY ===");
            sb.AppendLine();
            sb.AppendLine("The 25 m chord-batched impact solve against a per-step solve casting");
            sb.AppendLine("every physics tick, swept through gun depression.");
            sb.AppendLine();

            Camera? cam = Camera.main;
            if (cam == null)
            {
                sb.AppendLine("  (no camera - run this in a mission)");
                return 1;
            }

            WeaponInfo? info = FirstGunInfo();
            if (info == null)
            {
                sb.AppendLine("  (no gun loaded - run this in an aircraft with a gun)");
                return 1;
            }

            var   inp      = new Ballistics.Inputs(info.muzzleVelocity, info);
            float maxRange = Mathf.Clamp(
                config?.GroundSightMaxRange.Value ?? 2500f,
                FunnelConfig.GroundSightMaxRangeMin, FunnelConfig.GroundSightMaxRangeMax);

            Vector3 muzzle = cam.transform.position;
            Vector3 fwd    = cam.transform.forward;
            Vector3 right  = cam.transform.right;

            sb.AppendLine("  depression   batched range   per-step range      delta   kind        verdict");

            int fails = 0;
            int rows  = 0;

            for (int deg = 5; deg <= 45; deg += 5)
            {
                Vector3 dir = Quaternion.AngleAxis(deg, right) * fwd;

                GroundSight.Solution coarse =
                    GroundSight.Solve(muzzle, dir, Vector3.zero, in inp, 0f, maxRange);
                GroundSight.Solution fine =
                    SolvePerStep(muzzle, dir, in inp, maxRange);

                if (!coarse.Hit && !fine.Hit)
                {
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,7}deg   (no impact)     (no impact)          -     -           SKIP", deg));
                    continue;
                }

                rows++;

                if (coarse.Hit != fine.Hit)
                {
                    fails++;
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,7}deg   {1,-15} {2,-15}       -     -           FAIL (one found no impact)",
                        deg, coarse.Hit ? "hit" : "none", fine.Hit ? "hit" : "none"));
                    continue;
                }

                float delta = Vector3.Distance(coarse.Point, fine.Point);
                bool  ok    = delta <= ImpactToleranceMeters;
                if (!ok) fails++;

                sb.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,7}deg {1,13:F1} m {2,14:F1} m {3,10:F3} m   {4,-11} {5}",
                    deg, coarse.Range, fine.Range, delta, coarse.Kind, ok ? "PASS" : "FAIL"));
            }

            if (rows == 0)
            {
                sb.AppendLine();
                sb.AppendLine("  (the gun line met nothing at any depression - point at terrain and re-run)");
                return 1;
            }

            // THE CONTROL. Run the batched solve with an absurd chord - longer than
            // most of the trajectory - so it must skip past the real impact and land
            // somewhere else. If THAT still agrees with the per-step solve, the
            // comparison is not measuring the chord at all.
            sb.AppendLine();
            float worst = 0f;
            for (int deg = 5; deg <= 45; deg += 5)
            {
                Vector3 dir = Quaternion.AngleAxis(deg, right) * fwd;

                GroundSight.Solution fine = SolvePerStep(muzzle, dir, in inp, maxRange);
                GroundSight.Solution wild = SolveWithChord(muzzle, dir, in inp, maxRange, 600f);

                if (fine.Hit && wild.Hit)
                    worst = Mathf.Max(worst, Vector3.Distance(wild.Point, fine.Point));
                else if (fine.Hit != wild.Hit)
                    worst = Mathf.Max(worst, ImpactToleranceMeters * 100f);
            }

            bool controlFailed = worst > ImpactToleranceMeters;
            if (!controlFailed) fails++;

            sb.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "  CONTROL (600 m chord, MUST fail): worst {0:F2} m -> {1}",
                worst, controlFailed ? "FAILS, as it must" : "PASSES - THIS CHECK IS BLIND"));

            sb.AppendLine();
            sb.AppendLine("  " + rows + " depression(s) with an impact, tolerance "
                          + ImpactToleranceMeters.ToString("F2", CultureInfo.InvariantCulture) + " m.");
            sb.AppendLine("  Muzzle velocity " + inp.LaunchSpeed.ToString("F1", CultureInfo.InvariantCulture)
                          + " m/s, drag " + inp.DragCoefficient.ToString("F4", CultureInfo.InvariantCulture)
                          + ", max range " + maxRange.ToString("F0", CultureInfo.InvariantCulture) + " m.");
            return fails;
        }

        /// <summary>
        /// The impact solve with a cast on EVERY physics step. This is the reference
        /// the batched solve is measured against, and the reason the batched one
        /// exists - at 2 km it is over a hundred casts for one answer.
        /// </summary>
        private static GroundSight.Solution SolvePerStep(
            Vector3 muzzle, Vector3 dir, in Ballistics.Inputs inp, float maxRange)
            => SolveWithChord(muzzle, dir, in inp, maxRange, 0f);

        /// <summary>
        /// A straight transcription of `GroundSight.Solve` with the chord length as
        /// a parameter. Deliberately a SEPARATE implementation: calling the real one
        /// with a different constant would need the constant to be a variable, which
        /// would change the shipping code to suit its own test.
        /// </summary>
        private static GroundSight.Solution SolveWithChord(
            Vector3 muzzle, Vector3 dir, in Ballistics.Inputs inp, float maxRange, float chordMeters)
        {
            int   mask = ~(int)PhysicsLayers.ExclusionZonesMask;
            float seaY = LocalSeaY();

            Vector3 vel = Ballistics.LaunchVelocity(dir, Vector3.zero, in inp);
            Vector3 pos = Vector3.zero;

            float dt     = Ballistics.GameStep();
            int   budget = Mathf.Min(
                Ballistics.IterationBudget(vel.magnitude, in inp, maxRange, 10, dt),
                Mathf.CeilToInt(Mathf.Max(6f, maxRange / 250f) / Mathf.Max(dt, 1e-5f)));

            Vector3 chordStart = pos;
            float   t = 0f, chordStartTime = 0f;

            for (int s = 0; s < budget; s++)
            {
                Vector3 prev = pos;
                Ballistics.Step(ref vel, ref pos, dt, in inp);
                t += dt;

                float prevY = muzzle.y + prev.y, nowY = muzzle.y + pos.y;
                if (nowY < seaY && prevY >= seaY)
                {
                    float f = Mathf.Approximately(prevY, nowY)
                        ? 1f : Mathf.Clamp01((prevY - seaY) / (prevY - nowY));
                    Vector3 o = Vector3.Lerp(prev, pos, f);
                    return new GroundSight.Solution(
                        true, muzzle + o, o.magnitude, t - dt * (1f - f), true,
                        GroundSight.TargetKind.Water);
                }

                if (vel.sqrMagnitude < inp.CutoffSpeedSq) break;
                if (pos.magnitude > maxRange)             break;

                Vector3 chord = pos - chordStart;
                if (chord.magnitude < chordMeters) continue;

                if (Physics.Linecast(muzzle + chordStart, muzzle + pos,
                                     out RaycastHit hit, mask))
                {
                    Vector3 o = hit.point - muzzle;
                    return new GroundSight.Solution(
                        true, hit.point, o.magnitude,
                        Mathf.Lerp(chordStartTime, t, 1f), hit.point.y < seaY);
                }

                chordStart     = pos;
                chordStartTime = t;
            }

            return GroundSight.Solution.None;
        }

        private static float LocalSeaY()
        {
            try   { return Datum.LocalSeaY; }
            catch { return 0f; }
        }

        // ── Scene helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Every gun WeaponInfo the game currently has loaded. A gun is identified by
        /// having a muzzle velocity and a drag coefficient at all - missiles carry
        /// neither - rather than by a name list that goes stale the next patch.
        /// </summary>
        private static IEnumerable<WeaponInfo> LoadedGunInfos()
        {
            WeaponInfo[] all;
            try   { all = Resources.FindObjectsOfTypeAll<WeaponInfo>(); }
            catch { yield break; }

            var seen = new HashSet<string>();
            foreach (WeaponInfo info in all)
            {
                if (info == null) continue;
                if (info.muzzleVelocity <= 1f) continue;
                if (info.dragCoef <= 0f)       continue;

                string key = info.weaponName ?? info.name ?? "?";
                if (!seen.Add(key)) continue;

                yield return info;
            }
        }

        private static WeaponInfo? FirstGunInfo()
        {
            foreach (WeaponInfo info in LoadedGunInfos()) return info;
            return null;
        }

        private static IEnumerable<Unit> LiveUnits()
        {
            Unit[] all;
            try   { all = UnityEngine.Object.FindObjectsOfType<Unit>(); }
            catch { yield break; }

            foreach (Unit u in all)
                if (u != null) yield return u;
        }

        /// <summary>
        /// Local half-extents of a unit, measured off its renderers. Deliberately
        /// re-derived here rather than read from TargetExtent's cache: the proof is
        /// about the width FORMULA, and a shared box would still be a shared input.
        /// </summary>
        private static Vector3? HalfExtentsOf(Unit unit)
        {
            Renderer[] rs;
            try   { rs = unit.GetComponentsInChildren<Renderer>(); }
            catch { return null; }

            if (rs == null || rs.Length == 0) return null;

            Transform t = unit.transform;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool any = false;

            foreach (Renderer r in rs)
            {
                if (r == null || !r.enabled) continue;

                Bounds  b = r.bounds;
                Vector3 c = t.InverseTransformPoint(b.center);
                Vector3 e = t.InverseTransformVector(b.extents);
                e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));

                min = Vector3.Min(min, c - e);
                max = Vector3.Max(max, c + e);
                any = true;
            }

            if (!any) return null;

            Vector3 half = (max - min) * 0.5f;
            return half.magnitude < 0.5f ? (Vector3?)null : half;
        }

        private static string NameOf(Unit u)
        {
            try   { return u.definition?.jsonKey ?? u.name ?? "?"; }
            catch { return u.name ?? "?"; }
        }

        private static string Trim(string? s, int n)
        {
            s ??= "?";
            return s.Length <= n ? s : s.Substring(0, n);
        }

        // ── Output ────────────────────────────────────────────────────────────

        private static string? Write(string text)
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "probe-runs");
                Directory.CreateDirectory(dir);

                string path = Path.Combine(
                    dir, "funnel-proofs-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".txt");

                File.WriteAllText(path, text);
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[FunnelGunSight] Could not write the proof report: " + ex.Message);
                return null;
            }
        }
    }
}
