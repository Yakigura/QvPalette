using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppFocusHighlightTests
    {
        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        [Test]
        public void FocusFrameFollowsTargetRectAndHidesWhenCleared()
        {
            GameObject uiObject = new GameObject("UI");
            GameObject focusObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            try
            {
                var ui = uiObject.AddComponent<QppPaletteUI>();
                SetField(ui, "focusHighlight", focusObject.transform);
                SetField(ui, "focusRenderer", focusObject.GetComponent<Renderer>());
                focusObject.SetActive(false);

                ui._SetFocus(new Vector2(0.2f, -0.05f), new Vector2(0.055f, 0.055f), 0.5f);
                Assert.IsTrue(focusObject.activeSelf);
                Assert.Less(Vector3.Distance(focusObject.transform.localPosition, new Vector3(0.2f, -0.05f, 0.003f)), 1e-5f);
                Assert.Less(Mathf.Abs(focusObject.transform.localScale.x - 0.063f), 1e-5f);

                var block = new MaterialPropertyBlock();
                focusObject.GetComponent<Renderer>().GetPropertyBlock(block);
                Color half = block.GetColor("_Color");
                ui._SetFocus(new Vector2(0.2f, -0.05f), new Vector2(0.055f, 0.055f), 1f);
                focusObject.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.AreNotEqual(half, block.GetColor("_Color"), "triggered focus must look different");

                ui._ClearFocus();
                Assert.IsFalse(focusObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(focusObject);
                Object.DestroyImmediate(uiObject);
            }
        }
    }
}
