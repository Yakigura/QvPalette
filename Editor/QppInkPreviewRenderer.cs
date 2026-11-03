#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Yakigura.QvPalette.Editor
{
    public sealed class QppInkPreviewRenderer : IDisposable
    {
        private const int ImageWidth = 768;
        private const int ImageHeight = 256;
        // プレビューの線は、実際に引いた線を確かめやすいよう 20 cm にする。
        private const float StrokeLength = 0.20f;
        private const float StrokeWave = 0.012f;
        private const int StrokePoints = 32;
        private PreviewRenderUtility preview;
        private LineRenderer line;
        private RenderTexture texture;
        private ulong lastKey;
        private bool hasKey;

        public QppInkPreviewRenderer()
        {
            try
            {
                preview = new PreviewRenderUtility();
                var go = new GameObject("QPP ink preview") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(go);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = false;
                line.positionCount = StrokePoints;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                for (int i = 0; i < line.positionCount; i++)
                {
                    float t = i / (float)(line.positionCount - 1);
                    line.SetPosition(i, new Vector3(Mathf.Lerp(-StrokeLength * 0.5f, StrokeLength * 0.5f, t),
                        Mathf.Sin(t * Mathf.PI) * StrokeWave, 0f));
                }

                var camera = preview.camera;
                camera.orthographic = true;
                camera.transform.position = new Vector3(0f, StrokeWave * 0.5f, -1f);
                camera.transform.rotation = Quaternion.identity;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 10f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                foreach (var light in preview.lights) light.enabled = true;
                texture = new RenderTexture(ImageWidth, ImageHeight, 24, RenderTextureFormat.ARGB32)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    name = "QPP ink preview"
                };
                texture.Create();
                preview.ambientColor = Color.white;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public RenderTexture Render(QppInk ink, bool quest, bool rounded, float width, int background)
        {
            Material material = QppInkPreviewLogic.ResolveMaterial(ink, quest);
            if (!QppInkPreviewLogic.HasMaterial(material)) return null;
            QppInk effective = ink;
            if (ink.outlineRatio < 0f)
            {
                effective = ink.Clone();
                QppInkData.FillOutlineFromMaterial(effective);
            }
            int version = material.ComputeCRC();
            ulong key = QppInkPreviewLogic.MakeKey(material.GetInstanceID(), version, effective.gradient,
                effective.outlineRatio, effective.outlineColor, effective.outlineShift, width, background, quest, rounded);
            if (hasKey && key == lastKey && texture.IsCreated()) return texture;

            line.sharedMaterial = material;
            line.colorGradient = effective.gradient ?? QppInkData.SolidGradient(Color.white);
            line.widthMultiplier = rounded ? 0f : width;
            var block = new MaterialPropertyBlock();
            if (rounded) block.SetFloat("_Width", width);
            if (QppInkData.IsOutlined(ink.material))
            {
                block.SetFloat("_OutlineWidth", width * effective.outlineRatio);
                block.SetColor("_OutlineCustomColor", effective.outlineColor);
                block.SetFloat("_OutlineHueDelta", effective.outlineShift.x);
                block.SetFloat("_OutlineSaturationDelta", effective.outlineShift.y);
                block.SetFloat("_OutlineBrightnessDelta", effective.outlineShift.z);
            }
            line.SetPropertyBlock(block);
            var camera = preview.camera;
            camera.backgroundColor = QppInkPreviewLogic.BackgroundColor(background);
            float outline = QppInkData.IsOutlined(ink.material) ? Mathf.Max(0f, effective.outlineRatio) : 0f;
            // 縦は太さと縁取りが収まる高さ、横は線の長さ（StrokeLength）が収まる高さの大きいほうに合わせる。
            float vertical = StrokeWave + 0.004f + width * (0.7f + outline);
            // 丸い先端は、太さの半分（縁取り込み）だけ線の両端からはみ出すので、その分も枠に入れる。
            float capReach = width * (0.5f + outline);
            float horizontal = (StrokeLength * 0.5f + capReach + 0.006f) * ImageHeight / (float)ImageWidth;
            camera.orthographicSize = Mathf.Max(0.012f, vertical, horizontal);
            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;
            preview.BeginPreview(new Rect(0f, 0f, ImageWidth / pixelsPerPoint, ImageHeight / pixelsPerPoint), null);
            Texture rendered;
            try
            {
                preview.Render(false, false);
            }
            finally
            {
                rendered = preview.EndPreview();
            }
            Graphics.Blit(rendered, texture);
            lastKey = key;
            hasKey = true;
            return texture;
        }

        public void Dispose()
        {
            if (preview != null)
            {
                preview.camera.targetTexture = null;
                preview.Cleanup();
                preview = null;
                line = null;
            }
            if (texture != null)
            {
                texture.Release();
                Object.DestroyImmediate(texture);
                texture = null;
            }
            hasKey = false;
        }

        /// <summary>Renders a PNG using a graphics device. Run Unity batchmode without -nographics.</summary>
        /// <exception cref="InvalidOperationException">Thrown when no graphics device is available.</exception>
        public static void RenderPreviewToPng(Material m, Gradient g, float widthMeters, int bg, string path)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Ink preview requires a graphics device; run Unity batchmode without -nographics.");
            var ink = new QppInk { material = m, gradient = g };
            QppInkData.FillOutlineFromMaterial(ink);
            using (var renderer = new QppInkPreviewRenderer())
            {
                RenderTexture image = renderer.Render(ink, false, QppInkData.IsRoundedTrail(m), widthMeters, bg);
                RenderTexture previous = RenderTexture.active;
                Texture2D copy = null;
                try
                {
                    RenderTexture.active = image;
                    copy = new Texture2D(ImageWidth, ImageHeight, TextureFormat.RGB24, false);
                    copy.ReadPixels(new Rect(0, 0, ImageWidth, ImageHeight), 0, 0);
                    copy.Apply();
                    File.WriteAllBytes(path, copy.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (copy != null) Object.DestroyImmediate(copy);
                }
            }
        }
    }
}
#endif
