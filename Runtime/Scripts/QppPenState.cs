using QvPen.UdonScript;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QppPenState : UdonSharpBehaviour
    {
        [Header("References")]
        [SerializeField] private QppPenBinding binding;
        [SerializeField] private QppPenLedger ledger;
        [SerializeField] private QppPaletteUI[] uis;
        [SerializeField] private QvPen_PenManager penManager;
        [SerializeField] private float dropCooldownSeconds = 2.0f;
        [SerializeField] private int penSlot;

        [UdonSynced] private int syncedStyleId;
        [UdonSynced] private int syncedWidthIndex;
        [UdonSynced] private int syncedFromInkId;
        [UdonSynced] private int syncedEpoch;
        [UdonSynced] private int syncedLedgerTotal;
        [UdonSynced] private int syncedLastKnownInkId;

        private float droppedAt = -100f;
        private float lastSerializeTime = -1f;
        private bool serializeScheduled;
        private bool retryUsed;

        private bool hasPendingStyle;
        private int pendingStyleId;
        private int pendingWidthIndex;
        private bool pendingRetryScheduled;

        public int SyncedStyleId => syncedStyleId;
        public int SyncedWidthIndex => syncedWidthIndex;
        public int SyncedFromInkId => syncedFromInkId;
        public int SyncedEpoch => syncedEpoch;
        public QppPenLedger Ledger => ledger;
        public int SyncedLedgerTotal => syncedLedgerTotal;
        public int SyncedLastKnownInkId => syncedLastKnownInkId;

        /// <summary>
        /// パレットは 1 枚を使い回すため、このペンにバインドしているパネルだけが反応する。
        /// </summary>
        private void NotifyBusy()
        {
            if (uis == null) return;
            for (int i = 0; i < uis.Length; i++)
            {
                if (Utilities.IsValid(uis[i])) uis[i]._ShowSyncBusyFor(penSlot);
            }
        }

        private void NotifyLedgerFull()
        {
            if (uis == null) return;
            for (int i = 0; i < uis.Length; i++)
            {
                if (Utilities.IsValid(uis[i])) uis[i]._ShowLedgerFullFor(penSlot);
            }
        }

        /// <summary>台帳が満杯で、このペンの色・太さをこれ以上切り替えられないか。</summary>
        public bool IsLedgerFull => Utilities.IsValid(ledger) && !ledger.CanRecord(_ResolveFromInkId());

        private void NotifySelection(int styleId, int widthIndex)
        {
            if (uis == null) return;
            for (int i = 0; i < uis.Length; i++)
            {
                if (Utilities.IsValid(uis[i])) uis[i]._OnPenStateChanged(penSlot, styleId, widthIndex);
            }
        }

        public void OnPenDropByLocalPlayer()
        {
            droppedAt = Time.time;
            if (Networking.IsOwner(gameObject) && Utilities.IsValid(penManager))
            {
                syncedLastKnownInkId = penManager.InkId;
                RequestSerializationThrottled();
            }
        }

        public bool _CanSetStyle()
        {
            if (Utilities.IsValid(binding) && !binding.CanEdit) return false;
            if (IsLedgerFull)
            {
                NotifyLedgerFull();
                return false;
            }
            if (Time.time - droppedAt < dropCooldownSeconds)
            {
                NotifyBusy();
                return false;
            }
            if (Utilities.IsValid(ledger) && !ledger.CanQueueAppend(_ResolveFromInkId()))
            {
                NotifyBusy();
                return false;
            }
            return true;
        }

        public int _ResolveFromInkId()
        {
            if (!Utilities.IsValid(penManager)) return 0;
            if (Utilities.IsValid(binding) && binding.IsHeldByLocalPlayer)
                return penManager.InkId;
            return Mathf.Max(penManager.InkId, syncedLastKnownInkId);
        }

        public bool _SetStyle(int styleId, int widthIndex)
        {
            if (!_CanSetStyle())
            {
                // Remote ownership must not be retried indefinitely. Cooldown/ledger
                // backpressure is transient, so retain only the latest local request.
                // A full ledger is permanent, so that request is dropped instead.
                if ((!Utilities.IsValid(binding) || binding.CanEdit) && !IsLedgerFull)
                {
                    hasPendingStyle = true;
                    pendingStyleId = styleId;
                    pendingWidthIndex = widthIndex;
                    if (!pendingRetryScheduled)
                    {
                        pendingRetryScheduled = true;
                        SendCustomEventDelayedSeconds(nameof(_RetryPendingStyle), dropCooldownSeconds + 0.1f);
                    }
                }
                return false;
            }

            // Immediate local update for responsive UX
            if (Utilities.IsValid(binding))
                binding.ApplyLocal(styleId, widthIndex);
            NotifySelection(styleId, widthIndex);

            hasPendingStyle = false;
            CommitStyle(styleId, widthIndex);
            return true;
        }

        public void _RetryPendingStyle()
        {
            pendingRetryScheduled = false;
            if (!hasPendingStyle) return;
            if ((Utilities.IsValid(binding) && !binding.CanEdit) || IsLedgerFull)
            {
                hasPendingStyle = false;
                return;
            }
            if (!_CanSetStyle())
            {
                pendingRetryScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_RetryPendingStyle), 0.5f);
                return;
            }

            int styleId = pendingStyleId;
            int widthIndex = pendingWidthIndex;
            hasPendingStyle = false;
            if (Utilities.IsValid(binding))
                binding.ApplyLocal(styleId, widthIndex);
            NotifySelection(styleId, widthIndex);
            CommitStyle(styleId, widthIndex);
        }

        private void CommitStyle(int styleId, int widthIndex)
        {

            if (!Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }

            syncedStyleId = styleId;
            syncedWidthIndex = widthIndex;
            syncedFromInkId = _ResolveFromInkId();
            syncedEpoch++;

            if (Utilities.IsValid(ledger))
            {
                ledger._Append(syncedFromInkId, styleId, widthIndex);
                syncedLedgerTotal = ledger.LocalTotalCount;
            }

            RequestSerializationThrottled();
        }

        public void _UpdateSyncedLedgerTotal(int total)
        {
            if (syncedLedgerTotal != total)
            {
                syncedLedgerTotal = total;
                if (Networking.IsOwner(gameObject))
                {
                    RequestSerializationThrottled();
                }
            }
        }

        public void RequestSerializationThrottled()
        {
            if (!Networking.IsOwner(gameObject))
            {
                if (!serializeScheduled)
                {
                    serializeScheduled = true;
                    SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.2f);
                }
                return;
            }

            float now = Time.time;
            if (now - lastSerializeTime >= 0.2f && !serializeScheduled)
            {
                lastSerializeTime = now;
                RequestSerialization();
            }
            else if (!serializeScheduled)
            {
                serializeScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.2f);
            }
        }

        public void _FlushSerialization()
        {
            serializeScheduled = false;
            if (!Networking.IsOwner(gameObject))
            {
                serializeScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.2f);
                return;
            }

            lastSerializeTime = Time.time;
            retryUsed = false;
            RequestSerialization();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success) return;
            if (retryUsed)
            {
                Debug.LogWarning("[QPP] PenState serialize failed twice.");
                return;
            }
            retryUsed = true;
            SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.5f);
        }

        public override void OnDeserialization()
        {
            if (Utilities.IsValid(binding))
            {
                binding.ApplyLocal(syncedStyleId, syncedWidthIndex);
                binding.UnmarkStyledFrom(syncedFromInkId);
            }
            NotifySelection(syncedStyleId, syncedWidthIndex);
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(
            QppPenBinding b,
            QppPenLedger l,
            QppPaletteUI[] us,
            QvPen_PenManager pm,
            int slot)
        {
            binding = b;
            ledger = l;
            uis = us;
            penManager = pm;
            penSlot = slot;
        }
#endif
    }
}
