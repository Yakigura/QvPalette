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
            EditorGUILayout.LabelField(QppEditorText.T("パネルの表示位置", "Panel Position", "패널 표시 위치"),EditorStyles.boldLabel);
            if (anchor == null)
            {
                EditorGUILayout.HelpBox(
                    QppEditorText.T(
                        "表示位置のオブジェクトがありません。Tools > Yakigura > QvPalette > Setup（このシーンに導入・更新） で作られます。" +
                        "（ない間は見る人の方を向く自動配置）",
                        "There is no position object. It is created by Tools > Yakigura > QvPalette > Setup (install / update in this scene). " +
                        "(Until then, the panel is placed automatically facing the viewer.)",
                        "표시 위치 오브젝트가 없습니다. Tools > Yakigura > QvPalette > Setup (이 씬에 설치·업데이트)으로 생성됩니다. " +
                        "(없는 동안은 보는 사람 쪽을 향하는 자동 배치)"),
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                QppEditorText.T(
                    "ツールの子の「PanelPosition」を、シーン上で普通に移動・回転してください。" +
                    "水色の板がパネルの大きさで、文字が読める面が見る側です。パネルはツールに対してこの位置・向きで出ます。" +
                    "（水色の板は編集中だけの目印で、ワールドには入りません）",
                    "Move and rotate the tool's child \"PanelPosition\" normally in the scene. " +
                    "The light-blue plate shows the panel size, and the side where the text is readable faces the viewer. The panel appears at this position and orientation relative to the tool. " +
                    "(The light-blue plate is only a marker while editing and is not included in the world.)",
                    "도구의 자식 \"PanelPosition\"을 씬에서 일반적인 방법으로 이동·회전하세요. " +
                    "하늘색 판이 패널의 크기이고, 글자가 읽히는 면이 보는 쪽입니다. 패널은 도구를 기준으로 이 위치·방향에 표시됩니다. " +
                    "(하늘색 판은 편집 중에만 보이는 표시이며 월드에는 포함되지 않습니다)"),
                MessageType.Info);

            if (GUILayout.Button(QppEditorText.T("PanelPosition を選択して調整する", "Select PanelPosition to adjust", "PanelPosition을 선택하여 조정")))
            {
                Selection.activeTransform = anchor;
                EditorGUIUtility.PingObject(anchor);
            }

            Transform panelRoot = tool.EditorPanelRoot;
            using (new EditorGUI.DisabledScope(panelRoot == null))
            {
                bool previewing = previewTool == tool;
                if (GUILayout.Button(previewing ? QppEditorText.T("実際のパネルの表示をやめる", "Stop showing the actual panel", "실제 패널 표시 중지") : QppEditorText.T("実際のパネルをその位置に表示して確かめる", "Show the actual panel at that position to check", "실제 패널을 해당 위치에 표시하여 확인")))
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
