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
            var w = GetWindow<QppInkImportWindow>(true, QppEditorText.T("インクの取り込み", "Import Inks", "잉크 가져오기"));
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
                EditorGUILayout.HelpBox(QppEditorText.T("取り込み先のパレットがありません。", "There is no palette to import into.", "가져올 대상 팔레트가 없습니다."),MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(QppEditorText.T("読み込み元", "Source", "불러온 위치"), sourceLabel, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(QppEditorText.T("取り込み先", "Import to", "가져올 대상"), target.name);
            if (missingMaterials.Count > 0)
                EditorGUILayout.HelpBox(
                    QppEditorText.T(
                        "このプロジェクトで見つからない Material があります（" + string.Join(", ", missingMaterials) + "）。" +
                        "該当するインクは QvPen の標準インクで取り込みます。取り込み後に Material を指定し直してください。",
                        "Some Materials were not found in this project (" + string.Join(", ", missingMaterials) + "). " +
                        "Those inks are imported as the standard QvPen ink. Please set the Material again after importing.",
                        "이 프로젝트에서 찾을 수 없는 Material이 있습니다(" + string.Join(", ", missingMaterials) + "). " +
                        "해당 잉크는 QvPen의 기본 잉크로 가져옵니다. 가져온 후 Material을 다시 지정해 주세요."),
                    MessageType.Warning);

            int count = 0;
            foreach (bool s in selected)
                if (s) count++;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(QppEditorText.T("すべて選択", "Select All", "모두 선택"),GUILayout.Width(90))) SetAll(true);
            if (GUILayout.Button(QppEditorText.T("すべて解除", "Deselect All", "모두 해제"),GUILayout.Width(90))) SetAll(false);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(QppEditorText.T($"{count} / {candidates.Count} 個を選択", $"{count} / {candidates.Count} selected", $"{count} / {candidates.Count}개 선택"),GUILayout.Width(140));
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
                string mat = ink.material != null ? ink.material.name : QppEditorText.T("(標準インク)", "(Standard ink)", "(기본 잉크)");
                if (ink.questMaterial != null) mat += " / Quest: " + ink.questMaterial.name;
                EditorGUILayout.LabelField((missing ? "⚠ " : "") + mat, GUILayout.Width(170));
                EditorGUILayout.LabelField(ink.tags.Count > 0 ? string.Join(", ", ink.tags) : "-", GUILayout.Width(130));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            autoTag = EditorGUILayout.ToggleLeft(QppEditorText.T("色から自動でタグを付ける（パレットにある既定タグ名のみ）", "Add tags automatically from color (default tag names in the palette only)", "색상으로 태그 자동 지정(팔레트에 있는 기본 태그 이름만)"),autoTag);
            replace = EditorGUILayout.ToggleLeft(QppEditorText.T("今のインクをすべて消して置き換える（オフなら末尾に追加）", "Delete all current inks and replace them (if off, append to the end)", "현재 잉크를 모두 삭제하고 교체(끄면 끝에 추가)"),replace);

            int existing = target.EntryCount;
            int after = replace ? count : existing + count;
            if (after > QppInkData.MaxInks)
                EditorGUILayout.HelpBox(QppEditorText.T($"インクは最大 {QppInkData.MaxInks} 個です。超えた分は取り込みません。", $"The maximum is {QppInkData.MaxInks} inks. Any extra inks will not be imported.", $"잉크는 최대 {QppInkData.MaxInks}개입니다. 초과한 분은 가져오지 않습니다."),MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(QppEditorText.T("やめる", "Cancel", "취소"), GUILayout.Width(90))) Close();
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (GUILayout.Button(QppEditorText.T($"{count} 個を取り込む", $"Import {count}", $"{count}개 가져오기"),GUILayout.Width(140))) DoImport();
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
                    QppEditorText.T($"今のインク {target.EntryCount} 個をすべて消して置き換えます。よろしいですか？",
                        $"This will delete all {target.EntryCount} current inks and replace them. Continue?",
                        $"현재 잉크 {target.EntryCount}개를 모두 삭제하고 교체합니다. 계속하시겠습니까?"),
                    QppEditorText.T("置き換える", "Replace", "교체"), QppEditorText.T("やめる", "Cancel", "취소")))
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
            string message = QppEditorText.T($"{added} 個のインクを取り込みました。", $"Imported {added} inks.", $"잉크 {added}개를 가져왔습니다.");
            if (dropped.Count > 0)
                message += QppEditorText.T("\nタグが 27 個を超えるため、次のタグは付けていません: " + string.Join(", ", dropped),
                    "\nThe following tags were not added because there are more than 27 tags: " + string.Join(", ", dropped),
                    "\n태그가 27개를 초과하여 다음 태그는 지정하지 않았습니다: " + string.Join(", ", dropped));
            EditorUtility.DisplayDialog("QvPalette", message, "OK");
            Close();
        }
    }
}
#endif
