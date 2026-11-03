using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [DefaultExecutionOrder(100)]
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppSetup : UdonSharpBehaviour
    {
        [Header("Scene References (Baked by Editor)")]
        [SerializeField] private QvPen_PenManager[] penManagers;
        [SerializeField] private QvPen_Pen[] pens;
        [SerializeField] private QppPenBinding[] bindings;
        [SerializeField] private QppPenState[] penStates;
        [SerializeField] private QppPalette palette;

        [Header("Default Initial Styles")]
        [SerializeField] private int defaultStyleId = 0;
        [SerializeField] private int defaultWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;

        // 台帳（色・太さの切替履歴）の容量。ペン 1 本あたりのチャンク数と、1 チャンクの件数。
        // インスペクターで変え、Setup が使う（実行時には使わない）。
        [SerializeField] private int ledgerChunksPerPen = 8;
        [SerializeField] private int ledgerChunkCapacity = 512;

        private int[] registeredPenIds;
        private int registeredCount;
        private int lastPickedUpSlot = 0;

        public int LastPickedUpSlot => lastPickedUpSlot;
        public int PenCount => penManagers != null ? penManagers.Length : 0;

        public void _OnPenPickup(int slot)
        {
            lastPickedUpSlot = slot;
        }

        private void Start()
        {
            if (Utilities.IsValid(palette)) defaultWidthIndex = palette.DefaultWidthIndex;
            SendCustomEventDelayedFrames(nameof(_InitDelayed), 2);
        }

        public void _InitDelayed()
        {
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Length; i++)
                {
                    if (!Utilities.IsValid(bindings[i])) continue;

                    // 後から入った場合、初期化より先に同期値が届いていることがある。
                    // 既定値で上書きするとそのペンの色と太さが初期値に戻ってしまうため、
                    // 同期値を受け取り済みならそちらを採用する。
                    var state = (penStates != null && i < penStates.Length) ? penStates[i] : null;
                    if (Utilities.IsValid(state) && state.SyncedEpoch > 0)
                    {
                        bindings[i].ApplyLocal(state.SyncedStyleId, state.SyncedWidthIndex);
                        continue;
                    }

                    bindings[i].ApplyLocal(defaultStyleId, defaultWidthIndex);
                }
            }

            _ResolvePenIds();
        }

        public void _ResolvePenIds()
        {
            if (pens != null)
            {
                registeredCount = pens.Length;
                registeredPenIds = new int[registeredCount];
                for (int i = 0; i < registeredCount; i++)
                {
                    if (Utilities.IsValid(pens[i]))
                    {
                        registeredPenIds[i] = QvPenUtilities.Vector3ToInt32(pens[i].penIdVector);
                    }
                }
            }
        }

        public int SlotOf(int penId)
        {
            if (registeredPenIds != null)
            {
                for (int i = 0; i < registeredCount; i++)
                {
                    if (registeredPenIds[i] == penId) return i;
                }
            }

            // 初期フレーム用動的フォールバック（_InitDelayed 実行前でも penIdVector から即座に逆引き）
            if (pens != null)
            {
                for (int i = 0; i < pens.Length; i++)
                {
                    if (Utilities.IsValid(pens[i]))
                    {
                        int id = QvPenUtilities.Vector3ToInt32(pens[i].penIdVector);
                        if (id == penId) return i;
                    }
                }
            }
            return -1;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public int EditorLedgerChunksPerPen { get { return ledgerChunksPerPen; } set { ledgerChunksPerPen = value; } }
        public int EditorLedgerChunkCapacity { get { return ledgerChunkCapacity; } set { ledgerChunkCapacity = value; } }

        public void EditorSetReferences(
            QvPen_PenManager[] pms,
            QvPen_Pen[] ps,
            QppPenBinding[] bs,
            QppPenState[] states,
            QppPalette pal,
            int defStyle,
            int defWidth)
        {
            penManagers = pms;
            pens = ps;
            bindings = bs;
            penStates = states;
            palette = pal;
            defaultStyleId = defStyle;
            defaultWidthIndex = defWidth;
        }
#endif
    }
}
