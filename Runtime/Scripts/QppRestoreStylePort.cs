using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    // Usage from a save/restore tool (pseudo-code; each call uses this port's fields):
    // Save: set _inPenId, _inInkId; _ResolveStyle(); read _outHasStyle / _outStyleId / _outWidthIndex.
    // Before restore: set _inPenId; _ResolveFreeRecords(); if _outFreeRecords < required runs, write no styles.
    // Include the final return-to-original run in required runs; -1 means capacity is unknown.
    // The available count reserves space for queued writes; it does not reserve space for this restore.
    // Before restore: set _inPenId; _ResolveCurrentStyle(); retain the current style and width if available.
    // Before each restored stroke: set _inPenId / _inFromInkId / _inStyleId / _inWidthIndex;
    // _AppendStyleRun(); if !_outAccepted, abandon on _outBlockedPermanent, otherwise retry next frame.
    // At the end, including early exit: _AppendStyleRun() with the retained original style and width.
    // For local restored lines: set _inLine / _inStyleId / _inWidthIndex; _ApplyStyleToLine().
    // Always pass run-start inkIds in ascending order; ledger lookup uses binary search.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QppRestoreStylePort : UdonSharpBehaviour
    {
        [SerializeField] private QppSetup setup;
        [SerializeField] private QppPenLedger[] ledgers;
        [SerializeField] private QppInkObserver observer;

        [System.NonSerialized] public int _inPenId;
        [System.NonSerialized] public int _inInkId;
        [System.NonSerialized] public int _inFromInkId;
        [System.NonSerialized] public int _inStyleId;
        [System.NonSerialized] public int _inWidthIndex;
        [System.NonSerialized] public LineRenderer _inLine;
        [System.NonSerialized] public bool _outHasStyle;
        [System.NonSerialized] public int _outStyleId;
        [System.NonSerialized] public int _outWidthIndex;
        [System.NonSerialized] public bool _outHasCurrentStyle;
        [System.NonSerialized] public int _outCurrentStyleId;
        [System.NonSerialized] public int _outCurrentWidthIndex;
        [System.NonSerialized] public bool _outAccepted;
        [System.NonSerialized] public bool _outBlockedPermanent;
        [System.NonSerialized] public int _outFreeRecords;
        [System.NonSerialized] public bool _outApplied;
        [System.NonSerialized] public bool _outReadyForWrite;
        [System.NonSerialized] public int _outQueueFree;

        private QppPenLedger FindLedger()
        {
            if (!Utilities.IsValid(setup) || ledgers == null) return null;
            int slot = setup.SlotOf(_inPenId);
            if (slot < 0 || slot >= ledgers.Length || !Utilities.IsValid(ledgers[slot])) return null;
            return ledgers[slot];
        }

        public void _ResolveStyle()
        {
            _outHasStyle = false;
            _outStyleId = 0;
            _outWidthIndex = 0;
            QppPenLedger ledger = FindLedger();
            if (!Utilities.IsValid(ledger)) return;
            int styleId;
            int widthIndex;
            if (!ledger.TryResolve(_inInkId, out styleId, out widthIndex)) return;
            _outStyleId = styleId;
            _outWidthIndex = widthIndex;
            _outHasStyle = true;
        }

        public void _ResolveCurrentStyle()
        {
            _outHasCurrentStyle = false;
            _outCurrentStyleId = 0;
            _outCurrentWidthIndex = 0;
            QppPenLedger ledger = FindLedger();
            if (!Utilities.IsValid(ledger)) return;
            QppPenState state = ledger.PenState;
            if (Utilities.IsValid(state) && state.SyncedEpoch > 0)
            {
                _outCurrentStyleId = state.SyncedStyleId;
                _outCurrentWidthIndex = state.SyncedWidthIndex;
                _outHasCurrentStyle = true;
                return;
            }

            // まだ一度も色を変えていないペンでは、PenState の同期値は 0 のままで、
            // 台帳の最初の行（Setup がペンごとに決めた既定のインク）と食い違う。
            // その場合は、台帳の最後の行＝いまの色・太さを引く。
            int styleId;
            int widthIndex;
            if (!ledger.TryResolve(int.MaxValue - 1, out styleId, out widthIndex))
            {
                styleId = ledger.DefaultStyleId;
                widthIndex = ledger.DefaultWidthIndex;
            }
            _outCurrentStyleId = styleId;
            _outCurrentWidthIndex = widthIndex;
            _outHasCurrentStyle = true;
        }

        public void _ResolveFreeRecords()
        {
            _outFreeRecords = -1;
            QppPenLedger ledger = FindLedger();
            if (Utilities.IsValid(ledger)) _outFreeRecords = ledger.AvailableRecords;
        }

        public void _AppendStyleRun()
        {
            _outAccepted = false;
            _outBlockedPermanent = false;
            _outReadyForWrite = false;
            _outQueueFree = 0;
            QppPenLedger ledger = FindLedger();
            if (!Utilities.IsValid(ledger)) return;
            _outReadyForWrite = ledger.IsReadyForWrite;
            _outQueueFree = ledger.QueueFree;
            QppPenState state = ledger.PenState;
            if (Utilities.IsValid(state) && !state._CanSetStyleQuiet())
            {
                _outBlockedPermanent = state._lastBlockWasPermanent;
                return;
            }
            if (!ledger.CanRecord(_inFromInkId))
            {
                _outBlockedPermanent = ledger.FreeRecords <= 0;
                return;
            }
            if (!_outReadyForWrite || !ledger.CanQueueAppend(_inFromInkId)) return;
            if (Utilities.IsValid(state))
                _outAccepted = state._CommitStyleFrom(_inFromInkId, _inStyleId, _inWidthIndex);
            else
            {
                ledger._Append(_inFromInkId, _inStyleId, _inWidthIndex);
                _outAccepted = true;
            }
            _outReadyForWrite = ledger.IsReadyForWrite;
            _outQueueFree = ledger.QueueFree;
        }

        public void _ApplyStyleToLine()
        {
            _outApplied = Utilities.IsValid(observer) && observer.ApplyStyleToLineForRestore(_inLine, _inStyleId, _inWidthIndex);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(QppSetup s, QppPenLedger[] l, QppInkObserver o)
        {
            setup = s;
            ledgers = l;
            observer = o;
        }
#endif
    }
}
