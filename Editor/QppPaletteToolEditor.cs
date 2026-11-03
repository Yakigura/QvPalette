#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// パレットツールのインスペクター。パネルの表示位置（ツールの子「PanelPosition」）を選ぶボタンと、
    /// 実際のパネルをその位置に表示して確かめるボタンを出す。
    /// </summary>
    [CustomEditor(typeof(QppPaletteTool))]
    public class QppPaletteToolEditor : UnityEditor.Editor
    {
        // 実際のパネルを表示して確かめている間だけ、元の表示状態と位置を覚えておく。
        private static QppPaletteTool previewTool;
        private static bool previewWasActive;
        private static Vector3 previewOriginalPosition;
        private static Quaternion previewOriginalRotation;

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            DrawDefaultInspector();

            var tool = (QppPaletteTool)target;
            Transform anchor = tool.EditorPanelAnchor;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("パネルの表示位置", EditorStyles.boldLabel);
            if (anchor == null)
            {
                EditorGUILayout.HelpBox(
                    "表示位置のオブジェクトがありません。Tools > Yakigura > QvPalette > Setup（このシーンに導入・更新） で作られます。" +
                    "（ない間は見る人の方を向く自動配置）",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "ツールの子の「PanelPosition」を、シーン上で普通に移動・回転してください。" +
                "水色の板がパネルの大きさで、文字が読める面が見る側です。パネルはツールに対してこの位置・向きで出ます。" +
                "（水色の板は編集中だけの目印で、ワールドには入りません）",
                MessageType.Info);

            if (GUILayout.Button("PanelPosition を選択して調整する"))
            {
                Selection.activeTransform = anchor;
                EditorGUIUtility.PingObject(anchor);
            }

            Transform panelRoot = tool.EditorPanelRoot;
            using (new EditorGUI.DisabledScope(panelRoot == null))
            {
                bool previewing = previewTool == tool;
                if (GUILayout.Button(previewing ? "実際のパネルの表示をやめる" : "実際のパネルをその位置に表示して確かめる"))
                {
                    if (previewing) EndPreview();
                    else BeginPreview(tool);
                }
            }
        }

        private static void BeginPreview(QppPaletteTool tool)
        {
            if (previewTool != null) EndPreview();
            Transform panelRoot = tool.EditorPanelRoot;
            if (panelRoot == null || tool.EditorPanelAnchor == null) return;
            previewTool = tool;
            previewWasActive = panelRoot.gameObject.activeSelf;
            previewOriginalPosition = panelRoot.position;
            previewOriginalRotation = panelRoot.rotation;
            panelRoot.gameObject.SetActive(true);
            EditorApplication.update += FollowAnchor;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            FollowAnchor();
        }

        // PanelPosition を動かしている間も、表示中のパネルを追従させる。
        private static void FollowAnchor()
        {
            if (previewTool == null || previewTool.EditorPanelRoot == null || previewTool.EditorPanelAnchor == null)
            {
                EndPreview();
                return;
            }
            Transform panelRoot = previewTool.EditorPanelRoot;
            Transform anchor = previewTool.EditorPanelAnchor;
            if (panelRoot.position != anchor.position || panelRoot.rotation != anchor.rotation)
            {
                panelRoot.SetPositionAndRotation(anchor.position, anchor.rotation);
                SceneView.RepaintAll();
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) EndPreview();
        }

        private static void EndPreview()
        {
            EditorApplication.update -= FollowAnchor;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            if (previewTool != null && previewTool.EditorPanelRoot != null)
            {
                // 確かめるための表示なので、シーンには残さない（表示状態と位置を元に戻す）。
                Transform panelRoot = previewTool.EditorPanelRoot;
                panelRoot.SetPositionAndRotation(previewOriginalPosition, previewOriginalRotation);
                panelRoot.gameObject.SetActive(previewWasActive);
            }
            previewTool = null;
            SceneView.RepaintAll();
        }
    }
}
#endif
