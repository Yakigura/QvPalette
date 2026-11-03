using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPointerProbe : UdonSharpBehaviour
    {
        [SerializeField] private Transform panelRoot;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private bool enableFingerTouch = true;
        [Tooltip("VR で、触れている対象が変わったときと決定したときにコントローラーを振動させる。")]
        [SerializeField] private bool enableHaptics = true;

        private bool isUserInVR;
        private Transform displayRoot;
        private Vector3[] pointerWorld = new Vector3[3];
        private bool[] pointerActive = new bool[3];
        private Vector3[] prevLocal = new Vector3[3];
        private bool[] prevValid = new bool[3];
        private bool[] armedFromFront = new bool[3];
        private int lastLatchedSwatch = -1;
        private int lastLatchedPointer = -1;
        private int lastLatchedPreset = -1;
        private int lastLatchedPresetPointer = -1;

        // Require steady contact before a pen tip or finger activates a control.
        // かすめただけで変わらないよう、少しだけ留める時間を要求する。
        // フォーカス表示で狙いが見えるので、以前（0.30 / 0.40 秒）より短くしている。
        [Header("ペン先・指先の反応")]
        [Tooltip("ペン先が触れてから選ばれるまでの時間（秒）。短いほど敏感。")]
        [SerializeField] private float penTouchHoldSeconds = 0.06f;
        [Tooltip("指先が触れてから選ばれるまでの時間（秒）。短いほど敏感。")]
        [SerializeField] private float fingerTouchHoldSeconds = 0.10f;
        [Tooltip("ペン先が面の手前どこまで近づいたら触れたとみなすか（メートル）。大きいほど敏感。")]
        [SerializeField] private float penTouchDistance = 0.035f;
        [Tooltip("ペン先が面の奥どこまで入っていても触れ始めとみなすか（メートル）。速く押し込んでも拾いやすくなる。")]
        [SerializeField] private float penTouchBackDepth = 0.05f;
        [Tooltip("ペン先が一度触れたら、面の奥どこまで突き抜けても触れたままにするか（メートル）。")]
        [SerializeField] private float penStayDepth = 0.20f;

        // フォーカス表示と振動用。ポインターごとに今触れている対象を覚える。
        private const int FocusSwatch = 1;
        private const int FocusFavorite = 2;
        private const int FocusPreset = 3;
        private const int FocusControl = 4;
        private const int FocusSlider = 5;
        private int[] focusKey = new int[] { -1, -1, -1 };
        private int[] lastFocusKey = new int[] { -1, -1, -1 };
        private Vector2[] focusCenter = new Vector2[3];
        private Vector2[] focusSize = new Vector2[3];
        private float[] focusProgress = new float[3];
        private bool penInLeftHand;
        private int lastHapticWidthIndex = -1;
        private bool focusShown;
        private int[] currentHoverSwatch = new int[] { -1, -1, -1 };
        private float[] hoverSwatchTimer = new float[] { 0f, 0f, 0f };
        private bool[] swatchTriggered = new bool[] { false, false, false };
        private int[] currentHoverFavorite = new int[] { -1, -1, -1 };
        private float[] hoverFavoriteTimer = new float[] { 0f, 0f, 0f };

        private int[] currentHoverPreset = new int[] { -1, -1, -1 };
        private float[] hoverPresetTimer = new float[] { 0f, 0f, 0f };
        private bool[] presetTriggered = new bool[] { false, false, false };

        private int[] currentHoverControl = new int[] { -1, -1, -1 };
        private float[] hoverControlTimer = new float[] { 0f, 0f, 0f };
        private bool[] controlTriggered = new bool[] { false, false, false };
        private float[] sliderHoverTimer = new float[] { 0f, 0f, 0f };
        private bool[] sliderArmed = new bool[] { false, false, false };
        private float[] penSliderHoverTimer = new float[] { 0f, 0f, 0f };
        private bool[] penSliderArmed = new bool[] { false, false, false };
        private float lastHapticPenScale = -1f;
        private int lastLatchedControl = -1;
        private int lastLatchedControlPointer = -1;

        // 奥行きは DisplayRoot 基準。z < 0 が見る側（手前）、z > 0 がパネルの奥。
        // 指は表面の手前 3cm から触れ始める。一度触れたら、奥へ 10cm 突き抜けても触れたままにする。
        private const float TouchFront = -0.03f;
        private const float TouchBack = 0.018f;
        private const float StayFront = -0.025f;
        private const float StayBack = 0.10f;
        // 触れる前の狙い表示。手前 6cm から当たる場所を示す。
        private const float HoverFront = -0.06f;
        private const int HoverKeyOffset = 100000;
        private const float PressHitMargin = 0.004f;
        // 0.35 m/s permits slow aiming drift, but rejects a sideways swipe at entry
        // (about 4 mm per frame at 90 Hz). Active touches and drags bypass this gate.
        private const float LateralRejectSpeed = 0.35f;

        private bool[] touching = new bool[] { false, false, false };
        // 表面より奥にいる間は、表面を通った位置で選ぶ（斜めに刺さっても隣にずれない）。
        private bool[] surfaceAnchored = new bool[] { false, false, false };
        private Vector2[] anchorPoint = new Vector2[3];
        private Vector2[] prevPoint = new Vector2[3];

        private int hitKind;
        private int hitIndex;
        private Vector2 hitCenter;
        private Vector2 hitSize;
        private float hitDistanceSquared;

        private void Start()
        {
            var localPlayer = Networking.LocalPlayer;
            isUserInVR = Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR();
            if (Utilities.IsValid(panelRoot)) displayRoot = panelRoot.Find("DisplayRoot");
            ResetPrevValid();
        }

        public void ResetPrevValid()
        {
            if (prevValid == null) prevValid = new bool[3];
            if (armedFromFront == null) armedFromFront = new bool[3];
            if (prevLocal == null) prevLocal = new Vector3[3];
            if (currentHoverSwatch == null) currentHoverSwatch = new int[3];
            if (hoverSwatchTimer == null) hoverSwatchTimer = new float[3];
            if (swatchTriggered == null) swatchTriggered = new bool[3];
            if (currentHoverFavorite == null) currentHoverFavorite = new int[3];
            if (hoverFavoriteTimer == null) hoverFavoriteTimer = new float[3];
            if (currentHoverPreset == null) currentHoverPreset = new int[3];
            if (hoverPresetTimer == null) hoverPresetTimer = new float[3];
            if (presetTriggered == null) presetTriggered = new bool[3];
            if (currentHoverControl == null) currentHoverControl = new int[3];
            if (hoverControlTimer == null) hoverControlTimer = new float[3];
            if (controlTriggered == null) controlTriggered = new bool[3];
            if (sliderHoverTimer == null) sliderHoverTimer = new float[3];
            if (sliderArmed == null) sliderArmed = new bool[3];
            if (penSliderHoverTimer == null) penSliderHoverTimer = new float[3];
            if (penSliderArmed == null) penSliderArmed = new bool[3];
            if (touching == null) touching = new bool[3];
            if (surfaceAnchored == null) surfaceAnchored = new bool[3];
            if (anchorPoint == null) anchorPoint = new Vector2[3];
            if (prevPoint == null) prevPoint = new Vector2[3];

            for (int i = 0; i < 3; i++)
            {
                prevValid[i] = false;
                armedFromFront[i] = false;
                prevLocal[i] = Vector3.zero;
                currentHoverSwatch[i] = -1;
                hoverSwatchTimer[i] = 0f;
                swatchTriggered[i] = false;
                currentHoverFavorite[i] = -1;
                hoverFavoriteTimer[i] = 0f;
                currentHoverPreset[i] = -1;
                hoverPresetTimer[i] = 0f;
                presetTriggered[i] = false;
                currentHoverControl[i] = -1;
                hoverControlTimer[i] = 0f;
                controlTriggered[i] = false;
                sliderHoverTimer[i] = 0f;
                sliderArmed[i] = false;
                penSliderHoverTimer[i] = 0f;
                penSliderArmed[i] = false;
                touching[i] = false;
                surfaceAnchored[i] = false;
            }
            lastLatchedSwatch = -1;
            lastLatchedPointer = -1;
            lastLatchedPreset = -1;
            lastLatchedPresetPointer = -1;
            lastLatchedControl = -1;
            lastLatchedControlPointer = -1;

            if (focusKey == null) focusKey = new int[3];
            if (lastFocusKey == null) lastFocusKey = new int[3];
            for (int i = 0; i < 3; i++)
            {
                focusKey[i] = -1;
                lastFocusKey[i] = -1;
            }
            lastHapticWidthIndex = -1;
            if (focusShown && Utilities.IsValid(ui)) ui._ClearFocus();
            focusShown = false;
        }

        /// <summary>触れる前の狙い表示。決定の進み具合は 0 のまま、振動もさせない。</summary>
        private void SetHoverFocus(int p, int kind, int index, Vector2 center, Vector2 size)
        {
            focusKey[p] = HoverKeyOffset + kind * 1000 + index;
            focusCenter[p] = center;
            focusSize[p] = size;
            focusProgress[p] = 0f;
        }

        /// <summary>触れ始める手前側の境界（DisplayRoot の z）。ペン先はインスペクターの距離、指先は従来どおり。</summary>
        private float TouchFrontOf(int p)
        {
            return (p == 0) ? -Mathf.Max(penTouchDistance, 0.005f) : TouchFront;
        }

        /// <summary>触れ始めとみなす奥側の境界。ペン先はインスペクターの値、指先は従来どおり。</summary>
        private float TouchBackOf(int p)
        {
            return (p == 0) ? Mathf.Max(penTouchBackDepth, TouchBack) : TouchBack;
        }

        // 離れる手前側の境界は、触れ始める境界より常に 7 mm 手前に置く。逆転すると、触れては離れるを
        // 1 フレームごとに繰り返して保持時間が溜まらず、操作できなくなる。
        public static float ReleaseFront(float stayFront, float touchFront)
        {
            return Mathf.Min(stayFront, touchFront - 0.007f);
        }

        private float StayFrontOf(int p)
        {
            return ReleaseFront(StayFront, TouchFrontOf(p));
        }

        /// <summary>触れたままにする奥側の境界。ペン先はインスペクターの値、指先は従来どおり。</summary>
        private float StayBackOf(int p)
        {
            return (p == 0) ? Mathf.Max(penStayDepth, TouchBackOf(p)) : StayBack;
        }

        // touchBack is the maximum permitted entry depth (the existing stay limit).
        // prevZ must still be in front even if a stale arm were supplied.
        public static bool CanBeginTouch(bool armedFromFront, float prevZ, float curZ,
            float lateralSpeed, float touchFront, float touchBack, float maxLateral)
        {
            return armedFromFront && prevZ < touchFront && curZ >= touchFront &&
                curZ <= touchBack && lateralSpeed <= maxLateral;
        }

        // 従来の触れ始め判定（デスクトップ用）。前フレームが entryBack より手前なら、
        // 1 フレームで帯を飛び越える速い押し込みも拾う。
        public static bool CanBeginTouchDesktop(float prevZ, float curZ, float touchFront, float entryBack, float stayBack)
        {
            return prevZ <= entryBack && curZ >= touchFront && curZ <= stayBack;
        }

        public static bool InPressRect(float x, float y, Vector2 c, Vector2 s)
        {
            return Mathf.Abs(x - c.x) <= s.x * 0.5f + PressHitMargin &&
                Mathf.Abs(y - c.y) <= s.y * 0.5f + PressHitMargin;
        }

        // Slider geometry stays exact, including while a drag is already armed.
        private bool InRect(float x, float y, Vector2 c, Vector2 s)
        {
            return Mathf.Abs(x - c.x) <= s.x * 0.5f && Mathf.Abs(y - c.y) <= s.y * 0.5f;
        }

        private bool SetHit(int kind, int index, Vector2 c, Vector2 s)
        {
            hitKind = kind;
            hitIndex = index;
            hitCenter = c;
            hitSize = s;
            return true;
        }

        private void ConsiderHit(float x, float y, int kind, int index, Vector2 c, Vector2 s)
        {
            float distanceSquared = new Vector2(x - c.x, y - c.y).sqrMagnitude;
            // Keep the earlier target on an exact tie, so only one target can win.
            if (hitKind != 0 && distanceSquared >= hitDistanceSquared) return;
            hitDistanceSquared = distanceSquared;
            SetHit(kind, index, c, s);
        }

        /// <summary>余白が重なるときも、中心が最も近い一つの操作対象だけを選ぶ。</summary>
        private bool HitTest(float x, float y)
        {
            hitKind = 0;
            hitDistanceSquared = float.MaxValue;
            QppSwatchButton[] swatches = ui.SwatchButtons;
            if (swatches != null)
            {
                for (int i = 0; i < swatches.Length; i++)
                {
                    var sb = swatches[i];
                    if (Utilities.IsValid(sb) && InPressRect(x, y, sb.RectCenter, sb.RectSize))
                        ConsiderHit(x, y, FocusSwatch, sb.SlotIndex, sb.RectCenter, sb.RectSize);
                }
            }
            QppFavoriteButton[] favs = ui.FavoriteButtons;
            if (favs != null)
            {
                for (int i = 0; i < favs.Length; i++)
                {
                    var fb = favs[i];
                    if (Utilities.IsValid(fb) && InPressRect(x, y, fb.RectCenter, fb.RectSize))
                        ConsiderHit(x, y, FocusFavorite, fb.SlotIndex, fb.RectCenter, fb.RectSize);
                }
            }
            var ws = ui.WidthSlider;
            if (Utilities.IsValid(ws) && InRect(x, y, ws.SliderCenter, ws.SliderSize))
                ConsiderHit(x, y, FocusSlider, 0, ws.SliderCenter, ws.SliderSize);
            var pss = ui.PenSizeSlider;
            if (Utilities.IsValid(pss) && InRect(x, y, pss.SliderCenter, pss.SliderSize))
                ConsiderHit(x, y, FocusSlider, 1, pss.SliderCenter, pss.SliderSize);
            QppWidthPresetButton[] presets = ui.WidthPresetButtons;
            if (presets != null)
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    var pb = presets[i];
                    if (Utilities.IsValid(pb) && InPressRect(x, y, pb.RectCenter, pb.RectSize))
                        ConsiderHit(x, y, FocusPreset, pb.PresetIndex, pb.RectCenter, pb.RectSize);
                }
            }
            QppControlButton[] ctrlButtons = ui.ControlButtons;
            if (ctrlButtons != null)
            {
                for (int i = 0; i < ctrlButtons.Length; i++)
                {
                    var cb = ctrlButtons[i];
                    if (Utilities.IsValid(cb) && cb.gameObject.activeSelf && InPressRect(x, y, cb.RectCenter, cb.RectSize))
                        ConsiderHit(x, y, FocusControl, i, cb.RectCenter, cb.RectSize);
                }
            }
            return hitKind != 0;
        }

        private bool HitTestTouch(int p, float x, float y)
        {
            // Once a slider is armed, keep its original rect and drag continuation.
            // A neighboring button's margin must not steal an active drag.
            var ws = ui.WidthSlider;
            if (sliderArmed[p] && Utilities.IsValid(ws) && InRect(x, y, ws.SliderCenter, ws.SliderSize))
                return SetHit(FocusSlider, 0, ws.SliderCenter, ws.SliderSize);
            var pss = ui.PenSizeSlider;
            if (penSliderArmed[p] && Utilities.IsValid(pss) && InRect(x, y, pss.SliderCenter, pss.SliderSize))
                return SetHit(FocusSlider, 1, pss.SliderCenter, pss.SliderSize);

            // Exact slider contact wins over button margins from the first touch.
            // If slider rects overlap, retain the nearest-center selection and stable ties.
            hitKind = 0;
            hitDistanceSquared = float.MaxValue;
            if (Utilities.IsValid(ws) && InRect(x, y, ws.SliderCenter, ws.SliderSize))
                ConsiderHit(x, y, FocusSlider, 0, ws.SliderCenter, ws.SliderSize);
            if (Utilities.IsValid(pss) && InRect(x, y, pss.SliderCenter, pss.SliderSize))
                ConsiderHit(x, y, FocusSlider, 1, pss.SliderCenter, pss.SliderSize);
            if (hitKind != 0) return true;
            return HitTest(x, y);
        }

        private void SetFocus(int p, int kind, int index, Vector2 center, Vector2 size, float progress)
        {
            focusKey[p] = kind * 1000 + index;
            focusCenter[p] = center;
            focusSize[p] = size;
            focusProgress[p] = Mathf.Clamp01(progress);
        }

        /// <summary>VR のみ。ペン先はペンを持っている手、指先はその指の手を振動させる。</summary>
        private void Pulse(int p, float seconds, float amplitude)
        {
            if (!enableHaptics || !isUserInVR) return;
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;

            VRC_Pickup.PickupHand hand;
            if (p == 1) hand = VRC_Pickup.PickupHand.Left;
            else if (p == 2) hand = VRC_Pickup.PickupHand.Right;
            else hand = penInLeftHand ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right;
            localPlayer.PlayHapticEventInHand(hand, seconds, amplitude, 320f);
        }

        /// <summary>いちばん決定に近いポインターの対象をパネルに示す。</summary>
        private void ShowFocus()
        {
            int best = -1;
            float bestScore = 0f;
            for (int p = 0; p < 3; p++)
            {
                // 狙い表示（触れる前）では振動させない。触れた瞬間はキーが変わるので振動する。
                if (focusKey[p] >= 0 && focusKey[p] < HoverKeyOffset && focusKey[p] != lastFocusKey[p])
                    Pulse(p, 0.02f, 0.15f);
                lastFocusKey[p] = focusKey[p];

                if (focusKey[p] < 0) continue;
                // 触れているものを狙い表示より優先する。
                float score = focusKey[p] >= HoverKeyOffset ? -1f : focusProgress[p];
                if (best < 0 || score > bestScore)
                {
                    best = p;
                    bestScore = score;
                }
            }

            if (best >= 0)
            {
                ui._SetFocus(focusCenter[best], focusSize[best], focusProgress[best]);
                focusShown = true;
            }
            else if (focusShown)
            {
                ui._ClearFocus();
                focusShown = false;
            }
        }

        private Vector3 GetFingerTipPosition(VRCPlayerApi player, bool isLeft)
        {
            if (!Utilities.IsValid(player)) return Vector3.zero;

            HumanBodyBones distal = isLeft ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal;
            Vector3 pos = player.GetBonePosition(distal);
            if (pos != Vector3.zero) return pos;

            // Only a tracked index tip may press the palette. Falling back to the
            // palm makes an incidental hand brush indistinguishable from a tap.
            return Vector3.zero;
        }

        public void _UpdateProbe()
        {
            if (!Utilities.IsValid(panelRoot) || !panelRoot.gameObject.activeInHierarchy ||
                !Utilities.IsValid(ui) || !ui.CanOperate())
            {
                ResetPrevValid();
                return;
            }

            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer))
            {
                ResetPrevValid();
                return;
            }

            for (int p = 0; p < 3; p++) pointerActive[p] = false;

            // ペン先は自分が持っているペンのものを探して取得する
            if (Utilities.IsValid(ui) && ui.Bindings != null)
            {
                var allBindings = ui.Bindings;
                for (int b = 0; b < allBindings.Length; b++)
                {
                    var bind = allBindings[b];
                    if (Utilities.IsValid(bind) && bind.IsHeldByLocalPlayer && Utilities.IsValid(bind.PenTip))
                    {
                        pointerWorld[0] = bind.PenTip.position;
                        pointerActive[0] = true;
                        penInLeftHand = bind.IsHeldInLeftHand;
                        break;
                    }
                }
            }

            if (isUserInVR && enableFingerTouch)
            {
                Vector3 lf = GetFingerTipPosition(localPlayer, true);
                if (lf != Vector3.zero)
                {
                    pointerWorld[1] = lf;
                    pointerActive[1] = true;
                }

                Vector3 rf = GetFingerTipPosition(localPlayer, false);
                if (rf != Vector3.zero)
                {
                    pointerWorld[2] = rf;
                    pointerActive[2] = true;
                }
            }

            Transform refTr = Utilities.IsValid(displayRoot) ? displayRoot : panelRoot;

            for (int p = 0; p < 3; p++)
            {
                focusKey[p] = -1;
                if (!pointerActive[p])
                {
                    prevValid[p] = false;
                    armedFromFront[p] = false;
                    currentHoverSwatch[p] = -1;
                    hoverSwatchTimer[p] = 0f;
                    swatchTriggered[p] = false;
                    currentHoverFavorite[p] = -1;
                    hoverFavoriteTimer[p] = 0f;
                    currentHoverPreset[p] = -1;
                    hoverPresetTimer[p] = 0f;
                    presetTriggered[p] = false;
                    currentHoverControl[p] = -1;
                    hoverControlTimer[p] = 0f;
                    controlTriggered[p] = false;
                    sliderHoverTimer[p] = 0f;
                    sliderArmed[p] = false;
                    penSliderHoverTimer[p] = 0f;
                    penSliderArmed[p] = false;
                    touching[p] = false;
                    surfaceAnchored[p] = false;
                    if (lastLatchedPointer == p)
                    {
                        lastLatchedSwatch = -1;
                        lastLatchedPointer = -1;
                    }
                    if (lastLatchedPresetPointer == p)
                    {
                        lastLatchedPreset = -1;
                        lastLatchedPresetPointer = -1;
                    }
                    if (lastLatchedControlPointer == p)
                    {
                        lastLatchedControl = -1;
                        lastLatchedControlPointer = -1;
                    }
                    continue;
                }

                Vector3 cur = refTr.InverseTransformPoint(pointerWorld[p]);
                float touchFront = TouchFrontOf(p);

                if (!prevValid[p])
                {
                    prevLocal[p] = cur;
                    prevValid[p] = true;
                    armedFromFront[p] = cur.z >= HoverFront && cur.z < touchFront;
                    touching[p] = false;
                    surfaceAnchored[p] = false;
                    continue;
                }

                Vector3 prev = prevLocal[p];
                prevLocal[p] = cur;

                // Only a previous frame in front can start a touch. Consume the arm on
                // every entry attempt, including a rejected swipe; surface motion cannot re-arm.
                bool wasTouching = touching[p];
                if (wasTouching)
                    touching[p] = cur.z >= StayFrontOf(p) && cur.z <= StayBackOf(p);
                else if (isUserInVR)
                {
                    float lateralSpeed = new Vector2(cur.x - prev.x, cur.y - prev.y).magnitude / Mathf.Max(Time.deltaTime, 0.000001f);
                    // Keep the existing fast push-through allowance up to StayBackOf(p).
                    touching[p] = CanBeginTouch(armedFromFront[p], prev.z, cur.z, lateralSpeed,
                        touchFront, StayBackOf(p), LateralRejectSpeed);
                }
                else
                {
                    // デスクトップのペン先は視点に付いて動き、横速度が大きく手前の帯も 1 フレームで抜けるので、
                    // 手前から近づく条件と横滑りの拒否は VR だけに掛け、従来の判定のままにする。
                    touching[p] = CanBeginTouchDesktop(prev.z, cur.z, touchFront, TouchBackOf(p), StayBackOf(p));
                }
                // Update after the decision so arming always belongs to a previous frame.
                armedFromFront[p] = cur.z >= HoverFront && cur.z < touchFront;

                // 表面より奥では、表面を通った位置に固定する。
                if (!touching[p] || cur.z <= 0f)
                {
                    surfaceAnchored[p] = false;
                }
                else if (!surfaceAnchored[p])
                {
                    if (prev.z < 0f)
                    {
                        float tc = -prev.z / (cur.z - prev.z);
                        anchorPoint[p] = new Vector2(Mathf.Lerp(prev.x, cur.x, tc), Mathf.Lerp(prev.y, cur.y, tc));
                    }
                    else
                    {
                        anchorPoint[p] = new Vector2(cur.x, cur.y);
                    }
                    surfaceAnchored[p] = true;
                }
                Vector2 point = surfaceAnchored[p] ? anchorPoint[p] : new Vector2(cur.x, cur.y);

                // 留まっているかは面に沿った動きだけで見る（押し込む動きでは途切れさせない）。
                float observedStep = Mathf.Min(Time.deltaTime, 0.05f);
                // ペン先は手ぶれが乗りやすいので、留まっているとみなす幅を指先より広く取る。
                float stableStep = (p == 0) ? 0.004f + 0.30f * observedStep : 0.004f + 0.18f * observedStep;
                Vector2 lastPoint = wasTouching ? prevPoint[p] : new Vector2(prev.x, prev.y);
                bool steady = (point - lastPoint).sqrMagnitude <= stableStep * stableStep;
                prevPoint[p] = point;

                if (touching[p] && (steady || sliderArmed[p] || penSliderArmed[p]))
                {
                    float hx = point.x;
                    float hy = point.y;

                    float reqHold = (p == 0) ? penTouchHoldSeconds : fingerTouchHoldSeconds;
                    bool hasHit = HitTestTouch(p, hx, hy);

                    int hitSwatch = hasHit && hitKind == FocusSwatch ? hitIndex : -1;
                    Vector2 hitSwatchCenter = hitCenter;
                    Vector2 hitSwatchSize = hitSize;

                    // Keep the pointer on the same swatch for the dwell period.
                    if (hitSwatch >= 0)
                    {
                        if (currentHoverSwatch[p] == hitSwatch)
                        {
                            hoverSwatchTimer[p] += observedStep;
                            if (hoverSwatchTimer[p] >= reqHold && !swatchTriggered[p])
                            {
                                swatchTriggered[p] = true;
                                if (lastLatchedSwatch != hitSwatch || lastLatchedPointer != p)
                                {
                                    lastLatchedSwatch = hitSwatch;
                                    lastLatchedPointer = p;
                                    ui._OnSwatchClicked(hitSwatch);
                                    Pulse(p, 0.06f, 0.6f);
                                }
                            }
                        }
                        else
                        {
                            currentHoverSwatch[p] = hitSwatch;
                            hoverSwatchTimer[p] = 0f;
                            swatchTriggered[p] = false;
                        }
                        SetFocus(p, FocusSwatch, hitSwatch, hitSwatchCenter, hitSwatchSize,
                            swatchTriggered[p] ? 1f : hoverSwatchTimer[p] / reqHold);
                    }
                    else
                    {
                        currentHoverSwatch[p] = -1;
                        hoverSwatchTimer[p] = 0f;
                        swatchTriggered[p] = false;
                    }

                    // お気に入りスロット
                    int hitFavorite = hasHit && hitKind == FocusFavorite ? hitIndex : -1;
                    Vector2 hitFavoriteCenter = hitCenter;
                    Vector2 hitFavoriteSize = hitSize;

                    if (hitFavorite >= 0)
                    {
                        if (currentHoverFavorite[p] == hitFavorite)
                        {
                            bool wasBelow = hoverFavoriteTimer[p] < reqHold;
                            hoverFavoriteTimer[p] += observedStep;
                            if (hoverFavoriteTimer[p] >= reqHold)
                            {
                                ui._ReportFavoriteContact(hitFavorite);
                                if (wasBelow) Pulse(p, 0.06f, 0.6f);
                            }
                        }
                        else
                        {
                            currentHoverFavorite[p] = hitFavorite;
                            hoverFavoriteTimer[p] = 0f;
                        }
                        SetFocus(p, FocusFavorite, hitFavorite, hitFavoriteCenter, hitFavoriteSize,
                            hoverFavoriteTimer[p] / reqHold);
                    }
                    else
                    {
                        currentHoverFavorite[p] = -1;
                        hoverFavoriteTimer[p] = 0f;
                    }

                    // Arm the slider after a steady touch, then track its X coordinate.
                    var ws = ui.WidthSlider;
                    bool overSlider = false;
                    if (Utilities.IsValid(ws))
                    {
                        Vector2 sc = ws.SliderCenter;
                        Vector2 ss = ws.SliderSize;
                        if (hasHit && hitKind == FocusSlider && hitIndex == 0)
                        {
                            overSlider = true;
                            if (!sliderArmed[p])
                            {
                                sliderHoverTimer[p] += observedStep;
                                sliderArmed[p] = sliderHoverTimer[p] >= reqHold;
                                if (sliderArmed[p])
                                {
                                    Pulse(p, 0.05f, 0.5f);
                                    lastHapticWidthIndex = ui.CurrentWidthIndex;
                                }
                            }
                            if (sliderArmed[p])
                            {
                                // つまみは表面に固定せず、今の位置に追従させる。
                                ws.OnProbeDrag(cur.x);
                                // つまみが一定量動くごとに小さく振動させ、動いていることを手で分かるようにする。
                                int w = ui.CurrentWidthIndex;
                                if (lastHapticWidthIndex < 0 || Mathf.Abs(w - lastHapticWidthIndex) >= 16)
                                {
                                    if (lastHapticWidthIndex >= 0) Pulse(p, 0.015f, 0.25f);
                                    lastHapticWidthIndex = w;
                                }
                            }
                            SetFocus(p, FocusSlider, 0, sc, ss, sliderArmed[p] ? 1f : sliderHoverTimer[p] / reqHold);
                        }
                    }
                    if (!overSlider)
                    {
                        sliderHoverTimer[p] = 0f;
                        sliderArmed[p] = false;
                    }

                    // ペンの大きさスライダー（太さスライダーと同じく、少し留めてから追従）
                    var pss = ui.PenSizeSlider;
                    bool overPenSlider = false;
                    if (Utilities.IsValid(pss))
                    {
                        Vector2 pc = pss.SliderCenter;
                        Vector2 ps = pss.SliderSize;
                        if (hasHit && hitKind == FocusSlider && hitIndex == 1)
                        {
                            overPenSlider = true;
                            if (!penSliderArmed[p])
                            {
                                penSliderHoverTimer[p] += observedStep;
                                penSliderArmed[p] = penSliderHoverTimer[p] >= reqHold;
                                if (penSliderArmed[p])
                                {
                                    Pulse(p, 0.05f, 0.5f);
                                    lastHapticPenScale = -1f;
                                }
                            }
                            if (penSliderArmed[p])
                            {
                                pss.OnProbeDrag(cur.x);
                                float scale = ui.CurrentPenScale;
                                if (lastHapticPenScale < 0f || Mathf.Abs(scale - lastHapticPenScale) >= 0.1f)
                                {
                                    if (lastHapticPenScale >= 0f) Pulse(p, 0.015f, 0.25f);
                                    lastHapticPenScale = scale;
                                }
                            }
                            SetFocus(p, FocusSlider, 1, pc, ps, penSliderArmed[p] ? 1f : penSliderHoverTimer[p] / reqHold);
                        }
                    }
                    if (!overPenSlider)
                    {
                        penSliderHoverTimer[p] = 0f;
                        penSliderArmed[p] = false;
                    }

                    // 太さプリセットボタン
                    int hitPreset = hasHit && hitKind == FocusPreset ? hitIndex : -1;
                    Vector2 hitPresetCenter = hitCenter;
                    Vector2 hitPresetSize = hitSize;

                    if (hitPreset >= 0)
                    {
                        if (currentHoverPreset[p] == hitPreset)
                        {
                            hoverPresetTimer[p] += observedStep;
                            if (hoverPresetTimer[p] >= reqHold && !presetTriggered[p])
                            {
                                presetTriggered[p] = true;
                                if (lastLatchedPreset != hitPreset || lastLatchedPresetPointer != p)
                                {
                                    lastLatchedPreset = hitPreset;
                                    lastLatchedPresetPointer = p;
                                    ui._OnWidthPresetClicked(hitPreset);
                                    Pulse(p, 0.06f, 0.6f);
                                }
                            }
                        }
                        else
                        {
                            currentHoverPreset[p] = hitPreset;
                            hoverPresetTimer[p] = 0f;
                            presetTriggered[p] = false;
                        }
                        SetFocus(p, FocusPreset, hitPreset, hitPresetCenter, hitPresetSize,
                            presetTriggered[p] ? 1f : hoverPresetTimer[p] / reqHold);
                    }
                    else
                    {
                        currentHoverPreset[p] = -1;
                        hoverPresetTimer[p] = 0f;
                        presetTriggered[p] = false;
                    }

                    // コントロールボタン（閉じる、スタンドへ戻す、ページ送り、チルト切り替えなど）
                    int hitControl = hasHit && hitKind == FocusControl ? hitIndex : -1;
                    Vector2 hitControlCenter = hitCenter;
                    Vector2 hitControlSize = hitSize;
                    QppControlButton[] ctrlButtons = ui.ControlButtons;

                    if (hitControl >= 0)
                    {
                        if (currentHoverControl[p] == hitControl)
                        {
                            hoverControlTimer[p] += observedStep;
                            if (hoverControlTimer[p] >= reqHold && !controlTriggered[p])
                            {
                                controlTriggered[p] = true;
                                if (lastLatchedControl != hitControl || lastLatchedControlPointer != p)
                                {
                                    lastLatchedControl = hitControl;
                                    lastLatchedControlPointer = p;
                                    Pulse(p, 0.06f, 0.6f);
                                    ctrlButtons[hitControl]._Trigger();
                                }
                            }
                        }
                        else
                        {
                            currentHoverControl[p] = hitControl;
                            hoverControlTimer[p] = 0f;
                            controlTriggered[p] = false;
                        }
                        SetFocus(p, FocusControl, hitControl, hitControlCenter, hitControlSize,
                            controlTriggered[p] ? 1f : hoverControlTimer[p] / reqHold);
                    }
                    else
                    {
                        currentHoverControl[p] = -1;
                        hoverControlTimer[p] = 0f;
                        controlTriggered[p] = false;
                    }
                }
                else
                {
                    currentHoverSwatch[p] = -1;
                    hoverSwatchTimer[p] = 0f;
                    swatchTriggered[p] = false;
                    currentHoverFavorite[p] = -1;
                    hoverFavoriteTimer[p] = 0f;
                    currentHoverPreset[p] = -1;
                    hoverPresetTimer[p] = 0f;
                    presetTriggered[p] = false;
                    currentHoverControl[p] = -1;
                    hoverControlTimer[p] = 0f;
                    controlTriggered[p] = false;
                    sliderHoverTimer[p] = 0f;
                    sliderArmed[p] = false;
                    penSliderHoverTimer[p] = 0f;
                    penSliderArmed[p] = false;

                    // 触れる前（手前 6cm 以内）は、当たる場所を示して狙いを合わせやすくする。
                    if (!touching[p] && cur.z < touchFront && cur.z >= HoverFront && HitTest(cur.x, cur.y))
                        SetHoverFocus(p, hitKind, hitIndex, hitCenter, hitSize);
                }

                if (!touching[p])
                {
                    if (lastLatchedPointer == p)
                    {
                        lastLatchedSwatch = -1;
                        lastLatchedPointer = -1;
                    }
                    if (lastLatchedPresetPointer == p)
                    {
                        lastLatchedPreset = -1;
                        lastLatchedPresetPointer = -1;
                    }
                    if (lastLatchedControlPointer == p)
                    {
                        lastLatchedControl = -1;
                        lastLatchedControlPointer = -1;
                    }
                }
            }

            // Closing the panel from a control button resets the probe; nothing left to show then.
            if (Utilities.IsValid(panelRoot) && panelRoot.gameObject.activeInHierarchy) ShowFocus();
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            Transform root,
            QppPaletteUI u,
            bool finger)
        {
            panelRoot = root;
            ui = u;
            enableFingerTouch = finger;
        }
#endif
    }
}
