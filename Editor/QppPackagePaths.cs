using System;
using System.IO;
using UnityEditor;

namespace Yakigura.QvPalette.Editor
{
    public static class QppPackagePaths
    {
        private const string DefaultRoot = "Assets/Yakigura/QvPalette";
        private const string PaletteSuffix = "/Runtime/Scripts/QppPalette.cs";
        private static string root;

        public static string Root
        {
            get
            {
                if (root != null) return root;
                foreach (string guid in AssetDatabase.FindAssets("QppPalette t:MonoScript"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(PaletteSuffix, StringComparison.Ordinal)) continue;
                    // 見つかった場所だけ覚える。見つからなかった代替値は覚えず、次に呼ばれたときにもう一度探す。
                    root = path.Substring(0, path.Length - PaletteSuffix.Length);
                    return root;
                }
                return DefaultRoot;
            }
        }

        public static string RuntimeDir => Root + "/Runtime";
        public static string ScriptsDir => RuntimeDir + "/Scripts";
        public static string AsmDefPath => RuntimeDir + "/QvPalette.asmdef";
        public static string UdonAsmAssetPath => RuntimeDir + "/QvPalette.asset";
        public static string MaterialsDir => Root + "/Materials";

        // System.IO で読むための実フォルダー。Packages/ に置かれたものも、Library/PackageCache にあるものも解決する。
        public static string PhysicalRoot
        {
            get
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(Root);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath)) return info.resolvedPath;
                return Path.GetFullPath(Root);
            }
        }

        public static bool IsImmutable => IsImmutableRoot(Root);

        public static bool IsImmutableRoot(string packageRoot) =>
            packageRoot.StartsWith("Packages/", StringComparison.Ordinal);

        public static string WritableMaterialsDirForRoot(string packageRoot) =>
            IsImmutableRoot(packageRoot) ? "Assets/QvPalette Generated/Materials" : packageRoot + "/Materials";

        public static string WritableMaterialsDir
        {
            get
            {
                string path = WritableMaterialsDirForRoot(Root);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                if (IsImmutable && !AssetDatabase.IsValidFolder(path)) AssetDatabase.Refresh();
                return path;
            }
        }
    }
}
