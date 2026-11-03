using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// お気に入りスロット 1 個分のボタン。
    /// スウォッチと同じくインクプレビュー（実際の LineRenderer）で「呼び出せる線」を描く。
    /// 長押し中は縁が満ちる表示（Fill）で保存されることを事前に伝える。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppFavoriteButton : UdonSharpBehaviour
    {
        [SerializeField] private LineRenderer previewLine;
        [SerializeField] private TextMeshPro labelText;
        [SerializeField] private Transform fillTransform;
        [SerializeField] private Collider buttonCollider;
        [SerializeField] private QppPaletteUI ui;
        [SerializeField] private int slotIndex;
        [Tooltip("未登録のときに出す枠（どこがお気に入りか分かるように）。")]
        [SerializeField] private GameObject emptyFrame;

        [Header("Local Rect Bounds (relative to DisplayRoot)")]
        [SerializeField] private Vector2 rectCenter;
        [SerializeField] private Vector2 rectSize = new Vector2(0.033f, 0.033f);

        private float lastFill = -1f;

        public int SlotIndex => slotIndex;
        public Vector2 RectCenter => rectCenter;
        public Vector2 RectSize => rectSize;

        private void Start()
        {
            if (Utilities.IsValid(previewLine))
            {
                previewLine.useWorldSpace = false;
                const int ptCount = 8;
                previewLine.positionCount = ptCount;
                Vector3[] pts = new Vector3[ptCount];
                float halfW = 0.012f;
                for (int i = 0; i < ptCount; i++)
                {
                    float t = (float)i / (ptCount - 1);
                    pts[i] = new Vector3(Mathf.Lerp(-halfW, halfW, t), 0f, 0f);
                }
                previewLine.SetPositions(pts);
            }

            SetFillProgress(0f);

            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
            {
                DisableInteractive = true;
            }
        }

        public override void Interact()
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
                return;

            // ここで呼び出すと、長押しで上書き登録する前に登録済みのインクへ切り替わってしまう。
            // 触れたことだけ伝え、短いクリックなら呼び出し・長押しなら登録の判定は UI 側に任せる。
            if (Utilities.IsValid(ui))
            {
                ui._ReportFavoriteContact(slotIndex);
            }
        }

        public void ApplyFavoriteStyle(
            QppPalette palette,
            bool used,
            int styleId,
            int widthIndex,
            Gradient dedicatedGradient,
            GradientColorKey[] colKeys,
            GradientAlphaKey[] alphaKeys,
            MaterialPropertyBlock block)
        {
            bool empty = !Utilities.IsValid(palette) || !used || styleId < 0 || styleId >= palette.EntryCount;
            if (Utilities.IsValid(emptyFrame) && emptyFrame.activeSelf != empty) emptyFrame.SetActive(empty);
            if (empty)
            {
                if (Utilities.IsValid(previewLine))
                    previewLine.gameObject.SetActive(false);
                SetLabel("空");
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

                // お気に入りは「呼び出せる線」を示すため、登録された太さそのままで描く。
                float w = Mathf.Min(palette.WidthOf(widthIndex), 0.012f);
                if (palette.UseRoundedTrail(styleId))
                {
                    previewLine.widthMultiplier = 0f;
                    block.Clear();
                    previewLine.GetPropertyBlock(block);
                    block.SetFloat("_Width", w);
                    palette.ApplyOutline(block, styleId, w);
                    previewLine.SetPropertyBlock(block);
                }
                else
                {
                    previewLine.widthMultiplier = w;
                    block.Clear();
                    previewLine.GetPropertyBlock(block);
                    palette.ApplyOutline(block, styleId, w);
                    previewLine.SetPropertyBlock(block);
                }
            }

            SetLabel(palette.MillimetersTextOf(widthIndex));
        }

        private void SetLabel(string text)
        {
            if (Utilities.IsValid(labelText) && labelText.text != text)
                labelText.text = text;
        }

        public void SetFillProgress(float t)
        {
            t = Mathf.Clamp01(t);
            if (Mathf.Abs(t - lastFill) < 0.01f) return;
            lastFill = t;

            if (!Utilities.IsValid(fillTransform)) return;

            fillTransform.gameObject.SetActive(t > 0.001f);
            Vector3 s = fillTransform.localScale;
            fillTransform.localScale = new Vector3(rectSize.x * t, s.y, s.z);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            LineRenderer lr,
            TextMeshPro tmp,
            Transform fill,
            Collider col,
            QppPaletteUI u,
            int idx,
            Vector2 center,
            Vector2 size)
        {
            previewLine = lr;
            labelText = tmp;
            fillTransform = fill;
            buttonCollider = col;
            ui = u;
            slotIndex = idx;
            rectCenter = center;
            rectSize = size;
        }

        public void EditorSetEmptyFrame(GameObject frame)
        {
            emptyFrame = frame;
        }
#endif
    }
}
