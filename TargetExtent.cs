using System.Collections.Generic;
using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// How wide the target actually LOOKS, rather than how wide its wings are.
    ///
    /// THE PROBLEM THIS SOLVES. The funnel's walls are set one wingspan apart at
    /// every range: fly the target between the walls and the range is right. That
    /// only works while the target is showing you its wingspan. Head-on, or in a
    /// steep bank, it is showing you its LENGTH or its height, and the walls are
    /// then asking the player to fit a dimension the target is not presenting -
    /// which reads as the sight being wrong at exactly the aspect where a gun shot
    /// is easiest. Real EEGS is drawn as an envelope rather than a ruler for this
    /// reason.
    ///
    /// THE MEASURE IS THE ORIENTED BOX'S SUPPORT WIDTH, not its screen bounding
    /// box. For a box with half-extents (hx, hy, hz) on axes (right, up, forward),
    /// the extent along any unit direction d is
    ///
    ///     2 * (|dot(right * hx, d)| + |dot(up * hy, d)| + |dot(forward * hz, d)|)
    ///
    /// which is exact for a box and costs three dot products. Projecting eight
    /// corners through the camera would cost eight matrix multiplies and give the
    /// same number.
    ///
    /// THE BOX IS MEASURED ONCE PER AIRCRAFT TYPE AND CACHED. Walking the renderer
    /// hierarchy is not a per-frame operation, and every FS-12 has the same shape.
    /// The cache is keyed on the unit definition, so a type met once is free
    /// forever after - the same bargain <see cref="WingspanDatabase"/> already
    /// makes, and for the same reason.
    /// </summary>
    internal static class TargetExtent
    {
        /// <summary>
        /// Local-space half-extents per unit definition, in the unit root's own
        /// frame. Null means the type was examined and had nothing measurable, so
        /// it is not re-examined every frame.
        /// </summary>
        private static readonly Dictionary<string, Vector3?> _halfExtents =
            new Dictionary<string, Vector3?>();

        /// <summary>
        /// Below this, in metres, a measured box is treated as a failure rather than
        /// a very small aircraft. A unit whose renderers have not streamed in yet
        /// measures near zero, and a zero span would collapse the funnel walls onto
        /// the spine.
        /// </summary>
        private const float MinCredibleExtent = 0.5f;

        /// <summary>
        /// The target's apparent width along `worldDir`, in metres, or `fallback`
        /// when the target cannot be measured.
        ///
        /// `worldDir` is the world direction that the funnel's screen-space wall
        /// offset corresponds to - see <see cref="ScreenPerpToWorld"/>. Passing the
        /// line-of-sight-perpendicular instead would measure a width the player is
        /// not being asked to fit.
        /// </summary>
        internal static float ApparentWidth(
            Unit? target, Vector3 worldDir, float fallback, float capMeters)
        {
            if (target == null) return fallback;

            Vector3? half = HalfExtents(target);
            if (half == null) return fallback;

            Transform t = target.transform;
            Vector3   h = half.Value;
            Vector3   d = worldDir.sqrMagnitude > 1e-6f ? worldDir.normalized : Vector3.right;

            float extent = 2f * (
                Mathf.Abs(Vector3.Dot(t.right   * h.x, d)) +
                Mathf.Abs(Vector3.Dot(t.up      * h.y, d)) +
                Mathf.Abs(Vector3.Dot(t.forward * h.z, d)));

            if (extent < MinCredibleExtent) return fallback;

            // THE CAP IS NOT A SAFETY NET, IT IS PART OF THE SIGHT. Owner: "make the
            // adaptive width (wingspan) mode recognize buildings and ground units, or
            // cap its max size in meters." Recognising them by type would mean a list
            // of every unit class in a game that keeps adding them, and a wrong entry
            // fails silently; the cap is one number that is right for every large
            // object at once, named and known. A carrier, a hangar and a bridge all
            // stop mattering at the same width, because past a certain size the
            // envelope sight has stopped being a ranging tool anyway - you are not
            // fitting a carrier between two walls, you are pointing at it.
            return Mathf.Min(extent, Mathf.Max(capMeters, MinCredibleExtent));
        }

        /// <summary>
        /// The world direction a screen-space offset points along. The funnel builds
        /// its walls by stepping sideways in PIXELS, so the dimension the player is
        /// asked to fit is whatever that pixel direction corresponds to in the
        /// world - which depends on the camera's roll, not just on where the target
        /// is.
        /// </summary>
        internal static Vector3 ScreenPerpToWorld(Camera camera, Vector2 screenPerp)
        {
            Transform c = camera.transform;
            Vector3   d = c.right * screenPerp.x + c.up * screenPerp.y;
            return d.sqrMagnitude > 1e-6f ? d.normalized : c.right;
        }

        /// <summary>
        /// Half-extents for this unit's type, measuring them the first time the type
        /// is seen. Renderer bounds are used rather than colliders because a
        /// collider hull on these aircraft is coarser than the silhouette the player
        /// is actually looking at.
        /// </summary>
        private static Vector3? HalfExtents(Unit target)
        {
            string key = target.definition != null
                ? target.definition.name
                : target.GetType().Name;

            if (_halfExtents.TryGetValue(key, out Vector3? cached)) return cached;

            Vector3? measured = Measure(target);
            _halfExtents[key] = measured;
            return measured;
        }

        /// <summary>
        /// THE WHOLE MEASUREMENT IS RESTRICTED TO SOLID GEOMETRY, AND THAT IS THE
        /// BUG THAT SHIPPED IN 1.2.0.
        ///
        /// Owner, having flown it: "when i selected a plane it was sized to nearly
        /// the whole screen and flashing to fire while the target was far to the
        /// side."
        ///
        /// `GetComponentsInChildren&lt;Renderer&gt;` returns PARTICLE and TRAIL
        /// renderers too, and those simulate in WORLD space - their `bounds` is the
        /// box around every live particle, which for a contrail or a motor plume is
        /// hundreds of metres long and is nowhere near the aircraft. One of them in
        /// the union and the measured "wingspan" is the length of a smoke trail. A
        /// funnel sized to that fills the screen, and because the firing-solution
        /// test asks whether the target is inside the walls, walls that wide say
        /// yes to a target anywhere on the HUD. Both halves of the report are this
        /// one line.
        ///
        /// Only <see cref="MeshRenderer"/> and <see cref="SkinnedMeshRenderer"/>
        /// describe the silhouette a player is being asked to fit.
        /// </summary>
        private static bool IsSolidGeometry(Renderer r) =>
            r is MeshRenderer || r is SkinnedMeshRenderer;

        private static Vector3? Measure(Unit target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(false);
            if (renderers == null || renderers.Length == 0) return null;

            Transform root    = target.transform;
            bool      anyReal = false;
            var       bounds  = new Bounds(Vector3.zero, Vector3.zero);

            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled) continue;
                if (!IsSolidGeometry(r))     continue;

                // Renderer.bounds is a WORLD axis-aligned box; bringing its centre
                // and extents back into the root's frame by transforming the eight
                // corners is what keeps the cached box valid at any attitude, since
                // the type is measured once at whatever attitude it happened to be
                // in when first seen.
                Bounds  wb = r.bounds;
                Vector3 c  = wb.center;
                Vector3 e  = wb.extents;

                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        c.x + ((i & 1) == 0 ? -e.x : e.x),
                        c.y + ((i & 2) == 0 ? -e.y : e.y),
                        c.z + ((i & 4) == 0 ? -e.z : e.z));

                    Vector3 local = root.InverseTransformPoint(corner);
                    if (!anyReal) { bounds = new Bounds(local, Vector3.zero); anyReal = true; }
                    else          { bounds.Encapsulate(local); }
                }
            }

            if (!anyReal) return null;

            Vector3 half = bounds.extents;
            if (half.x < MinCredibleExtent && half.y < MinCredibleExtent && half.z < MinCredibleExtent)
                return null;

            return half;
        }
    }
}
