using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppSwatchButton : UdonSharpBehaviour
    {
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private TextMeshPro labelText;
        [SerializeField] private TextMeshProUGUI labelTextUGUI;
        [SerializeField] private Collider buttonCollider;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private int slotIndex;

        [Header("Local Rect Bounds (relative to panelRoot)")]
        [SerializeField] private Vector2 rectCenter;
        [SerializeField] private Vector2 rectSize = new Vector2(0.06f, 0.06f);

        private int boundStyleId = -1;

        public int BoundStyleId => boundStyleId;
        public int SlotIndex => slotIndex;
        public Vector2 RectCenter => rectCenter;
        public Vector2 RectSize => rectSize;

        private void Start()
        {
            if (Utilities.IsValid(previewLine))
            {
                previewLine.useWorldSpace = false;
                const int ptCount = 10;
                previewLine.positionCount = ptCount;
                Vector3[] pts = new Vector3[ptCount];
                float halfW = 0.022f;
                float amp = 0.005f;
                for (int i = 0; i < ptCount; i++)
                {
                    float t = (float)i / (ptCount - 1);
                    float x = Mathf.Lerp(-halfW, halfW, t);
                    float y = Mathf.Sin(t * Mathf.PI) * amp;
                    pts[i] = new Vector3(x, y, 0f);
                }
                previewLine.SetPositions(pts);
            }

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
                ui._OnSwatchClicked(slotIndex);
            }
        }

        public void ApplySwatchStyle(
            QppPalette palette,
            int styleId,
            Gradient dedicatedGradient,
            GradientColorKey[] colKeys,
            GradientAlphaKey[] alphaKeys,
            MaterialPropertyBlock block,
            float previewWidth)
        {
            boundStyleId = styleId;

            if (!Utilities.IsValid(palette) || styleId < 0 || styleId >= palette.EntryCount)
            {
                if (Utilities.IsValid(previewLine))
                    previewLine.gameObject.SetActive(false);
                SetLabelText(string.Empty);
                return;
            }

            if (Utilities.IsValid(previewLine))
            {
                previewLine.gameObject.SetActive(true);
                previewLine.sharedMaterial = palette.MaterialOf(styleId);

                if (palette.HasGradient(styleId))
                {
                    previewLine.colorGradient = palette.GradientOf(styleId);
                }
                else
                {
                    Color c = palette.ColorOf(styleId);
                    colKeys[0] = new GradientColorKey(c, 0f);
                    colKeys[1] = new GradientColorKey(c, 1f);
                    dedicatedGradient.SetKeys(colKeys, alphaKeys);
                    previewLine.colorGradient = dedicatedGradient;
                }

                block.Clear();
                previewLine.GetPropertyBlock(block);
                if (palette.UseRoundedTrail(styleId))
                {
                    previewLine.widthMultiplier = 0f;
                    block.SetFloat("_Width", previewWidth);
                }
                else
                {
                    previewLine.widthMultiplier = previewWidth;
                }
                // 縁取りも見本の線の太さに合わせる。
                palette.ApplyOutline(block, styleId, previewWidth);
                previewLine.SetPropertyBlock(block);
            }

            // 色の名称は出さない（タグで探す）。選択中の印（✔）だけを SetSelected が付ける。
            SetLabelText(string.Empty);
        }

        private void SetLabelText(string text)
        {
            if (Utilities.IsValid(labelText))
                labelText.text = text;
            if (Utilities.IsValid(labelTextUGUI))
                labelTextUGUI.text = text;
        }

        public void SetSelected(bool isSelected)
        {
            Color txtCol = isSelected ? new Color(0.2f, 1.0f, 0.4f, 1f) : new Color(0.85f, 0.85f, 0.85f, 1f);
            if (Utilities.IsValid(labelText))
            {
                labelText.color = txtCol;
                string t = labelText.text;
                if (isSelected)
                {
                    if (!t.StartsWith("✔ ")) labelText.text = "✔ " + t;
                }
                else
                {
                    if (t.StartsWith("✔ ")) labelText.text = t.Substring(2);
                }
            }
            if (Utilities.IsValid(labelTextUGUI))
            {
                labelTextUGUI.color = txtCol;
                string t = labelTextUGUI.text;
                if (isSelected)
                {
                    if (!t.StartsWith("✔ ")) labelTextUGUI.text = "✔ " + t;
                }
                else
                {
                    if (t.StartsWith("✔ ")) labelTextUGUI.text = t.Substring(2);
                }
            }
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            LineRenderer lr,
            TextMeshPro tmp,
            TextMeshProUGUI tmpu,
            Collider col,
            QppPaletteUI u,
            int idx,
            Vector2 center,
            Vector2 size)
        {
            previewLine = lr;
            labelText = tmp;
            labelTextUGUI = tmpu;
            buttonCollider = col;
            ui = u;
            slotIndex = idx;
            rectCenter = center;
            rectSize = size;
        }
#endif
    }
}
