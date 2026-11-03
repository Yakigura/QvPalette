using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPalette : UdonSharpBehaviour
    {
        [Header("Material Banks")]
        [SerializeField] private Material[] materialBank;
        [SerializeField] private Material[] materialBankQuest;
        [SerializeField] private bool[] materialRounded;
        [SerializeField] private bool allowRoundedOnQuest = false;

        [Header("Entries (Max 4096)")]
        [SerializeField] private int[] entryMaterialIndex;
        [SerializeField] private Color[] entryColor;
        [SerializeField] private int[] entryGradientIndex;
        [SerializeField] private Gradient[] gradientBank;
        [SerializeField] private string[] entryNames;

        [Header("Tags (Max 9, filter only; not synced)")]
        [SerializeField] private string[] tagNames;
        [Tooltip("エントリごとのタグ。ビット i が tagNames[i] に対応する。")]
        [SerializeField] private int[] entryTags;

        [Header("Outline (QvPenOutline shaders only)")]
        [Tooltip("マテリアルバンクと同じ並び。アウトライン付きシェーダーか（エディタがシェーダー名から焼く）。")]
        [SerializeField] private bool[] materialOutlined;
        [Tooltip("アウトラインの太さ。インクの太さに対する比率。")]
        [SerializeField] private float[] entryOutlineRatio;
        [Tooltip("色指定の Material（_OUTLINEUSECUSTOMCOLOR_ON）で使う色。")]
        [SerializeField] private Color[] entryOutlineColor;
        [Tooltip("線の色から作るときのずらし量（x: 色相, y: 彩度, z: 明るさ）。")]
        [SerializeField] private Vector3[] entryOutlineShift;

        [Header("インクの太さ（mm）")]
        [Tooltip("太さスライダーの左端（いちばん細い）。")]
        [SerializeField] private float inkWidthMinMillimeters = 3f;
        [Tooltip("太さスライダーの右端（いちばん太い）。")]
        [SerializeField] private float inkWidthMaxMillimeters = 150f;
        [Tooltip("最初に持ったときの太さ。最小〜最大の範囲に収める。")]
        [SerializeField] private float defaultInkWidthMillimeters = 5f;

        private bool warnedInvalidStyle;

        // ------------------------------------------------------------------
        // 太さ（番号 0〜1023 ⇔ メートル）。同期するのは番号だけで、各自のパレット設定で太さに直す。
        // ------------------------------------------------------------------

        public float MinInkWidth => Mathf.Max(inkWidthMinMillimeters, 0.1f) * 0.001f;
        public float MaxInkWidth => Mathf.Max(inkWidthMaxMillimeters * 0.001f, MinInkWidth * 1.01f);

        public float WidthOf(int widthIndex)
        {
            return QppCommon.WidthOf(widthIndex, MinInkWidth, MaxInkWidth);
        }

        public int IndexOfWidth(float width)
        {
            return QppCommon.IndexOfWidth(width, MinInkWidth, MaxInkWidth);
        }

        public int DefaultWidthIndex => IndexOfWidth(defaultInkWidthMillimeters * 0.001f);

        public string MillimetersTextOf(int widthIndex)
        {
            return QppCommon.MillimetersText(WidthOf(widthIndex));
        }

        // マテリアルごとの rounded_trail 判定キャッシュ（-1: 未判定 / 0: 通常 / 1: rounded）
        private int[] roundedCache;

        public int EntryCount => entryMaterialIndex != null ? entryMaterialIndex.Length : 0;

        public int ValidateStyleId(int styleId)
        {
            if (entryMaterialIndex == null || entryMaterialIndex.Length == 0) return 0;
            if (styleId >= 0 && styleId < entryMaterialIndex.Length) return styleId;

            if (!warnedInvalidStyle)
            {
                warnedInvalidStyle = true;
                Debug.LogWarning($"[QPP] Invalid styleId {styleId}, falling back to 0.");
            }
            return 0;
        }

        public Material MaterialOf(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            int matIdx = (entryMaterialIndex != null && styleId < entryMaterialIndex.Length) ? entryMaterialIndex[styleId] : 0;

#if UNITY_STANDALONE
            if (materialBank != null && matIdx >= 0 && matIdx < materialBank.Length)
                return materialBank[matIdx];
            return null;
#else
            Material[] bank = (materialBankQuest != null && materialBankQuest.Length > 0) ? materialBankQuest : materialBank;
            if (bank != null && matIdx >= 0 && matIdx < bank.Length)
                return bank[matIdx];
            return null;
#endif
        }

        /// <summary>
        /// rounded_trail シェーダーかどうか。
        ///
        /// このシェーダーは線幅を LineRenderer の widthMultiplier ではなくマテリアルの
        /// `_Width` から取るため、判定を誤ると「色は合っているのに太さだけマテリアル既定値になる」
        /// という症状になる。Inspector のフラグ任せにせず、QvPen 本体と同じくシェーダー名で判定する
        /// （QvPen_Pen._UpdateInkData と同じ条件）。
        /// </summary>
        public bool UseRoundedTrail(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            int matIdx = (entryMaterialIndex != null && styleId < entryMaterialIndex.Length) ? entryMaterialIndex[styleId] : 0;

#if !UNITY_STANDALONE
            if (!allowRoundedOnQuest) return false;
#endif

            if (matIdx < 0) return false;

            // Editor で焼き込まれたフラグがあれば最優先で採用（高速・Udon安全）
            if (materialRounded != null && matIdx < materialRounded.Length)
            {
                return materialRounded[matIdx];
            }

            if (roundedCache == null || roundedCache.Length <= matIdx)
            {
                int size = matIdx + 1;
                if (materialBank != null && materialBank.Length > size) size = materialBank.Length;

                int[] newCache = new int[size];
                for (int i = 0; i < size; i++)
                {
                    newCache[i] = (roundedCache != null && i < roundedCache.Length) ? roundedCache[i] : -1;
                }
                roundedCache = newCache;
            }

            if (roundedCache[matIdx] < 0)
            {
                bool rounded = false;

                Material m = MaterialOf(styleId);
                if (Utilities.IsValid(m))
                {
                    Shader sh = m.shader;
                    if (Utilities.IsValid(sh) && sh.name.Contains("rounded_trail"))
                    {
                        rounded = true;
                    }
                }

                roundedCache[matIdx] = rounded ? 1 : 0;
            }

            return roundedCache[matIdx] == 1;
        }

        public bool HasGradient(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            if (entryGradientIndex == null || styleId >= entryGradientIndex.Length) return false;
            int gIdx = entryGradientIndex[styleId];
            if (gIdx < 0 || gradientBank == null || gIdx >= gradientBank.Length) return false;
            return gradientBank[gIdx] != null;
        }

        public Gradient GradientOf(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            if (HasGradient(styleId))
            {
                return gradientBank[entryGradientIndex[styleId]];
            }
            return null;
        }

        public Color ColorOf(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            if (entryColor != null && styleId < entryColor.Length)
            {
                return entryColor[styleId];
            }
            return Color.white;
        }

        /// <summary>アウトライン付きシェーダーのインクか。Quest は QvPen がアウトラインを使わないので常に false。</summary>
        public bool IsOutlined(int styleId)
        {
#if UNITY_STANDALONE
            styleId = ValidateStyleId(styleId);
            int matIdx = (entryMaterialIndex != null && styleId < entryMaterialIndex.Length) ? entryMaterialIndex[styleId] : -1;
            return materialOutlined != null && matIdx >= 0 && matIdx < materialOutlined.Length && materialOutlined[matIdx];
#else
            return false;
#endif
        }

        /// <summary>
        /// アウトラインの太さ（インクの太さ × 比率）と色を線ごとに上書きする。
        /// 値はインク番号と太さだけから決まるので、全員の画面で同じになる（同期は不要）。
        /// </summary>
        public void ApplyOutline(MaterialPropertyBlock block, int styleId, float inkWidth)
        {
            if (!IsOutlined(styleId)) return;
            styleId = ValidateStyleId(styleId);
            if (entryOutlineRatio == null || styleId >= entryOutlineRatio.Length) return;

            block.SetFloat("_OutlineWidth", inkWidth * entryOutlineRatio[styleId]);
            if (entryOutlineColor != null && styleId < entryOutlineColor.Length)
                block.SetColor("_OutlineCustomColor", entryOutlineColor[styleId]);
            if (entryOutlineShift != null && styleId < entryOutlineShift.Length)
            {
                Vector3 s = entryOutlineShift[styleId];
                block.SetFloat("_OutlineHueDelta", s.x);
                block.SetFloat("_OutlineSaturationDelta", s.y);
                block.SetFloat("_OutlineBrightnessDelta", s.z);
            }
        }

        public const int MAX_TAGS = 9;

        public int TagCount => tagNames != null ? Mathf.Min(tagNames.Length, MAX_TAGS) : 0;

        public string TagNameOf(int tagIndex)
        {
            if (tagNames == null || tagIndex < 0 || tagIndex >= tagNames.Length) return string.Empty;
            return tagNames[tagIndex];
        }

        public bool EntryHasTag(int styleId, int tagIndex)
        {
            if (entryTags == null || styleId < 0 || styleId >= entryTags.Length) return false;
            if (tagIndex < 0 || tagIndex >= MAX_TAGS) return false;
            return (entryTags[styleId] & (1 << tagIndex)) != 0;
        }

        public string NameOf(int styleId)
        {
            styleId = ValidateStyleId(styleId);
            if (entryNames != null && styleId < entryNames.Length && !string.IsNullOrEmpty(entryNames[styleId]))
            {
                return entryNames[styleId];
            }
            return $"Style {styleId}";
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public float EditorInkWidthMinMillimeters => inkWidthMinMillimeters;
        public float EditorInkWidthMaxMillimeters => inkWidthMaxMillimeters;
        public float EditorInkWidthDefaultMillimeters => defaultInkWidthMillimeters;

        public void EditorSetInkWidthRange(float minMm, float maxMm, float defaultMm)
        {
            inkWidthMinMillimeters = minMm;
            inkWidthMaxMillimeters = maxMm;
            defaultInkWidthMillimeters = defaultMm;
        }

        public Material[] EditorMaterials => materialBank;
        public Material[] EditorMaterialsQuest => materialBankQuest;
        public bool[] EditorRounded => materialRounded;
        public bool EditorAllowRoundedOnQuest => allowRoundedOnQuest;
        public int[] EditorEntryMaterialIndex => entryMaterialIndex;
        public Color[] EditorEntryColor => entryColor;
        public int[] EditorEntryGradientIndex => entryGradientIndex;
        public Gradient[] EditorGradientBank => gradientBank;
        public string[] EditorEntryNames => entryNames;
        public string[] EditorTagNames => tagNames;
        public int[] EditorEntryTags => entryTags;
        public bool[] EditorOutlined => materialOutlined;
        public float[] EditorOutlineRatio => entryOutlineRatio;
        public Color[] EditorOutlineColor => entryOutlineColor;
        public Vector3[] EditorOutlineShift => entryOutlineShift;

        public void EditorSetOutline(bool[] outlined, float[] ratio, Color[] color, Vector3[] shift)
        {
            materialOutlined = outlined;
            entryOutlineRatio = ratio;
            entryOutlineColor = color;
            entryOutlineShift = shift;
        }

        public void EditorSetTags(string[] names, int[] masks)
        {
            tagNames = names;
            entryTags = masks;
        }

        public void EditorSetData(
            Material[] mats,
            Material[] matsQuest,
            bool[] rounded,
            int[] entryMats,
            Color[] colors,
            int[] entryGradients,
            Gradient[] gradients,
            string[] names)
        {
            materialBank = mats;
            materialBankQuest = matsQuest;
            materialRounded = rounded;
            entryMaterialIndex = entryMats;
            entryColor = colors;
            entryGradientIndex = entryGradients;
            gradientBank = gradients;
            entryNames = names;
        }
#endif
    }
}
