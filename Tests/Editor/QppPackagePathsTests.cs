using System;
using NUnit.Framework;
using UnityEditor;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppPackagePathsTests
    {
        [Test]
        public void RootResolvesToThePaletteScriptAndDerivedPathsExist()
        {
            const string suffix = "/Runtime/Scripts/QppPalette.cs";
            string scriptPath = QppPackagePaths.Root + suffix;
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath), Is.Not.Null);
            Assert.That(Array.Exists(AssetDatabase.FindAssets("QppPalette t:MonoScript"),
                guid => AssetDatabase.GUIDToAssetPath(guid) == scriptPath), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(QppPackagePaths.Root), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(QppPackagePaths.RuntimeDir), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(QppPackagePaths.ScriptsDir), Is.True);
            Assert.That(AssetDatabase.IsValidFolder(QppPackagePaths.MaterialsDir), Is.True);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(QppPackagePaths.AsmDefPath), Is.Not.Null);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(QppPackagePaths.UdonAsmAssetPath), Is.Not.Null);
            if (!QppPackagePaths.IsImmutable)
                Assert.That(QppPackagePaths.WritableMaterialsDir, Is.EqualTo(QppPackagePaths.MaterialsDir));
        }

        [TestCase("Packages/com.yakigura.qvpalette", true, "Assets/QvPalette Generated/Materials")]
        [TestCase("Assets/Yakigura/QvPalette", false, "Assets/Yakigura/QvPalette/Materials")]
        [TestCase("Assets/Packages/QvPalette", false, "Assets/Packages/QvPalette/Materials")]
        public void WritableMaterialsPathDependsOnRoot(string root, bool immutable, string expected)
        {
            Assert.That(QppPackagePaths.IsImmutableRoot(root), Is.EqualTo(immutable));
            Assert.That(QppPackagePaths.WritableMaterialsDirForRoot(root), Is.EqualTo(expected));
        }
    }
}
