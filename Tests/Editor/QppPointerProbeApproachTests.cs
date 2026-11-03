using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppPointerProbeApproachTests
    {
        private const BindingFlags PrivateFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject root;
        private QppPointerProbe probe;
        private QppPaletteUI ui;

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, PrivateFields).SetValue(target, value);
        }

        private static object GetField(object target, string name)
        {
            return target.GetType().GetField(name, PrivateFields).GetValue(target);
        }

        private void CreateProbe()
        {
            root = new GameObject("Pointer probe test");
            probe = (QppPointerProbe)root.AddComponent(typeof(QppPointerProbe));
            ui = (QppPaletteUI)root.AddComponent(typeof(QppPaletteUI));
            SetField(probe, "ui", ui);
        }

        private GameObject CreateTarget(string name)
        {
            var target = new GameObject(name);
            target.transform.SetParent(root.transform);
            return target;
        }

        private QppSwatchButton AddSwatch(float x, int slot)
        {
            var swatch = (QppSwatchButton)CreateTarget("Swatch").AddComponent(typeof(QppSwatchButton));
            SetField(swatch, "rectCenter", new Vector2(x, 0f));
            SetField(swatch, "rectSize", new Vector2(0.02f, 0.02f));
            SetField(swatch, "slotIndex", slot);
            return swatch;
        }

        private void AddSlider(bool penSize, Vector2 size)
        {
            var slider = CreateTarget("Slider").AddComponent(penSize ? typeof(QppPenSizeSlider) : typeof(QppWidthSlider));
            SetField(slider, "sliderCenter", Vector2.zero);
            SetField(slider, "sliderSize", size);
            SetField(ui, penSize ? "penSizeSlider" : "widthSlider", slider);
        }

        private void AddNeighboringButton(string kind)
        {
            Component button;
            var target = CreateTarget(kind);
            if (kind == "Swatch")
            {
                button = target.AddComponent(typeof(QppSwatchButton));
                SetField(ui, "swatchButtons", new QppSwatchButton[] { (QppSwatchButton)button });
            }
            else if (kind == "Favorite")
            {
                button = target.AddComponent(typeof(QppFavoriteButton));
                SetField(ui, "favoriteButtons", new QppFavoriteButton[] { (QppFavoriteButton)button });
            }
            else if (kind == "Preset")
            {
                button = target.AddComponent(typeof(QppWidthPresetButton));
                SetField(ui, "widthPresetButtons", new QppWidthPresetButton[] { (QppWidthPresetButton)button });
            }
            else
            {
                button = target.AddComponent(typeof(QppControlButton));
                SetField(ui, "controlButtons", new QppControlButton[] { (QppControlButton)button });
            }
            SetField(button, "rectCenter", new Vector2(0.025f, 0f));
            SetField(button, "rectSize", new Vector2(0.02f, 0.02f));
        }

        private bool HitTest(float x, float y)
        {
            return (bool)typeof(QppPointerProbe).GetMethod("HitTest", PrivateFields)
                .Invoke(probe, new object[] { x, y });
        }

        private bool HitTestTouch(float x, float y)
        {
            return (bool)typeof(QppPointerProbe).GetMethod("HitTestTouch", PrivateFields)
                .Invoke(probe, new object[] { 0, x, y });
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.DestroyImmediate(root);
        }

        // 離れる境界は、触れ始める境界より常に 7 mm 手前（触れ始めを広げたら、離れる側も一緒に手前へ動く）。
        [TestCase(0, 0.035f, -0.042f)]
        [TestCase(0, 0.05f, -0.057f)]
        [TestCase(0, 0.025f, -0.032f)]
        [TestCase(1, 0.035f, -0.037f)]
        [TestCase(2, 0.035f, -0.037f)]
        public void StayFrontStaysSevenMillimetresInFrontOfTheTouchFront(int pointer, float penDistance, float expected)
        {
            CreateProbe();
            var stayFront = typeof(QppPointerProbe).GetMethod("StayFrontOf", PrivateFields);
            var touchFront = typeof(QppPointerProbe).GetMethod("TouchFrontOf", PrivateFields);
            SetField(probe, "penTouchDistance", penDistance);
            float release = (float)stayFront.Invoke(probe, new object[] { pointer });
            Assert.AreEqual(expected, release, 0.0001f);
            Assert.Less(release, (float)touchFront.Invoke(probe, new object[] { pointer }));
        }

        [TestCase(-0.045f, -0.03f, -0.03f, 0.10f)]
        [TestCase(-0.045f, 0f, -0.03f, 0.10f)]
        [TestCase(-0.045f, -0.033f, -0.035f, 0.20f)]
        [TestCase(-0.045f, 0.15f, -0.035f, 0.20f)]
        public void PerpendicularApproachIsAcceptedIncludingFastPenPushThrough(
            float prevZ, float curZ, float front, float back)
        {
            Assert.IsTrue(QppPointerProbe.CanBeginTouch(true, prevZ, curZ, 0f, front, back, 0.35f));
        }

        [TestCase(-0.03f)]
        [TestCase(-0.02f)]
        [TestCase(0f)]
        [TestCase(0.02f)]
        public void LateralSlideAtOrAfterEntryPlaneCannotBeginEvenWithStaleArm(float z)
        {
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(true, z, z, 0.01f, -0.03f, 0.10f, 0.35f));
        }

        [TestCase(0f)]
        [TestCase(0.35f)]
        public void SlowDriftUpToTheSpeedLimitIsAccepted(float speed)
        {
            Assert.IsTrue(QppPointerProbe.CanBeginTouch(true, -0.04f, -0.02f, speed, -0.03f, 0.10f, 0.35f));
        }

        [TestCase(0.3501f)]
        [TestCase(1f)]
        public void FastLateralMotionAtPressIsRejected(float speed)
        {
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(true, -0.04f, -0.02f, speed, -0.03f, 0.10f, 0.35f));
        }

        [Test]
        public void UnarmedPointerCannotBeginTouch()
        {
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(false, -0.04f, -0.02f, 0f, -0.03f, 0.10f, 0.35f));
        }

        [TestCase(-0.031f)]
        [TestCase(0.101f)]
        public void PositionOutsideTheEntryDepthWindowIsRejected(float curZ)
        {
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(true, -0.04f, curZ, 0f, -0.03f, 0.10f, 0.35f));
        }

        [Test]
        public void RejectedSwipeCannotRetryOnSurfaceButCanApproachAgainFromFront()
        {
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(true, -0.04f, 0f, 0.5f, -0.03f, 0.10f, 0.35f));
            Assert.IsFalse(QppPointerProbe.CanBeginTouch(false, 0f, 0f, 0f, -0.03f, 0.10f, 0.35f));
            Assert.IsTrue(QppPointerProbe.CanBeginTouch(true, -0.04f, 0f, 0f, -0.03f, 0.10f, 0.35f));
        }

        [TestCase(0.0339f, 0f, true)]
        [TestCase(-0.0339f, 0f, true)]
        [TestCase(0f, 0.0239f, true)]
        [TestCase(0f, -0.0239f, true)]
        [TestCase(0.0339f, 0.0239f, true)]
        [TestCase(0.0341f, 0f, false)]
        [TestCase(-0.0341f, 0f, false)]
        [TestCase(0f, 0.0241f, false)]
        [TestCase(0f, -0.0241f, false)]
        public void PressMarginExtendsEveryRectEdgeByFourMillimetres(float x, float y, bool expected)
        {
            Assert.AreEqual(expected, QppPointerProbe.InPressRect(x, y, Vector2.zero, new Vector2(0.06f, 0.04f)));
        }

        // 離れる境界が触れ始める境界より奥にあると、触れる・離れるを毎フレーム繰り返して操作できなくなる。
        [TestCase(-0.025f)]  // 変更前のペン先
        [TestCase(-0.035f)]  // 現在のペン先（penTouchDistance 0.035）
        [TestCase(-0.05f)]
        [TestCase(-0.03f)]   // 指先
        public void ReleaseFrontIsAlwaysInFrontOfTheTouchFront(float touchFront)
        {
            float release = QppPointerProbe.ReleaseFront(-0.025f, touchFront);
            Assert.Less(release, touchFront - 0.0069f);
        }

        // デスクトップは従来どおり。手前の帯を 1 フレームで抜けても、横に動いていても触れ始めになる。
        [TestCase(-0.10f, -0.01f, true)]   // 帯（-0.06〜-0.03）を飛び越えた速い押し込み
        [TestCase(-0.04f, -0.02f, true)]
        [TestCase(0.01f, 0.01f, true)]     // すでに面の近くにあるところへ横から入る
        [TestCase(-0.04f, -0.05f, false)]  // まだ手前
        [TestCase(0.20f, 0.20f, false)]    // 奥へ入りすぎている
        public void DesktopTouchBeginKeepsTheLegacyRule(float prevZ, float curZ, bool expected)
        {
            Assert.AreEqual(expected, QppPointerProbe.CanBeginTouchDesktop(prevZ, curZ, -0.03f, 0.018f, 0.10f));
        }

        [Test]
        public void PressMarginCoversJustInsideItsEdgeAndUsesTheTargetCenter()
        {
            // float の丸めで境界ちょうどは当てにならないので、わずかに内側と外側で確かめる。
            float xEdge = 0.06f * 0.5f + 0.004f;
            float yEdge = 0.04f * 0.5f + 0.004f;
            Vector2 size = new Vector2(0.06f, 0.04f);
            Assert.IsTrue(QppPointerProbe.InPressRect(xEdge - 0.0001f, yEdge - 0.0001f, Vector2.zero, size));
            Assert.IsFalse(QppPointerProbe.InPressRect(xEdge + 0.0001f, 0f, Vector2.zero, size));
            Assert.IsFalse(QppPointerProbe.InPressRect(0f, yEdge + 0.0001f, Vector2.zero, size));
            Assert.IsTrue(QppPointerProbe.InPressRect(0.133f, -0.223f, new Vector2(0.10f, -0.20f), size));
        }

        [TestCase(0.012f, 3)]
        [TestCase(0.013f, 7)]
        [TestCase(0.0125f, 3)]
        public void OverlappingMarginsChooseOneNearestSwatchWithStableTies(float x, int expectedSlot)
        {
            CreateProbe();
            // Centers are 2.5 cm apart; both expanded 2 cm rects contain x.
            SetField(ui, "swatchButtons", new QppSwatchButton[] { AddSwatch(0f, 3), AddSwatch(0.025f, 7) });
            Assert.IsTrue(HitTest(x, 0f));
            Assert.AreEqual(expectedSlot, GetField(probe, "hitIndex"));
            Assert.AreEqual(GetField(probe, "FocusSwatch"), GetField(probe, "hitKind"));
        }

        [Test]
        public void NearestTargetWinsAcrossButtonCategories()
        {
            CreateProbe();
            SetField(ui, "swatchButtons", new QppSwatchButton[] { AddSwatch(0f, 3) });
            var control = (QppControlButton)CreateTarget("Control").AddComponent(typeof(QppControlButton));
            SetField(control, "rectCenter", new Vector2(0.025f, 0f));
            SetField(control, "rectSize", new Vector2(0.02f, 0.02f));
            SetField(ui, "controlButtons", new QppControlButton[] { control });
            Assert.IsTrue(HitTest(0.013f, 0f));
            Assert.AreEqual(GetField(probe, "FocusControl"), GetField(probe, "hitKind"));
            Assert.AreEqual(0, GetField(probe, "hitIndex"));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void SliderRectRemainsExactBeforeAndDuringDrag(bool penSize, bool armed)
        {
            CreateProbe();
            AddSlider(penSize, new Vector2(0.02f, 0.02f));
            ((bool[])GetField(probe, penSize ? "penSliderArmed" : "sliderArmed"))[0] = armed;
            Assert.IsTrue(HitTestTouch(0.009f, 0f));
            Assert.IsFalse(HitTestTouch(0.013f, 0f), "The button margin must not extend a slider rect");
        }

        [Test]
        public void InitialAndActiveSliderContactCannotBeStolenByANeighboringButtonMargin(
            [Values(false, true)] bool penSize,
            [Values("Swatch", "Favorite", "Preset", "Control")] string buttonKind)
        {
            CreateProbe();
            AddSlider(penSize, new Vector2(0.04f, 0.02f));
            AddNeighboringButton(buttonKind);
            // Outside the exact slider rect, the neighboring button remains selectable.
            Assert.IsTrue(HitTestTouch(0.021f, 0f));
            Assert.AreEqual(GetField(probe, "Focus" + buttonKind), GetField(probe, "hitKind"));
            // x is inside the slider, but only inside the neighboring button's margin.
            Assert.IsTrue(HitTestTouch(0.014f, 0f));
            Assert.AreEqual(GetField(probe, "FocusSlider"), GetField(probe, "hitKind"));
            Assert.AreEqual(penSize ? 1 : 0, GetField(probe, "hitIndex"));
            ((bool[])GetField(probe, penSize ? "penSliderArmed" : "sliderArmed"))[0] = true;
            Assert.IsTrue(HitTestTouch(0.014f, 0f));
            Assert.AreEqual(GetField(probe, "FocusSlider"), GetField(probe, "hitKind"));
            Assert.AreEqual(penSize ? 1 : 0, GetField(probe, "hitIndex"));
        }

        [Test]
        public void ResetClearsApproachArmsForEveryPointer()
        {
            CreateProbe();
            var arms = (bool[])GetField(probe, "armedFromFront");
            for (int p = 0; p < arms.Length; p++) arms[p] = true;
            probe.ResetPrevValid();
            for (int p = 0; p < arms.Length; p++) Assert.IsFalse(arms[p]);
        }
    }
}
