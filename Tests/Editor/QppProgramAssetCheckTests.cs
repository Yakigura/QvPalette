using System;
using System.IO;
using NUnit.Framework;
using Yakigura.QvPalette.Editor;

namespace Yakigura.QvPalette.Tests
{
    public class QppProgramAssetCheckTests
    {
        private string root;
        private string script;
        private const string Guid = "0123456789abcdef0123456789abcdef";

        [Test]
        public void RealPackageHasAllProgramAssets()
        {
            Assert.That(QppProgramAssetCheck.FindProblems(QppPackagePaths.PhysicalRoot), Is.Empty);
        }

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(Path.GetTempPath(), "QppProgramAssetCheckTests-" + System.Guid.NewGuid().ToString("N"));
            string runtime = Path.Combine(root, "Runtime");
            string scripts = Path.Combine(runtime, "Scripts");
            Directory.CreateDirectory(scripts);
            foreach (string name in new[] { "QvPalette.asmdef", "QvPalette.asmdef.meta", "QvPalette.asset", "QvPalette.asset.meta" })
                File.WriteAllText(Path.Combine(runtime, name), "fixture");
            script = Path.Combine(scripts, "Example.cs");
            File.WriteAllText(script, "class Example : UdonSharpBehaviour {}\n");
            File.WriteAllText(script + ".meta", "guid: " + Guid + "\n");
            File.WriteAllText(Path.ChangeExtension(script, ".asset"), "  sourceCsScript: {fileID: 11500000, guid: " + Guid + ", type: 3}\n");
            File.WriteAllText(Path.ChangeExtension(script, ".asset.meta"), "fixture");
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null && Directory.Exists(root)) Directory.Delete(root, true);
        }

        [Test]
        public void ValidFixtureAndNonBehaviourScriptPass()
        {
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(script), "Other.cs"), "// class Fake : UdonSharpBehaviour {}\nclass Other {}\n");
            Assert.That(QppProgramAssetCheck.FindProblems(root), Is.Empty);
        }

        [TestCase("Example.asset")]
        [TestCase("Example.cs.meta")]
        [TestCase("Example.asset.meta")]
        public void MissingProgramFileIsReported(string name)
        {
            File.Delete(Path.Combine(Path.GetDirectoryName(script), name));
            Assert.That(QppProgramAssetCheck.FindProblems(root), Has.Some.Contains(name));
        }

        [TestCase("QvPalette.asmdef")]
        [TestCase("QvPalette.asmdef.meta")]
        [TestCase("QvPalette.asset")]
        [TestCase("QvPalette.asset.meta")]
        public void MissingAssemblyFileIsReported(string name)
        {
            File.Delete(Path.Combine(root, "Runtime", name));
            Assert.That(QppProgramAssetCheck.FindProblems(root), Has.Some.Contains(name));
        }

        [Test]
        public void MismatchedSourceGuidIsReported()
        {
            File.WriteAllText(script + ".meta", "guid: ffffffffffffffffffffffffffffffff\n");
            Assert.That(QppProgramAssetCheck.FindProblems(root), Has.Some.Contains("sourceCsScript GUID"));
        }
    }
}
