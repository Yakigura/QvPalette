using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using EditorNs = global::Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppInitialInksTests
    {
        [Test]
        public void NoQvPenInksGivesOneDefaultInkAndNoGeneratedColors()
        {
            foreach (List<EditorNs.QppInk> none in new[] { null, new List<EditorNs.QppInk>() })
            {
                var inks = EditorNs.QppInitialInks.Build(none);
                Assert.AreEqual(1, inks.Count);
                Assert.AreEqual("Default", inks[0].name);
                Assert.IsNotNull(inks[0].material);
                Assert.AreEqual(Color.white, inks[0].gradient.Evaluate(0f));
                Assert.AreEqual(Color.white, inks[0].gradient.Evaluate(1f));
            }
        }

        [Test]
        public void QvPenInksAreKeptAsTheyAre()
        {
            var fromPens = new List<EditorNs.QppInk>
            {
                new EditorNs.QppInk { name = "Red", gradient = EditorNs.QppInkData.SolidGradient(Color.red) },
                new EditorNs.QppInk { name = "Blue", gradient = EditorNs.QppInkData.SolidGradient(Color.blue) },
            };
            var inks = EditorNs.QppInitialInks.Build(fromPens);
            Assert.AreSame(fromPens, inks);
            Assert.AreEqual(2, inks.Count);
        }

        [Test]
        public void AutoTagsAreAssignedAndTagNamesAreDistinct()
        {
            var inks = new List<EditorNs.QppInk>
            {
                new EditorNs.QppInk { name = "Red 1", gradient = EditorNs.QppInkData.SolidGradient(Color.red) },
                new EditorNs.QppInk { name = "Red 2", gradient = EditorNs.QppInkData.SolidGradient(Color.red) },
                new EditorNs.QppInk { name = "Blue", gradient = EditorNs.QppInkData.SolidGradient(Color.blue) },
            };
            string[] names = EditorNs.QppInitialInks.ApplyAutoTags(inks);
            Assert.IsNotEmpty(inks[0].tags);
            Assert.AreEqual(inks[0].tags, inks[1].tags);
            Assert.That(names, Is.Unique);
            foreach (var ink in inks)
                foreach (string tag in ink.tags)
                    Assert.Contains(tag, names);
        }
    }
}
