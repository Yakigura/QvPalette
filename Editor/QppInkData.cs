#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using QvPen.UdonScript;
using UnityEditor;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// エディタで扱うインク 1 つ分（色 / Material / タグ）。
    ///
    /// QppPalette 本体は Udon で引きやすいよう「マテリアルバンク + 番号」の形で持つが、
    /// 設定する人には 1 インク = 1 行で見せる。変換は <see cref="QppInkData"/> が受け持つ。
    /// </summary>
    public class QppInk
    {
        public string name = string.Empty;
        public Gradient gradient;
        public Material material;
        /// <summary>null は「PC と同じ」。</summary>
        public Material questMaterial;
        public List<string> tags = new List<string>();

        // アウトライン付きシェーダーのときだけ使う。outlineRatio < 0 は「Material の値から決める」。
        public float outlineRatio = -1f;
        public Color outlineColor = Color.white;
        /// <summary>x: 色相, y: 彩度, z: 明るさ（線の色からずらす量）。</summary>
        public Vector3 outlineShift = new Vector3(0f, 0f, -0.2f);

        public QppInk Clone()
        {
            var c = new QppInk
            {
                name = name,
                gradient = QppInkData.CopyGradient(gradient),
                material = material,
                questMaterial = questMaterial,
                outlineRatio = outlineRatio,
                outlineColor = outlineColor,
                outlineShift = outlineShift,
            };
            c.tags.AddRange(tags);
            return c;
        }
    }

    /// <summary>インク一覧の読み書き、ファイル入出力、QvPen からの取り込み。</summary>
    public static class QppInkData
    {
        public const int MaxInks = 4096;
        public const string FileExtension = "json";
        private const string FileFormat = "QvPenPalettePlus.Inks";

        /// <summary>パレットの内容が書き換わったとき（インポート窓から書いたときなど）。</summary>
        public static event Action<QppPalette> Changed;

        // ------------------------------------------------------------------
        // パレット <-> インク一覧
        // ------------------------------------------------------------------

        public static List<QppInk> Read(QppPalette palette)
        {
            var inks = new List<QppInk>();
            if (palette == null) return inks;

            int count = palette.EntryCount;
            var mats = palette.EditorMaterials;
            var matsQuest = palette.EditorMaterialsQuest;
            var matIndex = palette.EditorEntryMaterialIndex;
            var colors = palette.EditorEntryColor;
            var gradIndex = palette.EditorEntryGradientIndex;
            var gradients = palette.EditorGradientBank;
            var names = palette.EditorEntryNames;
            var tagNames = palette.EditorTagNames ?? new string[0];
            var masks = palette.EditorEntryTags;
            var outlineRatio = palette.EditorOutlineRatio;
            var outlineColor = palette.EditorOutlineColor;
            var outlineShift = palette.EditorOutlineShift;

            for (int i = 0; i < count; i++)
            {
                var ink = new QppInk();
                ink.name = names != null && i < names.Length && names[i] != null ? names[i] : string.Empty;

                Gradient g = null;
                if (gradIndex != null && i < gradIndex.Length && gradients != null &&
                    gradIndex[i] >= 0 && gradIndex[i] < gradients.Length)
                    g = gradients[gradIndex[i]];
                ink.gradient = g != null
                    ? CopyGradient(g)
                    : SolidGradient(colors != null && i < colors.Length ? colors[i] : Color.white);

                int mi = matIndex != null && i < matIndex.Length ? matIndex[i] : 0;
                ink.material = mats != null && mi >= 0 && mi < mats.Length ? mats[mi] : null;
                Material q = matsQuest != null && mi >= 0 && mi < matsQuest.Length ? matsQuest[mi] : null;
                ink.questMaterial = q == ink.material ? null : q;

                int mask = masks != null && i < masks.Length ? masks[i] : 0;
                for (int t = 0; t < tagNames.Length && t < QppPalette.MAX_TAGS; t++)
                    if ((mask & (1 << t)) != 0) ink.tags.Add(tagNames[t]);

                if (outlineRatio != null && i < outlineRatio.Length && outlineColor != null && i < outlineColor.Length &&
                    outlineShift != null && i < outlineShift.Length)
                {
                    ink.outlineRatio = outlineRatio[i];
                    ink.outlineColor = outlineColor[i];
                    ink.outlineShift = outlineShift[i];
                }
                else
                {
                    FillOutlineFromMaterial(ink);
                }

                inks.Add(ink);
            }
            return inks;
        }

        /// <summary>
        /// インク一覧をパレットへ書き込む。Material の組（PC / Quest）は重複をまとめてバンクにする。
        /// Material が空のインクは QvPen の標準インクにする（null のままだと描けないため）。
        /// </summary>
        public static void Write(QppPalette palette, List<QppInk> inks, string[] tagNames, string undoName = "Edit Palette Inks")
        {
            if (palette == null) return;
            int count = Mathf.Min(inks.Count, MaxInks);
            tagNames = tagNames ?? new string[0];

            var bank = new List<Material>();
            var bankQuest = new List<Material>();
            var entryMats = new int[count];
            var colors = new Color[count];
            var gradIndex = new int[count];
            var gradients = new Gradient[count];
            var names = new string[count];
            var masks = new int[count];
            var outlineRatio = new float[count];
            var outlineColor = new Color[count];
            var outlineShift = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                var ink = inks[i];
                if (ink.material == null) ink.material = DefaultInkMaterial(false);
                Material pc = ink.material;
                Material quest = ink.questMaterial != null ? ink.questMaterial : pc;

                int mi = -1;
                for (int b = 0; b < bank.Count; b++)
                {
                    if (bank[b] == pc && bankQuest[b] == quest)
                    {
                        mi = b;
                        break;
                    }
                }
                if (mi < 0)
                {
                    mi = bank.Count;
                    bank.Add(pc);
                    bankQuest.Add(quest);
                }

                entryMats[i] = mi;
                if (ink.gradient == null) ink.gradient = SolidGradient(Color.white);
                gradients[i] = CopyGradient(ink.gradient);
                gradIndex[i] = i;
                colors[i] = FirstColor(ink.gradient);
                names[i] = ink.name ?? string.Empty;
                masks[i] = MaskOf(ink.tags, tagNames);

                if (ink.outlineRatio < 0f) FillOutlineFromMaterial(ink);
                outlineRatio[i] = ink.outlineRatio;
                outlineColor[i] = ink.outlineColor;
                outlineShift[i] = ink.outlineShift;
            }

            if (bank.Count == 0)
            {
                bank.Add(DefaultInkMaterial(false));
                bankQuest.Add(DefaultInkMaterial(true));
            }

            Undo.RecordObject(palette, undoName);
            var bankArray = bank.ToArray();
            palette.EditorSetData(bankArray, bankQuest.ToArray(), RoundedFlags(bankArray), entryMats, colors, gradIndex,
                gradients, names);
            palette.EditorSetTags((string[])tagNames.Clone(), masks);
            var outlined = new bool[bankArray.Length];
            for (int b = 0; b < bankArray.Length; b++) outlined[b] = IsOutlined(bankArray[b]);
            palette.EditorSetOutline(outlined, outlineRatio, outlineColor, outlineShift);
            EditorUtility.SetDirty(palette);
            Changed?.Invoke(palette);
        }

        /// <summary>再バインド用。インク一覧の形に整え直し、アウトライン判定などを焼き直す。</summary>
        public static void Normalize(QppPalette palette)
        {
            if (palette == null || palette.EntryCount == 0) return;
            Write(palette, Read(palette), palette.EditorTagNames ?? new string[0], "Normalize Palette");
        }

        // ------------------------------------------------------------------
        // アウトライン（QvPenOutline のシェーダー）
        // ------------------------------------------------------------------

        public const string CustomOutlineColorKeyword = "_OUTLINEUSECUSTOMCOLOR_ON";
        private const string GeneratedFolder = "Assets/QvPalette Generated";
        private const string CustomColorSuffix = " (色指定)";
        private const string LineColorSuffix = " (線の色)";

        public static bool IsOutlined(Material m)
        {
            return m != null && m.shader != null && m.shader.name.Contains("outlined");
        }

        public static bool UsesCustomOutlineColor(Material m)
        {
            return m != null && m.IsKeywordEnabled(CustomOutlineColorKeyword);
        }

        /// <summary>Material に入っているアウトラインの値を、インクの初期値にする（太さは標準の太さに対する比率）。</summary>
        public static void FillOutlineFromMaterial(QppInk ink)
        {
            Material m = ink.material;
            float baseWidth = QppCommon.WidthOf(QppCommon.DEFAULT_WIDTH_INDEX);
            ink.outlineRatio = 0.6f;
            if (!IsOutlined(m)) return;
            if (m.HasProperty("_OutlineWidth")) ink.outlineRatio = m.GetFloat("_OutlineWidth") / baseWidth;
            if (m.HasProperty("_OutlineCustomColor")) ink.outlineColor = m.GetColor("_OutlineCustomColor");
            if (m.HasProperty("_OutlineHueDelta"))
                ink.outlineShift = new Vector3(m.GetFloat("_OutlineHueDelta"), m.GetFloat("_OutlineSaturationDelta"),
                    m.GetFloat("_OutlineBrightnessDelta"));
        }

        /// <summary>
        /// アウトラインの色を「指定色」にするか「線の色から作る」かは、シェーダーの都合で Material 側の設定になる。
        /// 切り替え先の Material を探し、無ければ複製して作る（元の Material は変えない）。
        /// </summary>
        public static Material OutlineColorVariant(Material m, bool custom)
        {
            if (!IsOutlined(m) || UsesCustomOutlineColor(m) == custom) return m;

            string baseName = m.name;
            if (baseName.EndsWith(CustomColorSuffix)) baseName = baseName.Substring(0, baseName.Length - CustomColorSuffix.Length);
            if (baseName.EndsWith(LineColorSuffix)) baseName = baseName.Substring(0, baseName.Length - LineColorSuffix.Length);
            string targetName = baseName + (custom ? CustomColorSuffix : LineColorSuffix);

            foreach (string name in custom ? new[] { targetName } : new[] { baseName, targetName })
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && candidate.name == name && candidate.shader == m.shader &&
                        UsesCustomOutlineColor(candidate) == custom)
                        return candidate;
                }
            }

            if (!AssetDatabase.IsValidFolder(GeneratedFolder)) AssetDatabase.CreateFolder("Assets", "QvPalette Generated");
            var variant = new Material(m) { name = targetName };
            if (custom) variant.EnableKeyword(CustomOutlineColorKeyword);
            else variant.DisableKeyword(CustomOutlineColorKeyword);
            variant.SetFloat("_OutlineUseCustomColor", custom ? 1f : 0f);
            AssetDatabase.CreateAsset(variant, AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/" + targetName + ".mat"));
            AssetDatabase.SaveAssets();
            return variant;
        }

        /// <summary>
        /// 選んだインクを今の一覧に足す（replace なら置き換える）。
        /// パレットに無いタグは空きがあれば追加し、27 個を超える分はインクから外す。
        /// </summary>
        public static List<QppInk> Merge(List<QppInk> current, string[] currentTags, List<QppInk> incoming, bool replace,
            out string[] mergedTags, out List<string> droppedTags)
        {
            var tags = new List<string>(currentTags ?? new string[0]);
            droppedTags = new List<string>();
            var result = replace ? new List<QppInk>() : new List<QppInk>(current);

            foreach (var src in incoming)
            {
                if (result.Count >= MaxInks) break;
                var ink = src.Clone();
                var kept = new List<string>();
                foreach (string t in ink.tags)
                {
                    if (!tags.Contains(t) && tags.Count < QppPalette.MAX_TAGS) tags.Add(t);
                    if (tags.Contains(t)) kept.Add(t);
                    else if (!droppedTags.Contains(t)) droppedTags.Add(t);
                }
                ink.tags = kept;
                result.Add(ink);
            }

            mergedTags = tags.ToArray();
            return result;
        }

        public static int MaskOf(List<string> tags, string[] tagNames)
        {
            int mask = 0;
            if (tags == null) return mask;
            for (int t = 0; t < tagNames.Length && t < QppPalette.MAX_TAGS; t++)
                if (tags.Contains(tagNames[t])) mask |= 1 << t;
            return mask;
        }

        public static Material DefaultInkMaterial(bool quest)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(quest
                ? "Packages/net.ureishi.qvpen/Material/Ink_Quest.mat"
                : "Packages/net.ureishi.qvpen/Material/Ink_PC.mat");
            if (m == null && quest) m = DefaultInkMaterial(false);
            return m != null ? m : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Line.mat");
        }

        public static bool[] RoundedFlags(Material[] mats)
        {
            if (mats == null || mats.Length == 0) return new bool[1];
            var flags = new bool[mats.Length];
            for (int i = 0; i < mats.Length; i++) flags[i] = IsRoundedTrail(mats[i]);
            return flags;
        }

        public static bool IsRoundedTrail(Material mat)
        {
            return mat != null && mat.shader != null && mat.shader.name.Contains("rounded_trail");
        }

        // ------------------------------------------------------------------
        // 色からの自動タグ（タグ名で付けるので、タグの並びや増減に左右されない）
        // ------------------------------------------------------------------

        public static List<string> AutoTagNames(Gradient g)
        {
            var result = new List<string>();
            Color start = FirstColor(g);
            Color end = LastColor(g);
            int mask = QppPaletteEditor.TagsOfColor(start) | QppPaletteEditor.TagsOfColor(end);
            if (ColorDistance(start, end) > 0.08f) mask |= 1 << 8;
            var defaults = QppPaletteEditor.DefaultTagNames;
            for (int t = 0; t < defaults.Length; t++)
                if ((mask & (1 << t)) != 0) result.Add(defaults[t]);
            return result;
        }

        /// <summary>既定タグ名のうちパレットにあるものだけを、色から付け直す。自分で作ったタグはそのまま。</summary>
        public static void ApplyAutoTags(QppInk ink, string[] tagNames)
        {
            var auto = AutoTagNames(ink.gradient);
            foreach (string d in QppPaletteEditor.DefaultTagNames) ink.tags.Remove(d);
            foreach (string t in auto)
                if (Array.IndexOf(tagNames, t) >= 0 && !ink.tags.Contains(t)) ink.tags.Add(t);
        }

        private static float ColorDistance(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);
        }

        // ------------------------------------------------------------------
        // QvPen のインク
        // ------------------------------------------------------------------

        /// <summary>
        /// QvPen のペン（QvPen_PenManager）から色と Material を読む。
        /// root が null ならシーン全体。同じ色・同じ Material のペンは 1 つにまとめる。
        /// </summary>
        public static List<QppInk> ReadQvPen(GameObject root)
        {
            QvPen_PenManager[] managers = root != null
                ? root.GetComponentsInChildren<QvPen_PenManager>(true)
                : UnityEngine.Object.FindObjectsOfType<QvPen_PenManager>(true);

            var inks = new List<QppInk>();
            foreach (var pm in managers)
            {
                if (pm == null) continue;
                var ink = new QppInk
                {
                    name = pm.transform.parent != null ? pm.transform.parent.name : pm.name,
                    gradient = CopyGradient(pm.colorGradient ?? SolidGradient(Color.white)),
                    material = pm.pcInkMaterial,
                    questMaterial = pm.questInkMaterial == pm.pcInkMaterial ? null : pm.questInkMaterial,
                };
                if (pm.pcInkMaterial == null) ink.material = DefaultInkMaterial(false);
                FillOutlineFromMaterial(ink);

                bool duplicate = false;
                foreach (var other in inks)
                {
                    if (other.material == ink.material && other.questMaterial == ink.questMaterial &&
                        GradientEquals(other.gradient, ink.gradient))
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate) inks.Add(ink);
            }
            return inks;
        }

        // ------------------------------------------------------------------
        // ファイル（JSON）
        // ------------------------------------------------------------------

        [Serializable]
        private class InkFile
        {
            public string format = FileFormat;
            public int version = 1;
            public string[] tags;
            public InkJson[] inks;
        }

        [Serializable]
        private class InkJson
        {
            public string name;
            public string mode;
            public ColorKeyJson[] colors;
            public AlphaKeyJson[] alphas;
            public MaterialRef material;
            public MaterialRef questMaterial;
            public string[] tags;
            public OutlineJson outline;
        }

        [Serializable]
        private class OutlineJson
        {
            public float ratio = -1f;
            public string color;
            public float hue;
            public float saturation;
            public float brightness;
        }

        [Serializable]
        private class ColorKeyJson
        {
            public string color;
            public float time;
        }

        [Serializable]
        private class AlphaKeyJson
        {
            public float alpha;
            public float time;
        }

        [Serializable]
        private class MaterialRef
        {
            public string name;
            public string guid;
            public string path;
        }

        public class FileContent
        {
            public string[] tagNames = new string[0];
            public List<QppInk> inks = new List<QppInk>();
            /// <summary>このプロジェクトで見つからなかった Material の名前（重複なし）。</summary>
            public List<string> missingMaterials = new List<string>();
            /// <summary>inks と同じ並び。true は Material が見つからなかったインク。</summary>
            public List<bool> materialMissing = new List<bool>();
        }

        public static string ToJson(List<QppInk> inks, string[] tagNames)
        {
            var file = new InkFile
            {
                tags = tagNames ?? new string[0],
                inks = new InkJson[inks.Count],
            };
            for (int i = 0; i < inks.Count; i++)
            {
                var ink = inks[i];
                Gradient g = ink.gradient ?? SolidGradient(Color.white);
                var colorKeys = g.colorKeys;
                var alphaKeys = g.alphaKeys;
                var json = new InkJson
                {
                    name = ink.name,
                    mode = g.mode.ToString(),
                    colors = new ColorKeyJson[colorKeys.Length],
                    alphas = new AlphaKeyJson[alphaKeys.Length],
                    material = RefOf(ink.material),
                    questMaterial = RefOf(ink.questMaterial),
                    tags = ink.tags.ToArray(),
                    outline = new OutlineJson
                    {
                        ratio = ink.outlineRatio,
                        color = "#" + ColorUtility.ToHtmlStringRGBA(ink.outlineColor),
                        hue = ink.outlineShift.x,
                        saturation = ink.outlineShift.y,
                        brightness = ink.outlineShift.z,
                    },
                };
                for (int k = 0; k < colorKeys.Length; k++)
                    json.colors[k] = new ColorKeyJson
                    {
                        color = "#" + ColorUtility.ToHtmlStringRGBA(colorKeys[k].color),
                        time = colorKeys[k].time,
                    };
                for (int k = 0; k < alphaKeys.Length; k++)
                    json.alphas[k] = new AlphaKeyJson { alpha = alphaKeys[k].alpha, time = alphaKeys[k].time };
                file.inks[i] = json;
            }
            return JsonUtility.ToJson(file, true);
        }

        /// <summary>読めない形式なら例外（ArgumentException）。</summary>
        public static FileContent FromJson(string text)
        {
            InkFile file;
            try
            {
                file = JsonUtility.FromJson<InkFile>(text);
            }
            catch (Exception e)
            {
                throw new ArgumentException("JSON として読めません: " + e.Message);
            }
            if (file == null || file.format != FileFormat || file.inks == null)
                throw new ArgumentException("QvPalette のインクファイルではありません。");

            var content = new FileContent { tagNames = file.tags ?? new string[0] };
            foreach (var json in file.inks)
            {
                if (json == null) continue;
                var ink = new QppInk { name = json.name ?? string.Empty };

                var g = new Gradient();
                var colorKeys = new List<GradientColorKey>();
                if (json.colors != null)
                {
                    foreach (var k in json.colors)
                    {
                        Color c;
                        if (k != null && ColorUtility.TryParseHtmlString(k.color, out c))
                            colorKeys.Add(new GradientColorKey(c, k.time));
                    }
                }
                if (colorKeys.Count == 0) colorKeys.Add(new GradientColorKey(Color.white, 0f));
                var alphaKeys = new List<GradientAlphaKey>();
                if (json.alphas != null)
                    foreach (var k in json.alphas)
                        if (k != null) alphaKeys.Add(new GradientAlphaKey(k.alpha, k.time));
                if (alphaKeys.Count == 0) alphaKeys.Add(new GradientAlphaKey(1f, 0f));
                g.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
                GradientMode mode;
                if (Enum.TryParse(json.mode, out mode)) g.mode = mode;
                ink.gradient = g;

                bool missing = false;
                ink.material = Resolve(json.material, content.missingMaterials, ref missing);
                ink.questMaterial = Resolve(json.questMaterial, content.missingMaterials, ref missing);
                if (json.tags != null)
                    foreach (string t in json.tags)
                        if (!string.IsNullOrEmpty(t) && !ink.tags.Contains(t)) ink.tags.Add(t);

                Color outlineColor;
                if (json.outline != null && json.outline.ratio >= 0f &&
                    ColorUtility.TryParseHtmlString(json.outline.color, out outlineColor))
                {
                    ink.outlineRatio = json.outline.ratio;
                    ink.outlineColor = outlineColor;
                    ink.outlineShift = new Vector3(json.outline.hue, json.outline.saturation, json.outline.brightness);
                }
                else
                {
                    FillOutlineFromMaterial(ink);
                }

                content.inks.Add(ink);
                content.materialMissing.Add(missing);
            }
            return content;
        }

        private static MaterialRef RefOf(Material m)
        {
            if (m == null) return null;
            string path = AssetDatabase.GetAssetPath(m);
            return new MaterialRef { name = m.name, path = path, guid = AssetDatabase.AssetPathToGUID(path) };
        }

        /// <summary>GUID → パス → 組み込み → 同名の Material の順で探す。</summary>
        private static Material Resolve(MaterialRef r, List<string> missingNames, ref bool missing)
        {
            if (r == null || string.IsNullOrEmpty(r.name)) return null;

            Material m = null;
            if (!string.IsNullOrEmpty(r.guid))
            {
                string p = AssetDatabase.GUIDToAssetPath(r.guid);
                if (!string.IsNullOrEmpty(p)) m = AssetDatabase.LoadAssetAtPath<Material>(p);
            }
            if (m == null && !string.IsNullOrEmpty(r.path))
            {
                if (r.path.StartsWith("Resources/unity_builtin_extra"))
                    m = AssetDatabase.GetBuiltinExtraResource<Material>(r.name + ".mat");
                else
                    m = AssetDatabase.LoadAssetAtPath<Material>(r.path);
            }
            if (m == null)
            {
                foreach (string guid in AssetDatabase.FindAssets(r.name + " t:Material"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                    if (candidate != null && candidate.name == r.name)
                    {
                        m = candidate;
                        break;
                    }
                }
            }
            if (m == null)
            {
                missing = true;
                if (!missingNames.Contains(r.name)) missingNames.Add(r.name);
            }
            return m;
        }

        // ------------------------------------------------------------------
        // Gradient
        // ------------------------------------------------------------------

        public static Gradient SolidGradient(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        public static Gradient CopyGradient(Gradient src)
        {
            if (src == null) return null;
            var g = new Gradient();
            g.SetKeys(src.colorKeys, src.alphaKeys);
            g.mode = src.mode;
            return g;
        }

        public static Color FirstColor(Gradient g)
        {
            var keys = g != null ? g.colorKeys : null;
            return keys != null && keys.Length > 0 ? keys[0].color : Color.white;
        }

        public static Color LastColor(Gradient g)
        {
            var keys = g != null ? g.colorKeys : null;
            return keys != null && keys.Length > 0 ? keys[keys.Length - 1].color : Color.white;
        }

        public static bool GradientEquals(Gradient a, Gradient b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);

            var ka = a.colorKeys;
            var kb = b.colorKeys;
            if (ka.Length != kb.Length) return false;
            for (int i = 0; i < ka.Length; i++)
            {
                if (ka[i].color != kb[i].color) return false;
                if (!Mathf.Approximately(ka[i].time, kb[i].time)) return false;
            }

            var aa = a.alphaKeys;
            var ab = b.alphaKeys;
            if (aa.Length != ab.Length) return false;
            for (int i = 0; i < aa.Length; i++)
            {
                if (!Mathf.Approximately(aa[i].alpha, ab[i].alpha)) return false;
                if (!Mathf.Approximately(aa[i].time, ab[i].time)) return false;
            }

            return a.mode == b.mode;
        }
    }
}
#endif
