#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// ファイルや QvPen から読んだインクを一覧で見せ、選んだものだけをパレットに取り込む窓。
    /// 一覧の色・名前は取り込む前に手直しできる（元のファイルやペンは変わらない）。
    /// </summary>
    public class QppInkImportWindow : EditorWindow
    {
        private QppPalette target;
        private string sourceLabel;
        private List<QppInk> candidates = new List<QppInk>();
        private List<bool> selected = new List<bool>();
        private List<bool> materialMissing = new List<bool>();
        private List<string> missingMaterials = new List<string>();
        private bool autoTag;
        private bool replace;
        private Vector2 scroll;

        public static void Open(QppPalette target, string sourceLabel, List<QppInk> inks, List<bool> materialMissing,
            List<string> missingMaterials, bool autoTag)
        {
            var w = GetWindow<QppInkImportWindow>(true, "インクの取り込み");
            w.target = target;
            w.sourceLabel = sourceLabel;
            w.candidates = inks;
            w.selected = new List<bool>();
            for (int i = 0; i < inks.Count; i++) w.selected.Add(true);
            w.materialMissing = materialMissing ?? new List<bool>();
            w.missingMaterials = missingMaterials ?? new List<string>();
            w.autoTag = autoTag;
            w.replace = false;
            w.scroll = Vector2.zero;
            w.minSize = new Vector2(620, 420);
            w.Show();
        }

        private void OnGUI()
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("取り込み先のパレットがありません。", MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField("読み込み元", sourceLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("取り込み先", target.name);
            if (missingMaterials.Count > 0)
                EditorGUILayout.HelpBox(
                    "このプロジェクトで見つからない Material があります（" + string.Join(", ", missingMaterials) + "）。" +
                    "該当するインクは QvPen の標準インクで取り込みます。取り込み後に Material を指定し直してください。",
                    MessageType.Warning);

            int count = 0;
            foreach (bool s in selected)
                if (s) count++;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべて選択", GUILayout.Width(90))) SetAll(true);
            if (GUILayout.Button("すべて解除", GUILayout.Width(90))) SetAll(false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"{count} / {candidates.Count} 個を選択", GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < candidates.Count; i++)
            {
                var ink = candidates[i];
                EditorGUILayout.BeginHorizontal();
                selected[i] = EditorGUILayout.Toggle(selected[i], GUILayout.Width(18));
                ink.name = EditorGUILayout.TextField(ink.name, GUILayout.Width(120));
                ink.gradient = EditorGUILayout.GradientField(ink.gradient, GUILayout.MinWidth(100));
                bool missing = i < materialMissing.Count && materialMissing[i];
                string mat = ink.material != null ? ink.material.name : "(標準インク)";
                if (ink.questMaterial != null) mat += " / Quest: " + ink.questMaterial.name;
                EditorGUILayout.LabelField((missing ? "⚠ " : "") + mat, GUILayout.Width(170));
                EditorGUILayout.LabelField(ink.tags.Count > 0 ? string.Join(", ", ink.tags) : "-", GUILayout.Width(130));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            autoTag = EditorGUILayout.ToggleLeft("色から自動でタグを付ける（パレットにある既定タグ名のみ）", autoTag);
            replace = EditorGUILayout.ToggleLeft("今のインクをすべて消して置き換える（オフなら末尾に追加）", replace);

            int existing = target.EntryCount;
            int after = replace ? count : existing + count;
            if (after > QppInkData.MaxInks)
                EditorGUILayout.HelpBox($"インクは最大 {QppInkData.MaxInks} 個です。超えた分は取り込みません。", MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("やめる", GUILayout.Width(90))) Close();
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button($"{count} 個を取り込む", GUILayout.Width(140))) DoImport();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SetAll(bool value)
        {
            for (int i = 0; i < selected.Count; i++) selected[i] = value;
        }

        private void DoImport()
        {
            if (replace && target.EntryCount > 0 &&
                !EditorUtility.DisplayDialog("QvPalette",
                    $"今のインク {target.EntryCount} 個をすべて消して置き換えます。よろしいですか？", "置き換える", "やめる"))
                return;

            var picked = new List<QppInk>();
            for (int i = 0; i < candidates.Count; i++)
                if (selected[i]) picked.Add(candidates[i]);

            string[] tagNames = target.EditorTagNames ?? new string[0];
            var current = QppInkData.Read(target);
            string[] mergedTags;
            List<string> dropped;
            var merged = QppInkData.Merge(current, tagNames, picked, replace, out mergedTags, out dropped);
            if (autoTag)
            {
                for (int i = replace ? 0 : current.Count; i < merged.Count; i++)
                    QppInkData.ApplyAutoTags(merged[i], mergedTags);
            }

            QppInkData.Write(target, merged, mergedTags, "Import Palette Inks");
            int added = merged.Count - (replace ? 0 : current.Count);
            string message = $"{added} 個のインクを取り込みました。";
            if (dropped.Count > 0)
                message += "\nタグが 27 個を超えるため、次のタグは付けていません: " + string.Join(", ", dropped);
            EditorUtility.DisplayDialog("QvPalette", message, "OK");
            Close();
        }
    }
}
#endif
