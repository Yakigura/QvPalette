using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppWidthRangeAndPanelPoseTests
    {
        [Test]
        public void InkWidthRangeFollowsPaletteInspectorSettings()
        {
            var go = new GameObject("Palette");
            try
            {
                var palette = go.AddComponent<QppPalette>();
                palette.EditorSetInkWidthRange(3f, 100f, 5f);
                Assert.AreEqual(0.003f, palette.WidthOf(0), 1e-6f);
                Assert.AreEqual(0.1f, palette.WidthOf(QppCommon.MAX_WIDTH_INDEX), 1e-5f);
                Assert.AreEqual(0.005f, palette.WidthOf(palette.DefaultWidthIndex), 0.0002f);

                palette.EditorSetInkWidthRange(1f, 20f, 5f);
                Assert.AreEqual(0.001f, palette.WidthOf(0), 1e-6f);
                Assert.AreEqual(0.02f, palette.WidthOf(QppCommon.MAX_WIDTH_INDEX), 1e-5f);
                Assert.AreEqual(0.005f, palette.WidthOf(palette.DefaultWidthIndex), 0.0002f);
                Assert.AreEqual("20.0 mm", palette.MillimetersTextOf(QppCommon.MAX_WIDTH_INDEX));

                // 逆転・ゼロの設定でも壊れない（最小 0.1mm、最大は最小より大きく保つ）。
                palette.EditorSetInkWidthRange(0f, 0f, 5f);
                Assert.Greater(palette.WidthOf(QppCommon.MAX_WIDTH_INDEX), palette.WidthOf(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PanelAppearsAtThePanelPositionObjectUnderTheTool()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                Vector3 lp = new Vector3(0.1f, 0.3f, -0.05f);
                Vector3 le = new Vector3(-20f, 180f, 5f);
                var anchor = new GameObject("PanelPosition").transform;
                anchor.SetParent(toolObject.transform, false);
                anchor.localPosition = lp;
                anchor.localRotation = Quaternion.Euler(le);
                tool.EditorSetPanelAnchor(anchor);

                toolObject.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(30f, 70f, 10f));
                tool.OnPickup();
                tool.DeployPanelAtCurrentPosition();

                Transform t = toolObject.transform;
                Assert.Less(Vector3.Distance(panelObject.transform.position, t.position + t.rotation * lp), 0.0001f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation, t.rotation * Quaternion.Euler(le)), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void ShortClickTogglesPanelAndLongHoldKeepsItOpen()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);

                // 短いクリック（編集モードでは時間が進まないので、判定時間を長くして短いとみなす）
                tool.EditorSetShortClickSeconds(10f);
                tool.OnPickup();
                Assert.IsFalse(tool.IsPanelOpen, "掴んだ瞬間には開かない");
                tool.OnDrop();
                Assert.IsTrue(tool.IsPanelOpen, "短いクリックで開く");
                tool.OnPickup();
                Assert.IsTrue(tool.IsPanelOpen, "開いているときに掴んでも閉じない");
                tool.OnDrop();
                Assert.IsFalse(tool.IsPanelOpen, "短いクリックで閉じる");

                // 長く掴んで離したときは、表示を変えない
                tool.DeployPanelAtCurrentPosition();
                tool.EditorSetShortClickSeconds(-1f);
                tool.OnPickup();
                tool.OnDrop();
                Assert.IsTrue(tool.IsPanelOpen);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void DesktopRollingTheToolKeepsPanelPositionAndAngle()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f);
                tool.EditorTreatAsDesktop(true);
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                var visual = new GameObject("Visual").transform;
                visual.SetParent(toolObject.transform, false);
                var anchor = new GameObject("PanelPosition").transform;
                anchor.SetParent(visual, false);
                anchor.localPosition = new Vector3(0f, 0.35f, 0.05f);
                anchor.localRotation = Quaternion.Euler(-10f, 0f, 0f);
                tool.EditorSetPanelAnchor(anchor);

                toolObject.transform.SetPositionAndRotation(new Vector3(1f, 1f, 1f), Quaternion.Euler(0f, 30f, 0f));
                tool.DeployPanelAtCurrentPosition();
                Vector3 pos = panelObject.transform.position;
                Quaternion rot = panelObject.transform.rotation;

                // パネルの向く方向を軸にツールをひねっても、パネルの位置・角度は変わらない（デスクトップは 2 方向だけ）。
                Vector3 facing = anchor.forward;
                toolObject.transform.rotation = Quaternion.AngleAxis(60f, facing) * toolObject.transform.rotation;
                tool.OnPickup();
                tool._FollowHeldPanel();
                Assert.Less(Vector3.Distance(panelObject.transform.position, pos), 0.0005f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation, rot), 0.1f);
                Assert.Less(Vector3.Angle(panelObject.transform.up, Vector3.ProjectOnPlane(Vector3.up, panelObject.transform.forward)), 0.1f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void AutoPanelPoseFacesTheViewerAboveTheTool()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                toolObject.transform.SetPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.Euler(84f, 0f, 0f));
                Vector3 lp, le;
                tool.EditorAutoPanelPose(new Vector3(0f, 1.5f, -2f), out lp, out le);

                Vector3 pos = toolObject.transform.position + toolObject.transform.rotation * lp;
                Quaternion rot = toolObject.transform.rotation * Quaternion.Euler(le);
                Assert.Greater(pos.y, 1.1f);
                Assert.Less(Vector3.Angle(rot * Vector3.up, Vector3.up), 0.1f);
                Assert.Less(Vector3.Angle(rot * Vector3.forward, Vector3.back), 0.1f);
            }
            finally
            {
                Object.DestroyImmediate(toolObject);
            }
        }
    }
}
