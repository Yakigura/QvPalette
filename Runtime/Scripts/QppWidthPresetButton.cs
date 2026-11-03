using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// 太さのプリセットボタン。スライドに加えて固定値へ一発で切り替えるための 4 個。
    /// （QvPen拡張パレット 仕様「サイズ変更は、スライドと、プリセットボタン４つで固定の値に切替できる」）
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppWidthPresetButton : UdonSharpBehaviour
    {
        [SerializeField] private TextMeshPro labelText;
        [Tooltip("このプリセットの太さで描く線の見本。")]
        [SerializeField] private LineRenderer sampleLine;
        [SerializeField] private Collider buttonCollider;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private int presetIndex;

        [Header("Local Rect Bounds (relative to DisplayRoot)")]
        [SerializeField] private Vector2 rectCenter;
        [SerializeField] private Vector2 rectSize = new Vector2(0.036f, 0.030f);

        public int PresetIndex => presetIndex;
        public Vector2 RectCenter => rectCenter;
        public Vector2 RectSize => rectSize;

        private void Start()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
            {
                DisableInteractive = true;
            }
        }

        public override void Interact()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
                return;

            if (Utilities.IsValid(ui))
            {
                ui._OnWidthPresetClicked(presetIndex);
            }
        }

        public void SetLabel(string text)
        {
            if (Utilities.IsValid(labelText) && labelText.text != text)
                labelText.text = text;
        }

        // ボタンからはみ出さない上限（これより太いプリセットは上限の太さで描く）。
        private const float SampleMaxWidth = 0.026f;

        /// <summary>選んでいるインクで、このプリセットの太さの線を描く。</summary>
        public void UpdateSample(QppPalette palette, int styleId, float width, Gradient dedicatedGradient,
            GradientColorKey[] colKeys, GradientAlphaKey[] alphaKeys, MaterialPropertyBlock block)
        {
            if (!Utilities.IsValid(sampleLine) || !Utilities.IsValid(palette)) return;
            sampleLine.sharedMaterial = palette.MaterialOf(styleId);
            if (palette.HasGradient(styleId))
            {
                sampleLine.colorGradient = palette.GradientOf(styleId);
            }
            else
            {
                Color c = palette.ColorOf(styleId);
                colKeys[0] = new GradientColorKey(c, 0f);
                colKeys[1] = new GradientColorKey(c, 1f);
                dedicatedGradient.SetKeys(colKeys, alphaKeys);
                sampleLine.colorGradient = dedicatedGradient;
            }

            float w = Mathf.Min(width, SampleMaxWidth);
            block.Clear();
            sampleLine.GetPropertyBlock(block);
            if (palette.UseRoundedTrail(styleId))
            {
                sampleLine.widthMultiplier = 0f;
                block.SetFloat("_Width", w);
            }
            else
            {
                sampleLine.widthMultiplier = w;
            }
            palette.ApplyOutline(block, styleId, w);
            sampleLine.SetPropertyBlock(block);
        }

        public void SetSelected(bool isSelected)
        {
            if (!Utilities.IsValid(labelText)) return;
            labelText.color = isSelected
                ? new Color(0.2f, 1.0f, 0.4f, 1f)
                : new Color(0.85f, 0.85f, 0.85f, 1f);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            TextMeshPro tmp,
            Collider col,
            QppPaletteUI u,
            int idx,
            Vector2 center,
            Vector2 size)
        {
            labelText = tmp;
            buttonCollider = col;
            ui = u;
            presetIndex = idx;
            rectCenter = center;
            rectSize = size;
        }

        public void EditorSetSampleLine(LineRenderer line)
        {
            sampleLine = line;
        }
#endif
    }
}
