using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppLedgerRegressionTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
                Object.DestroyImmediate(go);
            objects.Clear();
        }

        [Test]
        public void LateJoinerWaitsForCurrentRecordThenResolvesHistoricalStyle()
        {
            QppLedgerChunk chunk = Add<QppLedgerChunk>();
            chunk.EditorSetReferences(16, null, 0);
            chunk.Seed(128);
            chunk._Append(10, 3, 200);

            QppPenState state = Add<QppPenState>();
            SetField(state, "syncedEpoch", 1);
            SetField(state, "syncedFromInkId", 20);
            SetField(state, "syncedLedgerTotal", 3);

            QppPenLedger ledger = Add<QppPenLedger>();
            ledger.EditorSetReferences(new[] { chunk }, state, null, 0, 128);

            Assert.IsFalse(ledger.IsCaughtUp);
            Assert.IsTrue(ledger.TryResolve(15, out int oldStyle, out int oldWidth));
            Assert.AreEqual(3, oldStyle);
            Assert.AreEqual(200, oldWidth);
            Assert.IsFalse(ledger.TryResolve(25, out _, out _), "Current ink must wait for the missing ledger record.");

            chunk._Append(20, 5, 240);

            Assert.IsTrue(ledger.IsCaughtUp);
            Assert.IsTrue(ledger.TryResolve(25, out int newStyle, out int newWidth));
            Assert.AreEqual(5, newStyle);
            Assert.AreEqual(240, newWidth);
        }

        [Test]
        public void SameInkIdUpdatePreservesOrderAndPensRemainIndependent()
        {
            QppLedgerChunk first = Add<QppLedgerChunk>();
            first.EditorSetReferences(16, null, 0);
            first.Seed(128);
            first._Append(7, 2, 190);
            first._Append(7, 4, 210);

            QppLedgerChunk second = Add<QppLedgerChunk>();
            second.EditorSetReferences(16, null, 1);
            second.Seed(128);
            second._Append(7, 8, 160);

            QppPenLedger firstLedger = Add<QppPenLedger>();
            firstLedger.EditorSetReferences(new[] { first }, null, null, 0, 128);
            QppPenLedger secondLedger = Add<QppPenLedger>();
            secondLedger.EditorSetReferences(new[] { second }, null, null, 1, 128);

            Assert.AreEqual(2, first.Count, "An in-place change must not duplicate an InkId.");
            Assert.AreEqual(7, first.LastKey);
            Assert.IsTrue(firstLedger.TryResolve(9, out int firstStyle, out int firstWidth));
            Assert.IsTrue(secondLedger.TryResolve(9, out int secondStyle, out int secondWidth));
            Assert.AreEqual(4, firstStyle);
            Assert.AreEqual(210, firstWidth);
            Assert.AreEqual(8, secondStyle);
            Assert.AreEqual(160, secondWidth);
        }

        [Test]
        public void LedgerUpdateResweepsEveryPoolForOnlyItsPen()
        {
            QppPenBinding firstBinding = Add<QppPenBinding>();
            QppPenBinding secondBinding = Add<QppPenBinding>();
            SetField(firstBinding, "styledBits", new uint[2]);
            SetField(secondBinding, "styledBits", new uint[2]);
            firstBinding.MarkStyled(4);
            secondBinding.MarkStyled(4);

            QppInkObserver observer = Add<QppInkObserver>();
            Transform[] pools = { NewObject().transform, NewObject().transform, NewObject().transform };
            observer.EditorSetReferences(pools, new[] { 0, 1, 0 }, null, null,
                new[] { firstBinding, secondBinding }, null, null, null, null, null, 128);
            SetField(observer, "sweepCursor", new[] { 4, 5, 6 });
            SetField(observer, "sweeping", new[] { false, false, false });
            SetField(observer, "sweepRound", new[] { 2, 2, 2 });

            observer._OnLedgerUpdated(0);

            CollectionAssert.AreEqual(new[] { 0, 5, 0 }, GetField<int[]>(observer, "sweepCursor"));
            CollectionAssert.AreEqual(new[] { true, false, true }, GetField<bool[]>(observer, "sweeping"));
            CollectionAssert.AreEqual(new[] { 0, 2, 0 }, GetField<int[]>(observer, "sweepRound"));
            Assert.IsFalse(firstBinding.IsStyled(4));
            Assert.IsTrue(secondBinding.IsStyled(4));
        }

        [Test]
        public void TwentyPensCanTrackThirtyThousandInkIdsIndependently()
        {
            const int penCount = 20;
            const int finalInkId = 29999;
            for (int slot = 0; slot < penCount; slot++)
            {
                QppPenBinding binding = Add<QppPenBinding>();
                int words = GetField<int>(binding, "styledWordCount");
                Assert.GreaterOrEqual(words * 32, finalInkId + 1);
                SetField(binding, "styledBits", new uint[words]);

                binding.MarkStyled(finalInkId);
                binding.MarkStyled(slot);
                Assert.IsTrue(binding.IsStyled(finalInkId), $"Pen {slot}");
                Assert.IsTrue(binding.IsStyled(slot), $"Pen {slot}");
                Assert.IsFalse(binding.IsStyled((slot + 1) % penCount), $"Pen {slot}");

                QppLedgerChunk chunk = Add<QppLedgerChunk>();
                chunk.EditorSetReferences(512, null, slot);
                chunk.Seed(128);
                chunk._Append(finalInkId, slot + 1, 180 + slot);
                QppPenLedger ledger = Add<QppPenLedger>();
                ledger.EditorSetReferences(new[] { chunk }, null, binding, slot, 128);
                Assert.IsTrue(ledger.TryResolve(finalInkId, out int style, out int width));
                Assert.AreEqual(slot + 1, style);
                Assert.AreEqual(180 + slot, width);
            }
        }

        [Test]
        public void DefaultLedgerHoldsFourThousandSwitchesAcrossEightChunks()
        {
            // 既定の構成で、ペン 1 本あたり 4,096 件（8 チャンク × 512 件）まで記録できる。
            Assert.AreEqual(8, Yakigura.QvPalette.Editor.QppSceneBinder.TargetChunksPerPen);
            Assert.AreEqual(512, Yakigura.QvPalette.Editor.QppSceneBinder.TargetChunkCapacity);

            var chunks = new QppLedgerChunk[8];
            for (int c = 0; c < chunks.Length; c++)
            {
                chunks[c] = Add<QppLedgerChunk>();
                chunks[c].EditorSetReferences(512, null, 0);
            }
            QppPenLedger ledger = Add<QppPenLedger>();
            ledger.EditorSetReferences(chunks, null, null, 0, 128);
            Assert.AreEqual(4096, ledger.FreeRecords);

            // 1 本ごとに切り替えた記録で 8 チャンクを満たし、どのチャンクの区間も正しく引けること。
            chunks[0].Seed(128);
            int inkId = 1;
            for (int c = 0; c < chunks.Length; c++)
            {
                while (chunks[c].Count < chunks[c].Capacity)
                {
                    chunks[c]._Append(inkId, inkId % 1000, inkId % 700);
                    inkId++;
                }
            }
            Assert.AreEqual(4096, ledger.LocalTotalCount);
            Assert.AreEqual(0, ledger.FreeRecords);
            foreach (int probe in new[] { 1, 511, 512, 2047, 2048, 3000, 4095 })
            {
                Assert.IsTrue(ledger.TryResolve(probe, out int style, out int width), $"ink {probe}");
                Assert.AreEqual(probe % 1000, style, $"ink {probe}");
                Assert.AreEqual(probe % 700, width, $"ink {probe}");
            }
        }

        [Test]
        public void LedgerUpdateDuringSweepIsQueuedInsteadOfRestartingThePass()
        {
            QppPenBinding binding = Add<QppPenBinding>();
            SetField(binding, "styledBits", new uint[4]);
            QppInkObserver observer = Add<QppInkObserver>();
            observer.EditorSetReferences(new[] { NewObject().transform }, new[] { 0 }, null, null,
                new[] { binding }, null, null, null, null, null, 128);
            SetField(observer, "cursor", new[] { 0 });
            SetField(observer, "sweepCursor", new[] { 50 });
            SetField(observer, "sweeping", new[] { true });
            SetField(observer, "sweepAgain", new[] { false });
            SetField(observer, "sweepRound", new[] { 0 });
            SetField(observer, "sweepFromInkId", new[] { 10 });
            SetField(observer, "maxObservedInkId", new[] { 99 });
            SetField(observer, "pendingResweep", new[] { false });
            SetField(observer, "pendingFromInkId", new[] { 0 });

            observer._OnLedgerUpdatedFrom(0, 40);
            observer._OnLedgerUpdatedFrom(0, 30);

            Assert.AreEqual(50, GetField<int[]>(observer, "sweepCursor")[0], "the running pass must not restart");
            Assert.AreEqual(10, GetField<int[]>(observer, "sweepFromInkId")[0]);
            Assert.IsTrue(GetField<bool[]>(observer, "pendingResweep")[0]);
            Assert.AreEqual(30, GetField<int[]>(observer, "pendingFromInkId")[0], "queued start is the earliest change");
        }

        [Test]
        public void ReceivedChunkInvalidatesOnlyFromFirstChangedInkId()
        {
            QppPenBinding binding = Add<QppPenBinding>();
            SetField(binding, "styledBits", new uint[4]);
            QppInkObserver observer = Add<QppInkObserver>();
            observer.EditorSetReferences(new[] { NewObject().transform }, new[] { 0 }, null, null,
                new[] { binding }, null, null, null, null, null, 128);
            SetField(observer, "sweepCursor", new[] { 0 });
            SetField(observer, "sweeping", new[] { false });
            SetField(observer, "sweepAgain", new[] { false });
            SetField(observer, "sweepRound", new[] { 0 });
            SetField(observer, "sweepFromInkId", new[] { 0 });

            QppLedgerChunk chunk = Add<QppLedgerChunk>();
            chunk.EditorSetReferences(16, observer, 0);
            chunk.Seed(128);
            chunk._Append(10, 2, 180);
            chunk.OnDeserialization();

            binding.MarkStyled(5);
            binding.MarkStyled(15);
            SetField(observer, "sweeping", new[] { false });
            chunk._Append(20, 3, 200);
            chunk.OnDeserialization();
            Assert.IsTrue(binding.IsStyled(5));
            Assert.IsTrue(binding.IsStyled(15));
            Assert.AreEqual(20, GetField<int[]>(observer, "sweepFromInkId")[0]);

            binding.MarkStyled(25);
            SetField(observer, "sweeping", new[] { false });
            chunk._Append(20, 4, 210);
            chunk.OnDeserialization();
            Assert.IsTrue(binding.IsStyled(15));
            Assert.IsFalse(binding.IsStyled(25));
            Assert.AreEqual(20, GetField<int[]>(observer, "sweepFromInkId")[0]);
        }

        [Test]
        public void FutureLedgerRecordNeedsNoHistoricalResweep()
        {
            QppPenBinding binding = Add<QppPenBinding>();
            SetField(binding, "styledBits", new uint[64]);
            binding.MarkStyled(99);
            QppInkObserver observer = Add<QppInkObserver>();
            observer.EditorSetReferences(new[] { NewObject().transform }, new[] { 0 }, null, null,
                new[] { binding }, null, null, null, null, null, 128);
            SetField(observer, "cursor", new[] { 0 });
            SetField(observer, "sweepCursor", new[] { 7 });
            SetField(observer, "sweeping", new[] { false });
            SetField(observer, "sweepAgain", new[] { false });
            SetField(observer, "sweepRound", new[] { 2 });
            SetField(observer, "sweepFromInkId", new[] { 0 });
            SetField(observer, "maxObservedInkId", new[] { 99 });

            observer._OnLedgerUpdatedFrom(0, 100);
            Assert.IsTrue(binding.IsStyled(99));
            Assert.IsFalse(GetField<bool[]>(observer, "sweeping")[0]);
            Assert.AreEqual(7, GetField<int[]>(observer, "sweepCursor")[0]);
        }

        private T Add<T>() where T : Component
        {
            return NewObject().AddComponent<T>();
        }

        private GameObject NewObject()
        {
            GameObject go = new GameObject("QppRegressionFixture");
            objects.Add(go);
            return go;
        }

        private static void SetField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            return (T)field.GetValue(target);
        }
    }
}
