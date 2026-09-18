using System;
using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// Everything the sights draw in one frame.
    ///
    /// IT IS A STRUCT BECAUSE THE ARGUMENT LIST STOPPED BEING READABLE. The single
    /// `SetDrawData` call carried eight positional parameters for one sight; with
    /// four sights it would carry nineteen, and a pair of transposed `Vector2?`
    /// arguments in a list that long is a bug nobody finds by reading. Named
    /// fields cost nothing at runtime - the struct is passed by `in` and never
    /// boxed - and they make a wrong assignment visible at the call site.
    /// </summary>
    internal struct SightDrawData
    {
        /// <summary>Screen position of the gun boresight, for the pipper.</summary>
        internal Vector2 GunCross;

        internal Vector2[]? LeftWall;
        internal Vector2[]? RightWall;

        /// <summary>
        /// How many entries of the wall arrays are live. The arrays are pooled and
        /// grown, never trimmed, so their Length is not the point count.
        /// </summary>
        internal int WallCount;

        /// <summary>False blanks everything - gear down, or behind the camera.</summary>
        internal bool IsVisible;

        internal bool ShowWalls;

        internal Vector2? DotPos;
        internal float    DotRadius;
        internal bool     InSolution;

        internal Vector2? GroundPipper;
        internal float    GroundPipperRadius;

        /// <summary>
        /// What the rounds would land on. Carried through to the renderer because
        /// TWO separate decisions read it: whether to flash - only a killable thing
        /// is worth a flash, bare ground is not - and what to write in the label.
        /// </summary>
        internal GroundSight.TargetKind GroundKind;

        /// <summary>True when GroundKind is something that can be killed.</summary>
        internal bool     GroundIsTarget;

        internal Vector2? LeadDot;
        internal float    LeadDotRadius;
        /// <summary>
        /// True when the lead dot is standing on a MEASURED range - a locked target -
        /// and false when it is standing on the configured assumption. See the draw
        /// code: a measured dot is filled and an assumed one is a ring.
        /// </summary>
        internal bool     LeadDotMeasured;

        /// <summary>
        /// Range labels, already formatted. Null means no label. The renderer does
        /// not compute them because the range it would need is in world metres and
        /// the renderer works in pixels.
        /// </summary>
        internal string? RangeLabel;
        internal string? GroundLabel;
    }

    internal sealed class FunnelRenderer : MonoBehaviour
    {
        private Material? _material;

        private SightDrawData _d;
        private bool          _hasData;

        /// <summary>
        /// The frame `SetDrawData` last ran. See <see cref="MaxDataAgeFrames"/>.
        /// </summary>
        private int _dataFrame = int.MinValue;

        /// <summary>
        /// HOW STALE THE DRAW DATA MAY BE BEFORE IT IS DROPPED, IN FRAMES. This is
        /// the fix for the stuck tracer line.
        ///
        /// Owner, having flown 1.2.0: "Tracer line is stuck and looks broken", with
        /// a screenshot of one long straight line sweeping off the bottom right of
        /// the HUD at an angle nothing in the sight is drawn at.
        ///
        /// `UpdateFunnel` has several early returns that fire before it ever calls
        /// `SetDrawData` - no FlightHud, no camera, an aircraft that has gone
        /// inactive, a null weapon station. On any of those frames the renderer kept
        /// the LAST frame's data and drew it again, forever, because `_hasData` only
        /// ever went true.
        ///
        /// WHY IT WAS INVISIBLE UNTIL NOW. A stale FUNNEL is two short curves near
        /// the middle of the screen; it reads as a sight that has stopped updating,
        /// which is what the F9 reset key exists for. A stale TRACER LINE is a
        /// straight line hundreds of pixels long, frozen at whatever attitude the
        /// aircraft had when the data stopped arriving - so the same old bug
        /// suddenly had something loud to draw with.
        ///
        /// Two frames of tolerance rather than one, because OnGUI can repaint more
        /// than once per LateUpdate and a strict equality test would blank the sight
        /// on the extra repaints.
        /// </summary>
        private const int MaxDataAgeFrames = 2;

        private GUIStyle? _labelStyle;

        // The range circle used to be a fixed 16-gon. At a close lock the circle grows
        // to a third of the screen and sixteen flat sides are plainly visible, while at
        // 1200 m the same sixteen are drawn across a ring a few pixels wide and most of
        // them are wasted. Segment count now follows the radius, roughly one segment per
        // three pixels of circumference, between those two bounds.
        private const int MinCircleSegments = 12;
        private const int MaxCircleSegments = 72;

        // Screen.height is a property fetch that crosses into the engine, and Fy() runs
        // once per vertex - several thousand times a frame at a 50-point funnel with a
        // filled range circle. Cached for the duration of one repaint.
        private static float _screenHeight = 1080f;

        // A miter longer than this multiple of the half thickness is cut back to a bevel.
        // Without the clamp a near-reversal in the wall (which happens at the near end of
        // the funnel in a hard pull) throws a spike several hundred pixels long.
        private const float MaxMiterRatio = 4f;

        /// <summary>
        /// Gap between the ground reticle's own geometry and the near edge of its
        /// label, in pixels - independent of the reticle's angular size so it reads
        /// as a margin rather than shrinking to nothing on a small pipper.
        /// </summary>
        private const float GroundLabelMargin = 14f;

        /// <summary>Matches the Rect width DrawLabel uses.</summary>
        private const float GroundLabelWidth = 96f;

        /// <summary>
        /// How far each ground-sight shape's geometry reaches to the left of centre,
        /// in multiples of its own radius. Everything is radius-scoped to that
        /// radius except HeloCrosshair, whose ladder ticks reach x=300 in its
        /// design frame against a radius defined at x=160 (see DrawHeloCrosshair) -
        /// 1.25x, not 1x.
        /// </summary>
        private static float GroundLabelReach(GroundSightShape shape) => shape switch
        {
            GroundSightShape.HeloCrosshair => 1.25f,
            _                               => 1.0f,
        };

        /// <summary>
        /// Owner, 2026-09-17: put the range/kind reading to the LEFT of the ground
        /// pipper, in its left corner, clear of the reticle by a real margin, and
        /// sized per shape rather than by one offset for all five. The label's rect
        /// grows rightward from its own origin, so the origin has to sit a full
        /// label-width plus the margin out, or the text would overlap the ring.
        /// The upward bias keeps it reading as a corner rather than a strict
        /// mid-height label, and stays clear of SegmentedRings' diagonal corner
        /// ticks, which sit near 45/135/225/315 degrees, not at pure left.
        /// </summary>
        private static Vector2 GroundLabelOffset(GroundSightShape shape, float radius)
        {
            float reach = Mathf.Max(radius, 1f) * GroundLabelReach(shape);
            return new Vector2(-(reach + GroundLabelMargin + GroundLabelWidth), -reach * 0.55f);
        }

        /// <summary>
        /// Where the range readout sits relative to the gun cross. Far enough right
        /// and down to clear the pipper arms and the near end of both funnel walls,
        /// close enough that it is inside the same glance.
        /// </summary>
        private static readonly Vector2 RangeLabelOffset = new Vector2(26f, 18f);

        // ── Lifecycle ──────────────────────────────────────────────────────────

        private void Awake()
        {
            try
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                if (shader == null)
                {
                    FunnelGunSightPlugin.Instance?.Logger.LogError(
                        "[FunnelGunSight] Shader 'Hidden/Internal-Colored' not found.");
                    return;
                }
                _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend",
                    (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_Cull",   (int)UnityEngine.Rendering.CullMode.Off);
                _material.SetInt("_ZWrite", 0);
            }
            catch (Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] Renderer init failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        // ── Data ───────────────────────────────────────────────────────────────

        public void SetDrawData(in SightDrawData data)
        {
            _d = data;
            _d.WallCount = Mathf.Clamp(_d.WallCount, 0, Mathf.Min(
                _d.LeftWall?.Length ?? 0, _d.RightWall?.Length ?? 0));
            _hasData   = true;
            _dataFrame = Time.frameCount;
        }

        // ── Rendering ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_hasData || !_d.IsVisible)              return;
            if (_material == null)                       return;

            // Nothing fed us this frame, so what we are holding describes an
            // aircraft attitude that no longer exists. See MaxDataAgeFrames.
            if (Time.frameCount - _dataFrame > MaxDataAgeFrames) return;

            FunnelConfig? cfg = FunnelGunSightPlugin.Instance?.FunnelConfig;
            float opacity = cfg?.FunnelOpacity.Value ?? 1f;

            // HudTheme returns either the live HUD theme colour or the configured
            // override, depending on FollowHudTheme.
            Color baseCol = (cfg?.FlashOnFiringSolution.Value ?? false) && _d.InSolution
                ? HudTheme.FiringSolution(cfg)
                : HudTheme.Funnel(cfg);

            // Scale by the theme's own alpha rather than replacing it, so opacity
            // stays a relative dimmer in both colour modes.
            var color = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * opacity);

            // GL.Color() skips Unity's automatic gamma correction, so convert to
            // linear manually to match native HUD colors in linear color space.
            Color glColor = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? color.linear
                : color;

            float lineThickness    = cfg?.FunnelLineThickness.Value   ?? 2f;
            float dotLineThickness = cfg?.RangeDotLineThickness.Value ?? 2f;

            _screenHeight = Screen.height;

            try
            {
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                _material.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                GL.Color(glColor);

                if (cfg?.ShowPipper.Value ?? true)
                    DrawThickCross(_d.GunCross, cfg?.PipperSize.Value ?? 8f, lineThickness);

                if (_d.ShowWalls)
                {
                    if (_d.LeftWall  != null) DrawThickPolyline(_d.LeftWall,  _d.WallCount, lineThickness);
                    if (_d.RightWall != null) DrawThickPolyline(_d.RightWall, _d.WallCount, lineThickness);
                }

                if (_d.DotPos.HasValue && _d.DotRadius > 1f)
                {
                    if (cfg?.RangeDotFilled.Value ?? false)
                        DrawFilledCircle(_d.DotPos.Value, _d.DotRadius);
                    DrawThickCircleRing(_d.DotPos.Value, _d.DotRadius, dotLineThickness);
                }

                // A RING WITH A DOT IN IT, owner's call after flying the cross
                // version: "make CCIP ground sight a ring with a dot inside it
                // instead, it seems accurate." The cross arms ran outside the ring
                // and read as a second reticle; a centre dot marks the same point
                // and stays inside its own circle. It is still distinct from the
                // range circle, which is an empty ring by default.
                if (_d.GroundPipper.HasValue)
                {
                    // THE PIPPER FLASHES WHEN THE ROUNDS WOULD ACTUALLY HIT
                    // SOMETHING. Owner, 2026-09-17: "i want the Ground attack sight
                    // ccip to flash when ground target is a hit."
                    //
                    // IT IS A COLOUR SWAP AND NOT A BLINK, deliberately. Blanking
                    // the reticle for half of every cycle takes the aiming mark
                    // away at the one moment the player is using it; alternating
                    // between the funnel colour and the theme's attention colour
                    // says the same thing and never hides the mark.
                    //
                    // UNSCALED TIME, so the flash keeps its rate through the
                    // slow-motion the game applies on a kill.
                    bool flashOn =
                        (cfg?.FlashGroundSightOnTarget.Value ?? true) &&
                        _d.GroundIsTarget &&
                        Mathf.Repeat(
                            Time.unscaledTime * Mathf.Clamp(
                                cfg?.GroundSightFlashHz.Value ?? 4f,
                                FunnelConfig.GroundSightFlashHzMin, FunnelConfig.GroundSightFlashHzMax),
                            1f) < 0.5f;

                    if (flashOn)
                    {
                        Color f = HudTheme.FiringSolution(cfg);
                        Color fc = new Color(f.r, f.g, f.b, f.a * opacity);
                        GL.Color(QualitySettings.activeColorSpace == ColorSpace.Linear
                            ? fc.linear : fc);
                    }

                    DrawGroundReticle(
                        cfg?.GroundSightShapeSetting.Value ?? GroundSightShape.RingDot,
                        _d.GroundPipper.Value, _d.GroundPipperRadius, dotLineThickness);

                    // Put the colour back for everything drawn after this.
                    if (flashOn) GL.Color(glColor);
                }

                // A FILLED DOT IS A CLAIM ABOUT RANGE, AND WITHOUT A LOCK WE HAVE
                // NOT GOT ONE.
                //
                // Owner, 2026-09-17: "in LCOS test a filled range like dot appears
                // even with no lock." It did, and it looked exactly like the range
                // dot, which IS a measurement. Drawing an assumption in the same ink
                // as a measurement is the actual defect - the lead dot itself is
                // correct and is what LCOS is, standing at LeadDotRange when nothing
                // is locked.
                //
                // So the ink changes rather than the behaviour: filled when the range
                // under it was measured off a locked target, an empty ring when it is
                // the configured guess. Same place, same size, and the pilot can tell
                // at a glance which one he is looking at.
                if (_d.LeadDot.HasValue)
                {
                    if (_d.LeadDotMeasured)
                        DrawFilledCircle(_d.LeadDot.Value, _d.LeadDotRadius);
                    else
                        DrawThickCircleRing(_d.LeadDot.Value, _d.LeadDotRadius, dotLineThickness);
                }

                GL.End();
                GL.PopMatrix();
            }
            catch (Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] GL draw error: {ex.Message}");
            }

            // Text cannot go inside GL.Begin/GL.End - IMGUI issues its own draw
            // calls and would be cut off mid-batch - so the labels come after the
            // matrix is popped.
            // THE RANGE READOUT IS PINNED TO THE CROSS, NOT TO THE RANGE DOT.
            // Owner: "range readout is appearing inside the range dot, but it in
            // fixed position near the centered cross instead." The dot slides along
            // the spine as the target closes, so the number slid with it and had to
            // be hunted for. The ground pipper's own label stays ON the pipper,
            // because that mark IS where the player is looking during a gun run.
            DrawLabel(_d.RangeLabel, _d.GunCross, color, RangeLabelOffset);

            GroundSightShape groundShape = cfg?.GroundSightShapeSetting.Value ?? GroundSightShape.SegmentedRings;
            DrawLabel(_d.GroundLabel, _d.GroundPipper, color,
                GroundLabelOffset(groundShape, _d.GroundPipperRadius));
        }

        private void DrawLabel(string? text, Vector2? anchor, Color color, Vector2 offset)
        {
            if (string.IsNullOrEmpty(text) || !anchor.HasValue) return;

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 14,
                fontStyle = FontStyle.Bold,
                richText  = false
            };

            _labelStyle.normal.textColor = color;

            // GUI space shares the top-left origin GL.LoadPixelMatrix was given, so
            // the same Fy() flip applies here.
            Vector2 p = new Vector2(anchor.Value.x, Fy(anchor.Value.y)) + offset;
            GUI.Label(new Rect(p.x, p.y, 96f, 20f), text, _labelStyle);
        }

        // ── Primitives ─────────────────────────────────────────────────────────
        //
        // WorldToScreenPoint has (0,0) at bottom-left; GL.LoadPixelMatrix has (0,0)
        // at top-left. Fy() converts. GL.LINES has no width control on most render
        // backends, so thickness is built by hand as quads (two triangles each).

        private static float Fy(float y) => _screenHeight - y;

        private static Vector2 ToGL(Vector2 p) => new Vector2(p.x, Fy(p.y));

        private static void AddQuad(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);

            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(c.x, c.y, 0f);
            GL.Vertex3(d.x, d.y, 0f);
        }

        private static void DrawThickLineGL(Vector2 aGL, Vector2 bGL, float thickness)
        {
            Vector2 dir = bGL - aGL;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Vector2 perp = new Vector2(-dir.y, dir.x) * (thickness * 0.5f);
            AddQuad(aGL + perp, bGL + perp, bGL - perp, aGL - perp);
        }

        private static void DrawThickCross(Vector2 c, float arm, float thickness)
        {
            Vector2 cGL = ToGL(c);
            DrawThickLineGL(
                new Vector2(cGL.x - arm, cGL.y), new Vector2(cGL.x + arm, cGL.y), thickness);
            DrawThickLineGL(
                new Vector2(cGL.x, cGL.y - arm), new Vector2(cGL.x, cGL.y + arm), thickness);
        }

        // A funnel wall is one curve, and drawing it as a row of independent quads
        // leaves a wedge of empty pixels on the outside of every joint. At the default
        // 2 px that reads as a faintly ragged line; at 4 px in a hard pull, where the
        // near end of the wall bends most, it reads as a dashed one. The wall is now a
        // continuous strip: each interior vertex gets a single mitred offset shared by
        // the segment arriving and the segment leaving, so there is no seam to see.
        private static void DrawThickPolyline(Vector2[] pts, int count, float thickness)
        {
            int n = Mathf.Min(count, pts.Length);
            if (n < 2) return;

            float half = thickness * 0.5f;

            Vector2 prevGL = ToGL(pts[0]);
            Vector2 currGL = ToGL(pts[1]);

            Vector2 dirIn = currGL - prevGL;
            if (dirIn.sqrMagnitude < 1e-8f) dirIn = Vector2.right;
            dirIn.Normalize();

            // Start cap: square to the first segment.
            Vector2 offPrev = new Vector2(-dirIn.y, dirIn.x) * half;
            Vector2 aTop = prevGL + offPrev;
            Vector2 aBot = prevGL - offPrev;

            for (int i = 1; i < n; i++)
            {
                currGL = ToGL(pts[i]);

                Vector2 dirOut;
                if (i < n - 1)
                {
                    Vector2 next = ToGL(pts[i + 1]);
                    dirOut = next - currGL;
                    if (dirOut.sqrMagnitude < 1e-8f) dirOut = dirIn;
                    else dirOut.Normalize();
                }
                else
                {
                    dirOut = dirIn; // end cap, square to the last segment
                }

                // Miter direction is the bisector's normal; its length is set so the
                // offset edge stays `half` away from BOTH segments.
                Vector2 nIn   = new Vector2(-dirIn.y,  dirIn.x);
                Vector2 nOut  = new Vector2(-dirOut.y, dirOut.x);
                Vector2 miter = nIn + nOut;

                float miterLen = half;
                if (miter.sqrMagnitude >= 1e-8f)
                {
                    miter.Normalize();
                    float cos = Vector2.Dot(miter, nIn);

                    // cos falls to zero as the joint approaches a right angle and goes
                    // negative past it. Dividing by it there would invert the offset and
                    // fold the strip through itself, so anything that sharp falls back
                    // to a plain square joint.
                    if (cos > 0.25f)
                        miterLen = Mathf.Min(half / cos, half * MaxMiterRatio);
                    else
                        miter = nIn;
                }
                else
                {
                    miter = nIn; // exact reversal, nothing sensible to miter into
                }

                Vector2 off  = miter * miterLen;
                Vector2 bTop = currGL + off;
                Vector2 bBot = currGL - off;

                AddQuad(aTop, bTop, bBot, aBot);

                aTop  = bTop;
                aBot  = bBot;
                dirIn = dirOut;
            }
        }

        // One segment per ~3 px of circumference, clamped, so the ring is smooth when
        // it is large and cheap when it is small.
        private static int SegmentsFor(float radius)
        {
            int segments = Mathf.RoundToInt(Mathf.PI * 2f * radius / 3f);
            return Mathf.Clamp(segments, MinCircleSegments, MaxCircleSegments);
        }

        private static void DrawFilledCircle(Vector2 c, float radius)
        {
            if (radius <= 0.5f) return;

            Vector2 cGL      = ToGL(c);
            int     segments = SegmentsFor(radius);
            float   step     = Mathf.PI * 2f / segments;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                Vector2 edgeA = cGL + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                Vector2 edgeB = cGL + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;

                GL.Vertex3(cGL.x, cGL.y, 0f);
                GL.Vertex3(edgeA.x, edgeA.y, 0f);
                GL.Vertex3(edgeB.x, edgeB.y, 0f);
            }
        }

        private static void DrawThickCircleRing(Vector2 c, float radius, float thickness)
        {
            if (radius <= 0.5f) return;

            Vector2 cGL      = ToGL(c);
            int     segments = SegmentsFor(radius);
            float   step     = Mathf.PI * 2f / segments;
            float   rInner   = Mathf.Max(radius - thickness * 0.5f, 0f);
            float   rOuter   = radius + thickness * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                Vector2 dirA = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dirB = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AddQuad(
                    cGL + dirA * rOuter, cGL + dirB * rOuter,
                    cGL + dirB * rInner, cGL + dirA * rInner);
            }
        }

        /// <summary>
        /// An arc of a ring, from <paramref name="fromDeg"/> to
        /// <paramref name="toDeg"/> measured anticlockwise from screen-east.
        ///
        /// IT EXISTS FOR ONE SHAPE. SegmentedRings is two rings broken into four
        /// arcs each, and drawing those as full rings with something painted over
        /// the gaps is not available to us - the overlay draws additively into the
        /// HUD and has no background to paint with.
        /// </summary>
        private static void DrawThickArc(
            Vector2 c, float radius, float fromDeg, float toDeg, float thickness)
        {
            if (radius <= 0.5f) return;

            Vector2 cGL   = ToGL(c);
            float   a0    = fromDeg * Mathf.Deg2Rad;
            float   a1    = toDeg   * Mathf.Deg2Rad;
            float   sweep = a1 - a0;

            // Segment the arc at the same angular density a full ring would use, so
            // an arc and a ring of equal radius are equally smooth.
            int segments = Mathf.Max(2,
                Mathf.CeilToInt(SegmentsFor(radius) * Mathf.Abs(sweep) / (Mathf.PI * 2f)));
            float step   = sweep / segments;
            float rInner = Mathf.Max(radius - thickness * 0.5f, 0f);
            float rOuter = radius + thickness * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float b0 = a0 + i * step, b1 = a0 + (i + 1) * step;
                Vector2 dirA = new Vector2(Mathf.Cos(b0), Mathf.Sin(b0));
                Vector2 dirB = new Vector2(Mathf.Cos(b1), Mathf.Sin(b1));

                AddQuad(
                    cGL + dirA * rOuter, cGL + dirB * rOuter,
                    cGL + dirB * rInner, cGL + dirA * rInner);
            }
        }

        // -- The selectable ground reticles ------------------------------------
        //
        //  Owner, 2026-09-17: "i want to make different selectable shapes or themes
        //  for the Ground attack sight", with four SVGs attached.
        //
        //  EVERY SHAPE IS DRAWN IN ITS OWN SVG's 1000x1000 FRAME and mapped here by
        //  a single scale k, so the numbers below are the numbers in the files and
        //  can be checked against them. P(x, y) converts a point in that frame -
        //  where (500, 500) is the impact spot and +y is DOWN, as SVG has it - into
        //  a screen point around the pipper centre.
        //
        //  WHICH FEATURE IS THE RADIUS DIFFERS PER SHAPE, and that is deliberate:
        //  see the note on GroundSightShape. Each branch says which one it picked.

        private static void DrawGroundReticle(
            GroundSightShape shape, Vector2 centre, float radius, float thickness)
        {
            switch (shape)
            {
                case GroundSightShape.HeloCrosshair:
                    DrawHeloCrosshair(centre, radius, thickness);
                    break;
                case GroundSightShape.DiamondReticle:
                    DrawDiamondReticle(centre, radius, thickness);
                    break;
                case GroundSightShape.FunnelPipper:
                    DrawFunnelPipper(centre, radius, thickness);
                    break;
                case GroundSightShape.SegmentedRings:
                    DrawSegmentedRings(centre, radius, thickness);
                    break;

                default:
                    // The flown default. A ring with a dot in it.
                    DrawThickCircleRing(centre, radius, thickness);
                    DrawFilledCircle(centre, Mathf.Max(radius * 0.22f, thickness));
                    break;
            }
        }

        /// <summary>
        /// design1_helo_crosshair. The rails at x = 340 and 660 are 160 either side
        /// of centre, and THAT half-width is taken as the pipper radius.
        /// </summary>
        private static void DrawHeloCrosshair(Vector2 c, float radius, float th)
        {
            float k = radius / 160f;
            Vector2 P(float x, float y) =>
                new Vector2(c.x + (x - 500f) * k, c.y + (y - 500f) * k);

            // The two vertical rails, y 280..720.
            DrawThickLineGL(ToGL(P(340f, 280f)), ToGL(P(340f, 720f)), th);
            DrawThickLineGL(ToGL(P(660f, 280f)), ToGL(P(660f, 720f)), th);

            // The ladder. Ticks are 36 apart from y = 284, and every third one is
            // LONG - 300 and 700 in the file - while the rest stop at 318 and 682.
            for (int i = 0; i < 13; i++)
            {
                float y   = 284f + i * 36f;
                bool  lng = i % 3 == 0;
                float xL  = lng ? 300f : 318f;
                float xR  = lng ? 700f : 682f;
                DrawThickLineGL(ToGL(P(xL, y)),   ToGL(P(340f, y)), th * 0.7f);
                DrawThickLineGL(ToGL(P(660f, y)), ToGL(P(xR, y)),   th * 0.7f);
            }

            // The broken horizontal bar through the middle, and the centre dot.
            DrawThickLineGL(ToGL(P(340f, 500f)), ToGL(P(482f, 500f)), th);
            DrawThickLineGL(ToGL(P(518f, 500f)), ToGL(P(660f, 500f)), th);
            DrawFilledCircle(c, Mathf.Max(7f * k, th * 0.75f));
        }

        /// <summary>
        /// design2_diamond_reticle. The four chevron apexes sit at radius 280 and
        /// THAT is taken as the pipper radius. Each arm opens away from the centre
        /// by 77.78 on both axes, which is the file's 280 less 202.22.
        /// </summary>
        private static void DrawDiamondReticle(Vector2 c, float radius, float th)
        {
            float k = radius / 280f;
            Vector2 P(Vector2 d) => new Vector2(c.x + d.x * k, c.y + d.y * k);

            // N, E, S, W. The apex points AT the impact spot in every case.
            var apex = new[]
            {
                new Vector2(   0f, -280f), new Vector2( 280f,    0f),
                new Vector2(   0f,  280f), new Vector2(-280f,    0f),
            };

            foreach (Vector2 a in apex)
            {
                Vector2 inward = a.normalized;
                Vector2 perp   = new Vector2(-inward.y, inward.x);
                Vector2 back   = a - inward * 77.78f;

                DrawThickLineGL(ToGL(P(a)), ToGL(P(back + perp * 77.78f)), th);
                DrawThickLineGL(ToGL(P(a)), ToGL(P(back - perp * 77.78f)), th);

                // The cap dot on each apex, r = 5 in the file.
                DrawFilledCircle(P(a), Mathf.Max(5f * k, th * 0.5f));
            }

            DrawFilledCircle(c, Mathf.Max(8f * k, th * 0.75f));
        }

        /// <summary>
        /// design3_funnel_pipper. The broken bar reaches 92 from centre and THAT is
        /// the pipper radius, which puts the file's own r = 20 ring at about a fifth
        /// of it and the heading capsule roughly three and a half radii above. It is
        /// the only one of the four that is taller than it is wide.
        /// </summary>
        private static void DrawFunnelPipper(Vector2 c, float radius, float th)
        {
            float k = radius / 92f;
            Vector2 P(float x, float y) =>
                new Vector2(c.x + (x - 500f) * k, c.y + (y - 500f) * k);

            // The broken horizontal bar and the two short vertical nicks.
            DrawThickLineGL(ToGL(P(408f, 500f)), ToGL(P(466f, 500f)), th);
            DrawThickLineGL(ToGL(P(534f, 500f)), ToGL(P(592f, 500f)), th);
            DrawThickLineGL(ToGL(P(500f, 484f)), ToGL(P(500f, 496f)), th * 0.85f);
            DrawThickLineGL(ToGL(P(500f, 504f)), ToGL(P(500f, 516f)), th * 0.85f);

            DrawFilledCircle(c, Mathf.Max(9f * k, th * 0.75f));
            DrawThickCircleRing(c, 20f * k, th * 0.6f);

            // The stalk, climbing from the ring to y = 270, with four rungs. The
            // rungs alternate 48 wide and 28 wide, as the file has them.
            DrawThickLineGL(ToGL(P(500f, 480f)), ToGL(P(500f, 270f)), th * 0.85f);
            float[] rungY = { 438f, 396f, 354f, 312f };
            for (int i = 0; i < rungY.Length; i++)
            {
                float half = (i % 2 == 0) ? 24f : 14f;
                DrawThickLineGL(
                    ToGL(P(500f - half, rungY[i])), ToGL(P(500f + half, rungY[i])), th * 0.7f);
            }

            // The heading capsule at the top: an open ring of r = 95 about
            // (500, 175) with a stalked arrowhead standing in its gap. The SVG
            // writes it as a large-sweep arc between two points 83.3 apart, which is
            // a gap of about 50 degrees centred on straight up.
            Vector2 capC = P(500f, 175f);
            DrawThickArc(capC, 95f * k, -65f, 245f, th * 0.9f);

            DrawThickLineGL(ToGL(P(500f, 80f)), ToGL(P(500f, 50f)), th * 0.85f);
            DrawThickLineGL(ToGL(P(500f, 50f)), ToGL(P(526f, 64f)), th * 0.85f);
            DrawThickLineGL(ToGL(P(500f, 50f)), ToGL(P(474f, 64f)), th * 0.85f);
        }

        /// <summary>
        /// design4_segmented_rings. The outer broken ring is radius 280 and THAT is
        /// the pipper radius; the inner one is 165, and the two are offset by 45
        /// degrees so their gaps never line up.
        /// </summary>
        private static void DrawSegmentedRings(Vector2 c, float radius, float th)
        {
            float k     = radius / 280f;
            float inner = 165f * k;

            // Four outer arcs, each spanning 76 degrees about a compass point, so
            // the four gaps fall on the diagonals.
            for (int q = 0; q < 4; q++)
                DrawThickArc(c, radius, 90f * q - 38f, 90f * q + 38f, th);

            // Four inner arcs on the diagonals - the offset that staggers the gaps.
            for (int q = 0; q < 4; q++)
                DrawThickArc(c, inner, 45f + 90f * q - 38f, 45f + 90f * q + 38f, th);

            // The diagonal corner ticks, running outward across the outer gaps from
            // 274 to 310 in the file's frame.
            for (int q = 0; q < 4; q++)
            {
                float a = (45f + 90f * q) * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawThickLineGL(
                    ToGL(c + dir * (274f * k)), ToGL(c + dir * (310f * k)), th * 0.8f);
            }

            // The gapped centre cross, 80 out to 22 in on each arm, and the dot.
            float ao = 80f * k, ai = 22f * k;
            DrawThickLineGL(ToGL(new Vector2(c.x - ao, c.y)), ToGL(new Vector2(c.x - ai, c.y)), th * 0.7f);
            DrawThickLineGL(ToGL(new Vector2(c.x + ai, c.y)), ToGL(new Vector2(c.x + ao, c.y)), th * 0.7f);
            DrawThickLineGL(ToGL(new Vector2(c.x, c.y - ao)), ToGL(new Vector2(c.x, c.y - ai)), th * 0.7f);
            DrawThickLineGL(ToGL(new Vector2(c.x, c.y + ai)), ToGL(new Vector2(c.x, c.y + ao)), th * 0.7f);
            DrawFilledCircle(c, Mathf.Max(7f * k, th * 0.75f));

            // The two index marks above the reticle, at 210 and 250 up.
            DrawThickLineGL(
                ToGL(new Vector2(c.x - 16f * k, c.y - 210f * k)),
                ToGL(new Vector2(c.x + 16f * k, c.y - 210f * k)), th * 0.6f);
            DrawThickLineGL(
                ToGL(new Vector2(c.x - 24f * k, c.y - 250f * k)),
                ToGL(new Vector2(c.x + 24f * k, c.y - 250f * k)), th * 0.6f);
        }
    }
}
