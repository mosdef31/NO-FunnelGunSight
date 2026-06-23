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
            float      dotRadius)
        {
            _gunCrossScreen = gunCrossScreen;
            _leftWall       = leftWall;
            _rightWall      = rightWall;
            _isVisible      = isVisible;
            _dotPos         = dotPos;
            _dotRadius      = dotRadius;
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
            var   color   = new Color(0f, 1f, 0f, opacity);

            try
            {
                GL.PushMatrix();
                GL.LoadPixelMatrix();
                _material.SetPass(0);
                GL.Begin(GL.LINES);
                GL.Color(color);

                if (cfg?.ShowGunCross.Value ?? true)
                    DrawCross(_gunCrossScreen, cfg?.GunCrossSize.Value ?? 8f);

                if (_leftWall  != null) DrawPolyline(_leftWall);
                if (_rightWall != null) DrawPolyline(_rightWall);

                if (_dotPos.HasValue && _dotRadius > 1f)
                    DrawCircle(_dotPos.Value, _dotRadius);

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
        // All incoming coordinates come from Camera.WorldToScreenPoint, which
        // measures Y from the BOTTOM of the screen (OpenGL convention).
        // GL.LoadPixelMatrix() under URP on Windows/DX11 measures Y from the TOP
        // (DirectX convention). X is identical in both systems — only Y is flipped.
        // Fy() applies the one-line correction at every vertex so the rest of the
        // codebase stays in WorldToScreenPoint space throughout.

        private static float Fy(float y) => Screen.height - y;

        private static void DrawCross(Vector2 c, float arm)
        {
            GL.Vertex3(c.x - arm, Fy(c.y),       0f);
            GL.Vertex3(c.x + arm, Fy(c.y),       0f);
            GL.Vertex3(c.x,       Fy(c.y) - arm, 0f);
            GL.Vertex3(c.x,       Fy(c.y) + arm, 0f);
        }

        private static void DrawPolyline(Vector2[] pts)
        {
            for (int i = 0; i < pts.Length - 1; i++)
            {
                GL.Vertex3(pts[i].x,     Fy(pts[i].y),     0f);
                GL.Vertex3(pts[i + 1].x, Fy(pts[i + 1].y), 0f);
            }
        }

        private static void DrawCircle(Vector2 c, float r)
        {
            float step = Mathf.PI * 2f / CircleSegments;
            float cy   = Fy(c.y);
            for (int i = 0; i < CircleSegments; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                GL.Vertex3(c.x + Mathf.Cos(a0) * r, cy + Mathf.Sin(a0) * r, 0f);
                GL.Vertex3(c.x + Mathf.Cos(a1) * r, cy + Mathf.Sin(a1) * r, 0f);
            }
        }
    }
}
