using NUnit.Framework;
using UnityEngine;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppTagFilterTests
    {
        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static T GetField<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(target);
        }

        /// <summary>40 色。タグ 0 は 3 の倍数の色だけ（0, 3, ..., 39 の 14 色）。</summary>
        private static QppPalette MakePalette(GameObject go)
        {
            var palette = go.AddComponent<QppPalette>();
            const int count = 40;
            var masks = new int[count];
            for (int i = 0; i < count; i++) masks[i] = i % 3 == 0 ? 1 : 2;
            palette.EditorSetData(new Material[1], new Material[1], new bool[1], new int[count], new Color[count],
                new int[count], new Gradient[0], new string[count]);
            palette.EditorSetTags(new[] { "A", "B" }, masks);
            return palette;
        }

        [Test]
        public void TagsPageNineAtATimeAndSelectionUsesTheShownPage()
        {
            var go = new GameObject("Palette");
            try
            {
                var palette = go.AddComponent<QppPalette>();
                const int count = 40;
                var masks = new int[count];
                for (int i = 0; i < count; i++) masks[i] = 1 << (i % 12);
                var names = new string[12];
                for (int i = 0; i < names.Length; i++) names[i] = "T" + i;
                palette.EditorSetData(new Material[1], new Material[1], new bool[1], new int[count], new Color[count],
                    new int[count], new Gradient[0], new string[count]);
                palette.EditorSetTags(names, masks);
                var ui = go.AddComponent<QppPaletteUI>();
                SetField(ui, "palette", palette);

                ui._SelectTagButton(1);
                Assert.AreEqual(0, ui.ActiveTag, "page 1, first tag button is tag 0");
                ui._SelectTagButton(1);
                ui._NextTagPage();
                Assert.AreEqual(1, GetField<int>(ui, "tagPage"));
                ui._SelectTagButton(1);
                Assert.AreEqual(9, ui.ActiveTag, "page 2, first tag button is tag 9");
                ui._SelectTagButton(4);
                Assert.AreEqual(9, ui.ActiveTag, "tag 12 does not exist: the selection stays as it was");
                ui._NextTagPage();
                Assert.AreEqual(0, GetField<int>(ui, "tagPage"), "the last page wraps to the first");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void MaxTagsIs27AndNineShownPerPage()
        {
            Assert.AreEqual(27, QppPalette.MAX_TAGS);
            Assert.AreEqual(9, QppPalette.TAGS_PER_PAGE);
        }

        [Test]
        public void WidthPresetsHaveTwoPagesAndWrap()
        {
            var go = new GameObject("Palette");
            try
            {
                var ui = go.AddComponent<QppPaletteUI>();
                float[] values = GetField<float[]>(ui, "widthPresetMillimeters");
                Assert.AreEqual(8, values.Length);
                Assert.AreEqual(50f, values[4]);
                Assert.AreEqual(150f, values[7]);
                ui._NextPresetPage();
                Assert.AreEqual(1, GetField<int>(ui, "presetPage"));
                ui._NextPresetPage();
                Assert.AreEqual(0, GetField<int>(ui, "presetPage"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void TagFilterPagesOnlyMatchingStylesAndTogglesBackToAll()
        {
            var go = new GameObject("Palette");
            try
            {
                var palette = MakePalette(go);
                var ui = go.AddComponent<QppPaletteUI>();
                SetField(ui, "palette", palette);

                Assert.AreEqual(3, ui.PageCount(), "40 styles unfiltered");
                Assert.AreEqual(17, ui.StyleAt(1, 1));

                ui._SelectTagButton(1);
                Assert.AreEqual(0, ui.ActiveTag);
                Assert.AreEqual(1, ui.PageCount(), "14 styles fit on one page");
                Assert.AreEqual(0, ui.StyleAt(0, 0));
                Assert.AreEqual(39, ui.StyleAt(0, 13));
                Assert.AreEqual(-1, ui.StyleAt(0, 14));

                ui._SelectTagButton(1);
                Assert.AreEqual(-1, ui.ActiveTag, "pressing the active tag again shows all");
                ui._SelectTagButton(5);
                Assert.AreEqual(-1, ui.ActiveTag, "a tag the palette does not have is ignored");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SelectingTagShowsPageOfCurrentStyleAndFavoriteOutsideFilterClearsIt()
        {
            var go = new GameObject("Palette");
            try
            {
                var palette = MakePalette(go);
                var ui = go.AddComponent<QppPaletteUI>();
                SetField(ui, "palette", palette);

                SetField(ui, "currentSelectedStyleId", 38);
                ui._SelectTagButton(2);
                Assert.AreEqual(1, ui.ActiveTag);
                // tag B = 26 styles; style 38 is the 25th of them -> page 1
                Assert.AreEqual(1, GetField<int>(ui, "currentPage"));

                // A remote update to a style hidden by the filter keeps the viewer's filter.
                ui._RefreshSelection(3, 128);
                Assert.AreEqual(1, ui.ActiveTag);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PenInkChangesNeverSwitchThePage()
        {
            var go = new GameObject("Palette");
            try
            {
                var palette = MakePalette(go);
                var ui = go.AddComponent<QppPaletteUI>();
                SetField(ui, "palette", palette);
                SetField(ui, "activeSlot", 0);

                // お気に入りで 2 ページ目の色（20）を選んだが、見ているのは 1 ページ目。
                SetField(ui, "currentSelectedStyleId", 20);
                SetField(ui, "currentWidthIndex", 300);
                SetField(ui, "currentPage", 0);

                // ペンの状態からの知らせ（自分で選んだ結果）ではページを変えない。
                ui._OnPenStateChanged(0, 20, 300);
                Assert.AreEqual(0, GetField<int>(ui, "currentPage"));

                // ほかの経路でインクが変わったときも、ページは変えない。
                ui._OnPenStateChanged(0, 35, 300);
                Assert.AreEqual(0, GetField<int>(ui, "currentPage"));
                Assert.AreEqual(35, ui.CurrentSelectedStyleId);

                // ペンを持ち替えたとき（ほかのペンにつなぎ直す）も、ページは変えない。
                ui._RefreshSelection(39, 300);
                Assert.AreEqual(0, GetField<int>(ui, "currentPage"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ColorAutoTagsCoverHueFamiliesNeutralsAndShades()
        {
            Assert.AreEqual(1 << 0, QppPaletteEditor.TagsOfColor(Color.red));
            Assert.AreEqual(1 << 2, QppPaletteEditor.TagsOfColor(Color.green));
            Assert.AreEqual(1 << 3, QppPaletteEditor.TagsOfColor(Color.blue));
            Assert.AreEqual(1 << 5, QppPaletteEditor.TagsOfColor(Color.white));
            Assert.AreEqual(1 << 5, QppPaletteEditor.TagsOfColor(Color.black));
            Assert.AreEqual((1 << 4) | (1 << 6), QppPaletteEditor.TagsOfColor(new Color(1f, 0.7f, 0.9f)), "pastel pink");
            Assert.AreEqual((1 << 3) | (1 << 7), QppPaletteEditor.TagsOfColor(new Color(0f, 0f, 0.4f)), "dark blue");
        }
    }
}
