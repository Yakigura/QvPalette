using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPenBinding : UdonSharpBehaviour
    {
        [Header("References (Baked by Editor)")]
        [SerializeField] private QvPen_PenManager penManager;
        [SerializeField] private QvPen_Pen pen;
        [SerializeField] private Transform penTip;
        [SerializeField] private GameObject trailRendererGO;
        [SerializeField] private VRCPickup penPickup;
        [SerializeField] private QppPalette palette;
        [SerializeField] private int penSlot;

        [Header("Bitmap Settings")]
        [SerializeField] private int styledWordCount = 4096;

        private uint[] styledBits;
        private Gradient runtimeSolidGradient;
        private GradientColorKey[] solidColorKeys;
        private GradientAlphaKey[] solidAlphaKeys;
        private bool warnedBitOverflow;
        private MaterialPropertyBlock trailBlock;

        public QvPen_PenManager PenManager => penManager;
        public QvPen_Pen Pen => pen;
        public Transform PenTip => penTip;
        public GameObject TrailRendererGO => trailRendererGO;
        public QppPalette Palette => palette;
        public int PenSlot => penSlot;

        public bool IsHeldByLocalPlayer => Utilities.IsValid(pen) && pen.IsUser;

        /// <summary>
        /// 左手で持っているか。パレットを持ち手と同じ側に出すために使う（Qvシリーズ構成 §8）。
        /// </summary>
        public bool IsHeldInLeftHand =>
            Utilities.IsValid(penPickup) && penPickup.currentHand == VRC_Pickup.PickupHand.Left;
        public bool IsHeldByAnyone      => Utilities.IsValid(pen) && pen.isHeld;
        public bool IsDrawing           => Utilities.IsValid(trailRendererGO) && trailRendererGO.activeSelf;
        public bool CanEdit             => !IsHeldByAnyone || IsHeldByLocalPlayer;

        private void EnsureGradientBuffers()
        {
            if (solidColorKeys == null)
            {
                runtimeSolidGradient = new Gradient();
                solidColorKeys = new GradientColorKey[2];
                solidAlphaKeys = new GradientAlphaKey[2];
                solidAlphaKeys[0] = new GradientAlphaKey(1f, 0f);
                solidAlphaKeys[1] = new GradientAlphaKey(1f, 1f);
            }
        }

        private void Start()
        {
            if (Utilities.IsValid(palette)) currentWidthIndex = palette.DefaultWidthIndex;
            if (styledWordCount <= 0) styledWordCount = 4096;
            if (styledBits == null) styledBits = new uint[styledWordCount];

            EnsureGradientBuffers();
        }

        public Gradient ResolveLocalGradient(int styleId)
        {
            EnsureGradientBuffers();
            if (!Utilities.IsValid(palette)) return runtimeSolidGradient;

            if (palette.HasGradient(styleId))
            {
                return palette.GradientOf(styleId);
            }

            Color c = palette.ColorOf(styleId);
            solidColorKeys[0] = new GradientColorKey(c, 0f);
            solidColorKeys[1] = new GradientColorKey(c, 1f);
            runtimeSolidGradient.SetKeys(solidColorKeys, solidAlphaKeys);
            return runtimeSolidGradient;
        }

        private int currentStyleId;
        private int currentWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;
        private int localStyleRevision;

        public int CurrentStyleId => currentStyleId;
        public int CurrentWidthIndex => currentWidthIndex;
        public int LocalStyleRevision => localStyleRevision;

        public void ApplyLocal(int styleId, int widthIndex)
        {
            currentStyleId = styleId;
            currentWidthIndex = widthIndex;
            localStyleRevision++;

            if (!Utilities.IsValid(penManager) || !Utilities.IsValid(palette)) return;

            penManager.colorGradient    = ResolveLocalGradient(styleId);
            penManager.pcInkMaterial    = palette.MaterialOf(styleId);
            penManager.questInkMaterial = palette.MaterialOf(styleId);
            penManager._SetWidth(palette.WidthOf(widthIndex));
            ApplyOutlineToTrail(styleId, widthIndex);
        }

        // 描いている最中の線（QvPen のトレイル）にもアウトラインの太さと色を反映する。
        // QvPen は _Width だけを足すので、ここで入れた値は次の描画まで残る。
        private void ApplyOutlineToTrail(int styleId, int widthIndex)
        {
            if (!Utilities.IsValid(trailRendererGO) || !palette.IsOutlined(styleId)) return;
            var trail = trailRendererGO.GetComponent<TrailRenderer>();
            if (!Utilities.IsValid(trail)) return;
            if (trailBlock == null) trailBlock = new MaterialPropertyBlock();
            trailBlock.Clear();
            trail.GetPropertyBlock(trailBlock);
            palette.ApplyOutline(trailBlock, styleId, palette.WidthOf(widthIndex));
            trail.SetPropertyBlock(trailBlock);
        }

        public bool IsStyled(int inkId)
        {
            if (inkId < 0 || styledBits == null) return false;
            int word = inkId >> 5;
            if (word >= styledBits.Length)
            {
                if (!warnedBitOverflow)
                {
                    warnedBitOverflow = true;
                    Debug.LogWarning($"[QPP] inkId {inkId} exceeded styledBits range ({styledBits.Length * 32}).");
                }
                return false;
            }
            return (styledBits[word] & (1u << (inkId & 31))) != 0;
        }

        public void MarkStyled(int inkId)
        {
            if (inkId < 0 || styledBits == null) return;
            int word = inkId >> 5;
            if (word >= styledBits.Length) return;
            styledBits[word] |= (1u << (inkId & 31));
        }

        private bool needsResweep;
        private int resweepFromInkId;

        public bool ConsumeNeedsResweep()
        {
            bool res = needsResweep;
            needsResweep = false;
            return res;
        }

        public int ConsumeResweepFromInkId()
        {
            int from = resweepFromInkId;
            needsResweep = false;
            resweepFromInkId = 0;
            return from;
        }

        public void UnmarkStyledFrom(int fromInkId)
        {
            if (fromInkId < 0) fromInkId = 0;
            if (styledBits == null) return;
            int startWord = fromInkId >> 5;
            int bit = fromInkId & 31;
            if (startWord >= styledBits.Length) return;

            uint mask = bit == 0 ? 0u : (1u << bit) - 1u;
            styledBits[startWord] &= mask;

            for (int w = startWord + 1; w < styledBits.Length; w++)
            {
                styledBits[w] = 0;
            }
            if (!needsResweep || fromInkId < resweepFromInkId) resweepFromInkId = fromInkId;
            needsResweep = true;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            QvPen_PenManager pm,
            QvPen_Pen p,
            Transform tip,
            GameObject trGO,
            VRCPickup pickup,
            QppPalette pal,
            int slot)
        {
            penManager = pm;
            pen = p;
            penTip = tip;
            trailRendererGO = trGO;
            penPickup = pickup;
            palette = pal;
            penSlot = slot;
        }
#endif
    }
}
