#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using QvPen.UdonScript;
using Yakigura.QvInkPreview;
using Yakigura.QvInkPreview.Editor;
using TMPro;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Components;
using Object = UnityEngine.Object;

namespace Yakigura.QvPalette.Editor
{
    public static class QppSceneBinder
    {
        // ペン 1 本で色・太さを 4,096 回まで切り替えられる（8 チャンク × 512 件）。容量を増やすより
        // チャンクを増やすほうが、1 回の送信量（約 4KB）が変わらず影響が小さい。
        public static int TargetChunksPerPen = 8;
        public static int TargetChunkCapacity = 512;

        /// <summary>ペン追従プレビューを描画する最大距離（§8: 一定距離を超えたら描画を止める）。</summary>
        public static float PreviewMaxVisibleDistance = 5f;

        /// <summary>プレビューをペン尻からどれだけ後方に出すか（描画の邪魔にならないように）。</summary>
        public static float PreviewTailDistance = 0.02f;

        /// <summary>プレビューをペン尻の上へ持ち上げる量。</summary>
        public static float PreviewUpOffset = 0.05f;

        /// <summary>パレットは手ごとに 1 枚（右手用・左手用）。</summary>
        public const int PanelCount = 2;
        private const string PanelNameRight = "Palette_Right";
        private const string PanelNameLeft = "Palette_Left";

        // 横長パネル（0.84 x 0.42m）の配置。左列にお気に入りと太さ、中央に色グリッド、右端にタグボタンを置く。
        // 誤タッチを避けるため、各段の間は 2.5cm 以上空ける。
        private const float PanelWidth = 0.84f;
        private const float TopRowY = 0.175f;
        private const float PageRowY = -0.175f;
        private const float LeftColumnX = -0.245f;
        private const float RightColumnX = 0.085f;
        private const float TagColumnX = 0.29f;
        private const float TagColumnStepX = 0.08f;
        private const float TagRowTopY = 0.1f;
        private const float TagRowStepY = 0.05f;
        private const int TagButtonCount = QppPalette.TAGS_PER_PAGE + 1;
        private const float GridCenterY = 0f;
        // 左列は上から「ペンの大きさ（あまり変えない）→ 線の太さ → お気に入り」。
        private const float FavoritesCenterY = -0.15f;
        private const float WidthTextX = -0.155f;
        private const float WidthTextY = 0.080f;
        private const float SliderCenterY = 0.045f;
        private const float PresetCenterY = -0.025f;
        // ペンの大きさスライダーの段。パネルを下へ 6cm 伸ばして置く。
        private const float PenSizeRowY = 0.135f;
        private const float PanelHeight = 0.48f;
        private const float PanelCenterY = -0.03f;

        public static void ScanAndRebindBatch()
        {
            WarmUdonAssemblyCacheForBatch();
            string scenePath = "Assets/Scenes/VRCDefaultWorldScene.unity";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-qppScene") scenePath = args[i + 1];
            var scene = EditorSceneManager.OpenScene(scenePath);
            ScanAndRebind();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[QPP] Batch ScanAndRebind completed and saved.");
        }

        public static void MigratePreviewsBatch()
        {
            WarmUdonAssemblyCacheForBatch();
            string scenePath = "Assets/Scenes/QvPalette.unity";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-qppScene") scenePath = args[i + 1];
            var scene = EditorSceneManager.OpenScene(scenePath);
            MigratePreviews();
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Preview migration scene save failed.");
            ValidateSceneLocalReferences();
            Debug.Log("[QPP] Preview migration saved: " + scenePath);
        }

        public static void RemovePreviewWidthLabelsBatch()
        {
            WarmUdonAssemblyCacheForBatch();
            string scenePath = "Assets/Scenes/QvPalette.unity";
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-qppScene") scenePath = args[i + 1];
            var scene = EditorSceneManager.OpenScene(scenePath);
            int removed = 0;
            foreach (var preview in FindAllPenManagersInScene()
                         .SelectMany(manager => manager.GetComponentsInChildren<QvInkPreview_Pen>(true)))
                removed += RemovePreviewWidthLabels(preview);
            var qppRoot = FindQppRoot(scene);
            var rigs = qppRoot != null ? qppRoot.transform.Find("Rigs") : null;
            if (rigs != null)
                foreach (Transform rig in rigs)
                {
                    var legacyPreview = rig.Find("PenPreview");
                    if (legacyPreview != null) removed += RemoveWidthTextChildren(legacyPreview);
                }
            foreach (var bridge in Object.FindObjectsOfType<QppPenPreview>(true))
                if (bridge.gameObject.scene == scene) UdonSharpEditorUtility.CopyProxyToUdon(bridge);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Preview label cleanup scene save failed.");
            ValidateSceneLocalReferences();
            Debug.Log($"[QPP] Removed {removed} preview width labels: {scenePath}");
        }

        private static int RemovePreviewWidthLabels(QvInkPreview_Pen preview)
        {
            if (preview == null || preview.PreviewRoot == null) return 0;
            return RemoveWidthTextChildren(preview.PreviewRoot.transform);
        }

        private static int RemoveWidthTextChildren(Transform previewRoot)
        {
            int removed = 0;
            var transforms = previewRoot.GetComponentsInChildren<Transform>(true);
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                var tr = transforms[i];
                if (tr == previewRoot || tr.name != "WidthText") continue;
                // Recording Undo during batch cleanup caused Unity to omit unrelated
                // stripped QvPen Clear references when this scene was saved.
                if (Application.isBatchMode) Object.DestroyImmediate(tr.gameObject);
                else Undo.DestroyObjectImmediate(tr.gameObject);
                removed++;
            }
            return removed;
        }

        public static void ValidateInkPreviewBatch()
        {
            WarmUdonAssemblyCacheForBatch();
            InkPreviewValidation.RunAndExit();
        }


        public static void ValidateSceneLocalReferences()
        {
            var scenes = Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories);
            if (scenes.Length == 0) throw new InvalidOperationException("No scenes found under Assets/Scenes.");
            foreach (var scenePath in scenes)
            {
                var yaml = File.ReadAllText(scenePath);
                var defined = new HashSet<string>();
                foreach (Match match in Regex.Matches(yaml, @"(?m)^--- !u!\d+ &(-?\d+)"))
                    defined.Add(match.Groups[1].Value);
                foreach (Match match in Regex.Matches(yaml, @"\{fileID: (-?\d+)\}"))
                {
                    var fileId = match.Groups[1].Value;
                    if (fileId == "0" || defined.Contains(fileId)) continue;
                    int line = 1;
                    for (int i = 0; i < match.Index; i++) if (yaml[i] == '\n') line++;
                    throw new InvalidOperationException($"Dangling local fileID {fileId} in {scenePath}:{line}");
                }
            }
        }

        private static string GetScenePath(Transform tr)
        {
            string path = tr.name;
            while (tr.parent != null)
            {
                tr = tr.parent;
                path = tr.name + "/" + path;
            }
            return path;
        }

        public static void MigratePreviews()
        {
            EnsureAllProgramAssets();
            var scene = SceneManager.GetActiveScene();
            var root = FindQppRoot(scene);
            var rigs = root != null ? root.transform.Find("Rigs") : null;
            var palette = root != null ? root.GetComponentInChildren<QppPalette>(true) : null;
            var observer = root != null ? root.GetComponentInChildren<QppInkObserver>(true) : null;
            if (rigs == null || palette == null || observer == null)
                throw new InvalidOperationException("Run Scan Scene & Rebind before migrating previews.");

            // Validate the whole scene before removing old previews.
            var penManagers = FindAllPenManagersInScene();
            var bindings = new Dictionary<int, QppPenBinding>();
            foreach (Transform rig in rigs)
            {
                if (!TryGetPreviewSlot(rig, out int slot)) continue;
                if (slot < 0 || slot >= penManagers.Length || bindings.ContainsKey(slot))
                    throw new InvalidOperationException("Invalid or duplicate preview slot " + slot);
                var bindingTr = rig.Find("Binding");
                var binding = bindingTr != null ? bindingTr.GetComponent<QppPenBinding>() : null;
                if (binding == null || binding.Pen == null)
                    throw new InvalidOperationException("Pen binding missing for slot " + slot);
                bindings.Add(slot, binding);
            }
            var observerData = new SerializedObject(observer);
            var previewArray = observerData.FindProperty("penPreviews");
            if (previewArray == null || bindings.Count != penManagers.Length)
                throw new InvalidOperationException("Preview bindings or observer array are incomplete.");

            int removed = 0;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
            foreach (Transform rig in rigs)
            {
                if (!TryGetPreviewSlot(rig, out int slot)) continue;
                var old = rig.Find("PenPreview");
                if (old == null) continue;
                Undo.DestroyObjectImmediate(old.gameObject);
                removed++;
            }

            // QvInfoX and this tool both call the same idempotent package setup.
            QvInkPreview_AutoSetup.AutoSetup();
            previewArray.arraySize = penManagers.Length;
            int migrated = 0;
            foreach (Transform rig in rigs)
            {
                if (!TryGetPreviewSlot(rig, out int slot)) continue;
                var binding = bindings[slot];
                var pen = binding.Pen;
                var matches = pen.GetComponentsInChildren<QvInkPreview_Pen>(true);
                if (matches.Length != 1 || matches[0].PreviewRoot == null)
                    throw new InvalidOperationException("Expected one shared preview for slot " + slot);
                var shared = matches[0];
                RemovePreviewWidthLabels(shared);
                shared.tailDistance = PreviewTailDistance;
                shared.upOffset = PreviewUpOffset;
                shared.maxVisibleDistance = PreviewMaxVisibleDistance;
                UdonSharpEditorUtility.CopyProxyToUdon(shared);

                var bridgeTr = GetOrCreateChild(rig, "PenPreviewBinding");
                var bridge = GetOrAddComponent<QppPenPreview>(bridgeTr.gameObject);
                bridge.EditorSetReferences(shared, binding, palette);
                UdonSharpEditorUtility.CopyProxyToUdon(bridge);
                previewArray.GetArrayElementAtIndex(slot).objectReferenceValue = bridge;
                migrated++;
            }
            observerData.ApplyModifiedPropertiesWithoutUndo();
            UdonSharpEditorUtility.CopyProxyToUdon(observer);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[QPP] Preview migration: removed={removed}, shared={migrated}.");
            Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                // Restore detached labels and legacy previews if setup fails midway.
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }
        }

        // シーン直下のルートの名前。旧名（QvPenPalettePlus）のシーンも、そのまま見つけ、セットアップのときに新しい名前へ直す。
        private const string RootName = "QvPalette";
        private const string LegacyRootName = "QvPenPalettePlus";
        private static bool IsQppRootName(string name) { return name == RootName || name == LegacyRootName; }

        private static GameObject FindQppRoot(Scene scene)
        {
            GameObject found = null;
            foreach (var candidate in scene.GetRootGameObjects())
            {
                if (!IsQppRootName(candidate.name)) continue;
                if (found != null) throw new InvalidOperationException("Multiple QvPalette roots found.");
                found = candidate;
            }
            return found;
        }

        private static bool TryGetPreviewSlot(Transform rig, out int slot)
        {
            const string prefix = "QPP_Pen_";
            slot = -1;
            return rig.name.StartsWith(prefix) && int.TryParse(rig.name.Substring(prefix.Length), out slot);
        }

        // SDK 3.10.5 may initialize this cache on a worker while opening the first
        // scene in batchmode. AssetDatabase.FindAssets must run on the main thread.
        public static void WarmUdonAssemblyCacheForBatch()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("UdonSharp.Compiler.Udon.CompilerUdonInterface");
                if (type == null) continue;
                var property = type.GetProperty("UdonSharpAssemblyDefinitions", BindingFlags.Public | BindingFlags.Static);
                if (property == null) throw new InvalidOperationException("UdonSharp assembly cache API missing.");
                property.GetValue(null);
                return;
            }
            throw new InvalidOperationException("UdonSharp compiler assembly not loaded.");
        }


        public static bool ApplyChunkSettings(int chunksPerPen, int chunkCapacity)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[QPP] Chunk settings cannot be changed in Play mode.");
                return false;
            }

            if (chunksPerPen < 1 || chunksPerPen > 32 ||
                (chunkCapacity != 128 && chunkCapacity != 256 && chunkCapacity != 512 && chunkCapacity != 1024))
            {
                Debug.LogError("[QPP] Invalid chunk settings.");
                return false;
            }

            var scene = SceneManager.GetActiveScene();
            Transform root = null;
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                if (!IsQppRootName(sceneRoot.name)) continue;
                if (root != null)
                {
                    Debug.LogError("[QPP] Multiple QvPalette roots found. Resolve them before changing chunks.");
                    return false;
                }
                root = sceneRoot.transform;
            }

            var rigs = root != null ? root.Find("Rigs") : null;
            var observer = root != null ? root.GetComponentInChildren<QppInkObserver>(true) : null;
            if (rigs == null || observer == null)
            {
                Debug.LogError("[QPP] Bind the scene before changing chunk settings.");
                return false;
            }

            var setupComponent = root.GetComponentInChildren<QppSetup>(true);
            if (setupComponent != null)
            {
                Undo.RecordObject(setupComponent, "Change Ledger Capacity");
                setupComponent.EditorLedgerChunksPerPen = chunksPerPen;
                setupComponent.EditorLedgerChunkCapacity = chunkCapacity;
                EditorUtility.SetDirty(setupComponent);
            }

            var rigList = new List<Transform>();
            for (int slot = 0; slot < rigs.childCount; slot++)
            {
                var rig = rigs.Find($"QPP_Pen_{slot}");
                if (rig == null) break;
                if (rig.Find("Ledger") == null || rig.Find("State") == null || rig.Find("Binding") == null ||
                    rig.Find("Ledger").GetComponent<QppPenLedger>() == null ||
                    rig.Find("State").GetComponent<QppPenState>() == null ||
                    rig.Find("Binding").GetComponent<QppPenBinding>() == null)
                {
                    Debug.LogError($"[QPP] Rig {slot} is incomplete. Run Scan Scene & Rebind first.");
                    return false;
                }
                rigList.Add(rig);
            }

            if (rigList.Count == 0 || rigList.Count != rigs.childCount)
            {
                Debug.LogError("[QPP] Rig numbering is incomplete. Run Scan Scene & Rebind first.");
                return false;
            }

            foreach (var rig in rigList)
            {
                for (int i = 0; i < rig.childCount; i++)
                {
                    var child = rig.GetChild(i);
                    if (!child.name.StartsWith("Chunk_")) continue;
                    int index;
                    if (!int.TryParse(child.name.Substring("Chunk_".Length), out index) || index < 0)
                    {
                        Debug.LogError($"[QPP] Unexpected chunk name: {child.name}");
                        return false;
                    }
                    var chunk = child.GetComponent<QppLedgerChunk>();
                    if (chunk != null && (index >= chunksPerPen && chunk.Count > 0 || chunk.Count > chunkCapacity))
                    {
                        Debug.LogError($"[QPP] {child.name} has data that would be lost. Increase the requested count or capacity.");
                        return false;
                    }
                }
            }

            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply QvPalette chunk settings");
            for (int slot = 0; slot < rigList.Count; slot++)
            {
                var rig = rigList[slot];
                var chunks = new QppLedgerChunk[chunksPerPen];
                for (int c = 0; c < chunksPerPen; c++)
                {
                    var chunkTr = rig.Find($"Chunk_{c}");
                    if (chunkTr == null)
                    {
                        chunkTr = GetOrCreateChild(rig, $"Chunk_{c}");
                        Undo.RegisterCreatedObjectUndo(chunkTr.gameObject, "Create QvPalette chunk");
                    }
                    var chunk = GetOrAddComponent<QppLedgerChunk>(chunkTr.gameObject);
                    Undo.RecordObject(chunk, "Set QvPalette chunk capacity");
                    chunk.EditorSetReferences(chunkCapacity, observer, slot);
                    EditorUtility.SetDirty(chunk);
                    chunks[c] = chunk;
                }

                var ledger = rig.Find("Ledger").GetComponent<QppPenLedger>();
                Undo.RecordObject(ledger, "Bind QvPalette chunks");
                ledger.EditorSetReferences(chunks,
                    rig.Find("State").GetComponent<QppPenState>(),
                    rig.Find("Binding").GetComponent<QppPenBinding>(),
                    slot, QppCommon.DEFAULT_WIDTH_INDEX);
                EditorUtility.SetDirty(ledger);

                for (int i = rig.childCount - 1; i >= 0; i--)
                {
                    var child = rig.GetChild(i);
                    if (!child.name.StartsWith("Chunk_")) continue;
                    int index;
                    if (int.TryParse(child.name.Substring("Chunk_".Length), out index) && index >= chunksPerPen)
                        Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
            Undo.CollapseUndoOperations(undoGroup);
            TargetChunksPerPen = chunksPerPen;
            TargetChunkCapacity = chunkCapacity;
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[QPP] Updated {rigList.Count} ledgers to {chunksPerPen} chunks x {chunkCapacity} records without rebuilding Palette panels or tool.");
            return true;
        }

        public static bool EnsureAllProgramAssets()
        {
            if (QppPackagePaths.IsImmutable)
            {
                var problems = QppProgramAssetCheck.FindProblems(QppPackagePaths.PhysicalRoot);
                if (problems.Count > 0)
                {
                    Debug.LogWarning($"[QPP] U# program asset verification found {problems.Count} problem(s). Regenerate and commit the assets in the development project.");
                    foreach (string problem in problems) Debug.LogWarning("[QPP] " + problem);
                }
                else
                {
                    Debug.Log("[QPP] Program assets verified. Package is under Packages/: nothing was written.");
                }
                InvalidateUdonSharpCaches();
                return false;
            }

            bool createdAny = false;

            // Ensure UdonSharpAssemblyDefinition for Runtime asmdef
            string asmDefPath = QppPackagePaths.AsmDefPath;
            string uSharpAsmDefPath = QppPackagePaths.UdonAsmAssetPath;
            var existingUSharpAsm = AssetDatabase.LoadAssetAtPath<UdonSharpAssemblyDefinition>(uSharpAsmDefPath);
            if (existingUSharpAsm == null && File.Exists(asmDefPath))
            {
                var asmDefAsset = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(asmDefPath);
                if (asmDefAsset != null)
                {
                    var newUSharpAsm = ScriptableObject.CreateInstance<UdonSharpAssemblyDefinition>();
                    newUSharpAsm.sourceAssembly = asmDefAsset;
                    AssetDatabase.CreateAsset(newUSharpAsm, uSharpAsmDefPath);
                    Debug.Log($"[QPP] Created UdonSharpAssemblyDefinition at {uSharpAsmDefPath}");
                    createdAny = true;
                }
            }

            string scriptsDir = QppPackagePaths.ScriptsDir;
            if (!Directory.Exists(scriptsDir)) return createdAny;

            string[] csFiles = Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories);

            foreach (string csFile in csFiles)
            {
                string normalizedPath = csFile.Replace("\\", "/");
                MonoScript monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(normalizedPath);
                if (monoScript == null) continue;

                Type scriptType = monoScript.GetClass();
                if (scriptType == null || !typeof(UdonSharpBehaviour).IsAssignableFrom(scriptType) || scriptType.IsAbstract)
                    continue;

                string assetPath = Path.ChangeExtension(normalizedPath, ".asset");
                var existingAsset = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(assetPath);
                if (existingAsset == null)
                {
                    var newProgramAsset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                    newProgramAsset.sourceCsScript = monoScript;
                    AssetDatabase.CreateAsset(newProgramAsset, assetPath);
                    Debug.Log($"[QPP] Created UdonSharpProgramAsset for {scriptType.Name} at {assetPath}");
                    createdAny = true;
                }
                else if (existingAsset.sourceCsScript != monoScript)
                {
                    existingAsset.sourceCsScript = monoScript;
                    EditorUtility.SetDirty(existingAsset);
                    createdAny = true;
                }
            }

            if (createdAny)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            InvalidateUdonSharpCaches();

            return createdAny;
        }

        private const System.Reflection.BindingFlags StaticAny =
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public;

        /// <summary>
        /// UdonSharp は「型 → プログラムアセット」の対応表とプログラムアセット一覧を
        /// 静的にキャッシュしており、スクリプトやアセットを追加した直後は古いままになる。
        /// この状態で AddUdonSharpComponent を呼ぶと
        /// 「Unable to find valid U# program asset associated with script」で失敗するため、
        /// バインド前に 3 つのキャッシュをすべて作り直す。
        /// </summary>
        private static void InvalidateUdonSharpCaches()
        {
            try
            {
                Type compCtxType = Type.GetType("UdonSharp.Compiler.CompilationContext, UdonSharp.Editor");
                if (compCtxType != null)
                {
                    var resetMethod = compCtxType.GetMethod("ResetAssemblyCaches", StaticAny);
                    resetMethod?.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QPP] InvalidateUdonSharpCaches warning (CompilationContext): {ex.Message}");
            }

            try
            {
                // UdonSharpProgramAsset._programAssetCache（プロジェクト内アセット一覧）
                var clearMethod = typeof(UdonSharpProgramAsset).GetMethod("ClearProgramAssetCache", StaticAny);
                clearMethod?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QPP] InvalidateUdonSharpCaches warning (ProgramAssetCache): {ex.Message}");
            }

            try
            {
                // UdonSharpEditorUtility._programAssetLookup / _programAssetTypeLookup（型 → アセット）
                var resetCaches = typeof(UdonSharpEditorUtility).GetMethod("ResetCaches", StaticAny);
                resetCaches?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[QPP] InvalidateUdonSharpCaches warning (EditorUtility): {ex.Message}");
            }
        }

        private static string PackageRootPath => QppPackagePaths.Root;
        private static bool packageReimported;

        /// <summary>
        /// 型に対応する U# プログラムアセットを取得する。
        /// 引けない場合は、キャッシュ再構築 → パッケージフォルダの再インポート、の順で自己修復を試みる。
        /// </summary>
        private static bool TryResolveProgramAsset(Type behaviourType, out UdonSharpProgramAsset programAsset)
        {
            if (ResolveProgramAsset(behaviourType, out programAsset)) return true;

            InvalidateUdonSharpCaches();
            if (ResolveProgramAsset(behaviourType, out programAsset)) return true;

            // UdonSharp のアセット一覧は AssetDatabase の GUID → パス解決に依存しているため、
            // フォルダ名の大文字小文字が食い違っていると一覧から丸ごと落ちる。
            // フォルダを取り込み直してパスを引き直させる。
            if (!packageReimported && !QppPackagePaths.IsImmutable)
            {
                packageReimported = true;
                Debug.Log("[QPP] U# プログラムアセットを引けなかったため、パッケージフォルダを再インポートします...");
                AssetDatabase.ImportAsset(PackageRootPath, ImportAssetOptions.ImportRecursive);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                InvalidateUdonSharpCaches();

                if (ResolveProgramAsset(behaviourType, out programAsset)) return true;
            }

            return false;
        }

        private static bool ResolveProgramAsset(Type behaviourType, out UdonSharpProgramAsset programAsset)
        {
            programAsset = UdonSharpEditorUtility.GetUdonSharpProgramAsset(behaviourType);
            return programAsset != null && programAsset.SerializedProgramAsset != null;
        }

        /// <summary>
        /// プログラムアセットを引けなかった原因を切り分けて出力する。
        /// アセット自体は存在するのに一覧から引けない場合は、AssetDatabase のパス不整合が原因。
        /// </summary>
        private static string DescribeProgramAssetFailure(Type behaviourType)
        {
            string expectedPath = $"{PackageRootPath}/Runtime/Scripts/{behaviourType.Name}.asset";
            var direct = AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(expectedPath);

            int listedCount = 0;
            try
            {
                var all = UdonSharpProgramAsset.GetAllUdonSharpPrograms();
                listedCount = all != null ? all.Length : 0;
            }
            catch (Exception ex)
            {
                return $"U# プログラムアセット一覧の取得に失敗しました: {ex.Message}\n" +
                       "同じスクリプトのプログラムアセットが 2 つある可能性があります（上のログを確認してください）。";
            }

            if (direct == null)
            {
                return $"プログラムアセット {expectedPath} が存在しません。\n" +
                       "Unity のコンパイル完了を待ってから、もう一度実行してください。";
            }

            if (direct.SerializedProgramAsset == null)
            {
                return $"プログラムアセット {expectedPath} はありますが、まだコンパイルされていません。\n" +
                       "Unity のコンパイル完了を待ってから、もう一度実行してください。";
            }

            return $"プログラムアセット {expectedPath} は存在するのに、UdonSharp の一覧" +
                   $"（{listedCount} 件）から引けません。\n" +
                   "AssetDatabase のパス（フォルダ名の大文字小文字）が食い違っている状態です。\n" +
                   "Unity を一度閉じて開き直してから、もう一度 Scan Scene & Rebind を実行してください。";
        }

        /// <summary>
        /// 同じスクリプトに対する U# プログラムアセットが 2 つあると、UdonSharp の
        /// 型 → アセット対応表の構築が途中で失敗し、以降すべての追加が壊れる。先に検出して知らせる。
        /// </summary>
        private static void WarnDuplicateProgramAssets()
        {
            var seen = new Dictionary<Type, string>();

            foreach (var programAsset in UdonSharpProgramAsset.GetAllUdonSharpPrograms())
            {
                if (programAsset == null || programAsset.sourceCsScript == null) continue;

                Type cls = programAsset.sourceCsScript.GetClass();
                if (cls == null) continue;

                string path = AssetDatabase.GetAssetPath(programAsset);
                string first;
                if (seen.TryGetValue(cls, out first))
                {
                    Debug.LogError($"[QPP] 同じスクリプト {cls.Name} の U# プログラムアセットが 2 つあります。\n" +
                                   $"  {first}\n  {path}\n" +
                                   "どちらか一方を削除してから再実行してください。");
                }
                else
                {
                    seen[cls] = path;
                }
            }
        }

        [MenuItem("Tools/Yakigura/QvPalette/Setup（このシーンに導入・更新）", false, 10)]
        public static void ScanAndRebind()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[QPP] Scan Scene & Rebind cannot run in Play mode.");
                return;
            }
            // 0. Ensure all UdonSharpProgramAssets exist
            EnsureAllProgramAssets();
            WarnDuplicateProgramAssets();

            var scene = SceneManager.GetActiveScene();

            // Check for and remove old QvPalet object if present
            var oldQvPalet = GameObject.Find("QvPalet");
            if (oldQvPalet != null)
            {
                Debug.Log("[QPP] Found old QvPalet object in scene. Removing old implementation.");
                Undo.DestroyObjectImmediate(oldQvPalet);
            }

            var penManagers = FindAllPenManagersInScene();

            if (penManagers == null || penManagers.Length == 0)
            {
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("QvPalette", "シーン内に QvPen_PenManager が見つかりませんでした。", "OK");
                }
                Debug.LogWarning("[QPP] No QvPen_PenManager found in scene.");
                return;
            }

            if (penManagers.Length > 64)
            {
                if (!Application.isBatchMode)
                {
                    EditorUtility.DisplayDialog("QvPalette", $"検出されたペンの本数 ({penManagers.Length}) が上限 64 本を超えています。", "OK");
                }
                Debug.LogError($"[QPP] Pen count ({penManagers.Length}) exceeds maximum of 64.");
                return;
            }

            // 1. Locate or recreate clean QvPalette root (pure container)
            GameObject qppRoot = FindQppRoot(SceneManager.GetActiveScene());
            if (qppRoot != null)
            {
                // Clean up any legacy corrupted components directly on root if present
                var oldComps = qppRoot.GetComponents<UdonSharpBehaviour>();
                if (oldComps != null && oldComps.Length > 0)
                {
                    Undo.DestroyObjectImmediate(qppRoot);
                    qppRoot = null;
                }
            }

            if (qppRoot != null && qppRoot.name != RootName)
            {
                Undo.RecordObject(qppRoot, "Rename QvPalette Root");
                qppRoot.name = RootName;   // 旧名（QvPenPalettePlus）のルートを、新しい名前に直す
            }
            if (qppRoot == null)
            {
                qppRoot = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(qppRoot, "Create QvPalette Root");
            }
            qppRoot.transform.position = Vector3.zero;
            qppRoot.transform.rotation = Quaternion.identity;
            qppRoot.transform.localScale = Vector3.one;

            // Strict 1-GameObject-per-UdonSharpBehaviour hierarchy
            Transform setupTr = GetOrCreateChild(qppRoot.transform, "Setup");
            var setup = GetOrAddComponent<QppSetup>(setupTr.gameObject);
            // 台帳の容量は、Setup オブジェクトのインスペクターの値を使う（既定は 8 チャンク × 512 件）。
            TargetChunksPerPen = Mathf.Clamp(setup.EditorLedgerChunksPerPen, 1, 32);
            int savedCapacity = setup.EditorLedgerChunkCapacity;
            TargetChunkCapacity = (savedCapacity == 128 || savedCapacity == 256 || savedCapacity == 512 || savedCapacity == 1024) ? savedCapacity : 512;

            Transform paletteTr = GetOrCreateChild(qppRoot.transform, "Palette");
            var palette = GetOrAddComponent<QppPalette>(paletteTr.gameObject);
            // 旧い既定（最大 100 mm）のままのシーンは、新しい既定（150 mm）に上げる。ほかの値は、利用者の設定として残す。
            if (Mathf.Approximately(palette.EditorInkWidthMaxMillimeters, 100f))
            {
                Undo.RecordObject(palette, "QvPalette ink width max");
                palette.EditorSetInkWidthRange(palette.EditorInkWidthMinMillimeters, 150f, palette.EditorInkWidthDefaultMillimeters);
                EditorUtility.SetDirty(palette);
            }

            Transform observerTr = GetOrCreateChild(qppRoot.transform, "Observer");
            var observer = GetOrAddComponent<QppInkObserver>(observerTr.gameObject);

            // お気に入り 8 スロットはワールドに 1 つ。個人のものなので同期せず、
            // PlayerData で同一ワールド内のみ永続化する（Qvシリーズ構成 §8）。
            Transform favoritesTr = GetOrCreateChild(qppRoot.transform, "Favorites");
            var favorites = GetOrAddComponent<QppFavorites>(favoritesTr.gameObject);

            // 空のパレットと、旧版の仮 Material（Default-Line）だけのパレットは、初回の既定で埋める。
            // ワールドの QvPen のインクがあればそれを引き継ぎ、なければ標準のインク 1 個だけにする（色の自動生成はしない）。
            // 再バインドでシーンのペンの Material はパレット先頭へ書き換わるので、読み込みはその前に行う。
            // 自分で設定したインクがあるパレットは上書きしない。
            if (palette.EntryCount == 0 || HasOnlyPlaceholderMaterials(palette))
            {
                var initial = QppInitialInks.Build(QppInkData.ReadQvPen(null));
                QppInkData.Write(palette, initial, QppInitialInks.ApplyAutoTags(initial), "Set Initial Palette Inks");
            }

            // 既存パレットを「1 エントリ = 1 グラデーション」に正規化し、
            // rounded_trail 判定をマテリアルのシェーダーから焼き直す。
            QppPaletteEditor.EnsurePerEntryGradients(palette);
            QppPaletteEditor.RefreshRoundedFlags(palette);
            QppPaletteEditor.EnsureTags(palette);
            QppInkData.Normalize(palette);

            // 2. Locate or create Rigs and Panels container GameObjects
            Transform rigsContainer = GetOrCreateChild(qppRoot.transform, "Rigs");
            Transform panelsContainer = GetOrCreateChild(qppRoot.transform, "Panels");

            // Remove the old line and its Udon behaviour before the shared setup scans pens.
            // Existing shared previews (including ones created by QvInfoX) are reused.
            int removedLegacyPreviews = 0;
            foreach (Transform rig in rigsContainer)
            {
                var oldPreview = rig.Find("PenPreview");
                if (oldPreview == null) continue;
                Undo.DestroyObjectImmediate(oldPreview.gameObject);
                removedLegacyPreviews++;
            }
            QvInkPreview_AutoSetup.AutoSetup();
            Debug.Log($"[QPP] Preview migration: removed {removedLegacyPreviews} legacy preview objects.");

            int penCount = penManagers.Length;
            var pens = new QvPen_Pen[penCount];
            var bindings = new QppPenBinding[penCount];
            var ledgers = new QppPenLedger[penCount];
            var states = new QppPenState[penCount];
            // パレットはペンごとに用意しない。手ごとに 1 枚（右手用・左手用）だけ作り、
            // 持ったペンに動的にバインドする（Qvシリーズ構成 §8）。
            var paletteUIs = new QppPaletteUI[PanelCount];
            var menuToggles = new QppMenuToggle[PanelCount];
            var pointerProbes = new QppPointerProbe[PanelCount];
            var penPreviews = new QppPenPreview[penCount];
            var penSizes = new QppPenSize[penCount];
            var pickupListeners = new QppPenPickupListener[penCount];

            List<Transform> allPools = new List<Transform>();
            List<int> allPoolSlots = new List<int>();

            // Clean up excess rigs and panels if penCount decreased
            for (int i = rigsContainer.childCount - 1; i >= 0; i--)
            {
                var child = rigsContainer.GetChild(i);
                if (child.name.StartsWith("QPP_Pen_"))
                {
                    int slot;
                    if (int.TryParse(child.name.Substring("QPP_Pen_".Length), out slot))
                    {
                        if (slot >= penCount)
                        {
                            Undo.DestroyObjectImmediate(child.gameObject);
                        }
                    }
                }
            }

            // 旧実装のペンごとのパネル（Panel_0, Panel_1, ...）が残っていれば片付ける。
            for (int i = panelsContainer.childCount - 1; i >= 0; i--)
            {
                var child = panelsContainer.GetChild(i);
                if (child.name != PanelNameRight && child.name != PanelNameLeft)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }

            // 3. Build/update rig and panel for each pen
            for (int slot = 0; slot < penCount; slot++)
            {
                var pm = penManagers[slot];
                var pen = pm.GetComponentInChildren<QvPen_Pen>(true);
                pens[slot] = pen;

                Transform penTip = ExtractPenTip(pen);
                GameObject trailRendererGO = ExtractTrailRendererGO(pen);
                VRCPickup penPickup = (pen != null) ? pen.GetComponent<VRCPickup>() : null;

                // Collect pools from pen
                ExtractPools(pen, slot, allPools, allPoolSlots);

                // A. Rig GameObject (Container)
                string rigName = $"QPP_Pen_{slot}";
                Transform rigTr = GetOrCreateChild(rigsContainer, rigName);

                Transform bindingTr = GetOrCreateChild(rigTr, "Binding");
                var binding = GetOrAddComponent<QppPenBinding>(bindingTr.gameObject);

                Transform ledgerTr = GetOrCreateChild(rigTr, "Ledger");
                var ledger = GetOrAddComponent<QppPenLedger>(ledgerTr.gameObject);

                Transform pickupTr = GetOrCreateChild(rigTr, "PickupListener");
                var pickupListener = GetOrAddComponent<QppPenPickupListener>(pickupTr.gameObject);

                // Shared package owns placement, line, material and visibility.
                var sharedPreview = pen != null ? pen.GetComponentInChildren<QvInkPreview_Pen>(true) : null;
                if (sharedPreview == null || sharedPreview.PreviewRoot == null)
                    throw new InvalidOperationException($"[QPP] Shared preview missing for pen slot {slot}.");
                RemovePreviewWidthLabels(sharedPreview);
                sharedPreview.tailDistance = PreviewTailDistance;
                sharedPreview.upOffset = PreviewUpOffset;
                sharedPreview.maxVisibleDistance = PreviewMaxVisibleDistance;
                Transform previewTr = GetOrCreateChild(rigTr, "PenPreviewBinding");
                var penPreview = GetOrAddComponent<QppPenPreview>(previewTr.gameObject);
                penPreviews[slot] = penPreview;

                bindings[slot] = binding;
                ledgers[slot] = ledger;

                // ペン本体とクリアボタンの大きさ（全員に同期）。色・太さの State とは別オブジェクトにする。
                Transform penSizeTr = GetOrCreateChild(rigTr, "PenSize");
                var penSize = GetOrAddComponent<QppPenSize>(penSizeTr.gameObject);
                Transform clearTr = pm != null ? FindDescendant(pm.transform, "Clear") : null;
                int meshAxis;
                Transform penMeshTr = FindPenMesh(pen, out meshAxis);
                penSize.EditorSetReferences(
                    pen != null ? pen.transform : null,
                    clearTr,
                    clearTr != null ? clearTr.GetComponent<UnityEngine.Animations.PositionConstraint>() : null,
                    penMeshTr, meshAxis);
                penSizes[slot] = penSize;

                // State Child GameObject (Manual Synced)
                Transform stateTr = GetOrCreateChild(rigTr, "State");
                var penState = GetOrAddComponent<QppPenState>(stateTr.gameObject);
                states[slot] = penState;

                // Chunks (Manual Synced)
                var chunkList = new List<QppLedgerChunk>();
                for (int c = 0; c < TargetChunksPerPen; c++)
                {
                    string chunkName = $"Chunk_{c}";
                    Transform chunkTr = GetOrCreateChild(rigTr, chunkName);
                    var chunk = GetOrAddComponent<QppLedgerChunk>(chunkTr.gameObject);
                    chunk.EditorSetReferences(TargetChunkCapacity, observer, slot);
                    chunkList.Add(chunk);
                }

                // Clean up excess chunks if count decreased
                for (int i = rigTr.childCount - 1; i >= 0; i--)
                {
                    var child = rigTr.GetChild(i);
                    if (child.name.StartsWith("Chunk_"))
                    {
                        int cIdx;
                        if (int.TryParse(child.name.Substring("Chunk_".Length), out cIdx) && cIdx >= TargetChunksPerPen)
                        {
                            Undo.DestroyObjectImmediate(child.gameObject);
                        }
                    }
                }

                // B. Wire up Rig（パネルはペンごとに作らないので、ここではペン側だけを配線する）
                binding.EditorSetReferences(pm, pen, penTip, trailRendererGO, penPickup, palette, slot);
                penPreview.EditorSetReferences(sharedPreview, binding, palette);
                ledger.EditorSetReferences(chunkList.ToArray(), penState, binding, slot, QppCommon.DEFAULT_WIDTH_INDEX);

                states[slot] = penState;
                pickupListeners[slot] = pickupListener;
            }

            // 3.5 パレット本体（右手用・左手用の 2 枚）を作り、全ペンへの参照を渡す
            for (int side = 0; side < PanelCount; side++)
            {
                string panelName = (side == QppMenuToggle.SIDE_LEFT) ? PanelNameLeft : PanelNameRight;

                // パネルは作り直すが、Inspector で調整した値は引き継ぐ。
                Transform existing = panelsContainer.Find(panelName);
                string savedUiSettings = null;
                if (existing != null)
                {
                    var oldUi = existing.GetComponentInChildren<QppPaletteUI>(true);
                    if (oldUi != null) savedUiSettings = EditorJsonUtility.ToJson(oldUi);
                    Object.DestroyImmediate(existing.gameObject);
                }

                Transform panelTr = CreatePanelHierarchy(panelsContainer, panelName, palette, favorites, bindings, states);
                panelTr.localScale = Vector3.one;
                RestorePanelUiSettings(panelTr.GetComponentInChildren<QppPaletteUI>(true), savedUiSettings);

                // 開閉されるのは Panel_*/Root。位置合わせや近接判定の基準もこちら。
                Transform panelRootTr = panelTr.Find("Root");
                if (panelRootTr == null) panelRootTr = panelTr;

                var ui = panelTr.GetComponentInChildren<QppPaletteUI>(true);
                paletteUIs[side] = ui;
                ui.EditorSetPenSize(panelTr.GetComponentInChildren<QppPenSizeSlider>(true), penSizes);

                var menuToggle = panelTr.GetComponentInChildren<QppMenuToggle>(true);
                menuToggles[side] = menuToggle;

                var probe = panelTr.GetComponentInChildren<QppPointerProbe>(true);
                pointerProbes[side] = probe;

                if (menuToggle != null)
                {
                    // §4 の入力予約表により M キーと右スティックは QvShape の予約。
                    // 絵の具パレット型仕様（Rev.19）: ペン保持での自動開閉は行わず（autoOpen=false）、
                    // プレイヤーがミニ絵の具パレット具を操作して開閉・配置する。
                    menuToggle.EditorSetReferences(panelRootTr, ui, probe, bindings, side, KeyCode.None, 0.6f, 0.22f, false);
                }
                if (probe != null)
                {
                    probe.EditorSetReferences(panelRootTr, ui, true);
                }

                panelRootTr.gameObject.SetActive(false); // Initially closed (container stays active)
            }

            // 3.5.5 絵の具パレット型召喚・配置具（QppPaletteTool）を作成（§8）
            Transform toolsContainer = GetOrCreateChild(qppRoot.transform, "Tools");
            Vector3 toolSpawnPos = Vector3.up * 1.0f;
            Quaternion toolSpawnRot = Quaternion.identity;
            if (pens != null && pens.Length > 0 && pens[0] != null)
            {
                // ペンスタンド横（右に約 22cm、高さ約 2cm）
                toolSpawnPos = pens[0].transform.position + Vector3.right * 0.22f + Vector3.up * 0.02f;
                toolSpawnRot = pens[0].transform.rotation;
            }

            var mainUi = paletteUIs[QppMenuToggle.SIDE_RIGHT];
            var mainProbe = pointerProbes[QppMenuToggle.SIDE_RIGHT];
            Transform mainRootTr = panelsContainer.Find($"{PanelNameRight}/Root");
            if (mainRootTr == null) mainRootTr = panelsContainer.Find(PanelNameRight);

            if (mainUi != null && mainRootTr != null)
            {
                var paletteTool = CreatePaletteTool(toolsContainer, mainRootTr, mainUi, mainProbe, toolSpawnPos, toolSpawnRot);
                mainUi.EditorSetPaletteTool(paletteTool);
                CreateToolStand(toolsContainer, paletteTool);
            }

            // 3.6 ペン側からパネル群への参照を配線する
            for (int slot = 0; slot < penCount; slot++)
            {
                if (states[slot] != null)
                {
                    states[slot].EditorSetReferences(bindings[slot], ledgers[slot], paletteUIs, penManagers[slot], slot);
                }
                if (pickupListeners[slot] != null)
                {
                    pickupListeners[slot].EditorSetReferences(
                        setup, ledgers[slot], states[slot], paletteUIs.Length > 0 ? paletteUIs[0] : null,
                        observer,
                        penManagers[slot], pens[slot], slot, false);
                    pickupListeners[slot].EditorSetPenSize(penSizes[slot], favorites);
                }
            }

            // 4. Wire Setup and Observer
            setup.EditorSetReferences(penManagers, pens, bindings, states, palette, 0, QppCommon.DEFAULT_WIDTH_INDEX);
            favorites.EditorSetReferences(palette, "QPP_FAV_", true);
            observer.EditorSetReferences(
                allPools.ToArray(),
                allPoolSlots.ToArray(),
                setup,
                ledgers,
                bindings,
                palette,
                menuToggles,
                pointerProbes,
                paletteUIs,
                penPreviews,
                QppCommon.DEFAULT_WIDTH_INDEX);

            // 5. Apply default style (style 0) to all penManagers (§3.5)
            for (int i = 0; i < penCount; i++)
            {
                var pm = penManagers[i];
                if (pm != null)
                {
                    pm.pcInkMaterial = palette.MaterialOf(0);
                    pm.questInkMaterial = palette.MaterialOf(0);
                    pm.inkWidth = QppCommon.WidthOf(QppCommon.DEFAULT_WIDTH_INDEX);
                    EditorUtility.SetDirty(pm);
                }
            }

            // 6. Validation checks
            ValidateLayers(penManagers);
            ValidatePanelScales(paletteUIs);
            ApplyDeclaredSyncModes(scene);

            // Save dirty objects
            EditorUtility.SetDirty(qppRoot);
            EditorUtility.SetDirty(setup);
            EditorUtility.SetDirty(palette);
            EditorUtility.SetDirty(favorites);
            EditorUtility.SetDirty(observer);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[QPP] Scan Scene & Rebind completed. Bound {penCount} pens with {TargetChunksPerPen} chunks each ({allPools.Count} ink pools total).");
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("QvPalette", $"セットアップが完了しました。\n\nペン: {penCount} 本\nインク: {palette.EntryCount} 個\nチャンク: {TargetChunksPerPen} / ペン\n\nシーンを保存してください（Ctrl+S）。", "OK");
            }
        }

        /// <summary>
        /// 作り直した UdonBehaviour は同期方式が Unknown のまま残ることがあり、U# の自動補正は Unknown を対象にしない。
        /// スクリプトに宣言された同期方式をここで明示的に入れる。
        /// </summary>
        private static void ApplyDeclaredSyncModes(Scene scene)
        {
            int fixedCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var ub in root.GetComponentsInChildren<VRC.Udon.UdonBehaviour>(true))
            {
                var program = ub.programSource as UdonSharpProgramAsset;
                if (program == null) continue;
                VRC.SDKBase.Networking.SyncType want;
                switch (program.behaviourSyncMode)
                {
                    case BehaviourSyncMode.None: want = VRC.SDKBase.Networking.SyncType.None; break;
                    case BehaviourSyncMode.Manual: want = VRC.SDKBase.Networking.SyncType.Manual; break;
                    case BehaviourSyncMode.Continuous: want = VRC.SDKBase.Networking.SyncType.Continuous; break;
                    default: continue;
                }
                if (ub.SyncMethod == want) continue;
                ub.SyncMethod = want;
                EditorUtility.SetDirty(ub);
                fixedCount++;
            }
            if (fixedCount > 0) Debug.Log($"[QPP] Applied declared sync mode to {fixedCount} UdonBehaviours.");
        }

        /// <summary>ペンの見た目のメッシュ（ペン先・ポインターではないもの）と、その長さ方向の軸。</summary>
        private static Transform FindPenMesh(QvPen_Pen pen, out int lengthAxis)
        {
            lengthAxis = 1;
            if (pen == null) return null;
            Transform best = null;
            float bestSize = 0f;
            // ペン先（InkPosition 以下のポインター・マーカー）は除く。
            Transform inkPosition = pen.transform.Find("InkPosition");
            foreach (var mf in pen.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.transform == pen.transform) continue;
                if (inkPosition != null && mf.transform.IsChildOf(inkPosition)) continue;
                Vector3 size = mf.sharedMesh.bounds.size;
                float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                if (longest <= bestSize) continue;
                bestSize = longest;
                best = mf.transform;
                lengthAxis = size.x >= size.y && size.x >= size.z ? 0 : (size.y >= size.z ? 1 : 2);
            }
            return best;
        }

        private static QvPen_PenManager[] FindAllPenManagersInScene()
        {
            var pms = Object.FindObjectsOfType<QvPen_PenManager>();
            if (pms == null || pms.Length == 0) return new QvPen_PenManager[0];

            // Sort deterministically by hierarchy
            System.Array.Sort(pms, (a, b) => string.Compare(GetHierarchyPath(a.transform), GetHierarchyPath(b.transform), System.StringComparison.Ordinal));
            return pms;
        }

        private static string GetHierarchyPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform ExtractPenTip(QvPen_Pen pen)
        {
            if (pen == null) return null;
            SerializedObject so = new SerializedObject(pen);
            var p = so.FindProperty("inkPositionChild");
            if (p != null && p.objectReferenceValue != null)
            {
                return (Transform)p.objectReferenceValue;
            }
            p = so.FindProperty("inkPosition");
            if (p != null && p.objectReferenceValue != null)
            {
                return (Transform)p.objectReferenceValue;
            }
            return pen.transform;
        }

        private static GameObject ExtractTrailRendererGO(QvPen_Pen pen)
        {
            if (pen == null) return null;
            SerializedObject so = new SerializedObject(pen);
            var p = so.FindProperty("trailRenderer");
            if (p != null && p.objectReferenceValue != null)
            {
                var tr = (TrailRenderer)p.objectReferenceValue;
                return tr.gameObject;
            }
            return null;
        }

        private static void ExtractPools(QvPen_Pen pen, int slot, List<Transform> pools, List<int> slots)
        {
            if (pen == null) return;
            SerializedObject so = new SerializedObject(pen);
            var syncerProp = so.FindProperty("syncer");
            if (syncerProp != null && syncerProp.objectReferenceValue != null)
            {
                var syncer = (QvPen_LateSync)syncerProp.objectReferenceValue;
                if (syncer.InkPoolSynced != null)
                {
                    pools.Add(syncer.InkPoolSynced);
                    slots.Add(slot);
                }
                if (syncer.InkPoolNotSynced != null)
                {
                    pools.Add(syncer.InkPoolNotSynced);
                    slots.Add(slot);
                }
            }
        }

        private static Transform CreatePanelHierarchy(
            Transform parent,
            string panelName,
            QppPalette palette,
            QppFavorites favorites,
            QppPenBinding[] bindings,
            QppPenState[] penStates)
        {
            GameObject panelGO = new GameObject(panelName);
            panelGO.transform.SetParent(parent, false);
            panelGO.transform.localPosition = Vector3.zero;
            panelGO.transform.localRotation = Quaternion.identity;
            panelGO.transform.localScale = Vector3.one;

            // Panel_{slot} 自体は常にアクティブなままにし、開閉する中身は "Root" 配下に置く。
            // 表示制御を担う MenuToggle が、閉じている間も毎フレーム動ける必要があるため。
            Transform rootTr = GetOrCreateChild(panelGO.transform, "Root");
            rootTr.localPosition = Vector3.zero;
            rootTr.localRotation = Quaternion.identity;
            rootTr.localScale = Vector3.one;

            // Child GameObjects for each UdonSharpBehaviour to maintain 1-UdonSharpBehaviour-per-GameObject
            Transform uiTr = GetOrCreateChild(rootTr, "UI");
            var ui = GetOrAddComponent<QppPaletteUI>(uiTr.gameObject);

            Transform toggleTr = GetOrCreateChild(panelGO.transform, "MenuToggle");
            var toggle = GetOrAddComponent<QppMenuToggle>(toggleTr.gameObject);

            Transform probeTr = GetOrCreateChild(panelGO.transform, "PointerProbe");
            var probe = GetOrAddComponent<QppPointerProbe>(probeTr.gameObject);

            // Display Child (to allow 180 flip if visual mesh faces away, while panelRoot +Z stays pointing at player)
            Transform displayRoot = GetOrCreateChild(rootTr, "DisplayRoot");
            displayRoot.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // 減光（§3 の「開・非操作中」）を効かせるため、板のマテリアルは半透明対応にする
            Material bgMat = GetOrCreateMaterial("Qpp_Background", new Color(0.12f, 0.12f, 0.15f, 1f), true);
            Material lineMat = GetOrCreateMaterial("Qpp_PreviewLine", Color.white, false);
            Material fillMat = GetOrCreateMaterial("Qpp_FavoriteFill", new Color(0.2f, 0.9f, 1f, 1f), true);

            var dimRenderers = new List<Renderer>();
            var dimTexts = new List<TextMeshPro>();
            var touchColliders = new List<Collider>();

            // Background Quad
            GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bg.name = "Background";
            bg.transform.SetParent(displayRoot, false);
            // 板とボタンの前後差を十分に取る（1mm では近距離で並び順が不安定になる）。
            bg.transform.localPosition = new Vector3(0f, PanelCenterY, 0.006f);
            bg.transform.localScale = new Vector3(PanelWidth, PanelHeight, 1f);
            var bgRend = bg.GetComponent<MeshRenderer>();
            if (bgRend != null)
            {
                bgRend.sharedMaterial = bgMat;
                dimRenderers.Add(bgRend);
            }
            var bgCol = bg.GetComponent<Collider>();
            if (bgCol != null) Object.DestroyImmediate(bgCol); // Background doesn't need collision

            // Swatches Grid 4x4
            Transform gridGO = GetOrCreateChild(displayRoot, "Grid");
            gridGO.localPosition = new Vector3(RightColumnX, GridCenterY, 0f);

            var swatchButtons = new QppSwatchButton[16];
            // 隣を誤って選ばないよう、スウォッチ同士の間に 1.5cm の余白を取る。
            float startX = -0.105f;
            float startY = 0.105f;
            float stepX = 0.07f;
            float stepY = 0.07f;

            for (int i = 0; i < 16; i++)
            {
                int r = i / 4;
                int c = i % 4;
                Vector2 center = new Vector2(startX + c * stepX, startY - r * stepY);

                string btnName = $"Btn_{i}";
                Transform btnTr = GetOrCreateChild(gridGO, btnName);
                btnTr.localPosition = new Vector3(center.x, center.y, 0f);

                var boxCol = btnTr.GetComponent<BoxCollider>();
                if (boxCol == null) boxCol = btnTr.gameObject.AddComponent<BoxCollider>();
                boxCol.size = new Vector3(0.052f, 0.052f, 0.02f);
                boxCol.isTrigger = true; // Trigger to eliminate avatar collision
                touchColliders.Add(boxCol);

                // LineRenderer for preview line
                Transform lrTr = GetOrCreateChild(btnTr, "PreviewLine");
                var lr = lrTr.GetComponent<LineRenderer>();
                if (lr == null) lr = lrTr.gameObject.AddComponent<LineRenderer>();
                lr.useWorldSpace = false;
                // インクの部品（QvPen の inkPrefab）と同じく、線の色をリニアへ変換しない。
                // 変換するとインクのシェーダーがさらに変換するため、見本だけ色が暗く濃くなる。
                KeepGammaVertexColors(lr);
                lr.sharedMaterial = lineMat;

                // Label Text
                Transform txtTr = GetOrCreateChild(btnTr, "Label");
                txtTr.localPosition = new Vector3(0f, -0.022f, 0f);
                txtTr.localScale = Vector3.one * 0.005f;
                var tmp = txtTr.GetComponent<TextMeshPro>();
                if (tmp == null) tmp = txtTr.gameObject.AddComponent<TextMeshPro>();
                tmp.fontSize = 20;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.text = $"Style {i}";
                dimTexts.Add(tmp);

                var sb = GetOrAddComponent<QppSwatchButton>(btnTr.gameObject);
                sb.EditorSetReferences(lr, tmp, null, boxCol, ui, i, center + new Vector2(RightColumnX, GridCenterY), new Vector2(0.055f, 0.055f));
                swatchButtons[i] = sb;
            }

            // お気に入り 8 スロット（§8）。大量の色を実用的にするための必須の相方。
            Transform favRoot = GetOrCreateChild(displayRoot, "Favorites");
            favRoot.localPosition = new Vector3(LeftColumnX, FavoritesCenterY, 0f);

            // 見出し（どこがお気に入りか、どう登録するかを示す）
            Transform favTitleTr = GetOrCreateChild(favRoot, "Title");
            favTitleTr.localPosition = new Vector3(0f, 0.067f, 0f);
            favTitleTr.localScale = Vector3.one * 0.004f;
            var favTitleTmp = favTitleTr.GetComponent<TextMeshPro>();
            if (favTitleTmp == null) favTitleTmp = favTitleTr.gameObject.AddComponent<TextMeshPro>();
            favTitleTmp.fontSize = 18;
            favTitleTmp.enableWordWrapping = false;
            favTitleTmp.alignment = TextAlignmentOptions.Center;
            favTitleTmp.text = "お気に入り（長押しで登録）";
            dimTexts.Add(favTitleTmp);
            Material favFrameMat = GetOrCreateUnlitMaterial("Qpp_FavoriteFrame", new Color(0.6f, 0.6f, 0.65f, 1f));

            var favoriteButtons = new QppFavoriteButton[QppFavorites.SLOT_COUNT];
            // 4 個 x 2 段に並べる。
            const int favColumns = 4;
            float favStartX = -0.09f;
            float favStepX = 0.06f;
            float favStartY = 0.03f;
            float favStepY = 0.06f;
            Vector2 favRectSize = new Vector2(0.033f, 0.040f);

            for (int i = 0; i < QppFavorites.SLOT_COUNT; i++)
            {
                Vector2 center = new Vector2(favStartX + (i % favColumns) * favStepX, favStartY - (i / favColumns) * favStepY);

                Transform favTr = GetOrCreateChild(favRoot, $"Fav_{i}");
                favTr.localPosition = new Vector3(center.x, center.y, 0f);

                var favCol = favTr.GetComponent<BoxCollider>();
                if (favCol == null) favCol = favTr.gameObject.AddComponent<BoxCollider>();
                favCol.size = new Vector3(favRectSize.x, favRectSize.y, 0.02f);
                favCol.isTrigger = true;
                touchColliders.Add(favCol);

                Transform favLrTr = GetOrCreateChild(favTr, "PreviewLine");
                favLrTr.localPosition = new Vector3(0f, 0.006f, 0f);
                var favLr = favLrTr.GetComponent<LineRenderer>();
                if (favLr == null) favLr = favLrTr.gameObject.AddComponent<LineRenderer>();
                favLr.useWorldSpace = false;
                // インクの部品（QvPen の inkPrefab）と同じく、線の色をリニアへ変換しない。
                // 変換するとインクのシェーダーがさらに変換するため、見本だけ色が暗く濃くなる。
                KeepGammaVertexColors(favLr);
                favLr.sharedMaterial = lineMat;

                // 長押し中に満ちる表示（保存されることを事前に伝える）
                Transform fillTr = favTr.Find("Fill");
                if (fillTr == null)
                {
                    GameObject fillGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    fillGO.name = "Fill";
                    fillGO.transform.SetParent(favTr, false);
                    fillTr = fillGO.transform;
                    var fillCol = fillGO.GetComponent<Collider>();
                    if (fillCol != null) Object.DestroyImmediate(fillCol);
                }
                fillTr.localPosition = new Vector3(0f, -0.017f, 0f);
                fillTr.localScale = new Vector3(0.001f, 0.004f, 1f);
                var fillRend = fillTr.GetComponent<MeshRenderer>();
                if (fillRend != null) fillRend.sharedMaterial = fillMat;
                fillTr.gameObject.SetActive(false);

                Transform favTxtTr = GetOrCreateChild(favTr, "Label");
                favTxtTr.localPosition = new Vector3(0f, -0.011f, 0f);
                favTxtTr.localScale = Vector3.one * 0.004f;
                var favTmp = favTxtTr.GetComponent<TextMeshPro>();
                if (favTmp == null) favTmp = favTxtTr.gameObject.AddComponent<TextMeshPro>();
                favTmp.fontSize = 18;
                favTmp.alignment = TextAlignmentOptions.Center;
                favTmp.text = "空";
                dimTexts.Add(favTmp);

                // 未登録のときに出す枠
                Transform frameTr = GetOrCreateChild(favTr, "EmptyFrame");
                frameTr.localPosition = Vector3.zero;
                var frameLr = frameTr.GetComponent<LineRenderer>();
                if (frameLr == null) frameLr = frameTr.gameObject.AddComponent<LineRenderer>();
                frameLr.useWorldSpace = false;
                frameLr.loop = true;
                frameLr.alignment = LineAlignment.TransformZ;
                frameLr.widthMultiplier = 0.0012f;
                frameLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                frameLr.receiveShadows = false;
                frameLr.sharedMaterial = favFrameMat;
                float fhx = favRectSize.x * 0.5f - 0.001f;
                float fhy = favRectSize.y * 0.5f - 0.001f;
                frameLr.positionCount = 4;
                frameLr.SetPositions(new[]
                {
                    new Vector3(-fhx, fhy, 0f), new Vector3(fhx, fhy, 0f),
                    new Vector3(fhx, -fhy, 0f), new Vector3(-fhx, -fhy, 0f),
                });

                var fb = GetOrAddComponent<QppFavoriteButton>(favTr.gameObject);
                fb.EditorSetReferences(favLr, favTmp, fillTr, favCol, ui, i, center + new Vector2(LeftColumnX, FavoritesCenterY), favRectSize);
                fb.EditorSetEmptyFrame(frameTr.gameObject);
                favoriteButtons[i] = fb;
            }

            // Width Slider
            Transform sliderTr = GetOrCreateChild(displayRoot, "WidthSlider");
            sliderTr.localPosition = new Vector3(LeftColumnX, SliderCenterY, 0f);

            var sCol = sliderTr.GetComponent<BoxCollider>();
            if (sCol == null) sCol = sliderTr.gameObject.AddComponent<BoxCollider>();
            sCol.size = new Vector3(0.24f, 0.05f, 0.02f);
            sCol.isTrigger = true; // Trigger to eliminate avatar collision
            touchColliders.Add(sCol);

            // Slider Background Rail
            Transform railTr = sliderTr.Find("Rail");
            if (railTr == null)
            {
                GameObject railGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                railGO.name = "Rail";
                railGO.transform.SetParent(sliderTr, false);
                railTr = railGO.transform;
                var railCol = railGO.GetComponent<Collider>();
                if (railCol != null) Object.DestroyImmediate(railCol);
            }
            railTr.localPosition = Vector3.zero;
            railTr.localScale = new Vector3(0.20f, 0.018f, 0.008f);
            var railRend = railTr.GetComponent<MeshRenderer>();
            Material railMat = GetOrCreateMaterial("Qpp_SliderRail", new Color(0.55f, 0.58f, 0.70f, 1f), true, updateColor: true);
            if (railRend != null)
            {
                railRend.sharedMaterial = railMat;
                dimRenderers.Add(railRend);
            }

            // Slider Knob
            Transform knobTr = sliderTr.Find("Knob");
            if (knobTr == null)
            {
                GameObject knobGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                knobGO.name = "Knob";
                knobGO.transform.SetParent(sliderTr, false);
                knobTr = knobGO.transform;
                var knobCol = knobGO.GetComponent<Collider>();
                if (knobCol != null) Object.DestroyImmediate(knobCol);
            }
            knobTr.localPosition = new Vector3(0f, 0f, -0.006f);
            knobTr.localScale = new Vector3(0.026f, 0.040f, 0.020f);
            var knobRend = knobTr.GetComponent<MeshRenderer>();
            Material knobMat = GetOrCreateMaterial("Qpp_Knob", new Color(0.2f, 0.9f, 1.0f, 1f), true, updateColor: true);
            if (knobRend != null)
            {
                knobRend.sharedMaterial = knobMat;
                dimRenderers.Add(knobRend);
            }

            Transform wPreviewTr = GetOrCreateChild(sliderTr, "WidthPreviewLine");
            wPreviewTr.localPosition = new Vector3(0f, 0.025f, 0f);
            var wlr = wPreviewTr.GetComponent<LineRenderer>();
            if (wlr == null) wlr = wPreviewTr.gameObject.AddComponent<LineRenderer>();
            wlr.useWorldSpace = false;
            // インクの部品（QvPen の inkPrefab）と同じく、線の色をリニアへ変換しない。
            // 変換するとインクのシェーダーがさらに変換するため、見本だけ色が暗く濃くなる。
            KeepGammaVertexColors(wlr);
            wlr.sharedMaterial = lineMat;

            var ws = GetOrAddComponent<QppWidthSlider>(sliderTr.gameObject);
            ws.EditorSetReferences(wlr, knobTr, sCol, ui, new Vector2(-0.087f, 0.087f), new Vector2(LeftColumnX, SliderCenterY), new Vector2(0.24f, 0.04f));

            // 太さのプリセットボタン 4 個（固定値へ一発で切り替える）
            Transform presetRoot = GetOrCreateChild(displayRoot, "WidthPresets");
            presetRoot.localPosition = new Vector3(LeftColumnX, PresetCenterY, 0f);

            float[] presetMm = new float[] { 3f, 5f, 10f, 30f };
            var presetButtons = new QppWidthPresetButton[presetMm.Length];
            // 各ボタンに、そのプリセットの太さの線見本（上）と mm 表記（下）を出す。
            // 右端に、ページ切り替えのボタンを置くので、少し詰める。
            float presetStartX = -0.112f;
            float presetStepX = 0.056f;
            Vector2 presetRectSize = new Vector2(0.052f, 0.06f);

            for (int i = 0; i < presetMm.Length; i++)
            {
                Vector2 center = new Vector2(presetStartX + i * presetStepX, 0f);

                Transform presetTr = GetOrCreateChild(presetRoot, $"Preset_{i}");
                presetTr.localPosition = new Vector3(center.x, center.y, 0f);

                var presetCol = presetTr.GetComponent<BoxCollider>();
                if (presetCol == null) presetCol = presetTr.gameObject.AddComponent<BoxCollider>();
                presetCol.size = new Vector3(presetRectSize.x, presetRectSize.y, 0.02f);
                presetCol.isTrigger = true;
                touchColliders.Add(presetCol);

                Transform sampleTr = GetOrCreateChild(presetTr, "SampleLine");
                sampleTr.localPosition = new Vector3(0f, 0.008f, 0f);
                var sampleLr = sampleTr.GetComponent<LineRenderer>();
                if (sampleLr == null) sampleLr = sampleTr.gameObject.AddComponent<LineRenderer>();
                sampleLr.useWorldSpace = false;
                KeepGammaVertexColors(sampleLr);
                sampleLr.sharedMaterial = lineMat;
                sampleLr.positionCount = 2;
                sampleLr.SetPositions(new[] { new Vector3(-0.01f, 0f, 0f), new Vector3(0.01f, 0f, 0f) });
                sampleLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sampleLr.receiveShadows = false;

                Transform presetTxtTr = GetOrCreateChild(presetTr, "Label");
                presetTxtTr.localPosition = new Vector3(0f, -0.022f, 0f);
                presetTxtTr.localScale = Vector3.one * 0.004f;
                var presetTmp = presetTxtTr.GetComponent<TextMeshPro>();
                if (presetTmp == null) presetTmp = presetTxtTr.gameObject.AddComponent<TextMeshPro>();
                presetTmp.fontSize = 18;
                presetTmp.alignment = TextAlignmentOptions.Center;
                presetTmp.text = QppCommon.MillimetersText(presetMm[i] * 0.001f);
                dimTexts.Add(presetTmp);

                var pb = GetOrAddComponent<QppWidthPresetButton>(presetTr.gameObject);
                pb.EditorSetReferences(presetTmp, presetCol, ui, i, center + new Vector2(LeftColumnX, PresetCenterY), presetRectSize);
                pb.EditorSetSampleLine(sampleLr);
                presetButtons[i] = pb;
            }

            // ペンの大きさスライダー（左下）。左に倍率の表示、右にスライダー。
            const float penSliderOffsetX = 0.04f;
            Transform penSliderTr = GetOrCreateChild(displayRoot, "PenSizeSlider");
            penSliderTr.localPosition = new Vector3(LeftColumnX + penSliderOffsetX, PenSizeRowY, 0f);
            var penSliderCol = penSliderTr.GetComponent<BoxCollider>();
            if (penSliderCol == null) penSliderCol = penSliderTr.gameObject.AddComponent<BoxCollider>();
            penSliderCol.size = new Vector3(0.18f, 0.04f, 0.02f);
            penSliderCol.isTrigger = true;
            touchColliders.Add(penSliderCol);

            Transform penRailTr = penSliderTr.Find("Rail");
            if (penRailTr == null)
            {
                GameObject railGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                railGO.name = "Rail";
                railGO.transform.SetParent(penSliderTr, false);
                penRailTr = railGO.transform;
                Object.DestroyImmediate(railGO.GetComponent<Collider>());
            }
            penRailTr.localPosition = Vector3.zero;
            penRailTr.localScale = new Vector3(0.15f, 0.018f, 0.008f);
            var penRailRend = penRailTr.GetComponent<MeshRenderer>();
            penRailRend.sharedMaterial = railMat;
            dimRenderers.Add(penRailRend);

            Transform penKnobTr = penSliderTr.Find("Knob");
            if (penKnobTr == null)
            {
                GameObject knobGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
                knobGO.name = "Knob";
                knobGO.transform.SetParent(penSliderTr, false);
                penKnobTr = knobGO.transform;
                Object.DestroyImmediate(knobGO.GetComponent<Collider>());
            }
            penKnobTr.localPosition = new Vector3(0f, 0f, -0.006f);
            penKnobTr.localScale = new Vector3(0.026f, 0.040f, 0.020f);
            var penKnobRend = penKnobTr.GetComponent<MeshRenderer>();
            penKnobRend.sharedMaterial = knobMat;
            dimRenderers.Add(penKnobRend);

            Transform penTextTr = GetOrCreateChild(displayRoot, "PenSizeText");
            penTextTr.localPosition = new Vector3(LeftColumnX - 0.105f, PenSizeRowY, 0f);
            penTextTr.localScale = Vector3.one * 0.004f;
            var penTmp = penTextTr.GetComponent<TextMeshPro>();
            if (penTmp == null) penTmp = penTextTr.gameObject.AddComponent<TextMeshPro>();
            penTmp.fontSize = 18;
            penTmp.enableWordWrapping = false;
            penTmp.alignment = TextAlignmentOptions.Center;
            penTmp.text = QppCommon.PenScaleTextOf(1f);
            dimTexts.Add(penTmp);

            var pss = GetOrAddComponent<QppPenSizeSlider>(penSliderTr.gameObject);
            pss.EditorSetReferences(penKnobTr, penTmp, ui, new Vector2(-0.062f, 0.062f),
                new Vector2(LeftColumnX + penSliderOffsetX, PenSizeRowY), new Vector2(0.18f, 0.04f));

            // 太さの mm 表記（§8: 太さは見た目で判別しにくいため数値を併記する）
            Transform widthTextTr = GetOrCreateChild(displayRoot, "WidthText");
            widthTextTr.localPosition = new Vector3(WidthTextX, WidthTextY, 0f);
            widthTextTr.localScale = Vector3.one * 0.005f;
            var widthTmp = widthTextTr.GetComponent<TextMeshPro>();
            if (widthTmp == null) widthTmp = widthTextTr.gameObject.AddComponent<TextMeshPro>();
            widthTmp.fontSize = 24;
            widthTmp.alignment = TextAlignmentOptions.Center;
            widthTmp.enableWordWrapping = false;
            widthTmp.text = "Width " + QppCommon.MillimetersTextOf(QppCommon.DEFAULT_WIDTH_INDEX);
            dimTexts.Add(widthTmp);

            // Page text
            Transform pageTextTr = GetOrCreateChild(displayRoot, "PageText");
            pageTextTr.localPosition = new Vector3(RightColumnX, PageRowY, 0f);
            pageTextTr.localScale = Vector3.one * 0.006f;
            var pageTmp = pageTextTr.GetComponent<TextMeshPro>();
            if (pageTmp == null) pageTmp = pageTextTr.gameObject.AddComponent<TextMeshPro>();
            pageTmp.fontSize = 24;
            pageTmp.alignment = TextAlignmentOptions.Center;
            pageTmp.text = "1 / 4";
            dimTexts.Add(pageTmp);

            // 製品名とバージョン（パネルの右下の端）。バージョンは、Setup を実行したときのパッケージの版。
            Transform versionTr = GetOrCreateChild(displayRoot, "VersionLabel");
            versionTr.localPosition = new Vector3(0.405f, -0.252f, 0f);
            versionTr.localScale = Vector3.one * 0.0035f;
            var versionTmp = versionTr.GetComponent<TextMeshPro>();
            if (versionTmp == null) versionTmp = versionTr.gameObject.AddComponent<TextMeshPro>();
            versionTmp.rectTransform.pivot = new Vector2(1f, 0.5f);
            versionTmp.rectTransform.sizeDelta = new Vector2(70f, 6f);
            versionTmp.fontSize = 18;
            versionTmp.enableWordWrapping = false;
            versionTmp.alignment = TextAlignmentOptions.Right;
            versionTmp.color = new Color(0.78f, 0.81f, 0.88f, 1f);
            string packageVersion = QppPackagePaths.Version;
            versionTmp.text = string.IsNullOrEmpty(packageVersion) ? "QvPalette" : "QvPalette " + packageVersion;
            dimTexts.Add(versionTmp);

            // Status Indicators (Note: AddComponent<TextMeshPro> replaces Transform with RectTransform, so use component's gameObject)
            Transform busyTr = GetOrCreateChild(displayRoot, "BusyIndicator");
            busyTr.localPosition = new Vector3(0.30f, TopRowY - 0.01f, 0f);
            busyTr.localScale = Vector3.one * 0.005f;
            var busyTmp = busyTr.GetComponent<TextMeshPro>();
            if (busyTmp == null) busyTmp = busyTr.gameObject.AddComponent<TextMeshPro>();
            busyTmp.fontSize = 20;
            busyTmp.text = "SYNC...";
            busyTmp.color = Color.yellow;
            GameObject busyGO = busyTmp.gameObject;
            busyGO.SetActive(false);

            Transform roTr = GetOrCreateChild(displayRoot, "ReadOnlyIndicator");
            roTr.localPosition = new Vector3(-0.28f, TopRowY - 0.01f, 0f);
            roTr.localScale = Vector3.one * 0.005f;
            var roTmp = roTr.GetComponent<TextMeshPro>();
            if (roTmp == null) roTmp = roTr.gameObject.AddComponent<TextMeshPro>();
            roTmp.fontSize = 20;
            roTmp.text = "IN USE";
            roTmp.color = Color.red;
            GameObject roGO = roTmp.gameObject;
            roGO.SetActive(false);

            // 開くときにツールから飛び出す絵の具の粒。DisplayRoot の外に置き、表示の動きに引きずられないようにする。
            ParticleSystem openEffect = CreateOpenEffect(rootTr);

            // ペン先・指先が触れている対象を示す枠（1 枚を動かして使う）。当たり判定は持たせない。
            Transform focusTr = displayRoot.Find("FocusHighlight");
            if (focusTr == null)
            {
                GameObject focusGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
                focusGO.name = "FocusHighlight";
                focusGO.transform.SetParent(displayRoot, false);
                focusTr = focusGO.transform;
                var focusCol = focusGO.GetComponent<Collider>();
                if (focusCol != null) Object.DestroyImmediate(focusCol);
            }
            focusTr.localPosition = new Vector3(0f, 0f, 0.003f);
            focusTr.localScale = new Vector3(0.06f, 0.06f, 1f);
            var focusRend = focusTr.GetComponent<MeshRenderer>();
            if (focusRend != null)
            {
                focusRend.sharedMaterial = GetOrCreateMaterial("Qpp_Focus", new Color(1f, 1f, 1f, 0.3f), true);
                focusRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                focusRend.receiveShadows = false;
            }
            focusTr.gameObject.SetActive(false);

            // 台帳が満杯で色・太さを切り替えられないときの表示（上段中央）。
            Transform fullTr = GetOrCreateChild(displayRoot, "FullIndicator");
            fullTr.localPosition = new Vector3(0f, TopRowY, 0f);
            fullTr.localScale = Vector3.one * 0.005f;
            var fullTmp = fullTr.GetComponent<TextMeshPro>();
            if (fullTmp == null) fullTmp = fullTr.gameObject.AddComponent<TextMeshPro>();
            fullTmp.fontSize = 20;
            fullTmp.alignment = TextAlignmentOptions.Center;
            fullTmp.enableWordWrapping = false;
            fullTmp.text = "FULL - no more style changes";
            fullTmp.color = new Color(1f, 0.55f, 0.1f);
            GameObject fullGO = fullTmp.gameObject;
            fullGO.SetActive(false);

            // 「PIN」（空間固定）の表示は出さない。固定は常にオンで切り替える手段もなく、意味のない表示だったため。
            Transform obsoletePin = displayRoot.Find("PinIndicator");
            if (obsoletePin != null)
                Undo.DestroyObjectImmediate(obsoletePin.gameObject);
            GameObject pinGO = null;

            // コントロールボタン群（§8: 絵の具パレット型操作用の閉じる、スタンドへ戻す、ページ送りボタン）
            var controlButtonsList = new List<QppControlButton>();

            // 1. 閉じるボタン [×] (右上に配置)
            {
                Vector2 btnCenter = new Vector2(0.375f, TopRowY - 0.005f);
                Vector2 btnSize = new Vector2(0.06f, 0.05f);
                Transform closeTr = GetOrCreateChild(displayRoot, "Btn_Close");
                closeTr.localPosition = new Vector3(btnCenter.x, btnCenter.y, 0f);

                var closeCol = closeTr.GetComponent<BoxCollider>();
                if (closeCol == null) closeCol = closeTr.gameObject.AddComponent<BoxCollider>();
                closeCol.size = new Vector3(btnSize.x, btnSize.y, 0.02f);
                closeCol.isTrigger = true;
                touchColliders.Add(closeCol);

                Transform lblTr = GetOrCreateChild(closeTr, "Label");
                lblTr.localPosition = Vector3.zero;
                lblTr.localScale = Vector3.one * 0.01f;
                var lblTmp = lblTr.GetComponent<TextMeshPro>();
                if (lblTmp == null) lblTmp = lblTr.gameObject.AddComponent<TextMeshPro>();
                lblTmp.fontSize = 30;
                lblTmp.alignment = TextAlignmentOptions.Center;
                lblTmp.enableWordWrapping = false;
                lblTmp.text = "×";
                lblTmp.color = Color.white;
                dimTexts.Add(lblTmp);

                // 小さくて見づらかったので、赤い下地を付けて大きくする。
                EnsureButtonPlate(closeTr, btnSize, GetOrCreateUnlitMaterial("Qpp_CloseButton", new Color(0.8f, 0.25f, 0.25f, 1f)));

                var cb = GetOrAddComponent<QppControlButton>(closeTr.gameObject);
                cb.EditorSetReferences(lblTmp, closeCol, ui, "_OnCloseButtonClicked", btnCenter, btnSize);
                controlButtonsList.Add(cb);
            }

            // スタンドに戻すボタンはパネルに置かない（ツールの元の位置のスタンドを長押しして戻す）。既存のものは消す。
            Transform obsoleteResetButton = displayRoot.Find("Btn_ResetToStand");
            if (obsoleteResetButton != null)
                Undo.DestroyObjectImmediate(obsoleteResetButton.gameObject);

            // 3. 前ページボタン [◀] (ページ表記の左)
            {
                // 小さくて押しにくかったので大きくし、下地を付ける。
                Vector2 btnCenter = new Vector2(RightColumnX - 0.085f, PageRowY);
                Vector2 btnSize = new Vector2(0.06f, 0.05f);
                Transform prevTr = GetOrCreateChild(displayRoot, "Btn_PrevPage");
                prevTr.localPosition = new Vector3(btnCenter.x, btnCenter.y, 0f);

                var prevCol = prevTr.GetComponent<BoxCollider>();
                if (prevCol == null) prevCol = prevTr.gameObject.AddComponent<BoxCollider>();
                prevCol.size = new Vector3(btnSize.x, btnSize.y, 0.02f);
                prevCol.isTrigger = true;
                touchColliders.Add(prevCol);

                Transform lblTr = GetOrCreateChild(prevTr, "Label");
                lblTr.localPosition = Vector3.zero;
                lblTr.localScale = Vector3.one * 0.009f;
                var lblTmp = lblTr.GetComponent<TextMeshPro>();
                if (lblTmp == null) lblTmp = lblTr.gameObject.AddComponent<TextMeshPro>();
                lblTmp.fontSize = 22;
                lblTmp.alignment = TextAlignmentOptions.Center;
                lblTmp.enableWordWrapping = false;
                // 「◀」は文字の形がフォントによって小さな記号や □ になるので、三角形を線で描く。
                lblTmp.text = "";
                EnsureButtonPlate(prevTr, btnSize - new Vector2(0.004f, 0.006f), GetOrCreateUnlitMaterial("Qpp_TagButton", new Color(0.25f, 0.27f, 0.33f, 1f)));
                EnsureArrow(prevTr, -1f);
                lblTmp.color = Color.white;
                dimTexts.Add(lblTmp);

                var cb = GetOrAddComponent<QppControlButton>(prevTr.gameObject);
                cb.EditorSetReferences(lblTmp, prevCol, ui, "_PrevPage", btnCenter, btnSize);
                controlButtonsList.Add(cb);
            }

            // 4. 次ページボタン [▶] (ページ表記の右)
            {
                // 小さくて押しにくかったので大きくし、下地を付ける。
                Vector2 btnCenter = new Vector2(RightColumnX + 0.085f, PageRowY);
                Vector2 btnSize = new Vector2(0.06f, 0.05f);
                Transform nextTr = GetOrCreateChild(displayRoot, "Btn_NextPage");
                nextTr.localPosition = new Vector3(btnCenter.x, btnCenter.y, 0f);

                var nextCol = nextTr.GetComponent<BoxCollider>();
                if (nextCol == null) nextCol = nextTr.gameObject.AddComponent<BoxCollider>();
                nextCol.size = new Vector3(btnSize.x, btnSize.y, 0.02f);
                nextCol.isTrigger = true;
                touchColliders.Add(nextCol);

                Transform lblTr = GetOrCreateChild(nextTr, "Label");
                lblTr.localPosition = Vector3.zero;
                lblTr.localScale = Vector3.one * 0.009f;
                var lblTmp = lblTr.GetComponent<TextMeshPro>();
                if (lblTmp == null) lblTmp = lblTr.gameObject.AddComponent<TextMeshPro>();
                lblTmp.fontSize = 22;
                lblTmp.alignment = TextAlignmentOptions.Center;
                lblTmp.enableWordWrapping = false;
                // 「▶」は文字の形がフォントによって小さな記号や □ になるので、三角形を線で描く。
                lblTmp.text = "";
                EnsureButtonPlate(nextTr, btnSize - new Vector2(0.004f, 0.006f), GetOrCreateUnlitMaterial("Qpp_TagButton", new Color(0.25f, 0.27f, 0.33f, 1f)));
                EnsureArrow(nextTr, 1f);
                lblTmp.color = Color.white;
                dimTexts.Add(lblTmp);

                var cb = GetOrAddComponent<QppControlButton>(nextTr.gameObject);
                cb.EditorSetReferences(lblTmp, nextCol, ui, "_NextPage", btnCenter, btnSize);
                controlButtonsList.Add(cb);
            }

            // タグ絞り込みボタン（右端 2 列 x 5 段）。0 番は「すべて」。表示名は UI が実行時に書く。
            // 見出しを付け、ボタンには下地を敷く（選んでいるタグは下地の色で分かる）。
            Transform tagTitleTr = GetOrCreateChild(displayRoot, "TagTitle");
            tagTitleTr.localPosition = new Vector3(TagColumnX + TagColumnStepX * 0.5f, TagRowTopY + 0.037f, 0f);
            tagTitleTr.localScale = Vector3.one * 0.004f;
            var tagTitleTmp = tagTitleTr.GetComponent<TextMeshPro>();
            if (tagTitleTmp == null) tagTitleTmp = tagTitleTr.gameObject.AddComponent<TextMeshPro>();
            tagTitleTmp.fontSize = 18;
            tagTitleTmp.enableWordWrapping = false;
            tagTitleTmp.alignment = TextAlignmentOptions.Center;
            tagTitleTmp.text = "タグで絞り込み";
            dimTexts.Add(tagTitleTmp);
            Material tagPlateMat = GetOrCreateUnlitMaterial("Qpp_TagButton", new Color(0.25f, 0.27f, 0.33f, 1f));

            var tagButtonList = new QppControlButton[TagButtonCount];
            for (int i = 0; i < TagButtonCount; i++)
            {
                Vector2 btnCenter = new Vector2(TagColumnX + (i / 5) * TagColumnStepX, TagRowTopY - (i % 5) * TagRowStepY);
                Vector2 btnSize = new Vector2(0.074f, 0.042f);
                Transform tagTr = GetOrCreateChild(displayRoot, $"Btn_Tag_{i}");
                tagTr.localPosition = new Vector3(btnCenter.x, btnCenter.y, 0f);

                var tagCol = tagTr.GetComponent<BoxCollider>();
                if (tagCol == null) tagCol = tagTr.gameObject.AddComponent<BoxCollider>();
                tagCol.size = new Vector3(btnSize.x, btnSize.y, 0.02f);
                tagCol.isTrigger = true;
                touchColliders.Add(tagCol);

                Transform lblTr = GetOrCreateChild(tagTr, "Label");
                lblTr.localPosition = Vector3.zero;
                lblTr.localScale = Vector3.one * 0.0048f;
                var lblTmp = lblTr.GetComponent<TextMeshPro>();
                if (lblTmp == null) lblTmp = lblTr.gameObject.AddComponent<TextMeshPro>();
                lblTmp.fontSize = 20;
                lblTmp.alignment = TextAlignmentOptions.Center;
                lblTmp.enableWordWrapping = false;
                lblTmp.text = i == 0 ? "すべて" : $"Tag {i}";
                lblTmp.color = new Color(0.95f, 0.95f, 1f);
                dimTexts.Add(lblTmp);

                Renderer tagPlate = EnsureButtonPlate(tagTr, btnSize - new Vector2(0.004f, 0.006f), tagPlateMat);

                var cb = GetOrAddComponent<QppControlButton>(tagTr.gameObject);
                cb.EditorSetReferences(lblTmp, tagCol, ui, $"_OnTagButton{i}", btnCenter, btnSize);
                cb.EditorSetBackground(tagPlate);
                controlButtonsList.Add(cb);
                tagButtonList[i] = cb;
            }

            // ページ切り替えのボタン（太さプリセット 1/2、タグ）。ページが 1 つだけのときは、UI が隠す。
            QppControlButton presetPageButton = CreatePageButton(
                displayRoot, "Btn_PresetPage", ui, "_NextPresetPage",
                new Vector2(LeftColumnX + 0.112f, PresetCenterY), new Vector2(0.05f, 0.042f), "1/2",
                tagPlateMat, touchColliders, dimTexts, controlButtonsList);
            QppControlButton tagPageButton = CreatePageButton(
                displayRoot, "Btn_TagPage", ui, "_NextTagPage",
                new Vector2(TagColumnX + TagColumnStepX * 0.5f, -0.15f), new Vector2(0.154f, 0.036f), "タグ 1/3 →",
                tagPlateMat, touchColliders, dimTexts, controlButtonsList);
            ui.EditorEnsureWidthPresets();

            // Remove the obsolete angle button when rebinding an existing panel.
            Transform obsoleteTiltButton = displayRoot.Find("Btn_Tilt");
            if (obsoleteTiltButton != null)
                Undo.DestroyObjectImmediate(obsoleteTiltButton.gameObject);

            ui.EditorSetReferences(
                rootTr,
                bindings,
                penStates,
                palette,
                swatchButtons,
                favoriteButtons,
                favorites,
                ws,
                presetButtons,
                pageTmp,
                null,
                widthTmp,
                busyGO,
                roGO,
                pinGO,
                dimRenderers.ToArray(),
                dimTexts.ToArray(),
                touchColliders.ToArray(),
                null,
                controlButtonsList.ToArray(),
                fullGO,
                focusTr,
                tagButtonList,
                openEffect,
                presetPageButton,
                tagPageButton);

            // Default hide the panel contents until the local player picks up the pen.
            // 入れ物（Panel_{slot}）はアクティブのまま残す。
            rootTr.gameObject.SetActive(false);
            panelGO.SetActive(true);

            return panelGO.transform;
        }

        private static bool HasOnlyPlaceholderMaterials(QppPalette palette)
        {
            var mats = palette.EditorMaterials;
            if (mats == null) return true;
            foreach (var m in mats)
                if (m != null && !m.name.Contains("Default-Line")) return false;
            return true;
        }

        private static ParticleSystem CreateOpenEffect(Transform parent)
        {
            Transform fxTr = GetOrCreateChild(parent, "OpenEffect");
            var ps = fxTr.GetComponent<ParticleSystem>();
            if (ps == null) ps = fxTr.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            main.gravityModifier = -0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;

            // ツールの絵の具玉と同じ 6 色からランダムに選ぶ。
            var colors = new Gradient();
            colors.mode = GradientMode.Fixed;
            colors.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.95f, 0.25f, 0.25f), 0.17f),
                    new GradientColorKey(new Color(0.98f, 0.60f, 0.15f), 0.33f),
                    new GradientColorKey(new Color(0.98f, 0.90f, 0.15f), 0.5f),
                    new GradientColorKey(new Color(0.25f, 0.85f, 0.35f), 0.67f),
                    new GradientColorKey(new Color(0.20f, 0.65f, 0.95f), 0.83f),
                    new GradientColorKey(new Color(0.65f, 0.30f, 0.90f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));

            var renderer = fxTr.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>
        /// 絵の具パレット型召喚・配置具（QppPaletteTool）をワールド内に作成する（§8）。
        /// 手のひらサイズの薄いパレット板・親指穴・縁に並んだカラフルな絵の具玉で構成される。
        /// </summary>
        private static QppPaletteTool CreatePaletteTool(
            Transform parent,
            Transform panelRootTr,
            QppPaletteUI ui,
            QppPointerProbe probe,
            Vector3 defaultSpawnPos,
            Quaternion defaultSpawnRot)
        {
            // 既にあるツールは、シーン上で置き直された位置・角度をそのまま使う。
            bool toolExisted = parent.Find("PaletteTool") != null;
            Transform toolTr = GetOrCreateChild(parent, "PaletteTool");
            if (!toolExisted)
            {
                toolTr.position = defaultSpawnPos;
                toolTr.rotation = defaultSpawnRot;
            }

            // Rigidbody
            var rb = toolTr.GetComponent<Rigidbody>();
            if (rb == null) rb = toolTr.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            // 掴むための当たり判定はツール本体（VRCPickup と同じオブジェクト）に置く（子に置くと VRChat で掴めない）。
            // 以前の箱は見た目の板と傾きが違い、板の上の方でしか掴めなかったので、向きに左右されない球にして
            // 中心を板の中心に合わせる（下で Visual を作ったあとに設定する）。
            var oldRootCol = toolTr.GetComponent<BoxCollider>();
            if (oldRootCol != null) Object.DestroyImmediate(oldRootCol);

            // VRCPickup
            var pickup = toolTr.GetComponent<VRCPickup>();
            if (pickup == null) pickup = toolTr.gameObject.AddComponent<VRCPickup>();
            // Version 1.0 ignores AutoHold.No on several VR input devices.
            pickup.version = VRCPickup.Version.Version_1_1;
            pickup.AutoHold = VRCPickup.AutoHoldMode.No;
            pickup.allowManipulationWhenEquipped = true;
            pickup.UseText = "Trigger (VR) / P (Desktop): Toggle";
            pickup.InteractionText = "Hold to move Palette";
            // 掴める距離を縮め、パネル操作中にペンのレイがツールに向かうのを減らす（既定 2）。
            pickup.proximity = 0.5f;

            // QppPaletteTool UdonSharpBehaviour
            var pTool = GetOrAddComponent<QppPaletteTool>(toolTr.gameObject);

            // Cute Visual representation (Wood Palette plate + Thumb Hole + Paint Dabs)
            // 見た目の向きも、シーン上で調整済みならそのまま使う。
            bool visualExisted = toolTr.Find("Visual") != null;
            Transform visual = GetOrCreateChild(toolTr, "Visual");
            if (!visualExisted)
            {
                visual.localPosition = Vector3.zero;
                visual.localRotation = Quaternion.identity;
            }

            // 子（Visual）に当たり判定があるとレイを遮って掴めなくなるので外す。
            var visualCol = visual.GetComponent<Collider>();
            if (visualCol != null) Object.DestroyImmediate(visualCol);
            var grabCol = toolTr.GetComponent<SphereCollider>();
            if (grabCol == null) grabCol = toolTr.gameObject.AddComponent<SphereCollider>();
            grabCol.center = toolTr.InverseTransformPoint(visual.TransformPoint(new Vector3(0f, 0.004f, 0f)));
            grabCol.radius = 0.065f;
            grabCol.isTrigger = false;

            // 1. Palette Plate Body (thin cylinder)
            Transform bodyTr = visual.Find("Body");
            if (bodyTr == null)
            {
                GameObject bodyGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                bodyGO.name = "Body";
                bodyGO.transform.SetParent(visual, false);
                bodyTr = bodyGO.transform;
                var bCol = bodyGO.GetComponent<Collider>();
                if (bCol != null) Object.DestroyImmediate(bCol);
            }
            bodyTr.localPosition = Vector3.zero;
            bodyTr.localRotation = Quaternion.identity;
            bodyTr.localScale = new Vector3(0.12f, 0.004f, 0.09f); // 手のひらサイズの楕円パレット板
            var bodyRend = bodyTr.GetComponent<MeshRenderer>();
            Material woodMat = GetOrCreateMaterial("Qpp_PalettePlate", new Color(0.92f, 0.82f, 0.65f, 1f), false);
            if (bodyRend != null) bodyRend.sharedMaterial = woodMat;

            // 2. Thumb Hole (親指穴表現: 小さなダークグレーの円盤)
            Transform holeTr = visual.Find("ThumbHole");
            if (holeTr == null)
            {
                GameObject holeGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                holeGO.name = "ThumbHole";
                holeGO.transform.SetParent(visual, false);
                holeTr = holeGO.transform;
                var hCol = holeGO.GetComponent<Collider>();
                if (hCol != null) Object.DestroyImmediate(hCol);
            }
            holeTr.localPosition = new Vector3(-0.04f, 0.001f, -0.02f);
            holeTr.localScale = new Vector3(0.022f, 0.005f, 0.022f);
            var holeRend = holeTr.GetComponent<MeshRenderer>();
            Material holeMat = GetOrCreateMaterial("Qpp_PaletteHole", new Color(0.18f, 0.15f, 0.12f, 1f), false);
            if (holeRend != null) holeRend.sharedMaterial = holeMat;

            // 3. Paint Dabs (パレットの縁に並ぶカラフルな絵の具の盛り上がり)
            Color[] dabColors = new Color[]
            {
                new Color(0.95f, 0.25f, 0.25f), // 赤
                new Color(0.98f, 0.60f, 0.15f), // 橙
                new Color(0.98f, 0.90f, 0.15f), // 黄
                new Color(0.25f, 0.85f, 0.35f), // 緑
                new Color(0.20f, 0.65f, 0.95f), // 水色
                new Color(0.65f, 0.30f, 0.90f), // 紫
            };
            Vector3[] dabPositions = new Vector3[]
            {
                new Vector3(-0.038f, 0.003f, 0.025f),
                new Vector3(-0.018f, 0.003f, 0.036f),
                new Vector3(0.008f, 0.003f, 0.038f),
                new Vector3(0.030f, 0.003f, 0.030f),
                new Vector3(0.045f, 0.003f, 0.012f),
                new Vector3(0.045f, 0.003f, -0.012f),
            };

            Transform dabsTr = GetOrCreateChild(visual, "PaintDabs");
            for (int i = 0; i < dabColors.Length; i++)
            {
                string dabName = $"Dab_{i}";
                Transform dabTr = dabsTr.Find(dabName);
                if (dabTr == null)
                {
                    GameObject dabGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    dabGO.name = dabName;
                    dabGO.transform.SetParent(dabsTr, false);
                    dabTr = dabGO.transform;
                    var dCol = dabGO.GetComponent<Collider>();
                    if (dCol != null) Object.DestroyImmediate(dCol);
                }
                dabTr.localPosition = dabPositions[i];
                dabTr.localScale = new Vector3(0.016f, 0.008f, 0.016f);
                var dabRend = dabTr.GetComponent<MeshRenderer>();
                Material dabMat = GetOrCreateMaterial($"Qpp_DabColor_{i}", dabColors[i], false);
                if (dabRend != null) dabRend.sharedMaterial = dabMat;
            }

            pTool.EditorSetReferences(panelRootTr, ui, probe, toolTr.position, toolTr.rotation);
            pTool.EditorSetPanelAnchor(EnsurePanelAnchor(pTool, toolTr));
            return pTool;
        }

        /// <summary>
        /// パネルの表示位置を決めるツールの子「PanelPosition」。シーン上で動かす・回すとパネルがそこに出る。
        /// 既にあれば位置・向きはそのまま（ユーザーの調整を守る）。中の目印の板は EditorOnly（ワールドには入らない）。
        /// </summary>
        private static Transform EnsurePanelAnchor(QppPaletteTool pTool, Transform toolTr)
        {
            // 表示位置は見た目（Visual）の子にする。デスクトップで持つと見た目だけ向きを変えるので、
            // ツール本体の子だと、見た目とパネルの角度の関係が持つたびに変わってしまう。
            Transform visualTr = toolTr.Find("Visual");
            Transform anchorParent = visualTr != null ? visualTr : toolTr;
            Transform anchor = anchorParent.Find(PanelAnchorName);
            if (anchor == null)
            {
                anchor = toolTr.Find(PanelAnchorName); // 以前の配置（ツール本体の子）
                if (anchor != null)
                {
                    anchor.SetParent(anchorParent, true);
                }
                else
                {
                    anchor = new GameObject(PanelAnchorName).transform;
                    // 初期値は、これまでの自動配置（スポーン地点から見てツールの上に、見る人の方を向く）。
                    Vector3 lp, le;
                    pTool.EditorAutoPanelPose(FindViewerPosition(toolTr.position), out lp, out le);
                    anchor.SetPositionAndRotation(toolTr.position + toolTr.rotation * lp, toolTr.rotation * Quaternion.Euler(le));
                    anchor.SetParent(anchorParent, true);
                }
            }
            anchor.localScale = Vector3.one;

            Transform marker = anchor.Find("EditorPreview");
            if (marker == null)
            {
                marker = new GameObject("EditorPreview").transform;
                marker.SetParent(anchor, false);
            }
            marker.gameObject.tag = "EditorOnly";
            marker.localPosition = Vector3.zero;
            marker.localRotation = Quaternion.identity;
            marker.localScale = DivideScale(Vector3.one, anchor.lossyScale);

            // パネルの板（背景と同じ大きさ・位置）。両面から見えるようにする。
            Transform board = marker.Find("Board");
            if (board == null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Board";
                q.transform.SetParent(marker, false);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                board = q.transform;
            }
            board.localPosition = new Vector3(0f, PanelCenterY, 0f);
            board.localRotation = Quaternion.Euler(0f, 180f, 0f);
            board.localScale = new Vector3(PanelWidth, PanelHeight, 1f);
            var boardRend = board.GetComponent<MeshRenderer>();
            boardRend.sharedMaterial = GetOrCreateSpriteMaterial("Qpp_PanelPositionPreview", new Color(0.3f, 0.85f, 1f, 0.35f));
            boardRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            boardRend.receiveShadows = false;

            // 見る側の目印（文字は見る側から読める向き）
            Transform label = marker.Find("Label");
            if (label == null)
            {
                label = new GameObject("Label").transform;
                label.SetParent(marker, false);
            }
            label.localPosition = new Vector3(0f, PanelCenterY, 0.005f);
            label.localRotation = Quaternion.Euler(0f, 180f, 0f);
            label.localScale = Vector3.one * 0.01f;
            var tmp = label.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = label.gameObject.AddComponent<TextMeshPro>();
            tmp.fontSize = 36;
            tmp.enableWordWrapping = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.1f, 0.3f, 0.45f, 1f);
            // 編集画面の TMP フォントには日本語の文字がないので、目印は英字にする。
            tmp.text = "PANEL POSITION\nFRONT (viewer side)";
            return anchor;
        }

        /// <summary>
        /// LineRenderer の「色をリニア色空間へ変換する」（Unity 2022 で新規作成時の既定はオン）を切る。
        /// QvPen のインク（inkPrefab）はオフで、インクのシェーダー側で変換する。オンのままだと見本だけ二重に変換され、暗く濃く見える。
        /// Unity 2022.3 ではスクリプトから設定できないため、シリアライズされた値を書き換える。
        /// </summary>
        private static void KeepGammaVertexColors(LineRenderer lr)
        {
            var so = new SerializedObject(lr);
            var prop = so.FindProperty("m_ApplyActiveColorSpace");
            if (prop == null)
            {
                Debug.LogWarning("[QPP] LineRenderer.m_ApplyActiveColorSpace が見つかりません。見本の色がインクと違う可能性があります。");
                return;
            }
            if (!prop.boolValue) return;
            prop.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private const string PanelAnchorName = "PanelPosition";

        /// <summary>
        /// ツールの元の位置に置くスタンド。長押しでツールを元の位置に戻す（パネルの [戻す] ボタンの代わり）。
        /// 既にあれば位置・向きはそのまま（シーン上で置き直せる）。
        /// </summary>
        private static void CreateToolStand(Transform parent, QppPaletteTool tool)
        {
            Transform toolTr = tool.transform;
            bool existed = parent.Find("ToolStand") != null;
            Transform standTr = GetOrCreateChild(parent, "ToolStand");
            if (!existed)
            {
                // ツールの少し下に、見る人の方へ文字を向けて置く。
                Vector3 toViewer = FindViewerPosition(toolTr.position) - toolTr.position;
                toViewer.y = 0f;
                if (toViewer.sqrMagnitude < 1e-4f) toViewer = Vector3.back;
                standTr.SetPositionAndRotation(toolTr.position + Vector3.down * 0.07f,
                    Quaternion.LookRotation(-toViewer.normalized, Vector3.up));
            }
            standTr.localScale = Vector3.one;

            var col = standTr.GetComponent<BoxCollider>();
            if (col == null) col = standTr.gameObject.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0f, 0.01f, 0f);
            col.size = new Vector3(0.18f, 0.03f, 0.18f);

            Material baseMat = GetOrCreateUnlitMaterial("Qpp_ToolStand", new Color(0.55f, 0.42f, 0.3f, 1f));
            Material fillMat = GetOrCreateUnlitMaterial("Qpp_ToolStandFill", new Color(0.3f, 0.85f, 1f, 1f));

            Transform baseTr = standTr.Find("Base");
            if (baseTr == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Base";
                go.transform.SetParent(standTr, false);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                baseTr = go.transform;
            }
            baseTr.localPosition = Vector3.zero;
            baseTr.localRotation = Quaternion.identity;
            baseTr.localScale = new Vector3(0.16f, 0.006f, 0.16f);
            baseTr.GetComponent<MeshRenderer>().sharedMaterial = baseMat;

            Transform fillTr = standTr.Find("Fill");
            if (fillTr == null)
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Fill";
                go.transform.SetParent(standTr, false);
                Object.DestroyImmediate(go.GetComponent<Collider>());
                fillTr = go.transform;
            }
            fillTr.localPosition = new Vector3(0f, 0.004f, 0f);
            fillTr.localRotation = Quaternion.identity;
            fillTr.localScale = new Vector3(0.15f, 0.003f, 0.15f);
            fillTr.GetComponent<MeshRenderer>().sharedMaterial = fillMat;
            fillTr.gameObject.SetActive(false);

            Transform labelTr = GetOrCreateChild(standTr, "Label");
            labelTr.localPosition = new Vector3(0f, 0.025f, -0.11f);
            labelTr.localRotation = Quaternion.Euler(20f, 0f, 0f);
            labelTr.localScale = Vector3.one * 0.0065f;
            var tmp = labelTr.GetComponent<TextMeshPro>();
            if (tmp == null) tmp = labelTr.gameObject.AddComponent<TextMeshPro>();
            tmp.fontSize = 18;
            tmp.enableWordWrapping = false;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.text = "長押しでツールを戻す";

            var stand = GetOrAddComponent<QppToolStand>(standTr.gameObject);
            stand.EditorSetReferences(tool, fillTr, tmp);
            EditorUtility.SetDirty(stand);
        }

        /// <summary>
        /// ページ送りの三角形。太さが先端へ 0 になる線で描く（フォントに左右されない）。
        /// direction は DisplayRoot の x の向き（+1 / -1）で、先端がそちらを向く。
        /// </summary>
        private static void EnsureArrow(Transform button, float direction)
        {
            Transform arrowTr = GetOrCreateChild(button, "Arrow");
            arrowTr.localPosition = new Vector3(0f, 0f, -0.001f);
            arrowTr.localRotation = Quaternion.identity;
            arrowTr.localScale = Vector3.one;
            var lr = arrowTr.GetComponent<LineRenderer>();
            if (lr == null) lr = arrowTr.gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.alignment = LineAlignment.TransformZ;
            lr.positionCount = 2;
            lr.SetPositions(new[] { new Vector3(-0.011f * direction, 0f, 0f), new Vector3(0.011f * direction, 0f, 0f) });
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            lr.widthMultiplier = 0.028f;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = GetOrCreateSpriteMaterial("Qpp_ArrowWhite", Color.white);
        }

        /// <summary>ボタンの下地（文字の少し奥に置く板）。当たり判定は持たせない。</summary>
        private static QppControlButton CreatePageButton(
            Transform displayRoot, string name, QppPaletteUI ui, string eventName,
            Vector2 center, Vector2 size, string text, Material plateMat,
            List<Collider> touchColliders, List<TextMeshPro> dimTexts, List<QppControlButton> controlButtonsList)
        {
            Transform tr = GetOrCreateChild(displayRoot, name);
            tr.localPosition = new Vector3(center.x, center.y, 0f);

            var col = tr.GetComponent<BoxCollider>();
            if (col == null) col = tr.gameObject.AddComponent<BoxCollider>();
            col.size = new Vector3(size.x, size.y, 0.02f);
            col.isTrigger = true;
            touchColliders.Add(col);

            Transform lblTr = GetOrCreateChild(tr, "Label");
            lblTr.localPosition = Vector3.zero;
            lblTr.localScale = Vector3.one * 0.0048f;
            var lblTmp = lblTr.GetComponent<TextMeshPro>();
            if (lblTmp == null) lblTmp = lblTr.gameObject.AddComponent<TextMeshPro>();
            lblTmp.fontSize = 20;
            lblTmp.alignment = TextAlignmentOptions.Center;
            lblTmp.enableWordWrapping = false;
            lblTmp.text = text;
            lblTmp.color = new Color(0.95f, 0.95f, 1f);
            dimTexts.Add(lblTmp);

            Renderer plate = EnsureButtonPlate(tr, size - new Vector2(0.004f, 0.006f), plateMat);
            var cb = GetOrAddComponent<QppControlButton>(tr.gameObject);
            cb.EditorSetReferences(lblTmp, col, ui, eventName, center, size);
            cb.EditorSetBackground(plate);
            controlButtonsList.Add(cb);
            return cb;
        }

        private static Renderer EnsureButtonPlate(Transform button, Vector2 size, Material mat)
        {
            Transform plate = button.Find("Plate");
            if (plate == null)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Plate";
                q.transform.SetParent(button, false);
                Object.DestroyImmediate(q.GetComponent<Collider>());
                plate = q.transform;
            }
            // DisplayRoot では z > 0 が奥。背景板（0.006）より手前、文字（0）より奥。
            plate.localPosition = new Vector3(0f, 0f, 0.004f);
            plate.localRotation = Quaternion.identity;
            plate.localScale = new Vector3(size.x, size.y, 1f);
            var rend = plate.GetComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return rend;
        }

        private static Vector3 DivideScale(Vector3 v, Vector3 scale)
        {
            return new Vector3(
                Mathf.Abs(scale.x) > 1e-6f ? v.x / scale.x : v.x,
                Mathf.Abs(scale.y) > 1e-6f ? v.y / scale.y : v.y,
                Mathf.Abs(scale.z) > 1e-6f ? v.z / scale.z : v.z);
        }

        private static Material GetOrCreateSpriteMaterial(string matName, Color color)
        {
            string matFolder = QppPackagePaths.WritableMaterialsDir;
            string path = $"{matFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null && QppPackagePaths.IsImmutable)
                mat = AssetDatabase.LoadAssetAtPath<Material>($"{QppPackagePaths.MaterialsDir}/{matName}.mat");
            if (mat == null)
            {
                // Sprites/Default は両面描画・半透明で、色を _Color で指定できる。
                mat = new Material(Shader.Find("Sprites/Default")) { color = color };
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        /// <summary>プレイヤーが立つ位置（ワールドのスポーン地点）。見つからなければツールの手前 1m。</summary>
        public static Vector3 FindViewerPosition(Vector3 toolPosition)
        {
            var descriptor = Object.FindObjectOfType<VRC.SDKBase.VRC_SceneDescriptor>();
            if (descriptor != null)
            {
                if (descriptor.spawns != null && descriptor.spawns.Length > 0 && descriptor.spawns[0] != null)
                    return descriptor.spawns[0].position + Vector3.up * 1.5f;
                return descriptor.transform.position + Vector3.up * 1.5f;
            }
            return toolPosition + Vector3.back;
        }

        private static Material GetOrCreateUnlitMaterial(string matName, Color color)
        {
            string matFolder = QppPackagePaths.WritableMaterialsDir;
            string path = $"{matFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null && QppPackagePaths.IsImmutable)
                mat = AssetDatabase.LoadAssetAtPath<Material>($"{QppPackagePaths.MaterialsDir}/{matName}.mat");
            if (mat == null)
            {
                mat = new Material(Shader.Find("Unlit/Color")) { color = color };
                AssetDatabase.CreateAsset(mat, path);
            }
            return mat;
        }

        private static Material GetOrCreateMaterial(string matName, Color color, bool transparent, bool updateColor = false)
        {
            string matFolder = QppPackagePaths.WritableMaterialsDir;
            string path = $"{matFolder}/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null && QppPackagePaths.IsImmutable)
            {
                string packagePath = $"{QppPackagePaths.MaterialsDir}/{matName}.mat";
                mat = AssetDatabase.LoadAssetAtPath<Material>(packagePath);
                if (mat != null && (updateColor || transparent))
                {
                    if (!AssetDatabase.CopyAsset(packagePath, path))
                    {
                        Debug.LogWarning($"[QPP] Could not copy immutable material {packagePath} to {path}; skipping changes.");
                        return mat;
                    }
                    mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                    {
                        Debug.LogWarning($"[QPP] Could not load copied material {path}; skipping changes.");
                        return AssetDatabase.LoadAssetAtPath<Material>(packagePath);
                    }
                }
            }
            if (mat == null)
            {
                var shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                mat = new Material(shader);
                mat.color = color;
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (updateColor)
            {
                mat.color = color;
                EditorUtility.SetDirty(mat);
            }

            if (transparent)
            {
                MakeFadeMaterial(mat);
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        /// <summary>
        /// §3 の「開・非操作中（不透明度 0.25）」を MaterialPropertyBlock で表現するには、
        /// 板側が半透明を描けなければならない。Standard シェーダーを Fade モードに切り替える。
        /// </summary>
        private static void MakeFadeMaterial(Material mat)
        {
            if (mat == null || mat.shader == null) return;
            if (mat.shader.name != "Standard") return;

            mat.SetFloat("_Mode", 2f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            // 半透明キュー内は「カメラからの距離」で並ぶため、板とボタンが同じキューにいると
            // 近づいたときに並び順が入れ替わり、板がボタンや文字を覆い隠す。
            // Transparent surfaces must not write depth in VR; stale depth can leave visible trails.
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static Transform GetOrCreateChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                child = go.transform;
            }
            child.localScale = Vector3.one;
            return child;
        }

        private static T GetOrAddComponent<T>(GameObject go) where T : Component
        {
            var comp = go.GetComponent<T>();
            if (comp == null)
            {
                if (typeof(UdonSharpBehaviour).IsAssignableFrom(typeof(T)))
                {
                    UdonSharpProgramAsset programAsset;
                    if (!TryResolveProgramAsset(typeof(T), out programAsset))
                    {
                        string message =
                            $"{typeof(T).Name} の U# プログラムアセットが見つかりません。\n\n" +
                            DescribeProgramAssetFailure(typeof(T));

                        Debug.LogError($"[QPP] {message}");
                        if (!Application.isBatchMode)
                        {
                            EditorUtility.DisplayDialog("QvPalette", message, "OK");
                        }

                        throw new InvalidOperationException(
                            $"[QPP] Missing U# program asset for {typeof(T).FullName}");
                    }

                    comp = (T)(object)go.AddUdonSharpComponent(typeof(T));
                }
                else
                {
                    comp = go.AddComponent<T>();
                }
            }
            return comp;
        }

        private static void ValidateLayers(QvPen_PenManager[] pms)
        {
            if (pms == null) return;
            for (int i = 0; i < pms.Length; i++)
            {
                var pm = pms[i];
                if (pm == null) continue;
                // Layer 0 is Default. If UI elements are on Default, check if Default is in surftraceMask
                int defaultLayerMask = 1 << 0;
                if ((pm.surftraceMask.value & defaultLayerMask) != 0)
                {
                    Debug.LogWarning($"[QPP] PenManager '{pm.name}' surftraceMask includes Layer 0 (Default). " +
                        "QvPen surftrace may adhere to palette UI. Consider excluding UI layer from surftraceMask (§4.3).");
                }
            }
        }

        private static readonly string[] PanelUiSettingNames =
        {
            "widthPresetMillimeters", "panelOpacity", "inactiveOpacity", "inactiveTextOpacity",
            "fadeSeconds", "swatchPreviewWidth", "favoriteHoldSeconds",
        };

        private static void RestorePanelUiSettings(QppPaletteUI ui, string savedJson)
        {
            if (ui == null) return;
            if (string.IsNullOrEmpty(savedJson))
            {
                EnsurePresetPages(ui);
                return;
            }

            // 参照は作り直した側が正しいので、調整値だけを旧パネルから写す。
            var holder = new GameObject("QppUiSettingsHolder") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var saved = holder.AddComponent<QppPaletteUI>();
                EditorJsonUtility.FromJsonOverwrite(savedJson, saved);
                var from = new SerializedObject(saved);
                var to = new SerializedObject(ui);
                foreach (string name in PanelUiSettingNames)
                {
                    var prop = from.FindProperty(name);
                    if (prop != null) to.CopyFromSerializedProperty(prop);
                }
                to.ApplyModifiedPropertiesWithoutUndo();
                EnsurePresetPages(ui);   // 旧いパネルの値（4 個）を写したあとで、2 ページ分にそろえる
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }

        private static void EnsurePresetPages(QppPaletteUI ui)
        {
            var so = new SerializedObject(ui);
            var prop = so.FindProperty("widthPresetMillimeters");
            if (prop == null || prop.arraySize >= 8) return;
            float[] defaults = { 3f, 5f, 10f, 30f, 50f, 80f, 100f, 150f };
            int oldSize = prop.arraySize;
            prop.arraySize = defaults.Length;
            for (int i = oldSize; i < defaults.Length; i++) prop.GetArrayElementAtIndex(i).floatValue = defaults[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidatePanelScales(QppPaletteUI[] uis)
        {
            if (uis == null) return;
            for (int i = 0; i < uis.Length; i++)
            {
                if (uis[i] == null || uis[i].PanelRoot == null) continue;
                Vector3 s = uis[i].PanelRoot.lossyScale;
                if (Mathf.Abs(s.x - 1f) > 0.02f || Mathf.Abs(s.y - 1f) > 0.02f || Mathf.Abs(s.z - 1f) > 0.02f)
                {
                    Debug.LogWarning($"[QPP] PanelRoot '{uis[i].PanelRoot.name}' lossyScale is {s}, not (1,1,1). " +
                        "Proximity coordinates may be skewed (§4.3).");
                }
            }
        }
    }
}
#endif
