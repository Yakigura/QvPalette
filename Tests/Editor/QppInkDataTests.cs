using System.Collections.Generic;
using NUnit.Framework;
using QvPen.UdonScript;
using UnityEngine;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppInkDataTests
    {
        private static QppInk Ink(string name, Color c, Material pc, Material quest, params string[] tags)
        {
            var ink = new QppInk { name = name, gradient = QppInkData.SolidGradient(c), material = pc, questMaterial = quest };
            ink.tags.AddRange(tags);
            return ink;
        }

        [Test]
        public void WriteSharesMaterialPairsAndReadReturnsOneRowPerInk()
        {
            var go = new GameObject("Palette");
            var a = new Material(Shader.Find("Unlit/Color")) { name = "A" };
            var b = new Material(Shader.Find("Unlit/Color")) { name = "B" };
            var q = new Material(Shader.Find("Unlit/Color")) { name = "Q" };
            try
            {
                var palette = go.AddComponent<QppPalette>();
                var inks = new List<QppInk>
                {
                    Ink("red", Color.red, a, null, "赤"),
                    Ink("blue", Color.blue, b, q, "青", "濃い色"),
                    Ink("red2", Color.red, a, null),
                };
                QppInkData.Write(palette, inks, new[] { "赤", "青", "濃い色" });

                Assert.AreEqual(3, palette.EntryCount);
                Assert.AreEqual(2, palette.EditorMaterials.Length, "inks 0 and 2 share one bank slot");
                Assert.AreSame(a, palette.EditorMaterialsQuest[0], "no Quest material means the PC one");
                Assert.AreSame(q, palette.EditorMaterialsQuest[1]);
                Assert.IsTrue(palette.EntryHasTag(1, 2));

                var read = QppInkData.Read(palette);
                Assert.AreEqual("blue", read[1].name);
                Assert.AreSame(b, read[1].material);
                Assert.AreSame(q, read[1].questMaterial);
                Assert.IsNull(read[0].questMaterial);
                CollectionAssert.AreEqual(new[] { "青", "濃い色" }, read[1].tags);

                // 並べ替えてもタグと Material はインクに付いて動く。
                read.Reverse();
                QppInkData.Write(palette, read, new[] { "赤", "青", "濃い色" });
                Assert.AreSame(b, palette.EditorMaterials[palette.EditorEntryMaterialIndex[1]]);
                Assert.IsTrue(palette.EntryHasTag(1, 1));
                Assert.IsFalse(palette.EntryHasTag(0, 0));
                Assert.IsTrue(palette.EntryHasTag(2, 0));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(q);
            }
        }

        [Test]
        public void JsonRoundTripKeepsGradientMaterialAndTagsAndReportsMissingMaterials()
        {
            var ink = QppInkData.DefaultInkMaterial(false);
            var unknown = new Material(Shader.Find("Unlit/Color")) { name = "QppNoSuchMaterial_xyz" };
            try
            {
                var g = new Gradient();
                g.SetKeys(
                    new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.yellow, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 1f) });
                var inks = new List<QppInk>
                {
                    new QppInk { name = "sunset", gradient = g, material = ink },
                    Ink("lost", Color.green, unknown, null, "緑"),
                };
                inks[0].tags.Add("グラデ");

                string json = QppInkData.ToJson(inks, new[] { "グラデ", "緑" });
                var content = QppInkData.FromJson(json);

                CollectionAssert.AreEqual(new[] { "グラデ", "緑" }, content.tagNames);
                Assert.AreEqual(2, content.inks.Count);
                Assert.AreEqual("sunset", content.inks[0].name);
                Assert.IsTrue(QppInkData.GradientEquals(g, content.inks[0].gradient));
                Assert.AreSame(ink, content.inks[0].material);
                Assert.IsFalse(content.materialMissing[0]);

                Assert.IsNull(content.inks[1].material);
                Assert.IsTrue(content.materialMissing[1]);
                CollectionAssert.AreEqual(new[] { "QppNoSuchMaterial_xyz" }, content.missingMaterials);
                CollectionAssert.AreEqual(new[] { "緑" }, content.inks[1].tags);

                Assert.Throws<System.ArgumentException>(() => QppInkData.FromJson("{\"format\":\"other\"}"));
            }
            finally
            {
                Object.DestroyImmediate(unknown);
            }
        }

        [Test]
        public void MergeAddsNewTagsUntilNineAndDropsTheRest()
        {
            var current = new List<QppInk> { Ink("a", Color.white, null, null, "t1") };
            var currentTags = new[] { "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8" };
            var incoming = new List<QppInk> { Ink("b", Color.black, null, null, "t1", "new1", "new2") };

            string[] tags;
            List<string> dropped;
            var merged = QppInkData.Merge(current, currentTags, incoming, false, out tags, out dropped);

            Assert.AreEqual(2, merged.Count);
            Assert.AreEqual(9, tags.Length);
            Assert.AreEqual("new1", tags[8]);
            CollectionAssert.AreEqual(new[] { "new2" }, dropped);
            CollectionAssert.AreEqual(new[] { "t1", "new1" }, merged[1].tags);
            CollectionAssert.AreEqual(new[] { "t1", "new1", "new2" }, incoming[0].tags, "the source ink is not modified");

            var replaced = QppInkData.Merge(current, currentTags, incoming, true, out tags, out dropped);
            Assert.AreEqual(1, replaced.Count);
        }

        [Test]
        public void ReadQvPenTakesGradientAndMaterialsAndMergesIdenticalPens()
        {
            var root = new GameObject("Pens");
            var pc = new Material(Shader.Find("Unlit/Color")) { name = "PC" };
            var quest = new Material(Shader.Find("Unlit/Color")) { name = "Quest" };
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var pen = new GameObject("QvPen (" + i + ")");
                    pen.transform.SetParent(root.transform);
                    var managerGo = new GameObject("PenManager");
                    managerGo.transform.SetParent(pen.transform);
                    var pm = managerGo.AddComponent<QvPen_PenManager>();
                    pm.colorGradient = QppInkData.SolidGradient(i == 2 ? Color.blue : Color.red);
                    pm.pcInkMaterial = pc;
                    pm.questInkMaterial = quest;
                }

                var inks = QppInkData.ReadQvPen(root);
                Assert.AreEqual(2, inks.Count, "the two red pens become one ink");
                Assert.AreEqual("QvPen (0)", inks[0].name);
                Assert.AreSame(pc, inks[0].material);
                Assert.AreSame(quest, inks[0].questMaterial);
                Assert.AreEqual(Color.blue, QppInkData.FirstColor(inks[1].gradient));
                CollectionAssert.Contains(QppInkData.AutoTagNames(inks[1].gradient), "青");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(pc);
                Object.DestroyImmediate(quest);
            }
        }
    }
}
