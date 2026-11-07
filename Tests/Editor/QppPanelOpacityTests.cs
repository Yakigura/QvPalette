using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppPanelOpacityTests
    {
        [Test]
        public void PanelDoesNotDimWhileDrawingByDefault()
        {
            var go = new GameObject("UI");
            try
            {
                var ui = go.AddComponent<QppPaletteUI>();
                var field = typeof(QppPaletteUI).GetField("dimWhileDrawing",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.IsNotNull(field, "dimWhileDrawing exists");
                Assert.IsFalse((bool)field.GetValue(ui), "the panel keeps its opacity while drawing unless it is turned on");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
