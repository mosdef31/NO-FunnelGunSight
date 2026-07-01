using System;
using UnityEngine;

namespace FunnelGunSight
{
    internal sealed class FunnelRenderer : MonoBehaviour
    {
        private Material? _material;

        private Vector2    _gunCrossScreen;
        private Vector2[]? _leftWall;
        private Vector2[]? _rightWall;
        private bool       _isVisible;
        private bool       _hasData;
        private Vector2?   _dotPos;
        private float      _dotRadius;
        private bool       _inSolution;

        private const int CircleSegments = 16;

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

        public void SetDrawData(
            Vector2    gunCrossScreen,
            Vector2[]? leftWall,
            Vector2[]? rightWall,
            bool       isVisible,
            Vector2?   dotPos,
            float      dotRadius,
            bool       inSolution)
        {
            _gunCrossScreen = gunCrossScreen;
            _leftWall       = leftWall;
            _rightWall      = rightWall;
            _isVisible      = isVisible;
            _dotPos         = dotPos;
            _dotRadius      = dotRadius;
            _inSolution     = inSolution;
            _hasData        = true;
        }

        // ── Rendering ──────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!_hasData || !_isVisible)                return;
            if (_material == null)                       return;

            FunnelConfig? cfg = FunnelGunSightPlugin.Instance?.FunnelConfig;
            float opacity = cfg?.FunnelOpacity.Value ?? 1f;

            Color baseCol = (cfg?.FlashOnFiringSolution.Value ?? false) && _inSolution
                ? (cfg?.FiringSolutionColor.Value ?? Color.white)
                : (cfg?.FunnelColor.Value         ?? Color.gray);

            var color = new Color(baseCol.r, baseCol.g, baseCol.b, opacity);

            // GL.Color() skips Unity's automatic gamma correction, so convert to
            // linear manually to match native HUD colors in linear color space.
            Color glColor = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? color.linear
                : color;

            float lineThickness    = cfg?.FunnelLineThickness.Value   ?? 2f;
            float dotLineThickness = cfg?.RangeDotLineThickness.Value ?? 2f;

            try
            {
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                _material.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                GL.Color(glColor);

                if (cfg?.ShowPipper.Value ?? true)
                    DrawThickCross(_gunCrossScreen, cfg?.PipperSize.Value ?? 8f, lineThickness);

                if (_leftWall  != null) DrawThickPolyline(_leftWall,  lineThickness);
                if (_rightWall != null) DrawThickPolyline(_rightWall, lineThickness);

                if (_dotPos.HasValue && _dotRadius > 1f)
                {
                    if (cfg?.RangeDotFilled.Value ?? false)
                        DrawFilledCircle(_dotPos.Value, _dotRadius);
                    DrawThickCircleRing(_dotPos.Value, _dotRadius, dotLineThickness);
                }

                GL.End();
                GL.PopMatrix();
            }
            catch (Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] GL draw error: {ex.Message}");
            }
        }

        // ── Primitives ─────────────────────────────────────────────────────────
        //
        // WorldToScreenPoint has (0,0) at bottom-left; GL.LoadPixelMatrix has (0,0)
        // at top-left. Fy() converts. GL.LINES has no width control on most render
        // backends, so thickness is built by hand as quads (two triangles each).

        private static float Fy(float y) => Screen.height - y;

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

        private static void DrawThickPolyline(Vector2[] pts, float thickness)
        {
            for (int i = 0; i < pts.Length - 1; i++)
                DrawThickLineGL(ToGL(pts[i]), ToGL(pts[i + 1]), thickness);
        }

        private static void DrawFilledCircle(Vector2 c, float radius)
        {
            Vector2 cGL = ToGL(c);
            float   step = Mathf.PI * 2f / CircleSegments;

            for (int i = 0; i < CircleSegments; i++)
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
            Vector2 cGL    = ToGL(c);
            float   step   = Mathf.PI * 2f / CircleSegments;
            float   rInner = Mathf.Max(radius - thickness * 0.5f, 0f);
            float   rOuter = radius + thickness * 0.5f;

            for (int i = 0; i < CircleSegments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                Vector2 dirA = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                Vector2 dirB = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));

                AddQuad(
                    cGL + dirA * rOuter, cGL + dirB * rOuter,
                    cGL + dirB * rInner, cGL + dirA * rInner);
            }
        }
    }
}
