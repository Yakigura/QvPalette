using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppInkObserver : UdonSharpBehaviour
    {
        [Header("Scene Baked Pools")]
        [SerializeField] private Transform[] pools;
        [SerializeField] private int[] poolPenSlot;

        [Header("Layer References")]
        [SerializeField] private QppSetup setup;
        [SerializeField] private QppPenLedger[] ledgers;
        [SerializeField] private QppPenBinding[] bindings;
        [SerializeField] private QppPalette palette;

        [Header("UI References (Driven by Observer)")]
        [SerializeField] private QppMenuToggle[] menuToggles;
        [SerializeField] private QppPointerProbe[] pointerProbes;
        [SerializeField] private QppPaletteUI[] paletteUIs;
        [SerializeField] private QppPenPreview[] penPreviews;

        [Header("Settings")]
        [SerializeField] private int defaultWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;
        [SerializeField] private bool hideUntilResolved = false;
        [SerializeField] private bool rebakeCollider = false;

        private int[] cursor;
        private int[] sweepCursor;
        private int[] lastChildCount;
        private bool[] sweeping;
        private bool[] sweepAgain;
        private int[] sweepRound;
        private int[] lastTailInkId;
        private bool[] lastTailValid;
        private int[] sweepFromInkId;
        private int[] maxObservedInkId;
        // A ledger update that arrives mid-pass is queued here instead of restarting the pass.
        // Restarting from child 0 on every update starved large pools while someone kept drawing.
        private bool[] pendingResweep;
        private int[] pendingFromInkId;

        private bool isNetworkSettled = false;
        private MaterialPropertyBlock propertyBlock;

        private Gradient runtimeSolidGradient;
        private GradientColorKey[] solidColorKeys;
        private GradientAlphaKey[] solidAlphaKeys;

        private void Start()
        {
            if (Utilities.IsValid(palette)) defaultWidthIndex = palette.DefaultWidthIndex;
            int n = (pools != null) ? pools.Length : 0;
            cursor = new int[n];
            sweepCursor = new int[n];
            lastChildCount = new int[n];
            sweeping = new bool[n];
            sweepAgain = new bool[n];
            sweepRound = new int[n];
            lastTailInkId = new int[n];
            lastTailValid = new bool[n];
            sweepFromInkId = new int[n];
            maxObservedInkId = new int[n];
            pendingResweep = new bool[n];
            pendingFromInkId = new int[n];
            for (int i = 0; i < n; i++) maxObservedInkId[i] = -1;

            propertyBlock = new MaterialPropertyBlock();

            runtimeSolidGradient = new Gradient();
            solidColorKeys = new GradientColorKey[2];
            solidAlphaKeys = new GradientAlphaKey[2];
            solidAlphaKeys[0] = new GradientAlphaKey(1f, 0f);
            solidAlphaKeys[1] = new GradientAlphaKey(1f, 1f);
        }

        public void _OnLedgerUpdated(int penSlot)
        {
            _OnLedgerUpdatedFrom(penSlot, 0);
        }

        public void _OnLedgerUpdatedFrom(int penSlot, int fromInkId)
        {
            if (fromInkId < 0) fromInkId = 0;
            if (bindings != null && penSlot >= 0 && penSlot < bindings.Length && Utilities.IsValid(bindings[penSlot]))
            {
                bindings[penSlot].UnmarkStyledFrom(fromInkId);
                // Preserve an earlier pending invalidation from PenState.
                fromInkId = bindings[penSlot].ConsumeResweepFromInkId();
            }

            RequestResweepForSlot(penSlot, fromInkId);
        }

        private void RequestResweepForSlot(int penSlot, int fromInkId)
        {
            if (pools == null || sweepCursor == null) return;
            for (int i = 0; i < pools.Length; i++)
            {
                if (poolPenSlot[i] != penSlot) continue;
                int poolFromInkId = fromInkId;
                int count = Utilities.IsValid(pools[i]) ? pools[i].childCount : 0;
                if (!sweeping[i] && cursor != null && cursor[i] >= count &&
                    maxObservedInkId != null && poolFromInkId > maxObservedInkId[i]) continue;
                if (sweeping[i] && sweepCursor[i] > 0 && pendingResweep != null)
                {
                    // Let the current pass finish; inks after the cursor already read the new ledger.
                    pendingFromInkId[i] = pendingResweep[i] ? Mathf.Min(pendingFromInkId[i], poolFromInkId) : poolFromInkId;
                    pendingResweep[i] = true;
                    continue;
                }
                if (sweeping[i] && sweepFromInkId != null)
                    poolFromInkId = Mathf.Min(poolFromInkId, sweepFromInkId[i]);
                sweepCursor[i] = 0;
                sweeping[i] = true;
                if (sweepAgain != null) sweepAgain[i] = false;
                sweepRound[i] = 0;
                if (sweepFromInkId != null) sweepFromInkId[i] = poolFromInkId;
            }
        }

        private int GetInkIdFromGO(GameObject inkGO)
        {
            if (Utilities.IsValid(inkGO) && QvPenUtilities.TryGetIdFromInk(inkGO, out var _pen, out var inkIdV, out var _own))
            {
                return QvPenUtilities.Vector3ToInt32(inkIdV);
            }
            return -1;
        }

        public int GetMaxInkId(int penSlot)
        {
            int maxId = -1;
            if (pools != null)
            {
                for (int i = 0; i < pools.Length; i++)
                {
                    if (poolPenSlot[i] == penSlot && Utilities.IsValid(pools[i]))
                    {
                        int count = pools[i].childCount;
                        for (int c = 0; c < count; c++)
                        {
                            Transform child = pools[i].GetChild(c);
                            if (Utilities.IsValid(child))
                            {
                                int id = GetInkIdFromGO(child.gameObject);
                                if (id > maxId) maxId = id;
                            }
                        }
                    }
                }
            }
            return maxId;
        }

        public override void PostLateUpdate()
        {
            if (!isNetworkSettled && (Networking.IsNetworkSettled || Time.time > 2.0f))
            {
                isNetworkSettled = true;
            }

            // 1. Drive UI elements from PostLateUpdate
            // 表示はペンの保持状態で決まるため、全ペン分を毎フレーム見る。
            // 両手で 2 本持てば 2 枚のパレットが同時に出る（Qvシリーズ構成 §8）。
            if (menuToggles != null)
            {
                for (int i = 0; i < menuToggles.Length; i++)
                {
                    if (Utilities.IsValid(menuToggles[i]))
                    {
                        menuToggles[i]._PollInput();
                    }
                }
            }

            // Pickup の移動後に追従させ、ポインターは最終姿勢で判定する。
            // パネルの移動と角度調整は PaletteTool だけが行う。
            if (paletteUIs != null)
            {
                for (int i = 0; i < paletteUIs.Length; i++)
                {
                    if (Utilities.IsValid(paletteUIs[i]) && Utilities.IsValid(paletteUIs[i].PaletteTool))
                        paletteUIs[i].PaletteTool._FollowHeldPanel();
                }
            }

            if (pointerProbes != null)
            {
                for (int i = 0; i < pointerProbes.Length; i++)
                {
                    if (Utilities.IsValid(pointerProbes[i]))
                    {
                        pointerProbes[i]._UpdateProbe();
                    }
                }
            }

            if (paletteUIs != null)
            {
                for (int i = 0; i < paletteUIs.Length; i++)
                {
                    if (Utilities.IsValid(paletteUIs[i]))
                    {
                        paletteUIs[i]._PollBusyStatus();
                    }
                }
            }

            // ペン追従インクプレビュー（§8）。ペンの状態を示す唯一の手段なので常時更新する。
            if (penPreviews != null)
            {
                for (int i = 0; i < penPreviews.Length; i++)
                {
                    if (Utilities.IsValid(penPreviews[i]))
                    {
                        penPreviews[i]._UpdatePreview();
                    }
                }
            }

            // 2. Ink Observer pool loops
            if (pools == null) return;

            for (int i = 0; i < pools.Length; i++)
            {
                Transform pool = pools[i];
                if (!Utilities.IsValid(pool)) continue;

                int penSlot = poolPenSlot[i];
                int count = pool.childCount;

                // Structure change detection (§3.4)
                bool structureChanged = false;
                if (bindings != null && penSlot >= 0 && penSlot < bindings.Length && Utilities.IsValid(bindings[penSlot]))
                {
                    if (bindings[penSlot].ConsumeNeedsResweep())
                    {
                        RequestResweepForSlot(penSlot, bindings[penSlot].ConsumeResweepFromInkId());
                    }
                }

                if (count < lastChildCount[i])
                {
                    structureChanged = true;
                }
                else if (count > 0)
                {
                    int tail = GetInkIdFromGO(pool.GetChild(count - 1).gameObject);
                    if (lastTailValid[i])
                    {
                        if (count == lastChildCount[i] && tail != lastTailInkId[i]) structureChanged = true;
                        if (count > lastChildCount[i] && tail <= lastTailInkId[i]) structureChanged = true;
                    }
                    lastTailInkId[i] = tail;
                    lastTailValid[i] = true;
                }

                if (structureChanged)
                {
                    if (cursor[i] > count) cursor[i] = count;
                    sweepCursor[i] = 0;
                    sweeping[i] = true;
                    sweepRound[i] = 0;
                    if (sweepFromInkId != null) sweepFromInkId[i] = 0;
                    if (pendingResweep != null) pendingResweep[i] = false;
                }

                // Forward sweep budget
                int backlog = count - cursor[i];
                int budget;
                if (!isNetworkSettled) budget = 8;
                else if (backlog >= 64) budget = 128;
                else budget = 32;

                while (cursor[i] < count && budget > 0)
                {
                    GameObject inkGO = pool.GetChild(cursor[i]).gameObject;
                    bool resolved = ApplyStyleToInk(inkGO, penSlot, sweepRound[i], i);
                    if (!resolved)
                    {
                        sweeping[i] = true;
                    }
                    cursor[i]++;
                    budget--;
                }

                // Resweep loop
                bool ledgerReady = isNetworkSettled && (ledgers != null && penSlot < ledgers.Length && Utilities.IsValid(ledgers[penSlot]) && ledgers[penSlot].IsDelivered);
                if (ledgerReady && sweeping[i] && count > 0)
                {
                    int scanBudget = 64;
                    int styleBudget = 8;
                    while (scanBudget > 0 && styleBudget > 0 && sweepCursor[i] < count)
                    {
                        GameObject inkGO = pool.GetChild(sweepCursor[i]).gameObject;
                        int inkId = GetInkIdFromGO(inkGO);
                        if (inkId >= (sweepFromInkId != null ? sweepFromInkId[i] : 0))
                        {
                            bool res = ApplyStyleToInk(inkGO, penSlot, sweepRound[i], i);
                            if (!res) sweepAgain[i] = true;
                            styleBudget--;
                        }
                        sweepCursor[i]++;
                        scanBudget--;
                    }

                    if (sweepCursor[i] >= count)
                    {
                        sweepCursor[i] = 0;
                        sweepRound[i]++;
                        if (pendingResweep != null && pendingResweep[i])
                        {
                            // Start the queued pass. Keep the older start point if this pass also left work.
                            int from = pendingFromInkId[i];
                            if (sweepAgain[i] && sweepFromInkId != null) from = Mathf.Min(from, sweepFromInkId[i]);
                            if (sweepFromInkId != null) sweepFromInkId[i] = from;
                            pendingResweep[i] = false;
                            sweepAgain[i] = false;
                            sweepRound[i] = 0;
                        }
                        else if (sweepAgain[i])
                        {
                            sweepAgain[i] = false;
                        }
                        else
                        {
                            sweeping[i] = false;
                        }
                    }
                }

                // Unconditional update of lastChildCount (§3.4 Item 0 & 6)
                lastChildCount[i] = count;
            }
        }

        private bool ApplyStyleToInk(GameObject inkGO, int penSlot, int round, int poolIndex)
        {
            if (!Utilities.IsValid(inkGO)) return true;
            if (!QvPenUtilities.TryGetIdFromInk(inkGO, out var _pen, out var inkIdV, out var _own)) return true;

            int inkId = QvPenUtilities.Vector3ToInt32(inkIdV);
            if (maxObservedInkId != null && poolIndex >= 0 && poolIndex < maxObservedInkId.Length &&
                inkId > maxObservedInkId[poolIndex]) maxObservedInkId[poolIndex] = inkId;

            // Resolve exact penSlot from ink's pen ID vector
            int actualPenId = QvPenUtilities.Vector3ToInt32(_pen);
            if (Utilities.IsValid(setup) && bindings != null)
            {
                int matchedSlot = setup.SlotOf(actualPenId);
                if (matchedSlot >= 0 && matchedSlot < bindings.Length)
                {
                    penSlot = matchedSlot;
                }
            }

            if (penSlot < 0 || bindings == null || penSlot >= bindings.Length || !Utilities.IsValid(bindings[penSlot]))
            {
                return true;
            }

            var binding = bindings[penSlot];
            if (binding.IsStyled(inkId)) return true;

            int styleId = 0;
            int widthIndex = defaultWidthIndex;
            bool resolved = false;
            bool fromLedger = false;

            if (Utilities.IsValid(binding) && binding.IsHeldByLocalPlayer)
            {
                // 自分が描いた線：最新のローカル選択値を即座に適用（遅延ゼロ）
                styleId = binding.CurrentStyleId;
                widthIndex = binding.CurrentWidthIndex;
                resolved = true;
                fromLedger = true;
            }
            else
            {
                // リモートプレイヤー描画：台帳（Ledger）から区間ログを解決
                var ledger = (ledgers != null && penSlot < ledgers.Length) ? ledgers[penSlot] : null;
                if (Utilities.IsValid(ledger) && ledger.TryResolve(inkId, out styleId, out widthIndex))
                {
                    resolved = true;
                    fromLedger = true;
                }
                else
                {
                    // 台帳データ（チャンク）配送待ち:
                    // 早すぎる確定で styleId 0 (黒) や defaultWidthIndex (初期太さ) に固定化されるのを防ぐため、
                    // 台帳レコードから解決できるまで MarkStyled による確定は行わず、未解決 (false) として再走査を待機する。
                    // ただし、もし binding が最新の同期値を持っていれば、一時プレビューとして適用して見栄えを保つ。
                    if (Utilities.IsValid(binding))
                    {
                        styleId = binding.CurrentStyleId;
                        widthIndex = binding.CurrentWidthIndex;
                    }
                    else
                    {
                        styleId = 0;
                        widthIndex = defaultWidthIndex;
                    }

                    if (hideUntilResolved)
                    {
                        var lr = inkGO.GetComponent<LineRenderer>();
                        if (Utilities.IsValid(lr)) lr.enabled = false;
                        return false;
                    }

                    resolved = false;
                    fromLedger = false;
                }
            }

            var line = inkGO.GetComponent<LineRenderer>();
            if (Utilities.IsValid(line))
            {
                if (hideUntilResolved) line.enabled = true;

                if (Utilities.IsValid(palette))
                {
                    line.sharedMaterial = palette.MaterialOf(styleId);

                    if (palette.HasGradient(styleId))
                    {
                        var grad = palette.GradientOf(styleId);
                        if (grad != null) line.colorGradient = grad;
                    }
                    else
                    {
                        Color c = palette.ColorOf(styleId);
                        if (solidColorKeys == null)
                        {
                            runtimeSolidGradient = new Gradient();
                            solidColorKeys = new GradientColorKey[2];
                            solidAlphaKeys = new GradientAlphaKey[2];
                            solidAlphaKeys[0] = new GradientAlphaKey(1f, 0f);
                            solidAlphaKeys[1] = new GradientAlphaKey(1f, 1f);
                        }
                        solidColorKeys[0] = new GradientColorKey(c, 0f);
                        solidColorKeys[1] = new GradientColorKey(c, 1f);
                        runtimeSolidGradient.SetKeys(solidColorKeys, solidAlphaKeys);
                        line.colorGradient = runtimeSolidGradient;
                    }

                    float w = palette.WidthOf(widthIndex);
                    bool isRounded = palette.UseRoundedTrail(styleId);
                    line.widthMultiplier = isRounded ? 0f : w;

                    propertyBlock.Clear();
                    line.GetPropertyBlock(propertyBlock);
                    propertyBlock.SetFloat("_Width", w);
                    palette.ApplyOutline(propertyBlock, styleId, w);
                    line.SetPropertyBlock(propertyBlock);

                    if (rebakeCollider)
                    {
                        var col = inkGO.GetComponent<MeshCollider>();
                        if (Utilities.IsValid(col))
                        {
                            Mesh m = new Mesh();
                            line.BakeMesh(m, true);
                            col.sharedMesh = m;
                        }
                    }
                }
            }

            if (fromLedger)
            {
                bool canMark = false;
                if (Utilities.IsValid(binding) && binding.IsHeldByLocalPlayer)
                {
                    canMark = true;
                }
                else
                {
                    var ledger = (ledgers != null && penSlot < ledgers.Length) ? ledgers[penSlot] : null;
                    int lastKey = Utilities.IsValid(ledger) ? ledger.LastKey : -1;
                    bool ledgerCaughtUp = Utilities.IsValid(ledger) && ledger.IsCaughtUp;

                    if (inkId < lastKey && ledgerCaughtUp)
                    {
                        canMark = true;
                    }
                    else if (ledgerCaughtUp && Utilities.IsValid(binding) && !binding.IsHeldByAnyone)
                    {
                        var state = Utilities.IsValid(ledger) ? ledger.PenState : null;
                        bool hasPendingStyle = Utilities.IsValid(state) && state.SyncedEpoch > 0 && lastKey < state.SyncedFromInkId;
                        if (!hasPendingStyle)
                        {
                            canMark = true;
                        }
                    }
                }

                if (canMark)
                {
                    binding.MarkStyled(inkId);
                }
                else
                {
                    resolved = false;
                }
            }
            return resolved;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            Transform[] ps,
            int[] slots,
            QppSetup s,
            QppPenLedger[] ls,
            QppPenBinding[] bs,
            QppPalette pal,
            QppMenuToggle[] mts,
            QppPointerProbe[] pps,
            QppPaletteUI[] puis,
            QppPenPreview[] previews,
            int defWidth)
        {
            pools = ps;
            poolPenSlot = slots;
            setup = s;
            ledgers = ls;
            bindings = bs;
            palette = pal;
            menuToggles = mts;
            pointerProbes = pps;
            paletteUIs = puis;
            penPreviews = previews;
            defaultWidthIndex = defWidth;
        }
#endif
    }
}
