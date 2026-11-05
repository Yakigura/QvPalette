using System.Collections.Generic;
using NUnit.Framework;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppInkSelectionTests
    {
        [Test]
        public void RemoveSelectedRemovesByInkIndexFromTheEnd()
        {
            var inks = new List<string> { "a", "b", "c", "d", "e" };
            int removed = QppInkSelection.RemoveSelected(inks, new HashSet<int> { 0, 2, 4 });
            Assert.AreEqual(3, removed);
            CollectionAssert.AreEqual(new[] { "b", "d" }, inks);
        }

        [Test]
        public void RemoveSelectedIgnoresIndicesOutsideTheList()
        {
            var inks = new List<string> { "a", "b" };
            int removed = QppInkSelection.RemoveSelected(inks, new HashSet<int> { 1, 7 });
            Assert.AreEqual(1, removed);
            CollectionAssert.AreEqual(new[] { "a" }, inks);
        }
    }
}
