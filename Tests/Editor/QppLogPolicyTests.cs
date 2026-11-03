using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Yakigura.QvPalette.Tests
{
    // 公開版では、通常の利用でログが出ないようにする。情報ログ（Debug.Log）は debugLog の後ろに置き、
    // 警告とエラー（LogWarning / LogError）は異常のときだけ出す。
    public class QppLogPolicyTests
    {
        // 公開パッケージに含めないテスト用の部品は対象外にする。
        private static readonly string[] Excluded = { "QppStressTester.cs" };

        [Test]
        public void InfoLogsAreGatedBehindDebugLog()
        {
            string scripts = Path.Combine(global::Yakigura.QvPalette.Editor.QppPackagePaths.PhysicalRoot, "Runtime", "Scripts");
            var infoLog = new Regex(@"\bDebug\.Log\s*\(");
            var offenders = new System.Collections.Generic.List<string>();
            foreach (string file in Directory.GetFiles(scripts, "*.cs"))
            {
                if (System.Array.IndexOf(Excluded, Path.GetFileName(file)) >= 0) continue;
                string[] lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || !infoLog.IsMatch(line)) continue;
                    // 同じ行の if (debugLog) か、「if (...)」を前の行に置いて debugLog を含む場合だけ許す。
                    bool gated = line.Contains("if (debugLog)") ||
                                 (i > 0 && lines[i - 1].Contains("debugLog") && lines[i - 1].TrimStart().StartsWith("if"));
                    if (!gated) offenders.Add(Path.GetFileName(file) + ":" + (i + 1) + "  " + trimmed);
                }
            }
            Assert.That(offenders, Is.Empty, "debugLog の後ろに置かれていない Debug.Log:\n" + string.Join("\n", offenders));
        }
    }
}
