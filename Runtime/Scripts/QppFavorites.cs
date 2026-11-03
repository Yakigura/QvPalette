using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Persistence;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// お気に入り 8 スロット（色 + 太さ）。
    /// お気に入りはペンの設定と違い「個人のもの」なので同期しない。
    /// 永続化は VRChat Persistence（PlayerData）で同一ワールド内のみ行う。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppFavorites : UdonSharpBehaviour
    {
        public const int SLOT_COUNT = 8;

        [SerializeField] private QppPalette palette;
        [SerializeField] private string keyPrefix = "QPP_FAV_";
        [SerializeField] private bool usePersistence = true;
        // 操作のログ（クリック、バインドなど）を出す。公開版の既定はオフ。警告とエラーは常に出る。
        [SerializeField] private bool debugLog = false;

        private int[] styleIds;
        private int[] widthIndices;
        private bool[] used;
        private bool restored;

        // ペンの大きさ（個人の好み）。未設定（< 0）の間はアバターの目の高さから決める。
        private const string PenScaleKey = "PEN_SCALE";
        private float penScale = -1f;

        public int SlotCount => SLOT_COUNT;
        public bool IsRestored => restored;

        private void Start()
        {
            EnsureBuffers();
            _RestoreLocal();
        }

        private void EnsureBuffers()
        {
            if (styleIds == null || styleIds.Length != SLOT_COUNT)
            {
                styleIds = new int[SLOT_COUNT];
                widthIndices = new int[SLOT_COUNT];
                used = new bool[SLOT_COUNT];
                for (int i = 0; i < SLOT_COUNT; i++)
                {
                    styleIds[i] = 0;
                    widthIndices[i] = QppCommon.DEFAULT_WIDTH_INDEX;
                    used[i] = false;
                }
            }
        }

        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            _RestoreLocal();
        }

        public void _RestoreLocal()
        {
            EnsureBuffers();
            if (!usePersistence) return;

            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return;

            int entryCount = Utilities.IsValid(palette) ? palette.EntryCount : 0;

            for (int i = 0; i < SLOT_COUNT; i++)
            {
                string key = KeyOf(i);
                int packed;
                if (!PlayerData.TryGetInt(localPlayer, key, out packed)) continue;
                if (packed < 0) continue;

                int styleId = packed >> 16;
                int widthIndex = packed & 0xFFFF;

                // ワールド制作者がパレット構成を変更するとインデックスがずれるため、
                // 範囲外のスロットは読み捨てる（§8）。
                if (entryCount > 0 && styleId >= entryCount) continue;
                if (widthIndex < 0 || widthIndex > QppCommon.MAX_WIDTH_INDEX) continue;

                styleIds[i] = styleId;
                widthIndices[i] = widthIndex;
                used[i] = true;
            }

            float savedScale;
            if (PlayerData.TryGetFloat(localPlayer, keyPrefix + PenScaleKey, out savedScale) && savedScale > 0f)
                penScale = QppCommon.ClampPenScale(savedScale);

            restored = true;
        }

        private string KeyOf(int slot)
        {
            return keyPrefix + slot;
        }

        public bool IsUsed(int slot)
        {
            EnsureBuffers();
            if (slot < 0 || slot >= SLOT_COUNT) return false;
            return used[slot];
        }

        public int StyleOf(int slot)
        {
            EnsureBuffers();
            if (slot < 0 || slot >= SLOT_COUNT) return 0;
            return styleIds[slot];
        }

        public int WidthOf(int slot)
        {
            EnsureBuffers();
            if (slot < 0 || slot >= SLOT_COUNT) return QppCommon.DEFAULT_WIDTH_INDEX;
            return widthIndices[slot];
        }

        public bool HasSavedPenScale => penScale > 0f;

        /// <summary>自分が持つペンの大きさ。スライダーで決めた値、なければアバターの大きさから。</summary>
        public float PenScale => penScale > 0f ? penScale : AutoPenScale();

        public float AutoPenScale()
        {
            var localPlayer = Networking.LocalPlayer;
            if (!Utilities.IsValid(localPlayer)) return 1f;
            float eye = localPlayer.GetAvatarEyeHeightAsMeters();
            if (eye <= 0f) return 1f;
            return QppCommon.SnapPenScale(eye / QppCommon.PEN_SCALE_REFERENCE_EYE_HEIGHT);
        }

        public void _SavePenScale(float scale)
        {
            penScale = QppCommon.ClampPenScale(scale);
            if (usePersistence) PlayerData.SetFloat(keyPrefix + PenScaleKey, penScale);
        }

        public void _Save(int slot, int styleId, int widthIndex)
        {
            EnsureBuffers();
            if (slot < 0 || slot >= SLOT_COUNT) return;
            if (styleId < 0) styleId = 0;
            widthIndex = Mathf.Clamp(widthIndex, 0, QppCommon.MAX_WIDTH_INDEX);

            styleIds[slot] = styleId;
            widthIndices[slot] = widthIndex;
            used[slot] = true;

            if (usePersistence)
            {
                PlayerData.SetInt(KeyOf(slot), (styleId << 16) | widthIndex);
            }

            if (debugLog) Debug.Log($"[QPP] Favorite slot {slot} saved (style={styleId}, width={widthIndex}).");
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(QppPalette pal, string prefix, bool persistence)
        {
            palette = pal;
            keyPrefix = prefix;
            usePersistence = persistence;
        }
#endif
    }
}
