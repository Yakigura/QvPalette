using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// パネル左下の「ペンの大きさ」スライダー。0.5 倍〜2 倍で、中央が等倍。
    /// 操作はポインター（ペン先・指先）とデスクトップのレイ判定から OnProbeDrag で受ける。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPenSizeSlider : UdonSharpBehaviour
    {
        [SerializeField] private Transform knobTransform;
        [SerializeField] private TextMeshPro label;
        [SerializeField] private QppPaletteUI ui;

        [Header("Slider Geometry (relative to panelRoot)")]
        [SerializeField] private Vector2 sliderRangeX = new Vector2(-0.07f, 0.07f);
        [SerializeField] private Vector2 sliderCenter;
        [SerializeField] private Vector2 sliderSize = new Vector2(0.18f, 0.04f);

        public Vector2 SliderCenter => sliderCenter;
        public Vector2 SliderSize => sliderSize;

        private float shownScale = -1f;

        private void Start()
        {
            DisableInteractive = true;
        }

        public void UpdateVisual(float scale)
        {
            if (Mathf.Approximately(scale, shownScale)) return;
            shownScale = scale;
            if (Utilities.IsValid(knobTransform))
            {
                Vector3 pos = knobTransform.localPosition;
                pos.x = Mathf.Lerp(sliderRangeX.x, sliderRangeX.y, QppCommon.PenScalePositionOf(scale));
                knobTransform.localPosition = pos;
            }
            if (Utilities.IsValid(label)) label.text = QppCommon.PenScaleTextOf(scale);
        }

        public void OnProbeDrag(float localX)
        {
            float t = Mathf.InverseLerp(sliderRangeX.x, sliderRangeX.y, localX - sliderCenter.x);
            if (Utilities.IsValid(ui)) ui._OnPenScaleChanged(QppCommon.SnapPenScale(QppCommon.PenScaleOf(t)));
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(Transform knob, TextMeshPro lbl, QppPaletteUI u, Vector2 rangeX, Vector2 center,
            Vector2 size)
        {
            knobTransform = knob;
            label = lbl;
            ui = u;
            sliderRangeX = rangeX;
            sliderCenter = center;
            sliderSize = size;
        }
#endif
    }
}
