using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    /// <summary>
    /// パレットとペンの紐づけ:
    /// 入室直後はどのペンにも紐づけない / 紐づいていない間に選んだものは覚えておき、次に持ったペンへ反映する /
    /// 反映はそのペンの台帳が届いてから（途中入室直後に低すぎるインク番号から記録しないため）。
    /// </summary>
    public class QppPaletteBindingTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects) Object.DestroyImmediate(go);
            objects.Clear();
        }

        [Test]
        public void SelectionWhileUnboundIsRememberedAndAppliedToTheNextPen()
        {
            var ui = MakeUi(out QppPenBinding[] bindings);
            Assert.AreEqual(-1, ui.ActiveSlot, "入室直後はどのペンにも紐づけない");

            ui._OnSwatchClicked(7);
            Assert.IsTrue(ui.HasPendingSelection);
            Assert.AreEqual(0, bindings[0].CurrentStyleId, "紐づいていないので、どのペンも変わらない");
            Assert.AreEqual(0, bindings[1].CurrentStyleId);

            ui._BindTo(1);
            Assert.IsFalse(ui.HasPendingSelection);
            Assert.AreEqual(7, bindings[1].CurrentStyleId, "次に持ったペンへ反映する");
            Assert.AreEqual(ui.CurrentWidthIndex, bindings[1].CurrentWidthIndex);
            Assert.AreEqual(0, bindings[0].CurrentStyleId);
        }

        [Test]
        public void PendingSelectionWaitsForThePensLedger()
        {
            var ui = MakeUi(out QppPenBinding[] bindings);
            var chunk = Add<QppLedgerChunk>();
            chunk.EditorSetReferences(16, null, 0);
            var ledger = Add<QppPenLedger>();
            var state = Add<QppPenState>();
            SetField(state, "ledger", ledger);
            ledger.EditorSetReferences(new[] { chunk }, state, null, 0, 128);
            SetField(ui, "penStates", new[] { state, null });

            ui._OnSwatchClicked(9);
            ui._BindTo(0);

            // 台帳がまだ届いていない（チャンク 0 が空）ので、反映を待つ。パレットの選択もそのまま。
            Assert.IsTrue(ui.HasPendingSelection);
            Assert.AreEqual(9, ui.CurrentSelectedStyleId);

            // 待っている間にペン側の色の知らせが来ても、選んだものを上書きしない。
            ui._OnPenStateChanged(0, 2, 100);
            Assert.AreEqual(9, ui.CurrentSelectedStyleId);
            Assert.IsTrue(ui.HasPendingSelection);
        }

        private QppPaletteUI MakeUi(out QppPenBinding[] bindings)
        {
            var go = NewObject();
            var palette = go.AddComponent<QppPalette>();
            const int count = 40;
            palette.EditorSetData(new Material[1], new Material[1], new bool[1], new int[count], new Color[count],
                new int[count], new Gradient[0], new string[count]);
            var ui = go.AddComponent<QppPaletteUI>();
            SetField(ui, "palette", palette);
            bindings = new[] { Add<QppPenBinding>(), Add<QppPenBinding>() };
            SetField(ui, "bindings", bindings);
            return ui;
        }

        private T Add<T>() where T : Component
        {
            return NewObject().AddComponent<T>();
        }

        private GameObject NewObject()
        {
            var go = new GameObject("QppBindingFixture");
            objects.Add(go);
            return go;
        }

        private static void SetField<T>(object target, string name, T value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
