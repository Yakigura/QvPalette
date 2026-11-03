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
        private static readonly string[] CapacityLabels =
        {
            "128 件（約 1 KB）", "256 件（約 2 KB）", "512 件（約 4 KB、既定）", "1024 件（約 8 KB）",
        };

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
            var setup = (QppSetup)target;

            EditorGUILayout.LabelField("台帳の容量（色・太さを切り替えられる回数）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "ペン 1 本ごとに、色や太さを切り替えた回数だけを記録します（描いた線の数は、数えません）。\n" +
                "満杯になったペンは、色・太さを切り替えられなくなります（今の色・太さのままなら描けます）。\n" +
                "個別の台帳なので、ほかのペンの空きは使えません。", MessageType.None);

            chunks = EditorGUILayout.IntSlider("ペンあたりのチャンク数", chunks, 1, 32);
            int index = System.Array.IndexOf(Capacities, capacity);
            if (index < 0) index = 2;
            index = EditorGUILayout.Popup("チャンクの容量", index, CapacityLabels);
            capacity = Capacities[index];

            int switches = chunks * capacity;
            EditorGUILayout.LabelField("ペンあたりの切替回数", switches.ToString("N0") + " 回");
            EditorGUILayout.LabelField("1 回の同期の最大量", (capacity * 8 / 1024f).ToString("F1") + " KB");

            bool changed = chunks != setup.EditorLedgerChunksPerPen || capacity != setup.EditorLedgerChunkCapacity;
            using (new EditorGUI.DisabledScope(!changed))
            {
                if (GUILayout.Button(changed ? "シーンに適用（チャンクだけ更新）" : "適用済み"))
                {
                    if (QppSceneBinder.ApplyChunkSettings(chunks, capacity))
                    {
                        chunks = Mathf.Clamp(setup.EditorLedgerChunksPerPen, 1, 32);
                        capacity = setup.EditorLedgerChunkCapacity;
                    }
                }
            }
            EditorGUILayout.HelpBox("先に Setup を実行してから、変更してください。変更は、Play モードの外で行います。", MessageType.None);

            EditorGUILayout.Space();
            showInternal = EditorGUILayout.Foldout(showInternal, "内部の参照（通常は触りません）", true);
            if (showInternal) DrawDefaultInspector();
        }
    }
}
#endif
