using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppPenLedger : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private QppLedgerChunk[] chunks;
        [SerializeField] private QppPenState penState;
        [SerializeField] private QppPenBinding binding;
        [SerializeField] private int penSlot;

        [Header("Settings")]
        [SerializeField] private int defaultWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;
        [SerializeField] private float seedGraceSeconds = 3.0f;
        [SerializeField] private float ledgerWriteGraceSeconds = 5.0f;

        private float settledAt = -1f;
        private bool isNetworkSettled;

        private const int PQ = 8;
        private int[] qFrom = new int[PQ];
        private int[] qStyle = new int[PQ];
        private int[] qWidth = new int[PQ];
        private int qCount;
        private int flushRetry;

        private int lastHitChunk = -1;
        private bool warnedLedgerFull;

        public QppLedgerChunk[] Chunks => chunks;

        public int LocalTotalCount
        {
            get
            {
                int t = 0;
                if (chunks != null)
                {
                    for (int i = 0; i < chunks.Length; i++)
                    {
                        if (Utilities.IsValid(chunks[i])) t += chunks[i].Count;
                    }
                }
                return t;
            }
        }

        public int LastKey
        {
            get
            {
                if (chunks == null) return -1;
                for (int i = chunks.Length - 1; i >= 0; i--)
                {
                    if (Utilities.IsValid(chunks[i]) && !chunks[i].IsEmpty)
                    {
                        return chunks[i].LastKey;
                    }
                }
                return -1;
            }
        }

        public bool IsCaughtUp
        {
            get
            {
                if (!Utilities.IsValid(penState)) return true;
                return LocalTotalCount >= penState.SyncedLedgerTotal;
            }
        }

        public QppPenState PenState => penState;

        public bool IsReadyForWrite
        {
            get
            {
                if (!CheckNetworkSettled()) return false;
                if (Utilities.IsValid(binding) && binding.IsHeldByLocalPlayer) return true;
                if (settledAt < 0f || Time.time - settledAt < ledgerWriteGraceSeconds) return false;
                if (Utilities.IsValid(penState) && LocalTotalCount < penState.SyncedLedgerTotal) return false;
                return true;
            }
        }

        public bool IsDelivered
        {
            get
            {
                if (chunks == null || chunks.Length == 0) return false;
                if (!Utilities.IsValid(chunks[0])) return false;
                return chunks[0].Count > 0;
            }
        }

        private bool CheckNetworkSettled()
        {
            if (isNetworkSettled) return true;
            if (Networking.IsNetworkSettled || Time.time > 2.0f)
            {
                isNetworkSettled = true;
                if (settledAt < 0f) settledAt = Time.time;
                return true;
            }
            return false;
        }

        private void Start()
        {
            // 既定の太さはパレットの設定（太さの範囲・既定の太さ）から決める。
            if (Utilities.IsValid(binding) && Utilities.IsValid(binding.Palette))
                defaultWidthIndex = binding.Palette.DefaultWidthIndex;
            SendCustomEventDelayedSeconds(nameof(_TrySeed), 1.0f);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player.isLocal)
            {
                SendCustomEventDelayedSeconds(nameof(_TrySeed), 0.5f);
            }
        }

        public QppLedgerChunk GetActiveChunk()
        {
            if (chunks == null) return null;
            for (int i = 0; i < chunks.Length; i++)
            {
                var c = chunks[i];
                if (!Utilities.IsValid(c)) continue;
                if (c.Count < c.Capacity) return c;
            }
            return null;
        }

        public QppLedgerChunk GetChunkAfter(QppLedgerChunk cur)
        {
            if (chunks == null) return null;
            for (int i = 0; i < chunks.Length - 1; i++)
            {
                if (chunks[i] == cur) return chunks[i + 1];
            }
            return null;
        }

        private void TakeOwnership(GameObject go)
        {
            if (!Utilities.IsValid(go)) return;
            if (!Networking.IsOwner(go))
            {
                Networking.SetOwner(Networking.LocalPlayer, go);
            }
        }

        public void _OnPickupByLocalPlayer()
        {
            if (!Utilities.IsValid(binding) || !binding.IsHeldByLocalPlayer) return;

            if (!CheckNetworkSettled())
            {
                SendCustomEventDelayedSeconds(nameof(_OnPickupByLocalPlayer), 0.5f);
                return;
            }

            if (Utilities.IsValid(penState))
                TakeOwnership(penState.gameObject);

            var active = GetActiveChunk();
            if (Utilities.IsValid(active))
                TakeOwnership(active.gameObject);

            if (chunks == null || chunks.Length == 0 || active != chunks[0]) return;
            if (active.Count > 0) return;
            for (int i = 1; i < chunks.Length; i++)
            {
                if (Utilities.IsValid(chunks[i]) && chunks[i].Count > 0) return;
            }
            if (Utilities.IsValid(penState) && penState.SyncedEpoch > 0) return;
            if (!Networking.IsMaster && settledAt >= 0f && (Time.time - settledAt < seedGraceSeconds))
            {
                SendCustomEventDelayedSeconds(nameof(_OnPickupByLocalPlayer), 1.0f);
                return;
            }

            active.Seed(defaultWidthIndex);
            active._MarkDirty();
        }

        public void _TrySeed()
        {
            if (!CheckNetworkSettled())
            {
                SendCustomEventDelayedSeconds(nameof(_TrySeed), 1.0f);
                return;
            }

            if (chunks == null || chunks.Length == 0) return;
            var c0 = chunks[0];
            if (!Utilities.IsValid(c0)) return;
            if (c0.Count > 0) return;

            for (int i = 1; i < chunks.Length; i++)
            {
                if (Utilities.IsValid(chunks[i]) && chunks[i].Count > 0) return;
            }
            if (Utilities.IsValid(penState) && penState.SyncedEpoch > 0) return;

            // 仕様書 §3.3.5：判定対象は「チャンクの GameObject」。
            // オーナーでなければシードせず、配送を待つ（勝手に TakeOwnership して上書きしない）
            if (!Networking.IsOwner(c0.gameObject))
            {
                SendCustomEventDelayedSeconds(nameof(_TrySeed), 5.0f);
                return;
            }

            c0.Seed(defaultWidthIndex);
            c0._MarkDirty();
        }

        /// <summary>切替記録をあと何件積めるか（送信待ちの分を差し引いた数）。</summary>
        public int FreeRecords
        {
            get
            {
                int cap = 0;
                if (chunks != null)
                {
                    for (int i = 0; i < chunks.Length; i++)
                    {
                        if (Utilities.IsValid(chunks[i])) cap += chunks[i].Capacity;
                    }
                }
                return cap - LocalTotalCount - qCount;
            }
        }

        /// <summary>
        /// この線IDからの切替を記録できるか。満杯なら false。
        /// 記録できない切替を受け付けると、描いた本人と他の人で線の色・太さが食い違う。
        /// </summary>
        public bool CanRecord(int fromInkId)
        {
            if (qCount > 0 && qFrom[qCount - 1] == fromInkId) return true;
            return FreeRecords > 0;
        }

        public bool CanQueueAppend(int fromInkId)
        {
            if (qCount < PQ) return true;
            if (qCount > 0 && qFrom[qCount - 1] == fromInkId) return true;
            return false;
        }

        public void _Append(int fromInkId, int styleId, int widthIndex)
        {
            if (qCount > 0 && qFrom[qCount - 1] == fromInkId)
            {
                qStyle[qCount - 1] = styleId;
                qWidth[qCount - 1] = widthIndex;
            }
            else if (qCount < PQ)
            {
                qFrom[qCount] = fromInkId;
                qStyle[qCount] = styleId;
                qWidth[qCount] = widthIndex;
                qCount++;
            }
            else
            {
                Debug.LogError("[QPP] Append queue overflow. Change rejected.");
                return;
            }
            flushRetry = 0;
            _FlushAppend();
        }

        public void _FlushAppend()
        {
            if (!IsReadyForWrite)
            {
                SendCustomEventDelayedSeconds(nameof(_FlushAppend), 1.0f);
                return;
            }

            while (qCount > 0)
            {
                var chunk = GetActiveChunk();
                if (!Utilities.IsValid(chunk))
                {
                    if (!warnedLedgerFull)
                    {
                        warnedLedgerFull = true;
                        Debug.LogWarning("[QPP] Ledger full. Records dropped.");
                    }
                    qCount = 0;
                    return;
                }

                TakeOwnership(chunk.gameObject);
                if (!Networking.IsOwner(chunk.gameObject))
                {
                    if (++flushRetry < 60)
                    {
                        SendCustomEventDelayedFrames(nameof(_FlushAppend), 1);
                    }
                    else
                    {
                        SendCustomEventDelayedSeconds(nameof(_FlushAppend), 0.2f);
                    }
                    return;
                }

                // Chunk 0 must ALWAYS start at inkId = 0
                if (chunk == chunks[0] && chunk.Count == 0)
                {
                    if (qFrom[0] > 0)
                    {
                        chunk.Seed(defaultWidthIndex);
                    }
                }

                chunk._Append(qFrom[0], qStyle[0], qWidth[0]);
                chunk._MarkDirty();

                if (Utilities.IsValid(penState))
                {
                    penState._UpdateSyncedLedgerTotal(LocalTotalCount);
                }

                for (int i = 1; i < qCount; i++)
                {
                    qFrom[i - 1] = qFrom[i];
                    qStyle[i - 1] = qStyle[i];
                    qWidth[i - 1] = qWidth[i];
                }
                qCount--;
                flushRetry = 0;

                if (chunk.Count >= chunk.Capacity - 1)
                {
                    var next = GetChunkAfter(chunk);
                    if (Utilities.IsValid(next)) TakeOwnership(next.gameObject);
                }
            }
        }

        public bool TryResolve(int inkId, out int styleId, out int widthIndex)
        {
            styleId = 0;
            widthIndex = defaultWidthIndex;
            if (chunks == null || chunks.Length == 0) return false;

            // ガード 1: PenState が最新区間（syncedFromInkId 以降）のスタイル変更を予告しているが、
            // 台帳チャンクがまだその区間（LastKey < syncedFromInkId）を受信していない場合、
            // 古い区間のレコードで誤解決させず、配送待ち（false）とする。
            // これにより、QppInkObserver は PenState の最新プレビュー色（CurrentStyleId）を即座に適用し、
            // チャンク配送後に正式確定できる。
            if (Utilities.IsValid(penState) && penState.SyncedEpoch > 0)
            {
                if (inkId >= penState.SyncedFromInkId && LastKey < penState.SyncedFromInkId)
                {
                    return false;
                }
            }

            // ガード 2: 台帳全体のレコード総数が PenState の SyncedLedgerTotal に追いついていない場合、
            // 最新区間のインクは配送待ちとする。
            if (Utilities.IsValid(penState) && LocalTotalCount < penState.SyncedLedgerTotal)
            {
                if (inkId >= penState.SyncedFromInkId)
                {
                    return false;
                }
            }

            if (lastHitChunk >= 0 && lastHitChunk < chunks.Length)
            {
                var cc = chunks[lastHitChunk];
                if (Utilities.IsValid(cc) && !cc.IsEmpty && cc.FirstKey <= inkId)
                {
                    var nx = GetChunkAfter(cc);
                    if (!Utilities.IsValid(nx) || nx.IsEmpty || nx.FirstKey > inkId)
                    {
                        int pk;
                        if (cc.TryFindRecord(inkId, out pk))
                        {
                            styleId = (pk >> 10) & 0x0FFF;
                            widthIndex = pk & 0x03FF;
                            return true;
                        }
                    }
                }
            }

            for (int c = chunks.Length - 1; c >= 0; c--)
            {
                var ch = chunks[c];
                if (!Utilities.IsValid(ch)) continue;
                if (ch.IsEmpty) continue;
                if (ch.FirstKey > inkId) continue;

                int packed;
                if (!ch.TryFindRecord(inkId, out packed)) continue;
                styleId = (packed >> 10) & 0x0FFF;
                widthIndex = packed & 0x03FF;
                lastHitChunk = c;
                return true;
            }

            return false;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            QppLedgerChunk[] cks,
            QppPenState ps,
            QppPenBinding b,
            int slot,
            int defWidth)
        {
            chunks = cks;
            penState = ps;
            binding = b;
            penSlot = slot;
            defaultWidthIndex = defWidth;
        }
#endif
    }
}
