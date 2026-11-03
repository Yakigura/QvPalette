#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests.Editor
{
    public class QppInkPreviewTests
    {
        private static Gradient GradientOf(Color start, float colorTime, float alpha, float alphaTime,
            GradientMode mode = GradientMode.Blend)
        {
            var gradient = new Gradient { mode = mode };
            gradient.SetKeys(
                new[] { new GradientColorKey(start, colorTime), new GradientColorKey(Color.blue, 1f) },
                new[] { new GradientAlphaKey(alpha, alphaTime), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        private static ulong Key(int materialId = 1, int materialVersion = 7, Gradient gradient = null,
            float outlineRatio = 0.6f, Color? outlineColor = null, Vector3? outlineShift = null,
            float width = 0.005f, int background = 0, bool quest = false, bool rounded = false)
        {
            return QppInkPreviewLogic.MakeKey(materialId, materialVersion, gradient,
                outlineRatio, outlineColor ?? Color.white, outlineShift ?? new Vector3(0f, 0f, -0.2f),
                width, background, quest, rounded);
        }

        [Test]
        public void CacheKeyIsDeterministicForEquivalentValues()
        {
            var a = GradientOf(Color.red, 0f, 0.5f, 0f);
            var b = GradientOf(Color.red, 0f, 0.5f, 0f);
            Assert.That(Key(gradient: a), Is.EqualTo(Key(gradient: a)));
            Assert.That(Key(gradient: a), Is.EqualTo(Key(gradient: b)));
            Assert.That(Key(), Is.EqualTo(Key()));
        }

        [Test]
        public void CacheKeyChangesForEveryPreviewSetting()
        {
            var baseline = Key();
            Assert.That(Key(materialId: 2), Is.Not.EqualTo(baseline));
            Assert.That(Key(materialVersion: 8), Is.Not.EqualTo(baseline));
            Assert.That(Key(gradient: GradientOf(Color.red, 0f, 0.5f, 0f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(outlineRatio: 0.7f), Is.Not.EqualTo(baseline));
            Assert.That(Key(outlineColor: Color.black), Is.Not.EqualTo(baseline));
            Assert.That(Key(outlineShift: new Vector3(0.1f, 0f, -0.2f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(outlineShift: new Vector3(0f, 0.1f, -0.2f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(outlineShift: new Vector3(0f, 0f, -0.1f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(width: 0.006f), Is.Not.EqualTo(baseline));
            Assert.That(Key(background: 1), Is.Not.EqualTo(baseline));
            Assert.That(Key(quest: true), Is.Not.EqualTo(baseline));
            Assert.That(Key(rounded: true), Is.Not.EqualTo(baseline));
        }

        [Test]
        public void CacheKeyChangesForGradientKeysAndMode()
        {
            var baseline = Key(gradient: GradientOf(Color.red, 0f, 0.5f, 0f));
            Assert.That(Key(gradient: GradientOf(Color.green, 0f, 0.5f, 0f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(gradient: GradientOf(Color.red, 0.2f, 0.5f, 0f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(gradient: GradientOf(Color.red, 0f, 0.4f, 0f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(gradient: GradientOf(Color.red, 0f, 0.5f, 0.2f)), Is.Not.EqualTo(baseline));
            Assert.That(Key(gradient: GradientOf(Color.red, 0f, 0.5f, 0f, GradientMode.Fixed)), Is.Not.EqualTo(baseline));
        }

        [Test]
        public void WidthEndpointsMatchRuntimeMapping()
        {
            const float min = QppCommon.WIDTH_MIN;
            const float max = QppCommon.WIDTH_MAX;
            Assert.That(QppInkPreviewLogic.WidthOf(0, min, max), Is.EqualTo(0.003f).Within(0.000001f));
            Assert.That(QppInkPreviewLogic.WidthOf(QppCommon.MAX_WIDTH_INDEX, min, max),
                Is.EqualTo(0.1f).Within(0.000001f));
            Assert.That(QppInkPreviewLogic.WidthOf(-1, min, max), Is.EqualTo(0.003f).Within(0.000001f));
            Assert.That(QppInkPreviewLogic.WidthOf(QppCommon.MAX_WIDTH_INDEX + 1, min, max),
                Is.EqualTo(0.1f).Within(0.000001f));
        }

        [Test]
        public void NullMaterialIsMissingAndQuestFallsBackToPc()
        {
            var ink = new QppInk();
            Assert.That(QppInkPreviewLogic.HasMaterial(QppInkPreviewLogic.ResolveMaterial(null, false)), Is.False);
            Assert.That(QppInkPreviewLogic.HasMaterial(QppInkPreviewLogic.ResolveMaterial(ink, false)), Is.False);
            Assert.That(QppInkPreviewLogic.HasMaterial(QppInkPreviewLogic.ResolveMaterial(ink, true)), Is.False);
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assert.That(shader, Is.Not.Null);
            var pc = new Material(shader);
            var quest = new Material(shader);
            try
            {
                ink.material = pc;
                Assert.That(QppInkPreviewLogic.ResolveMaterial(ink, true), Is.SameAs(pc));
                Assert.That(QppInkPreviewLogic.HasMaterial(pc), Is.True);
                ink.questMaterial = quest;
                Assert.That(QppInkPreviewLogic.ResolveMaterial(ink, true), Is.SameAs(quest));
                Assert.That(QppInkPreviewLogic.ResolveMaterial(ink, false), Is.SameAs(pc));
            }
            finally
            {
                Object.DestroyImmediate(pc);
                Object.DestroyImmediate(quest);
            }
        }

        [Test]
        public void BackgroundChoicesAreDistinctAndOpaque()
        {
            var dark = QppInkPreviewLogic.BackgroundColor(0);
            var mid = QppInkPreviewLogic.BackgroundColor(1);
            var light = QppInkPreviewLogic.BackgroundColor(2);
            Assert.That(dark, Is.Not.EqualTo(mid));
            Assert.That(light, Is.Not.EqualTo(dark));
            Assert.That(light, Is.Not.EqualTo(mid));
            Assert.That(dark.a, Is.EqualTo(1f));
            Assert.That(mid.a, Is.EqualTo(1f));
            Assert.That(light.a, Is.EqualTo(1f));
        }
    }
}
#endif
