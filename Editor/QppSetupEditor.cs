#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// QvPalette/Setup のインスペクター。台帳の容量（ペン 1 本あたりのチャンク数と、1 チャンクの件数）を、ここで決める。
    /// 値は Setup オブジェクトに保存され、次の Setup でも使われる。
    /// </summary>
    [CustomEditor(typeof(QppSetup))]
    public class QppSetupEditor : UnityEditor.Editor
    {
        private static readonly int[] Capacities = { 128, 256, 512, 1024 };
        private static string[] CapacityLabels()
        {
            return new[]
            {
                QppEditorText.T("128 件（約 1 KB）", "128 entries (about 1 KB)", "128개 (약 1 KB)"),
                QppEditorText.T("256 件（約 2 KB）", "256 entries (about 2 KB)", "256개 (약 2 KB)"),
                QppEditorText.T("512 件（約 4 KB、既定）", "512 entries (about 4 KB, default)", "512개 (약 4 KB, 기본값)"),
                QppEditorText.T("1024 件（約 8 KB）", "1024 entries (about 8 KB)", "1024개 (약 8 KB)"),
            };
        }

        private int chunks;
        private int capacity;
        private bool showInternal;

        private void OnEnable()
        {
            var setup = (QppSetup)target;
            chunks = Mathf.Clamp(setup.EditorLedgerChunksPerPen, 1, 32);
            capacity = setup.EditorLedgerChunkCapacity;
        }

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            QppEditorText.DrawLanguagePopup();
            var setup = (QppSetup)target;

            EditorGUILayout.LabelField(QppEditorText.T("ワールド内の文字の言語", "Language of the in-world text", "월드 내 문자 언어"), EditorStyles.boldLabel);
            string[] worldLanguages =
            {
                QppEditorText.T("自動（見る人の VRChat の言語）", "Auto (each viewer's VRChat language)", "자동 (보는 사람의 VRChat 언어)"),
                "日本語", "English", "한국어",
            };
            int worldLanguage = Mathf.Clamp(setup.EditorLanguageSetting, 0, 3);
            int nextWorldLanguage = EditorGUILayout.Popup(QppEditorText.T("表示言語", "Display language", "표시 언어"), worldLanguage, worldLanguages);
            if (nextWorldLanguage != worldLanguage)
            {
                Undo.RecordObject(setup, "Set In-world Language");
                setup.EditorLanguageSetting = nextWorldLanguage;
                EditorUtility.SetDirty(setup);
                QppSceneBinder.ApplyLanguageSetting(nextWorldLanguage);
            }
            EditorGUILayout.HelpBox(
                QppEditorText.T(
                    "「自動」のときは、ワールドに来た人の VRChat の言語に合わせます（日本語・韓国語。それ以外は English）。",
                    "With Auto, the text follows each visitor's VRChat language (Japanese or Korean; English for everything else).",
                    "'자동'일 때는 월드를 방문한 사람의 VRChat 언어에 맞춥니다(일본어·한국어. 그 외에는 English)."),
                MessageType.None);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(QppEditorText.T("台帳の容量（色・太さを切り替えられる回数）", "Ledger capacity (number of color / width switches)", "장부 용량 (색상·두께를 전환할 수 있는 횟수)"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                QppEditorText.T(
                    "ペン 1 本ごとに、色や太さを切り替えた回数だけを記録します（描いた線の数は、数えません）。\n" +
                    "満杯になったペンは、色・太さを切り替えられなくなります（今の色・太さのままなら描けます）。\n" +
                    "個別の台帳なので、ほかのペンの空きは使えません。",
                    "Each pen records only how many times its color or width was switched (the number of lines drawn is not counted).\n" +
                    "A pen whose ledger is full can no longer switch color or width (it can still draw with the current color and width).\n" +
                    "Each pen has its own ledger, so free space on other pens cannot be used.",
                    "펜마다 색상이나 두께를 전환한 횟수만 기록합니다(그린 선의 수는 세지 않습니다).\n" +
                    "장부가 가득 찬 펜은 색상·두께를 전환할 수 없습니다(현재 색상·두께 그대로라면 그릴 수 있습니다).\n" +
                    "펜별 개별 장부이므로 다른 펜의 남은 공간은 사용할 수 없습니다."), MessageType.None);

            chunks = EditorGUILayout.IntSlider(QppEditorText.T("ペンあたりのチャンク数", "Chunks per pen", "펜당 청크 수"), chunks, 1, 32);
            int index = System.Array.IndexOf(Capacities, capacity);
            if (index < 0) index = 2;
            index = EditorGUILayout.Popup(QppEditorText.T("チャンクの容量", "Chunk capacity", "청크 용량"), index, CapacityLabels());
            capacity = Capacities[index];

            int switches = chunks * capacity;
            EditorGUILayout.LabelField(QppEditorText.T("ペンあたりの切替回数", "Switches per pen", "펜당 전환 횟수"),
                QppEditorText.T(switches.ToString("N0") + " 回", switches.ToString("N0"), switches.ToString("N0") + "회"));
            EditorGUILayout.LabelField(QppEditorText.T("1 回の同期の最大量", "Max size per sync", "1회 동기화 최대량"),(capacity * 8 / 1024f).ToString("F1") + " KB");

            bool changed = chunks != setup.EditorLedgerChunksPerPen || capacity != setup.EditorLedgerChunkCapacity;
            using (new EditorGUI.DisabledScope(!changed))
            {
                if (GUILayout.Button(changed ? QppEditorText.T("シーンに適用（チャンクだけ更新）", "Apply to Scene (updates chunks only)", "씬에 적용 (청크만 업데이트)") : QppEditorText.T("適用済み", "Applied", "적용됨")))
                {
                    if (QppSceneBinder.ApplyChunkSettings(chunks, capacity))
                    {
                        chunks = Mathf.Clamp(setup.EditorLedgerChunksPerPen, 1, 32);
                        capacity = setup.EditorLedgerChunkCapacity;
                    }
                }
            }
            EditorGUILayout.HelpBox(QppEditorText.T("先に Setup を実行してから、変更してください。変更は、Play モードの外で行います。", "Run Setup first, then change these values. Changes must be made outside Play mode.", "먼저 Setup을 실행한 후 변경해 주세요. 변경은 Play 모드 밖에서 해야 합니다."),MessageType.None);

            EditorGUILayout.Space();
            showInternal = EditorGUILayout.Foldout(showInternal, QppEditorText.T("内部の参照（通常は触りません）", "Internal references (normally leave as is)", "내부 참조 (보통은 건드리지 않습니다)"), true);
            if (showInternal) DrawDefaultInspector();
        }
    }
}
#endif
