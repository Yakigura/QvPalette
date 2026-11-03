using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppWidthSlider : UdonSharpBehaviour
    {
        private const float WidthPreviewMax = 0.03f;

        [SerializeField] private LineRenderer widthPreviewLine;
        [SerializeField] private Transform knobTransform;
        [SerializeField] private Collider sliderCollider;
        [SerializeField] private QppPaletteUI ui;

        [Header("Slider Geometry (relative to panelRoot)")]
        [SerializeField] private Vector2 sliderRangeX = new Vector2(-0.1f, 0.1f);
        [SerializeField] private Vector2 sliderCenter = new Vector2(0f, -0.15f);
        [SerializeField] private Vector2 sliderSize = new Vector2(0.24f, 0.05f);

        public Vector2 SliderCenter => sliderCenter;
        public Vector2 SliderSize => sliderSize;

        private void Start()
        {
            if (Utilities.IsValid(widthPreviewLine))
            {
                widthPreviewLine.useWorldSpace = false;
                const int ptCount = 10;
                widthPreviewLine.positionCount = ptCount;
                Vector3[] pts = new Vector3[ptCount];
                float halfW = 0.04f;
                for (int i = 0; i < ptCount; i++)
                {
                    float t = (float)i / (ptCount - 1);
                    float x = Mathf.Lerp(-halfW, halfW, t);
                    pts[i] = new Vector3(x, 0f, 0f);
                }
                widthPreviewLine.SetPositions(pts);
            }

            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
            {
                DisableInteractive = true;
            }
        }

        public void UpdateSliderVisual(int widthIndex)
        {
            float t = Mathf.Clamp01(widthIndex / (float)QppCommon.MAX_WIDTH_INDEX);
            if (Utilities.IsValid(knobTransform))
            {
                Vector3 pos = knobTransform.localPosition;
                pos.x = Mathf.Lerp(sliderRangeX.x, sliderRangeX.y, t);
                knobTransform.localPosition = pos;
            }
        }

        public void UpdatePreviewLine(
            QppPalette palette,
            int styleId,
            int widthIndex,
            Gradient dedicatedGradient,
            GradientColorKey[] colKeys,
            GradientAlphaKey[] alphaKeys,
            MaterialPropertyBlock block)
        {
            if (!Utilities.IsValid(widthPreviewLine) || !Utilities.IsValid(palette)) return;

            widthPreviewLine.gameObject.SetActive(true);
            widthPreviewLine.sharedMaterial = palette.MaterialOf(styleId);

            if (palette.HasGradient(styleId))
            {
                widthPreviewLine.colorGradient = palette.GradientOf(styleId);
            }
            else
            {
                Color c = palette.ColorOf(styleId);
                colKeys[0] = new GradientColorKey(c, 0f);
                colKeys[1] = new GradientColorKey(c, 1f);
                dedicatedGradient.SetKeys(colKeys, alphaKeys);
                widthPreviewLine.colorGradient = dedicatedGradient;
            }

            // 太い線はパネルからはみ出すので、見本は 30mm で頭打ちにする（実際の太さは数値で示す）。
            float w = Mathf.Min(palette.WidthOf(widthIndex), WidthPreviewMax);
            block.Clear();
            widthPreviewLine.GetPropertyBlock(block);
            if (palette.UseRoundedTrail(styleId))
            {
                widthPreviewLine.widthMultiplier = 0f;
                block.SetFloat("_Width", w);
            }
            else
            {
                widthPreviewLine.widthMultiplier = w;
            }
            palette.ApplyOutline(block, styleId, w);
            widthPreviewLine.SetPropertyBlock(block);
        }

        public void OnProbeDrag(float localX)
        {
            // localX はパネル座標。つまみの可動域はスライダー中心からの相対値なので合わせる。
            float t = Mathf.InverseLerp(sliderRangeX.x, sliderRangeX.y, localX - sliderCenter.x);
            int idx = Mathf.Clamp(Mathf.RoundToInt(t * QppCommon.MAX_WIDTH_INDEX), 0, QppCommon.MAX_WIDTH_INDEX);
            if (Utilities.IsValid(ui))
            {
                ui._OnWidthChanged(idx);
            }
        }

        public override void Interact()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
                return;

            if (Utilities.IsValid(ui))
            {
                int cur = ui.CurrentWidthIndex;
                int next = (cur + QppCommon.WIDTH_STEPS / 8) % QppCommon.WIDTH_STEPS;
                ui._OnWidthChanged(next);
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            LineRenderer lr,
            Transform knob,
            Collider col,
            QppPaletteUI u,
            Vector2 rangeX,
            Vector2 center,
            Vector2 size)
        {
            widthPreviewLine = lr;
            knobTransform = knob;
            sliderCollider = col;
            ui = u;
            sliderRangeX = rangeX;
            sliderCenter = center;
            sliderSize = size;
        }
#endif
    }
}
