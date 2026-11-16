#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// パレット編集ウィンドウ。
    ///
    /// 1 インク = 1 行で「色（グラデーション）」「Material」「タグ」を並べて設定する。
    /// 単色は「両端が同じ色のグラデーション」として扱う。Material は各インクに直接指定し、
    /// Udon 用のマテリアルバンク（重複をまとめた一覧と番号）は書き込み時に自動で作る。
    /// 並び順がそのままパネルの表示順（インク番号）になる。
    /// </summary>
    public class QppPaletteEditor : EditorWindow
    {
        private const int FilterAll = -1;
        private const int FilterUntagged = -2;

        private QppPalette targetPalette;
        private QppPalette loadedPalette;
        private List<QppInk> inks = new List<QppInk>();
        private string[] tagNames = new string[0];
        private readonly List<int> view = new List<int>();
        private ReorderableList list;
        private int filter = FilterAll;
        private Vector2 scrollPos;
        private float scrollViewHeight = 400f;
        private bool writing;
        private QppInk previewInk;
        private QppPalette previewPalette;
        private QppInkPreviewRenderer previewRenderer;
        private int previewWidthIndex = QppCommon.DEFAULT_WIDTH_INDEX;
        private int previewBackground = 1;
        private bool previewQuest;

        private bool tagsFoldout = true;
        private bool qvpenFoldout = true;
        private bool hsvFoldout;
        private int generateColorCount = 64;
        private GameObject qvpenSource;
        private Material bulkMaterial;
        private Material bulkQuestMaterial;
        // 複数選択（インク番号の集合）。ReorderableList の標準の複数選択（Ctrl+クリックで追加、Shift+クリックで範囲）から読む。
        private readonly HashSet<int> picked = new HashSet<int>();

        [MenuItem("Tools/Yakigura/QvPalette/Palette Editor", false, 20)]
        public static void ShowWindow()
        {
            GetWindow<QppPaletteEditor>("QPP Palette Editor");
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            Undo.postprocessModifications += OnExternalModifications;
            QppInkData.Changed += OnPaletteChanged;
            loadedPalette = null;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.postprocessModifications -= OnExternalModifications;
            QppInkData.Changed -= OnPaletteChanged;
            CleanupPreview();
        }

        private void OnDestroy() => CleanupPreview();

        private void CleanupPreview()
        {
            if (previewRenderer == null) return;
            previewRenderer.Dispose();
            previewRenderer = null;
        }

        private void OnInspectorUpdate() => Repaint();

        private void OnUndoRedo()
        {
            loadedPalette = null;
            Repaint();
        }

        private void OnPaletteChanged(QppPalette palette)
        {
            if (writing || palette != targetPalette) return;
            loadedPalette = null;
            Repaint();
        }

        // Inspector など、この窓以外でパレットが書き換わったとき。
        // （フォーカスが戻るたびに読み直すと、グラデーション編集窓から戻ったときに編集前の値へ戻ってしまう）
        private UndoPropertyModification[] OnExternalModifications(UndoPropertyModification[] mods)
        {
            if (targetPalette != null)
            {
                foreach (var m in mods)
                {
                    if (m.currentValue != null && m.currentValue.target == targetPalette)
                    {
                        loadedPalette = null;
                        Repaint();
                        break;
                    }
                }
            }
            return mods;
        }

        // ------------------------------------------------------------------
        // 読み込み / 書き込み
        // ------------------------------------------------------------------

        private void Reload()
        {
            bool newPalette = previewPalette != targetPalette;
            int previousCount = inks.Count;
            int previousPreviewIndex = newPalette ? -1 : inks.IndexOf(previewInk);
            EnsurePerEntryGradients(targetPalette);
            EnsureTags(targetPalette);
            inks = QppInkData.Read(targetPalette);
            if (newPalette || inks.Count != previousCount) ClearPicked();
            previewInk = previousPreviewIndex >= 0 && previousPreviewIndex < inks.Count ? inks[previousPreviewIndex] : null;
            if (newPalette)
            {
                previewWidthIndex = targetPalette.DefaultWidthIndex;
                previewPalette = targetPalette;
            }
            tagNames = (string[])(targetPalette.EditorTagNames ?? new string[0]).Clone();
            loadedPalette = targetPalette;
            if (filter >= tagNames.Length) filter = FilterAll;
            RebuildView();
        }

        private void Commit(string undoName = "Edit Palette Inks")
        {
            writing = true;
            try
            {
                QppInkData.Write(targetPalette, inks, tagNames, undoName);
            }
            finally
            {
                writing = false;
            }
        }

        private void RebuildView()
        {
            view.Clear();
            for (int i = 0; i < inks.Count; i++)
                if (MatchesFilter(inks[i])) view.Add(i);

            if (list == null)
            {
                list = new ReorderableList(view, typeof(int), true, false, true, false);
                list.elementHeight = ElementHeight;
                list.elementHeightCallback = index =>
                    ElementHeightOf(index < view.Count && QppInkData.IsOutlined(inks[view[index]].material));
                list.drawElementCallback = DrawElement;
                list.multiSelect = true;
                list.onReorderCallbackWithDetails = OnReorder;
                list.onAddCallback = _ => AddInk();
                list.onSelectCallback = l =>
                {
                    if (l.index >= 0 && l.index < view.Count) previewInk = inks[view[l.index]];
                    SyncPicked();
                    Repaint();
                };
            }
            list.list = view;
            list.draggable = filter == FilterAll;
        }

        private bool MatchesFilter(QppInk ink)
        {
            if (filter == FilterAll) return true;
            if (filter == FilterUntagged) return ink.tags.Count == 0;
            return filter < tagNames.Length && ink.tags.Contains(tagNames[filter]);
        }

        // ------------------------------------------------------------------
        // 画面
        // ------------------------------------------------------------------

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("QvPalette - Palette Editor", EditorStyles.boldLabel);
            QppEditorText.DrawLanguagePopup();

            var pickedPalette = (QppPalette)EditorGUILayout.ObjectField("Target Palette", targetPalette, typeof(QppPalette), true);
            if (pickedPalette != targetPalette) targetPalette = pickedPalette;
            if (targetPalette == null) targetPalette = FindObjectOfType<QppPalette>();
            if (targetPalette == null)
            {
                EditorGUILayout.HelpBox(QppEditorText.T("Scene内に QppPalette が見つかりません。Prefabを配置するか選択してください。", "No QppPalette found in the scene. Place the Prefab or select one.", "씬에서 QppPalette를 찾을 수 없어요. Prefab을 배치하거나 선택해 주세요."), MessageType.Warning);
                return;
            }
            if (loadedPalette != targetPalette || inks.Count != targetPalette.EntryCount) Reload();

            SyncPicked();

            // Delete キーで、選んだインクを消す（名前などを書き換えている最中は、文字の削除として扱う）。
            var keyEvent = Event.current;
            if (keyEvent.type == EventType.KeyDown && keyEvent.keyCode == KeyCode.Delete && picked.Count > 0 &&
                !EditorGUIUtility.editingTextField)
            {
                keyEvent.Use();
                DeletePicked();
                GUIUtility.ExitGUI();
            }

            DrawFileBar();
            DrawQvPenSection();
            DrawTagSection();
            DrawHsvSection();
            DrawInkList();
            DrawPreview();
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(QppEditorText.T("プレビュー", "Preview", "미리보기"), EditorStyles.boldLabel);
            if (previewInk == null || !inks.Contains(previewInk))
                previewInk = view.Count > 0 ? inks[view[0]] : null;
            if (previewInk == null)
            {
                EditorGUILayout.HelpBox(QppEditorText.T("インクを選択してください。", "Select an ink.", "잉크를 선택해 주세요."), MessageType.None);
                return;
            }

            EditorGUILayout.LabelField(QppEditorText.T("選択中", "Selected", "선택됨"), string.IsNullOrEmpty(previewInk.name)
                ? QppEditorText.T($"インク {inks.IndexOf(previewInk)}", $"Ink {inks.IndexOf(previewInk)}", $"잉크 {inks.IndexOf(previewInk)}") : $"{inks.IndexOf(previewInk)}: {previewInk.name}");
            EditorGUILayout.BeginHorizontal();
            previewQuest = EditorGUILayout.ToggleLeft("Quest", previewQuest, GUILayout.Width(65f));
            previewBackground = EditorGUILayout.Popup(QppEditorText.T("背景", "Background", "배경"), previewBackground, new[] { QppEditorText.T("暗い", "Dark", "어두움"), QppEditorText.T("中間", "Medium", "중간"), QppEditorText.T("明るい", "Light", "밝음") });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            previewWidthIndex = EditorGUILayout.IntSlider(QppEditorText.T("太さ", "Width", "두께"), previewWidthIndex, 0, QppCommon.MAX_WIDTH_INDEX);
            EditorGUILayout.LabelField(targetPalette.MillimetersTextOf(previewWidthIndex), GUILayout.Width(75f));
            EditorGUILayout.EndHorizontal();

            Rect rect = GUILayoutUtility.GetRect(1f, 110f, GUILayout.ExpandWidth(true));
            Material material = QppInkPreviewLogic.ResolveMaterial(previewInk, previewQuest);
            if (!QppInkPreviewLogic.HasMaterial(material))
            {
                EditorGUI.DrawRect(rect, QppInkPreviewLogic.BackgroundColor(previewBackground));
                GUI.Label(rect, QppEditorText.T("Material 未設定", "Material not set", "Material 미설정"), EditorStyles.centeredGreyMiniLabel);
                return;
            }
            if (Event.current.type != EventType.Repaint) return;
            if (previewRenderer == null) previewRenderer = new QppInkPreviewRenderer();
            bool rounded = QppInkData.IsRoundedTrail(previewInk.material);
            if (previewQuest)
            {
                rounded = rounded && targetPalette.EditorAllowRoundedOnQuest;
            }
            float width = QppInkPreviewLogic.WidthOf(previewWidthIndex, targetPalette.MinInkWidth, targetPalette.MaxInkWidth);
            var texture = previewRenderer.Render(previewInk, previewQuest, rounded, width, previewBackground);
            if (texture != null)
            {
                EditorGUI.DrawRect(rect, QppInkPreviewLogic.BackgroundColor(previewBackground));
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, false);
            }
        }

        private void DrawFileBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(QppEditorText.T($"インク {inks.Count} / {QppInkData.MaxInks}", $"Inks {inks.Count} / {QppInkData.MaxInks}", $"잉크 {inks.Count} / {QppInkData.MaxInks}"), EditorStyles.boldLabel, GUILayout.Width(140));
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(inks.Count >= QppInkData.MaxInks))
            {
                if (GUILayout.Button(QppEditorText.T("＋ インク追加", "+ Add ink", "+ 잉크 추가"), GUILayout.Width(100))) AddInk();
            }
            if (GUILayout.Button(QppEditorText.T("ファイルに書き出す", "Export to file", "파일로 내보내기"), GUILayout.Width(120))) ExportFile(inks, tagNames, "palette");
            if (GUILayout.Button(QppEditorText.T("ファイルから取り込む…", "Import from file...", "파일에서 가져오기..."), GUILayout.Width(130))) ImportFile();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawQvPenSection()
        {
            qvpenFoldout = EditorGUILayout.Foldout(qvpenFoldout, QppEditorText.T("QvPen のインクを使う", "Use QvPen inks", "QvPen 잉크 사용"), true, EditorStyles.foldoutHeader);
            if (!qvpenFoldout) return;

            EditorGUI.indentLevel++;
            qvpenSource = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent(QppEditorText.T("読み込み元", "Source", "가져올 대상"), QppEditorText.T("QvPen のペンや、ペンをまとめた Prefab。空ならシーン内のすべてのペン。", "A QvPen pen or a Prefab that bundles pens. If empty, all pens in the scene.", "QvPen 펜 또는 펜을 모아 둔 Prefab. 비어 있으면 씬의 모든 펜.")),
                qvpenSource, typeof(GameObject), true);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button(QppEditorText.T("インクを選んで取り込む…", "Choose inks to import...", "잉크를 선택해 가져오기..."))) ImportQvPen();
            if (GUILayout.Button(QppEditorText.T("ファイルに書き出す", "Export to file", "파일로 내보내기"))) ExportQvPen();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                QppEditorText.T(
                    "QvPen の各ペンの色（グラデーション）と Material を読みます。同じ色・同じ Material のペンは 1 つにまとめます。" +
                    "シーンのペンの Material は再バインド時にパレット先頭のものへ書き換わるため、元の Material を使いたいときは" +
                    "ペンの Prefab を読み込み元に指定してください。",
                    "Reads the color (gradient) and Material of each QvPen pen. Pens with the same color and Material are merged into one. " +
                    "Because the Material of scene pens is overwritten with the first palette Material on rebind, " +
                    "specify the pen Prefab as the source if you want to keep the original Material.",
                    "QvPen 각 펜의 색(그라디언트)과 Material을 읽어요. 같은 색, 같은 Material의 펜은 하나로 합쳐요. " +
                    "씬 펜의 Material은 다시 바인딩할 때 팔레트 맨 앞의 것으로 바뀌므로, 원래 Material을 쓰려면 " +
                    "펜의 Prefab을 가져올 대상으로 지정해 주세요."),
                MessageType.None);
            EditorGUI.indentLevel--;
        }

        private void DrawTagSection()
        {
            tagsFoldout = EditorGUILayout.Foldout(tagsFoldout, QppEditorText.T($"タグ（{tagNames.Length} / {QppPalette.MAX_TAGS}、パネルで絞り込み）", $"Tags ({tagNames.Length} / {QppPalette.MAX_TAGS}, filter on panel)", $"태그 ({tagNames.Length} / {QppPalette.MAX_TAGS}, 패널에서 필터링)"), true,
                EditorStyles.foldoutHeader);
            if (!tagsFoldout) return;

            EditorGUI.indentLevel++;
            for (int t = 0; t < tagNames.Length; t++)
            {
                int used = 0;
                foreach (var ink in inks)
                    if (ink.tags.Contains(tagNames[t])) used++;

                EditorGUILayout.BeginHorizontal();
                string renamed = EditorGUILayout.DelayedTextField(QppEditorText.T($"タグ {t + 1}", $"Tag {t + 1}", $"태그 {t + 1}"), tagNames[t]);
                EditorGUILayout.LabelField(QppEditorText.T($"{used} 個", $"{used} inks", $"{used}개"), GUILayout.Width(50));
                bool remove = GUILayout.Button("×", GUILayout.Width(22));
                EditorGUILayout.EndHorizontal();

                if (renamed != tagNames[t] && !string.IsNullOrEmpty(renamed) &&
                    System.Array.IndexOf(tagNames, renamed) < 0)
                {
                    foreach (var ink in inks)
                    {
                        int k = ink.tags.IndexOf(tagNames[t]);
                        if (k >= 0) ink.tags[k] = renamed;
                    }
                    tagNames[t] = renamed;
                    Commit("Rename Palette Tag");
                }
                if (remove && (used == 0 || EditorUtility.DisplayDialog("QvPalette",
                        QppEditorText.T($"タグ「{tagNames[t]}」を削除し、{used} 個のインクから外します。よろしいですか？", $"Delete tag '{tagNames[t]}' and remove it from {used} inks. Are you sure?", $"태그 '{tagNames[t]}'을(를) 삭제하고 잉크 {used}개에서 제거해요. 계속할까요?"), QppEditorText.T("削除", "Delete", "삭제"), QppEditorText.T("やめる", "Cancel", "취소"))))
                {
                    foreach (var ink in inks) ink.tags.Remove(tagNames[t]);
                    var rest = new List<string>(tagNames);
                    rest.RemoveAt(t);
                    tagNames = rest.ToArray();
                    filter = FilterAll;
                    Commit("Remove Palette Tag");
                    RebuildView();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            using (new EditorGUI.DisabledScope(tagNames.Length >= QppPalette.MAX_TAGS))
            {
                if (GUILayout.Button(QppEditorText.T("＋ タグ追加", "+ Add tag", "+ 태그 추가")))
                {
                    var more = new List<string>(tagNames) { UniqueTagName(QppEditorText.T("新しいタグ", "New tag", "새 태그")) };
                    tagNames = more.ToArray();
                    Commit("Add Palette Tag");
                }
            }
            if (GUILayout.Button(QppEditorText.T("色から自動タグ付け", "Auto-tag by color", "색으로 자동 태그")))
            {
                if (EditorUtility.DisplayDialog("QvPalette",
                        QppEditorText.T("すべてのインクの既定タグ（赤・青・パステルなど）を色から付け直します。自分で作ったタグはそのままです。", "Re-apply the default tags (red, blue, pastel, etc.) to all inks based on color. Tags you created yourself are kept.", "모든 잉크의 기본 태그(빨강, 파랑, 파스텔 등)를 색에 따라 다시 붙여요. 직접 만든 태그는 그대로 둬요."),
                        QppEditorText.T("付け直す", "Re-apply", "다시 붙이기"), QppEditorText.T("やめる", "Cancel", "취소")))
                {
                    foreach (var ink in inks) QppInkData.ApplyAutoTags(ink, tagNames);
                    Commit("Auto Tag Palette");
                    RebuildView();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                QppEditorText.T(
                    "タグは最大 27 個（パネルでは 9 個ずつ 3 ページ）。パネル右端のボタンで、そのタグのインクだけを表示します。" +
                    "自動タグ付けは既定のタグ名（" + string.Join(" / ", DefaultTagNames) + "）のうち、ここにあるものだけを付けます。",
                    "Up to 27 tags (3 pages of 9 on the panel). The button at the right edge of the panel shows only the inks with that tag. " +
                    "Auto-tagging only applies the default tag names (" + string.Join(" / ", DefaultTagNames) + ") that exist here.",
                    "태그는 최대 27개예요(패널에서는 9개씩 3페이지). 패널 오른쪽 끝의 버튼으로 해당 태그의 잉크만 표시해요. " +
                    "자동 태그는 기본 태그 이름(" + string.Join(" / ", DefaultTagNames) + ") 중 여기에 있는 것만 붙여요."),
                MessageType.None);
            EditorGUI.indentLevel--;
        }

        private string UniqueTagName(string baseName)
        {
            string name = baseName;
            for (int n = 2; System.Array.IndexOf(tagNames, name) >= 0; n++) name = baseName + n;
            return name;
        }

        private void DrawHsvSection()
        {
            hsvFoldout = EditorGUILayout.Foldout(hsvFoldout, QppEditorText.T("補助: HSV で一括生成（すべて置き換え）", "Helper: Generate all from HSV (replaces everything)", "보조: HSV로 일괄 생성(모두 교체)"), true, EditorStyles.foldoutHeader);
            if (!hsvFoldout) return;

            EditorGUI.indentLevel++;
            generateColorCount = EditorGUILayout.IntSlider(QppEditorText.T("生成色数", "Color count", "생성 색 수"), generateColorCount, 16, QppInkData.MaxInks);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button(QppEditorText.T($"HSVカラー一括生成 ({generateColorCount}色)", $"Generate HSV colors ({generateColorCount})", $"HSV 색 일괄 생성 ({generateColorCount}색)")) &&
                EditorUtility.DisplayDialog("QvPalette",
                    QppEditorText.T("現在のインクをすべて置き換えます。よろしいですか？", "This replaces all current inks. Are you sure?", "현재 잉크를 모두 교체해요. 계속할까요?"), QppEditorText.T("置き換える", "Replace", "교체"), QppEditorText.T("やめる", "Cancel", "취소")))
            {
                GenerateHSVColors(targetPalette, generateColorCount);
                loadedPalette = null;
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel--;
        }

        private void DrawInkList()
        {
            tagRowsCache = -1;
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            var options = new List<string> { QppEditorText.T("すべて", "All", "전체") };
            options.AddRange(tagNames);
            options.Add(QppEditorText.T("タグなし", "No tag", "태그 없음"));
            int popupIndex = filter == FilterAll ? 0 : filter == FilterUntagged ? options.Count - 1 : filter + 1;
            int newIndex = EditorGUILayout.Popup(QppEditorText.T("表示するインク", "Inks to show", "표시할 잉크"), popupIndex, options.ToArray());
            EditorGUILayout.EndHorizontal();
            if (newIndex != popupIndex)
            {
                filter = newIndex == 0 ? FilterAll : newIndex == options.Count - 1 ? FilterUntagged : newIndex - 1;
                RebuildView();
            }

            EditorGUILayout.BeginHorizontal();
            bulkMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent(QppEditorText.T("Material 一括設定", "Set Material in bulk", "Material 일괄 설정"), QppEditorText.T("表示中のインクすべての Material を変える。", "Changes the Material of all shown inks.", "표시 중인 모든 잉크의 Material을 바꿔요.")), bulkMaterial, typeof(Material), false);
            bulkQuestMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("", QppEditorText.T("Quest 用（空なら PC と同じ）", "For Quest (same as PC if empty)", "Quest용 (비어 있으면 PC와 동일)")), bulkQuestMaterial, typeof(Material), false, GUILayout.Width(140));
            using (new EditorGUI.DisabledScope(bulkMaterial == null || picked.Count == 0))
            {
                if (GUILayout.Button(QppEditorText.T($"選択中の {picked.Count} 個に適用", $"Apply to {picked.Count} selected", $"선택한 {picked.Count}개에 적용"), GUILayout.Width(130)))
                {
                    foreach (int i in picked)
                    {
                        if (i < 0 || i >= inks.Count) continue;
                        inks[i].material = bulkMaterial;
                        inks[i].questMaterial = bulkQuestMaterial;
                    }
                    Commit("Set Palette Materials");
                }
            }
            using (new EditorGUI.DisabledScope(bulkMaterial == null || view.Count == 0))
            {
                if (GUILayout.Button(QppEditorText.T($"表示中の {view.Count} 個に適用", $"Apply to {view.Count} shown", $"표시 중인 {view.Count}개에 적용"), GUILayout.Width(130)))
                {
                    foreach (int i in view)
                    {
                        inks[i].material = bulkMaterial;
                        inks[i].questMaterial = bulkQuestMaterial;
                    }
                    Commit("Set Palette Materials");
                }
            }
            EditorGUILayout.EndHorizontal();

            // 複数選択の操作。
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                new GUIContent(QppEditorText.T($"選択中 {picked.Count} 個", $"Selected {picked.Count}", $"선택됨 {picked.Count}개"), QppEditorText.T("行をクリックで選択。Ctrl+クリックで追加・解除、Shift+クリックで範囲。Delete キーで削除。", "Click a row to select. Ctrl+click to add/remove, Shift+click for a range. Delete key to delete.", "행을 클릭해 선택해요. Ctrl+클릭으로 추가/해제, Shift+클릭으로 범위 선택. Delete 키로 삭제해요.")),
                GUILayout.Width(100));
            if (GUILayout.Button(QppEditorText.T("表示中をすべて選択", "Select all shown", "표시 중인 항목 모두 선택"), GUILayout.Width(130)) && list != null && view.Count > 0)
            {
                list.SelectRange(0, view.Count - 1);
                SyncPicked();
            }
            using (new EditorGUI.DisabledScope(picked.Count == 0))
            {
                if (GUILayout.Button(QppEditorText.T("選択を解除", "Deselect", "선택 해제"), GUILayout.Width(90))) ClearPicked();
                if (GUILayout.Button(QppEditorText.T("選択を削除（Delete キー）", "Delete selected (Delete key)", "선택 항목 삭제 (Delete 키)"), GUILayout.Width(170))) DeletePicked();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                filter == FilterAll
                    ? QppEditorText.T(
                        "左端の ≡ をドラッグするか、番号を書き換えると並べ替えられます。並び順がパネルの表示順です。" +
                        "行をクリックで選択（Ctrl+クリックで追加、Shift+クリックで範囲）。選んだインクは、Delete キーで削除、Material は「選択中の個数に適用」でまとめて変えられます。" +
                        "公開済みのワールドで並べ替え・削除すると、プレイヤーが保存したお気に入りが別のインクを指すことがあります。",
                        "Drag the ≡ at the left edge or edit the number to reorder. The order is the display order on the panel. " +
                        "Click a row to select (Ctrl+click to add, Shift+click for a range). Selected inks can be deleted with the Delete key, and their Material changed together with \"Apply to selected\". " +
                        "Reordering or deleting in a published world may make players' saved favorites point to different inks.",
                        "왼쪽 끝의 ≡를 드래그하거나 번호를 고치면 순서를 바꿀 수 있어요. 순서가 패널의 표시 순서예요. " +
                        "행을 클릭해 선택해요(Ctrl+클릭으로 추가, Shift+클릭으로 범위). 선택한 잉크는 Delete 키로 삭제하고, Material은 \"선택한 항목에 적용\"으로 한꺼번에 바꿀 수 있어요. " +
                        "공개된 월드에서 순서를 바꾸거나 삭제하면, 플레이어가 저장한 즐겨찾기가 다른 잉크를 가리킬 수 있어요.")
                    : QppEditorText.T("絞り込み中はドラッグで並べ替えできません（番号の書き換えはできます）。", "Drag reordering is unavailable while filtering (you can still edit the number).", "필터링 중에는 드래그로 순서를 바꿀 수 없어요(번호는 고칠 수 있어요)."),
                MessageType.None);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            list.DoLayoutList();
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) scrollViewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float ElementHeight => Line * 3f + 10f;
        private static float OutlinedElementHeight => Line * 4f + 13f;

        // タグのボタンは、幅に入らないときは次の行へ折り返す。行数はどのインクでも同じなので、描画ごとに 1 回だけ数える。
        private int tagRowsCache = -1;

        private static float TagButtonWidth(string tagName)
        {
            return Mathf.Max(36f, EditorStyles.miniButton.CalcSize(new GUIContent(tagName)).x + 14f);
        }

        /// <summary>タグのボタンを幅 width に並べたときの行数（最低 1）。</summary>
        private int TagRows(float width)
        {
            int rows = 1;
            float x = 0f;
            for (int t = 0; t < tagNames.Length; t++)
            {
                float w = TagButtonWidth(tagNames[t]);
                if (x > 0f && x + w > width)
                {
                    rows++;
                    x = 0f;
                }
                x += w + 2f;
            }
            return rows;
        }

        private float ElementHeightOf(bool outlined)
        {
            // 行の幅は、描画側の rect.width より少し狭めに見積もる（見積もりが多めの行数になるだけで、はみ出さない）。
            if (tagRowsCache < 0) tagRowsCache = TagRows(Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 70f));
            return (outlined ? OutlinedElementHeight : ElementHeight) + (tagRowsCache - 1) * (Line + 2f);
        }

        private void DrawElement(Rect rect, int viewIndex, bool isActive, bool isFocused)
        {
            // 見えていない行は描かない（4096 個でも重くならないように）。
            if (rect.yMax < scrollPos.y - rect.height ||
                rect.y > scrollPos.y + scrollViewHeight + rect.height) return;
            if (viewIndex >= view.Count) return;

            int i = view[viewIndex];
            var ink = inks[i];
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                previewInk = ink;
                Repaint();
            }
            float y = rect.y + 2f;
            float x = rect.x;
            float right = rect.xMax;

            // 1 行目: 番号 / 名前 / 色 / 単色化 / 複製 / 削除
            int moved = EditorGUI.DelayedIntField(new Rect(x, y, 42f, Line),
                new GUIContent("", QppEditorText.T("インク番号。書き換えるとその位置へ移動する。", "Ink number. Editing it moves the ink to that position.", "잉크 번호. 고치면 그 위치로 이동해요.")), i);
            x += 46f;
            string name = EditorGUI.TextField(new Rect(x, y, 110f, Line), ink.name);
            x += 114f;
            bool remove = GUI.Button(new Rect(right - 22f, y, 22f, Line), new GUIContent("×", QppEditorText.T("このインクを削除", "Delete this ink", "이 잉크 삭제")));
            bool duplicate = GUI.Button(new Rect(right - 68f, y, 42f, Line), QppEditorText.T("複製", "Copy", "복제"));
            bool flatten = GUI.Button(new Rect(right - 124f, y, 52f, Line),
                new GUIContent(QppEditorText.T("単色化", "Solid", "단색화"), QppEditorText.T("左端の色で両端をそろえる", "Make both ends the same as the left color", "왼쪽 끝 색으로 양끝을 맞춰요")));
            Gradient grad = EditorGUI.GradientField(new Rect(x, y, Mathf.Max(40f, right - 128f - x), Line), ink.gradient);

            // 2 行目: Material（PC / Quest）
            y += Line + 3f;
            float half = (rect.width - 4f) / 2f;
            EditorGUI.LabelField(new Rect(rect.x, y, 64f, Line), "Material");
            Material pc = (Material)EditorGUI.ObjectField(new Rect(rect.x + 64f, y, half - 64f, Line), ink.material,
                typeof(Material), false);
            EditorGUI.LabelField(new Rect(rect.x + half + 4f, y, 46f, Line), new GUIContent("Quest", QppEditorText.T("空なら PC と同じ", "Same as PC if empty", "비어 있으면 PC와 동일")));
            Material quest = (Material)EditorGUI.ObjectField(new Rect(rect.x + half + 50f, y, half - 50f, Line),
                ink.questMaterial, typeof(Material), false);

            // 3 行目: タグ
            y += Line + 3f;
            x = rect.x;
            bool tagsChanged = false;
            if (tagNames.Length == 0)
                EditorGUI.LabelField(new Rect(x, y, 200f, Line), QppEditorText.T("（タグなし）", "(no tags)", "(태그 없음)"), EditorStyles.miniLabel);
            for (int t = 0; t < tagNames.Length; t++)
            {
                bool on = ink.tags.Contains(tagNames[t]);
                float w = TagButtonWidth(tagNames[t]);
                if (x > rect.x && x + w > right)
                {
                    x = rect.x;
                    y += Line + 2f;
                }
                bool next = GUI.Toggle(new Rect(x, y, w, Line), on, (on ? "✔" : "") + tagNames[t], EditorStyles.miniButton);
                x += w + 2f;
                if (next == on) continue;
                if (next) ink.tags.Add(tagNames[t]);
                else ink.tags.Remove(tagNames[t]);
                tagsChanged = true;
            }

            // 4 行目: アウトライン（アウトライン付きシェーダーのときだけ）
            bool outlineChanged = false;
            Material switchTo = null;
            if (QppInkData.IsOutlined(ink.material))
            {
                y += Line + 3f;
                x = rect.x;
                EditorGUI.LabelField(new Rect(x, y, 64f, Line), new GUIContent(QppEditorText.T("縁取り", "Outline", "테두리"), QppEditorText.T("アウトラインの太さと色", "Outline width and color", "아웃라인의 두께와 색")));
                x += 64f;
                EditorGUI.LabelField(new Rect(x, y, 30f, Line), new GUIContent(QppEditorText.T("太さ", "Width", "두께"), QppEditorText.T("インクの太さに対する比率", "Ratio to the ink width", "잉크 두께에 대한 비율")));
                x += 30f;
                float ratio = EditorGUI.Slider(new Rect(x, y, 150f, Line), ink.outlineRatio, 0f, 3f);
                x += 156f;
                if (!Mathf.Approximately(ratio, ink.outlineRatio))
                {
                    ink.outlineRatio = ratio;
                    outlineChanged = true;
                }

                bool custom = QppInkData.UsesCustomOutlineColor(ink.material);
                if (custom)
                {
                    EditorGUI.LabelField(new Rect(x, y, 22f, Line), QppEditorText.T("色", "Color", "색"));
                    Color c = EditorGUI.ColorField(new Rect(x + 22f, y, 70f, Line), ink.outlineColor);
                    if (c != ink.outlineColor)
                    {
                        ink.outlineColor = c;
                        outlineChanged = true;
                    }
                    if (GUI.Button(new Rect(right - 96f, y, 96f, Line),
                            new GUIContent(QppEditorText.T("線の色から作る", "From line color", "선 색에서 만들기"), QppEditorText.T("線の色の明るさなどをずらして縁の色にする（Material を切り替える）", "Shift the brightness etc. of the line color to make the outline color (switches the Material)", "선 색의 밝기 등을 조정해 테두리 색으로 해요(Material을 전환해요)"))))
                        switchTo = QppInkData.OutlineColorVariant(ink.material, false);
                }
                else
                {
                    float fieldWidth = Mathf.Max(30f, (right - 100f - x) / 3f - 36f);
                    Vector3 s = ink.outlineShift;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), QppEditorText.T("色相", "Hue", "색상"));
                    s.x = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.x);
                    x += 36f + fieldWidth;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), QppEditorText.T("彩度", "Sat", "채도"));
                    s.y = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.y);
                    x += 36f + fieldWidth;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), QppEditorText.T("明るさ", "Value", "밝기"));
                    s.z = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.z);
                    s = new Vector3(Mathf.Clamp(s.x, -1f, 1f), Mathf.Clamp(s.y, -1f, 1f), Mathf.Clamp(s.z, -1f, 1f));
                    if (s != ink.outlineShift)
                    {
                        ink.outlineShift = s;
                        outlineChanged = true;
                    }
                    if (GUI.Button(new Rect(right - 96f, y, 96f, Line),
                            new GUIContent(QppEditorText.T("色を指定する", "Set color", "색 지정"), QppEditorText.T("縁の色を直接指定する（色指定用の Material に切り替える）", "Specify the outline color directly (switches to a color-specifiable Material)", "테두리 색을 직접 지정해요(색 지정용 Material로 전환해요)"))))
                        switchTo = QppInkData.OutlineColorVariant(ink.material, true);
                }
            }

            if (remove)
            {
                ClearPicked();
                inks.RemoveAt(i);
                Commit("Remove Palette Ink");
                RebuildView();
                GUIUtility.ExitGUI();
            }
            if (duplicate && inks.Count < QppInkData.MaxInks)
            {
                ClearPicked();
                inks.Insert(i + 1, ink.Clone());
                Commit("Duplicate Palette Ink");
                RebuildView();
                GUIUtility.ExitGUI();
            }

            bool changed = tagsChanged || outlineChanged;
            if (switchTo != null && switchTo != ink.material)
            {
                // 同じ Material を使っているほかのインクはそのまま。このインクだけ切り替える。
                pc = switchTo;
            }
            if (name != ink.name)
            {
                ink.name = name;
                changed = true;
            }
            // グラデーションの編集窓は渡した Gradient をその場で書き換えるので、
            // 戻り値と ink.gradient は同じ物になる。比較はパレットに保存済みの値と行う。
            if (!QppInkData.GradientEquals(grad, StoredGradient(i)))
            {
                ink.gradient = grad;
                changed = true;
            }
            if (flatten)
            {
                ink.gradient = QppInkData.SolidGradient(QppInkData.FirstColor(ink.gradient));
                changed = true;
            }
            if (pc != ink.material || quest != ink.questMaterial)
            {
                // アウトライン付きの Material を新しく選んだら、その Material の縁取り設定を初期値にする。
                if (QppInkData.IsOutlined(pc) && !QppInkData.IsOutlined(ink.material))
                {
                    ink.material = pc;
                    QppInkData.FillOutlineFromMaterial(ink);
                }
                ink.material = pc;
                ink.questMaterial = quest == pc ? null : quest;
                changed = true;
            }
            if (changed)
            {
                Commit();
                Repaint();
            }
            if (tagsChanged && filter != FilterAll) RebuildView();

            if (moved != i)
            {
                MoveInk(i, Mathf.Clamp(moved, 0, inks.Count - 1));
                GUIUtility.ExitGUI();
            }
        }

        private Gradient StoredGradient(int i)
        {
            var bank = targetPalette.EditorGradientBank;
            var index = targetPalette.EditorEntryGradientIndex;
            if (bank == null || index == null || i >= index.Length) return null;
            int gi = index[i];
            return gi >= 0 && gi < bank.Length ? bank[gi] : null;
        }

        private void OnReorder(ReorderableList l, int oldIndex, int newIndex)
        {
            // 絞り込みなしのときだけドラッグできるので、表示位置 = インク番号。
            MoveInk(oldIndex, newIndex);
        }

        private void ClearPicked()
        {
            picked.Clear();
            if (list != null) list.ClearSelection();
        }

        /// <summary>ReorderableList で選ばれている行（表示位置）を、インク番号の集合に直す。</summary>
        private void SyncPicked()
        {
            picked.Clear();
            if (list == null) return;
            foreach (int position in list.selectedIndices)
                if (position >= 0 && position < view.Count) picked.Add(view[position]);
        }

        private void DeletePicked()
        {
            if (picked.Count == 0) return;
            int count = picked.Count;
            if (!EditorUtility.DisplayDialog("QvPalette",
                    QppEditorText.T(
                        $"選んだ {count} 個のインクを削除します。\n" +
                        "公開済みのワールドで削除すると、プレイヤーが保存したお気に入りが別のインクを指すことがあります。\n" +
                        "（Ctrl+Z で元に戻せます）",
                        $"Delete the {count} selected inks.\n" +
                        "Deleting in a published world may make players' saved favorites point to different inks.\n" +
                        "(You can undo with Ctrl+Z)",
                        $"선택한 잉크 {count}개를 삭제해요.\n" +
                        "공개된 월드에서 삭제하면, 플레이어가 저장한 즐겨찾기가 다른 잉크를 가리킬 수 있어요.\n" +
                        "(Ctrl+Z로 되돌릴 수 있어요)"),
                    QppEditorText.T("削除する", "Delete", "삭제"), QppEditorText.T("やめる", "Cancel", "취소"))) return;
            QppInkSelection.RemoveSelected(inks, picked);
            ClearPicked();
            previewInk = null;
            Commit("Delete Palette Inks");
            RebuildView();
        }

        private void MoveInk(int from, int to)
        {
            if (from == to || from < 0 || from >= inks.Count) return;
            ClearPicked();
            var ink = inks[from];
            inks.RemoveAt(from);
            inks.Insert(Mathf.Clamp(to, 0, inks.Count), ink);
            Commit("Reorder Palette Inks");
            RebuildView();
        }

        private void AddInk()
        {
            if (inks.Count >= QppInkData.MaxInks) return;
            var last = inks.Count > 0 ? inks[inks.Count - 1] : null;
            var ink = new QppInk
            {
                gradient = QppInkData.SolidGradient(Color.white),
                material = last != null ? last.material : QppInkData.DefaultInkMaterial(false),
                questMaterial = last != null ? last.questMaterial : null,
            };
            if (filter >= 0 && filter < tagNames.Length) ink.tags.Add(tagNames[filter]);
            inks.Add(ink);
            Commit("Add Palette Ink");
            RebuildView();
            scrollPos.y = float.MaxValue;
        }

        // ------------------------------------------------------------------
        // ファイル / QvPen
        // ------------------------------------------------------------------

        private static void ExportFile(List<QppInk> source, string[] tags, string defaultName)
        {
            string path = EditorUtility.SaveFilePanel(QppEditorText.T("インクをファイルに書き出す", "Export inks to file", "잉크를 파일로 내보내기"), "", defaultName + "." + QppInkData.FileExtension,
                QppInkData.FileExtension);
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, QppInkData.ToJson(source, tags), new UTF8Encoding(false));
            Debug.Log($"[QPP] Exported {source.Count} inks to {path}");
        }

        private void ImportFile()
        {
            string path = EditorUtility.OpenFilePanel(QppEditorText.T("インクファイルを開く", "Open ink file", "잉크 파일 열기"), "", QppInkData.FileExtension);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            QppInkData.FileContent content;
            try
            {
                content = QppInkData.FromJson(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (System.ArgumentException e)
            {
                EditorUtility.DisplayDialog("QvPalette", e.Message, "OK");
                return;
            }
            QppInkImportWindow.Open(targetPalette, Path.GetFileName(path), content.inks, content.materialMissing,
                content.missingMaterials, false);
        }

        private List<QppInk> ReadQvPenOrWarn()
        {
            var found = QppInkData.ReadQvPen(qvpenSource);
            if (found.Count == 0)
                EditorUtility.DisplayDialog("QvPalette",
                    qvpenSource != null ? QppEditorText.T($"「{qvpenSource.name}」に QvPen のペンが見つかりません。", $"No QvPen pens found in '{qvpenSource.name}'.", $"'{qvpenSource.name}'에서 QvPen 펜을 찾을 수 없어요.") : QppEditorText.T("シーンに QvPen のペンが見つかりません。", "No QvPen pens found in the scene.", "씬에서 QvPen 펜을 찾을 수 없어요."),
                    "OK");
            return found;
        }

        private void ImportQvPen()
        {
            var found = ReadQvPenOrWarn();
            if (found.Count == 0) return;
            QppInkImportWindow.Open(targetPalette, qvpenSource != null ? qvpenSource.name : QppEditorText.T("シーン内の QvPen", "QvPen in scene", "씬의 QvPen"), found, null, null,
                true);
        }

        private void ExportQvPen()
        {
            var found = ReadQvPenOrWarn();
            if (found.Count == 0) return;
            var tags = new List<string>();
            foreach (var ink in found)
            {
                ink.tags = QppInkData.AutoTagNames(ink.gradient);
                foreach (string t in ink.tags)
                    if (!tags.Contains(t)) tags.Add(t);
            }
            ExportFile(found, tags.ToArray(), "qvpen_inks");
        }

        // ------------------------------------------------------------------
        // タグ（パネルのタグボタンで絞り込むためのもの。同期には関係しない）
        // ------------------------------------------------------------------

        public static readonly string[] DefaultTagNames =
        {
            "赤", "橙・黄", "緑", "青", "紫・ピンク", "白黒・灰", "パステル", "濃い色", "グラデ",
        };

        /// <summary>
        /// タグ情報がインク数と合わないときに整える。初めてのパレットには既定のタグ名と色からの自動タグを入れる。
        /// タグをすべて削除したパレット（名前 0 個・インク数ぶんの空タグ）はそのままにする。
        /// </summary>
        public static void EnsureTags(QppPalette palette)
        {
            if (palette == null) return;
            string[] names = palette.EditorTagNames;
            int[] masks = palette.EditorEntryTags;
            int count = palette.EntryCount;

            bool masksMissing = masks == null || masks.Length != count;
            if (!masksMissing) return;

            if (names == null || names.Length == 0) names = (string[])DefaultTagNames.Clone();
            int[] fresh = ComputeColorTags(palette, names);
            if (masks != null)
            {
                // 既存インクに手で付けたタグは残し、増えた分だけ自動で付ける。
                for (int i = 0; i < fresh.Length && i < masks.Length; i++) fresh[i] = masks[i];
            }
            Undo.RecordObject(palette, "Edit Palette Tags");
            palette.EditorSetTags(names, fresh);
            EditorUtility.SetDirty(palette);
        }

        /// <summary>色からの自動タグを、names の並びのビットにして返す（既定タグ名にあるものだけ）。</summary>
        public static int[] ComputeColorTags(QppPalette palette, string[] names)
        {
            var inks = QppInkData.Read(palette);
            var masks = new int[inks.Count];
            for (int i = 0; i < inks.Count; i++)
                masks[i] = QppInkData.MaskOf(QppInkData.AutoTagNames(inks[i].gradient), names);
            return masks;
        }

        /// <summary>既定タグ（DefaultTagNames の並び）のビット。</summary>
        public static int TagsOfColor(Color c)
        {
            float h, s, v;
            Color.RGBToHSV(c, out h, out s, out v);
            if (s < 0.15f || v < 0.12f) return 1 << 5;

            float deg = h * 360f;
            int mask;
            if (deg < 15f || deg >= 340f) mask = 1 << 0;
            else if (deg < 65f) mask = 1 << 1;
            else if (deg < 170f) mask = 1 << 2;
            else if (deg < 260f) mask = 1 << 3;
            else mask = 1 << 4;

            if (s < 0.45f && v > 0.8f) mask |= 1 << 6;
            if (v < 0.5f) mask |= 1 << 7;
            return mask;
        }

        // ------------------------------------------------------------------
        // 再バインドから呼ばれる整備
        // ------------------------------------------------------------------

        /// <summary>
        /// 1 エントリにつき 1 つの Gradient を持つ形に整える。
        /// 単色エントリは「両端が同じ色のグラデーション」に変換する。
        /// </summary>
        public static void EnsurePerEntryGradients(QppPalette palette)
        {
            int entryCount = palette.EntryCount;
            if (entryCount == 0) return;

            var oldGradients = palette.EditorGradientBank;
            var gradIndex = palette.EditorEntryGradientIndex;
            var colors = palette.EditorEntryColor;

            bool needsRebuild = oldGradients == null || oldGradients.Length != entryCount;
            if (!needsRebuild && gradIndex != null)
            {
                for (int i = 0; i < entryCount; i++)
                {
                    if (i >= gradIndex.Length || gradIndex[i] != i || oldGradients[i] == null)
                    {
                        needsRebuild = true;
                        break;
                    }
                }
            }

            if (!needsRebuild) return;

            var newGradients = new Gradient[entryCount];
            var newIndex = new int[entryCount];

            for (int i = 0; i < entryCount; i++)
            {
                Gradient src = null;
                if (gradIndex != null && i < gradIndex.Length && oldGradients != null)
                {
                    int gi = gradIndex[i];
                    if (gi >= 0 && gi < oldGradients.Length) src = oldGradients[gi];
                }

                if (src == null)
                {
                    Color c = (colors != null && i < colors.Length) ? colors[i] : Color.white;
                    src = QppInkData.SolidGradient(c);
                }

                newGradients[i] = src;
                newIndex[i] = i;
            }

            Undo.RecordObject(palette, "Normalize Palette Gradients");
            palette.EditorSetData(
                palette.EditorMaterials,
                palette.EditorMaterialsQuest,
                QppInkData.RoundedFlags(palette.EditorMaterials),
                palette.EditorEntryMaterialIndex,
                palette.EditorEntryColor,
                newIndex,
                newGradients,
                palette.EditorEntryNames);
            EditorUtility.SetDirty(palette);
        }

        /// <summary>
        /// マテリアルのシェーダーから rounded_trail 判定を焼き直す。
        /// 判定を誤ると「色は合うのに太さだけマテリアル既定値になる」ため、再バインドのたびに更新する。
        /// </summary>
        public static void RefreshRoundedFlags(QppPalette palette)
        {
            if (palette == null) return;

            Material[] mats = palette.EditorMaterials;
            if (mats == null || mats.Length == 0) return;

            palette.EditorSetData(
                mats,
                palette.EditorMaterialsQuest,
                QppInkData.RoundedFlags(mats),
                palette.EditorEntryMaterialIndex,
                palette.EditorEntryColor,
                palette.EditorEntryGradientIndex,
                palette.EditorGradientBank,
                palette.EditorEntryNames);
            EditorUtility.SetDirty(palette);
        }

        public static void GenerateHSVColors(QppPalette palette, int count)
        {
            if (palette == null) return;

            int entryCount = Mathf.Clamp(count, 16, QppInkData.MaxInks);
            var inks = new List<QppInk>(entryCount);
            Material pc = QppInkData.DefaultInkMaterial(false);
            Material quest = QppInkData.DefaultInkMaterial(true);

            // 先頭は白と黒、残りは色相を一周。
            var colors = new List<Color> { Color.white };
            if (entryCount > 1) colors.Add(Color.black);
            int remaining = entryCount - colors.Count;
            for (int i = 0; i < remaining; i++) colors.Add(Color.HSVToRGB((float)i / remaining, 0.9f, 0.95f));

            for (int i = 0; i < colors.Count; i++)
            {
                inks.Add(new QppInk
                {
                    name = i == 0 ? "White" : i == 1 ? "Black" : $"Color {i}",
                    gradient = QppInkData.SolidGradient(colors[i]),
                    material = pc,
                    questMaterial = quest == pc ? null : quest,
                });
            }

            string[] names = palette.EditorTagNames;
            if (names == null || names.Length == 0) names = (string[])DefaultTagNames.Clone();
            foreach (var ink in inks) QppInkData.ApplyAutoTags(ink, names);

            QppInkData.Write(palette, inks, names, "Generate HSV Palette");
            Debug.Log($"[QPP] Generated {entryCount} HSV palette entries successfully.");
        }
    }
}
#endif
