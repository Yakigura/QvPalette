using System.Reflection;
using NUnit.Framework;
using QvPen.UdonScript;
using UnityEditor;
using UnityEngine;
using Yakigura.QvInkPreview;

namespace Yakigura.QvPalette.Tests
{
    public class QppPenPreviewSourceTests
    {
        [Test]
        public void PaletteChangesMatchRealInkColorWidthMaterialAndOutline()
        {
            var owner = new GameObject("Palette preview test");
            var previewRoot = new GameObject("Preview");
            var trailObject = new GameObject("InkTrail");
            previewRoot.transform.SetParent(owner.transform);
            trailObject.transform.SetParent(owner.transform);
            var materialA = new Material(Shader.Find("Standard"));
            var materialB = new Material(Shader.Find("Standard"));
            try
            {
                var palette = owner.AddComponent<QppPalette>();
                palette.EditorSetData(new[] { materialA, materialB }, new[] { materialA, materialB },
                    new[] { false, false }, new[] { 0, 1 }, new[] { Color.red, Color.cyan },
                    new[] { -1, -1 }, new Gradient[0], new[] { "Red", "Cyan" });
                var manager = owner.AddComponent<QvPen_PenManager>();
                var trail = trailObject.AddComponent<TrailRenderer>();
                trailObject.SetActive(false);
                var binding = owner.AddComponent<QppPenBinding>();
                var bindingData = new SerializedObject(binding);
                bindingData.FindProperty("penManager").objectReferenceValue = manager;
                bindingData.FindProperty("penTip").objectReferenceValue = owner.transform;
                bindingData.FindProperty("trailRendererGO").objectReferenceValue = trailObject;
                bindingData.FindProperty("palette").objectReferenceValue = palette;
                bindingData.ApplyModifiedPropertiesWithoutUndo();
                var preview = owner.AddComponent<QvInkPreview_Pen>();
                var line = previewRoot.AddComponent<LineRenderer>();
                var previewData = new SerializedObject(preview);
                previewData.FindProperty("penManager").objectReferenceValue = manager;
                previewData.FindProperty("trailRenderer").objectReferenceValue = trail;
                previewData.FindProperty("previewRoot").objectReferenceValue = previewRoot;
                previewData.FindProperty("previewLine").objectReferenceValue = line;
                previewData.ApplyModifiedPropertiesWithoutUndo();
                var bridge = owner.AddComponent<QppPenPreview>();
                bridge.EditorSetReferences(preview, binding, palette);

                AssertStyle(0, 100, Color.red, materialA);
                AssertStyle(1, 900, Color.cyan, materialB);

                void AssertStyle(int style, int widthIndex, Color color, Material material)
                {
                    typeof(QppPenBinding).GetField("currentStyleId", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(binding, style);
                    typeof(QppPenBinding).GetField("currentWidthIndex", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(binding, widthIndex);
                    typeof(QppPenBinding).GetField("localStyleRevision", BindingFlags.Instance | BindingFlags.NonPublic)
                        .SetValue(binding, style + 1);
                    manager.pcInkMaterial = material;
                    manager.questInkMaterial = material;
                    manager.colorGradient = binding.ResolveLocalGradient(style);
                    manager.inkWidth = palette.WidthOf(widthIndex);
                    var inkBlock = new MaterialPropertyBlock();
                    inkBlock.SetColor("_OutlineColor", color);
                    trail.SetPropertyBlock(inkBlock);
                    bridge._UpdatePreview();
                    typeof(QvInkPreview_Pen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(preview, null);

                    Assert.AreSame(material, line.sharedMaterial);
                    Assert.AreEqual(palette.WidthOf(widthIndex), line.widthMultiplier, 0.0001f);
                    Assert.Less(ColorDistance(color, line.colorGradient.Evaluate(0.5f)), 0.001f);
                    Assert.Less(ColorDistance(manager.colorGradient.Evaluate(0.5f), line.colorGradient.Evaluate(0.5f)), 0.001f);
                    var actualBlock = new MaterialPropertyBlock();
                    line.GetPropertyBlock(actualBlock);
                    Assert.Less(ColorDistance(inkBlock.GetColor("_OutlineColor"), actualBlock.GetColor("_OutlineColor")), 0.001f);

                    // An unchanged source must not reapply the block every frame.
                    actualBlock.SetColor("_OutlineColor", Color.magenta);
                    line.SetPropertyBlock(actualBlock);
                    typeof(QvInkPreview_Pen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(preview, null);
                    line.GetPropertyBlock(actualBlock);
                    Assert.Less(ColorDistance(Color.magenta, actualBlock.GetColor("_OutlineColor")), 0.001f);

                    inkBlock.SetColor("_OutlineColor", Color.green);
                    trail.SetPropertyBlock(inkBlock);
                    typeof(QvInkPreview_Pen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(preview, null);
                    line.GetPropertyBlock(actualBlock);
                    Assert.Less(ColorDistance(Color.green, actualBlock.GetColor("_OutlineColor")), 0.001f);
                }
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(materialA);
                Object.DestroyImmediate(materialB);
            }
        }

        private static float ColorDistance(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }
    }
}
