using NUnit.Framework;
using UnityEngine;

namespace Yakigura.QvPalette.Tests
{
    public class QppPaletteToolFollowTests
    {
        [Test]
        public void OpeningAfterRotatingAndReopeningPreservesToolRelativeAngle()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                tool.OnPickup();

                toolObject.transform.rotation = Quaternion.Euler(0f, 60f, 25f);
                tool.DeployPanelAtCurrentPosition();
                // パネルはツールに対して垂直に立つ（パネルの上方向 = ツールの上方向）。見る人がいなければツールの正面を向く。
                Assert.Less(Vector3.Angle(panelObject.transform.up, toolObject.transform.up), 0.01f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation, toolObject.transform.rotation), 0.01f);

                Quaternion localRotation = Quaternion.Inverse(toolObject.transform.rotation) * panelObject.transform.rotation;
                tool._ClosePanel();
                toolObject.transform.rotation = Quaternion.Euler(15f, 105f, 40f);
                tool.DeployPanelAtCurrentPosition();
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation,
                    toolObject.transform.rotation * localRotation), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void PanelAppearsAboveToolAndReturnsThereWhenToolIsGrabbedAgain()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                toolObject.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(20f, 75f, 30f));
                tool.OnPickup();
                tool.DeployPanelAtCurrentPosition();

                Vector3 local = toolObject.transform.InverseTransformPoint(panelObject.transform.position);
                Assert.Less(Mathf.Abs(local.x), 0.0001f);
                Assert.Less(Mathf.Abs(local.z), 0.0001f);
                Assert.Greater(local.y, 0.1f);

                tool.OnDrop();
                toolObject.transform.SetPositionAndRotation(new Vector3(-2f, 1f, 0.5f), Quaternion.Euler(5f, 200f, 10f));
                tool.OnPickup();
                Assert.Less(Vector3.Distance(local,
                    toolObject.transform.InverseTransformPoint(panelObject.transform.position)), 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void ReopeningAfterReleasePreservesAngleAndOnlyPickupResumesFollowing()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                tool.OnPickup();
                tool.DeployPanelAtCurrentPosition();
                toolObject.transform.rotation = Quaternion.Euler(30f, 40f, 50f);
                tool._FollowHeldPanel();
                tool.OnDrop();
                Quaternion releasedAngle = panelObject.transform.rotation;
                tool._ClosePanel();
                tool.DeployPanelAtCurrentPosition();
                Assert.Less(Quaternion.Angle(releasedAngle, panelObject.transform.rotation), 0.01f);

                Quaternion offset = Quaternion.Inverse(toolObject.transform.rotation) * panelObject.transform.rotation;
                toolObject.transform.rotation = Quaternion.Euler(60f, 80f, 100f);
                tool._FollowHeldPanel();
                Assert.Less(Quaternion.Angle(releasedAngle, panelObject.transform.rotation), 0.01f);
                tool.OnPickup();
                Assert.Less(Quaternion.Angle(toolObject.transform.rotation * offset, panelObject.transform.rotation), 0.01f);
                toolObject.transform.rotation = Quaternion.Euler(15f, 90f, 10f);
                tool._FollowHeldPanel();
                Assert.Less(Quaternion.Angle(toolObject.transform.rotation * offset, panelObject.transform.rotation), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }

        [Test]
        public void HeldToolMovesAndRotatesPanelThenDropLeavesItInPlace()
        {
            GameObject toolObject = new GameObject("PaletteTool");
            GameObject panelObject = new GameObject("PanelRoot");
            try
            {
                var tool = toolObject.AddComponent<QppPaletteTool>();
                tool.EditorSetShortClickSeconds(-1f); // 編集モードでは時間が進まないので、短いクリック判定を切る
                tool.EditorSetReferences(panelObject.transform, null, null, Vector3.zero, Quaternion.identity);
                panelObject.transform.rotation = Quaternion.Euler(10f, 25f, 0f);

                tool.OnPickup();
                tool.OnPickupUseDown();
                Assert.IsTrue(tool.IsPanelOpen);
                tool.OnPickupUseDown();
                Assert.IsFalse(tool.IsPanelOpen);
                tool.OnPickupUseDown();
                Assert.IsTrue(tool.IsPanelOpen);

                Vector3 localPosition = Quaternion.Inverse(toolObject.transform.rotation) *
                                        (panelObject.transform.position - toolObject.transform.position);
                Quaternion localRotation = Quaternion.Inverse(toolObject.transform.rotation) * panelObject.transform.rotation;

                toolObject.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(20f, 75f, 30f));
                tool._FollowHeldPanel();
                Assert.Less(Vector3.Distance(panelObject.transform.position,
                    toolObject.transform.position + toolObject.transform.rotation * localPosition), 0.0001f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation,
                    toolObject.transform.rotation * localRotation), 0.01f);

                // Only the tool moves the panel: an external pose change is overridden on the next follow.
                panelObject.transform.SetPositionAndRotation(new Vector3(2f, 3f, 4f), Quaternion.Euler(0f, 140f, 20f));
                toolObject.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(5f, 115f, 10f));
                tool._FollowHeldPanel();
                Assert.Less(Vector3.Distance(panelObject.transform.position,
                    toolObject.transform.position + toolObject.transform.rotation * localPosition), 0.0001f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation,
                    toolObject.transform.rotation * localRotation), 0.01f);

                tool.OnDrop();
                Vector3 droppedPosition = panelObject.transform.position;
                Quaternion droppedRotation = panelObject.transform.rotation;
                toolObject.transform.position += Vector3.right;
                tool._FollowHeldPanel();
                Assert.Less(Vector3.Distance(panelObject.transform.position, droppedPosition), 0.0001f);
                Assert.Less(Quaternion.Angle(panelObject.transform.rotation, droppedRotation), 0.01f);
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
                Object.DestroyImmediate(toolObject);
            }
        }
    }
}
