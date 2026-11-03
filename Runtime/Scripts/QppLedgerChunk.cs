using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace Yakigura.QvPalette
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QppLedgerChunk : UdonSharpBehaviour
    {
        [SerializeField] private int capacity = 512;
        [SerializeField] private QppInkObserver observer;
        [SerializeField] private int penSlot;

        [UdonSynced] private int[] keys;
        [UdonSynced] private int[] vals;
        [UdonSynced] private int count;

        private bool dirty;
        private bool serializeScheduled;
        private bool retryUsed;
        private int[] receivedKeys;
        private int[] receivedVals;
        private int receivedCount;

        public int Count => count;
        public int Capacity => capacity;
        public bool IsEmpty => keys == null || count == 0;
        public int FirstKey => (keys != null && count > 0) ? keys[0] : int.MaxValue;
        public int LastKey => (keys != null && count > 0) ? keys[count - 1] : -1;

        public void EnsureArrays()
        {
            if (keys == null || keys.Length < capacity)
            {
                keys = new int[capacity];
                vals = new int[capacity];
                count = 0;
            }
        }

        public void Seed(int defaultWidthIndex)
        {
            EnsureArrays();
            if (count > 0 && keys[0] == 0) return;
            keys[0] = 0;
            vals[0] = (0 << 10) | (defaultWidthIndex & 0x03FF);
            if (count == 0) count = 1;
        }

        public void _Append(int fromInkId, int styleId, int widthIndex)
        {
            EnsureArrays();
            if (count >= capacity) return;

            // In-place update if same fromInkId (prevents duplicate keys and preserves order)
            if (count > 0 && keys[count - 1] == fromInkId)
            {
                vals[count - 1] = ((styleId & 0x03FF) << 10) | (widthIndex & 0x03FF);
                return;
            }

            keys[count] = fromInkId;
            vals[count] = ((styleId & 0x03FF) << 10) | (widthIndex & 0x03FF);
            count++;
        }

        public void _MarkDirty()
        {
            dirty = true;
            if (serializeScheduled) return;
            serializeScheduled = true;
            SendCustomEventDelayedFrames(nameof(_FlushSerialization), 1);
        }

        public void _FlushSerialization()
        {
            serializeScheduled = false;
            if (!dirty) return;
            if (!Networking.IsOwner(gameObject))
            {
                serializeScheduled = true;
                SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.2f);
                return;
            }

            dirty = false;
            retryUsed = false;
            RequestSerialization();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (result.success) return;
            if (retryUsed)
            {
                Debug.LogWarning("[QPP] Chunk serialize failed twice.");
                return;
            }
            retryUsed = true;
            dirty = true;
            SendCustomEventDelayedSeconds(nameof(_FlushSerialization), 0.5f);
        }

        public override void OnDeserialization()
        {
            // A new record only changes ink from its key onward. Keep a local copy
            // because Udon has already replaced the synced arrays by this event.
            int safeCount = count;
            if (keys == null || vals == null || safeCount < 0 ||
                safeCount > keys.Length || safeCount > vals.Length)
            {
                receivedKeys = null;
                receivedVals = null;
                receivedCount = 0;
                if (Utilities.IsValid(observer)) observer._OnLedgerUpdated(penSlot);
                return;
            }

            int shared = Mathf.Min(receivedCount, safeCount);
            int changedFrom = int.MaxValue;
            for (int i = 0; i < shared; i++)
            {
                if (receivedKeys[i] != keys[i] || receivedVals[i] != vals[i])
                {
                    changedFrom = Mathf.Min(receivedKeys[i], keys[i]);
                    break;
                }
            }
            if (changedFrom == int.MaxValue && safeCount > receivedCount)
                changedFrom = keys[receivedCount];
            if (changedFrom == int.MaxValue && receivedCount > safeCount)
                changedFrom = receivedKeys[safeCount];

            if (receivedKeys == null || receivedKeys.Length < safeCount)
            {
                receivedKeys = new int[safeCount];
                receivedVals = new int[safeCount];
            }
            for (int i = 0; i < safeCount; i++)
            {
                receivedKeys[i] = keys[i];
                receivedVals[i] = vals[i];
            }
            receivedCount = safeCount;

            if (changedFrom != int.MaxValue && Utilities.IsValid(observer))
                observer._OnLedgerUpdatedFrom(penSlot, Mathf.Max(0, changedFrom));
        }

        public bool TryFindRecord(int targetInkId, out int outVal)
        {
            outVal = 0;
            if (keys == null || count == 0) return false;
            if (keys[0] > targetInkId) return false;

            int l = 0, r = count - 1, best = 0;
            while (l <= r)
            {
                int m = (l + r) / 2;
                if (keys[m] <= targetInkId)
                {
                    best = m;
                    l = m + 1;
                }
                else
                {
                    r = m - 1;
                }
            }

            outVal = vals[best];
            return true;
        }

#if !COMPILER_UDONSHARP && UNITY_EDITOR
        public void EditorSetReferences(int cap, QppInkObserver obs, int slot)
        {
            capacity = cap;
            observer = obs;
            penSlot = slot;
        }
#endif
    }
}
