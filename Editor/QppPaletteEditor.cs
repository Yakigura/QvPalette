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
            int previousPreviewIndex = newPalette ? -1 : inks.IndexOf(previewInk);
            EnsurePerEntryGradients(targetPalette);
            EnsureTags(targetPalette);
            inks = QppInkData.Read(targetPalette);
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
                    index < view.Count && QppInkData.IsOutlined(inks[view[index]].material) ? OutlinedElementHeight : ElementHeight;
                list.drawElementCallback = DrawElement;
                list.onReorderCallbackWithDetails = OnReorder;
                list.onAddCallback = _ => AddInk();
                list.onSelectCallback = l =>
                {
                    if (l.index >= 0 && l.index < view.Count) previewInk = inks[view[l.index]];
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

            var picked = (QppPalette)EditorGUILayout.ObjectField("Target Palette", targetPalette, typeof(QppPalette), true);
            if (picked != targetPalette) targetPalette = picked;
            if (targetPalette == null) targetPalette = FindObjectOfType<QppPalette>();
            if (targetPalette == null)
            {
                EditorGUILayout.HelpBox("Scene内に QppPalette が見つかりません。Prefabを配置するか選択してください。", MessageType.Warning);
                return;
            }
            if (loadedPalette != targetPalette || inks.Count != targetPalette.EntryCount) Reload();

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
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            if (previewInk == null || !inks.Contains(previewInk))
                previewInk = view.Count > 0 ? inks[view[0]] : null;
            if (previewInk == null)
            {
                EditorGUILayout.HelpBox("インクを選択してください。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("選択中", string.IsNullOrEmpty(previewInk.name)
                ? $"インク {inks.IndexOf(previewInk)}" : $"{inks.IndexOf(previewInk)}: {previewInk.name}");
            EditorGUILayout.BeginHorizontal();
            previewQuest = EditorGUILayout.ToggleLeft("Quest", previewQuest, GUILayout.Width(65f));
            previewBackground = EditorGUILayout.Popup("背景", previewBackground, new[] { "暗い", "中間", "明るい" });
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            previewWidthIndex = EditorGUILayout.IntSlider("太さ", previewWidthIndex, 0, QppCommon.MAX_WIDTH_INDEX);
            EditorGUILayout.LabelField(targetPalette.MillimetersTextOf(previewWidthIndex), GUILayout.Width(75f));
            EditorGUILayout.EndHorizontal();

            Rect rect = GUILayoutUtility.GetRect(1f, 110f, GUILayout.ExpandWidth(true));
            Material material = QppInkPreviewLogic.ResolveMaterial(previewInk, previewQuest);
            if (!QppInkPreviewLogic.HasMaterial(material))
            {
                EditorGUI.DrawRect(rect, QppInkPreviewLogic.BackgroundColor(previewBackground));
                GUI.Label(rect, "Material 未設定", EditorStyles.centeredGreyMiniLabel);
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
            EditorGUILayout.LabelField($"インク {inks.Count} / {QppInkData.MaxInks}", EditorStyles.boldLabel, GUILayout.Width(140));
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(inks.Count >= QppInkData.MaxInks))
            {
                if (GUILayout.Button("＋ インク追加", GUILayout.Width(100))) AddInk();
            }
            if (GUILayout.Button("ファイルに書き出す", GUILayout.Width(120))) ExportFile(inks, tagNames, "palette");
            if (GUILayout.Button("ファイルから取り込む…", GUILayout.Width(130))) ImportFile();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawQvPenSection()
        {
            qvpenFoldout = EditorGUILayout.Foldout(qvpenFoldout, "QvPen のインクを使う", true, EditorStyles.foldoutHeader);
            if (!qvpenFoldout) return;

            EditorGUI.indentLevel++;
            qvpenSource = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("読み込み元", "QvPen のペンや、ペンをまとめた Prefab。空ならシーン内のすべてのペン。"),
                qvpenSource, typeof(GameObject), true);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button("インクを選んで取り込む…")) ImportQvPen();
            if (GUILayout.Button("ファイルに書き出す")) ExportQvPen();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "QvPen の各ペンの色（グラデーション）と Material を読みます。同じ色・同じ Material のペンは 1 つにまとめます。" +
                "シーンのペンの Material は再バインド時にパレット先頭のものへ書き換わるため、元の Material を使いたいときは" +
                "ペンの Prefab を読み込み元に指定してください。",
                MessageType.None);
            EditorGUI.indentLevel--;
        }

        private void DrawTagSection()
        {
            tagsFoldout = EditorGUILayout.Foldout(tagsFoldout, $"タグ（{tagNames.Length} / {QppPalette.MAX_TAGS}、パネルで絞り込み）", true,
                EditorStyles.foldoutHeader);
            if (!tagsFoldout) return;

            EditorGUI.indentLevel++;
            for (int t = 0; t < tagNames.Length; t++)
            {
                int used = 0;
                foreach (var ink in inks)
                    if (ink.tags.Contains(tagNames[t])) used++;

                EditorGUILayout.BeginHorizontal();
                string renamed = EditorGUILayout.DelayedTextField($"タグ {t + 1}", tagNames[t]);
                EditorGUILayout.LabelField($"{used} 個", GUILayout.Width(50));
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
                        $"タグ「{tagNames[t]}」を削除し、{used} 個のインクから外します。よろしいですか？", "削除", "やめる")))
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
                if (GUILayout.Button("＋ タグ追加"))
                {
                    var more = new List<string>(tagNames) { UniqueTagName("新しいタグ") };
                    tagNames = more.ToArray();
                    Commit("Add Palette Tag");
                }
            }
            if (GUILayout.Button("色から自動タグ付け"))
            {
                if (EditorUtility.DisplayDialog("QvPalette",
                        "すべてのインクの既定タグ（赤・青・パステルなど）を色から付け直します。自分で作ったタグはそのままです。",
                        "付け直す", "やめる"))
                {
                    foreach (var ink in inks) QppInkData.ApplyAutoTags(ink, tagNames);
                    Commit("Auto Tag Palette");
                    RebuildView();
                }
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                "タグは最大 27 個（パネルでは 9 個ずつ 3 ページ）。パネル右端のボタンで、そのタグのインクだけを表示します。" +
                "自動タグ付けは既定のタグ名（" + string.Join(" / ", DefaultTagNames) + "）のうち、ここにあるものだけを付けます。",
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
            hsvFoldout = EditorGUILayout.Foldout(hsvFoldout, "補助: HSV で一括生成（すべて置き換え）", true, EditorStyles.foldoutHeader);
            if (!hsvFoldout) return;

            EditorGUI.indentLevel++;
            generateColorCount = EditorGUILayout.IntSlider("生成色数", generateColorCount, 16, QppInkData.MaxInks);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15f);
            if (GUILayout.Button($"HSVカラー一括生成 ({generateColorCount}色)") &&
                EditorUtility.DisplayDialog("QvPalette",
                    "現在のインクをすべて置き換えます。よろしいですか？", "置き換える", "やめる"))
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
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            var options = new List<string> { "すべて" };
            options.AddRange(tagNames);
            options.Add("タグなし");
            int popupIndex = filter == FilterAll ? 0 : filter == FilterUntagged ? options.Count - 1 : filter + 1;
            int newIndex = EditorGUILayout.Popup("表示するインク", popupIndex, options.ToArray());
            EditorGUILayout.EndHorizontal();
            if (newIndex != popupIndex)
            {
                filter = newIndex == 0 ? FilterAll : newIndex == options.Count - 1 ? FilterUntagged : newIndex - 1;
                RebuildView();
            }

            EditorGUILayout.BeginHorizontal();
            bulkMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Material 一括設定", "表示中のインクすべての Material を変える。"), bulkMaterial, typeof(Material), false);
            bulkQuestMaterial = (Material)EditorGUILayout.ObjectField(
                new GUIContent("", "Quest 用（空なら PC と同じ）"), bulkQuestMaterial, typeof(Material), false, GUILayout.Width(140));
            using (new EditorGUI.DisabledScope(bulkMaterial == null || view.Count == 0))
            {
                if (GUILayout.Button($"{view.Count} 個に適用", GUILayout.Width(90)))
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

            EditorGUILayout.HelpBox(
                filter == FilterAll
                    ? "左端の ≡ をドラッグするか、番号を書き換えると並べ替えられます。並び順がパネルの表示順です。" +
                      "公開済みのワールドで並べ替え・削除すると、プレイヤーが保存したお気に入りが別のインクを指すことがあります。"
                    : "絞り込み中はドラッグで並べ替えできません（番号の書き換えはできます）。",
                MessageType.None);

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));
            list.DoLayoutList();
            EditorGUILayout.EndScrollView();
            if (Event.current.type == EventType.Repaint) scrollViewHeight = GUILayoutUtility.GetLastRect().height;
        }

        private static float Line => EditorGUIUtility.singleLineHeight;
        private static float ElementHeight => Line * 3f + 10f;
        private static float OutlinedElementHeight => Line * 4f + 13f;

        private void DrawElement(Rect rect, int viewIndex, bool isActive, bool isFocused)
        {
            // 見えていない行は描かない（4096 個でも重くならないように）。
            if (rect.yMax < scrollPos.y - OutlinedElementHeight ||
                rect.y > scrollPos.y + scrollViewHeight + OutlinedElementHeight) return;
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
                new GUIContent("", "インク番号。書き換えるとその位置へ移動する。"), i);
            x += 46f;
            string name = EditorGUI.TextField(new Rect(x, y, 110f, Line), ink.name);
            x += 114f;
            bool remove = GUI.Button(new Rect(right - 22f, y, 22f, Line), new GUIContent("×", "このインクを削除"));
            bool duplicate = GUI.Button(new Rect(right - 68f, y, 42f, Line), "複製");
            bool flatten = GUI.Button(new Rect(right - 124f, y, 52f, Line),
                new GUIContent("単色化", "左端の色で両端をそろえる"));
            Gradient grad = EditorGUI.GradientField(new Rect(x, y, Mathf.Max(40f, right - 128f - x), Line), ink.gradient);

            // 2 行目: Material（PC / Quest）
            y += Line + 3f;
            float half = (rect.width - 4f) / 2f;
            EditorGUI.LabelField(new Rect(rect.x, y, 64f, Line), "Material");
            Material pc = (Material)EditorGUI.ObjectField(new Rect(rect.x + 64f, y, half - 64f, Line), ink.material,
                typeof(Material), false);
            EditorGUI.LabelField(new Rect(rect.x + half + 4f, y, 46f, Line), new GUIContent("Quest", "空なら PC と同じ"));
            Material quest = (Material)EditorGUI.ObjectField(new Rect(rect.x + half + 50f, y, half - 50f, Line),
                ink.questMaterial, typeof(Material), false);

            // 3 行目: タグ
            y += Line + 3f;
            x = rect.x;
            bool tagsChanged = false;
            if (tagNames.Length == 0)
                EditorGUI.LabelField(new Rect(x, y, 200f, Line), "（タグなし）", EditorStyles.miniLabel);
            for (int t = 0; t < tagNames.Length; t++)
            {
                bool on = ink.tags.Contains(tagNames[t]);
                float w = Mathf.Max(36f, EditorStyles.miniButton.CalcSize(new GUIContent(tagNames[t])).x + 14f);
                if (x + w > right) break;
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
                EditorGUI.LabelField(new Rect(x, y, 64f, Line), new GUIContent("縁取り", "アウトラインの太さと色"));
                x += 64f;
                EditorGUI.LabelField(new Rect(x, y, 30f, Line), new GUIContent("太さ", "インクの太さに対する比率"));
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
                    EditorGUI.LabelField(new Rect(x, y, 22f, Line), "色");
                    Color c = EditorGUI.ColorField(new Rect(x + 22f, y, 70f, Line), ink.outlineColor);
                    if (c != ink.outlineColor)
                    {
                        ink.outlineColor = c;
                        outlineChanged = true;
                    }
                    if (GUI.Button(new Rect(right - 96f, y, 96f, Line),
                            new GUIContent("線の色から作る", "線の色の明るさなどをずらして縁の色にする（Material を切り替える）")))
                        switchTo = QppInkData.OutlineColorVariant(ink.material, false);
                }
                else
                {
                    float fieldWidth = Mathf.Max(30f, (right - 100f - x) / 3f - 36f);
                    Vector3 s = ink.outlineShift;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), "色相");
                    s.x = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.x);
                    x += 36f + fieldWidth;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), "彩度");
                    s.y = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.y);
                    x += 36f + fieldWidth;
                    EditorGUI.LabelField(new Rect(x, y, 34f, Line), "明るさ");
                    s.z = EditorGUI.FloatField(new Rect(x + 34f, y, fieldWidth, Line), s.z);
                    s = new Vector3(Mathf.Clamp(s.x, -1f, 1f), Mathf.Clamp(s.y, -1f, 1f), Mathf.Clamp(s.z, -1f, 1f));
                    if (s != ink.outlineShift)
                    {
                        ink.outlineShift = s;
                        outlineChanged = true;
                    }
                    if (GUI.Button(new Rect(right - 96f, y, 96f, Line),
                            new GUIContent("色を指定する", "縁の色を直接指定する（色指定用の Material に切り替える）")))
                        switchTo = QppInkData.OutlineColorVariant(ink.material, true);
                }
            }

            if (remove)
            {
                inks.RemoveAt(i);
                Commit("Remove Palette Ink");
                RebuildView();
                GUIUtility.ExitGUI();
            }
            if (duplicate && inks.Count < QppInkData.MaxInks)
            {
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

        private void MoveInk(int from, int to)
        {
            if (from == to || from < 0 || from >= inks.Count) return;
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
            string path = EditorUtility.SaveFilePanel("インクをファイルに書き出す", "", defaultName + "." + QppInkData.FileExtension,
                QppInkData.FileExtension);
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, QppInkData.ToJson(source, tags), new UTF8Encoding(false));
            Debug.Log($"[QPP] Exported {source.Count} inks to {path}");
        }

        private void ImportFile()
        {
            string path = EditorUtility.OpenFilePanel("インクファイルを開く", "", QppInkData.FileExtension);
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
                    qvpenSource != null ? $"「{qvpenSource.name}」に QvPen のペンが見つかりません。" : "シーンに QvPen のペンが見つかりません。",
                    "OK");
            return found;
        }

        private void ImportQvPen()
        {
            var found = ReadQvPenOrWarn();
            if (found.Count == 0) return;
            QppInkImportWindow.Open(targetPalette, qvpenSource != null ? qvpenSource.name : "シーン内の QvPen", found, null, null,
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
