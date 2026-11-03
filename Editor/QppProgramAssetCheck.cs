using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Yakigura.QvPalette.Editor
{
    public static class QppProgramAssetCheck
    {
        // Ignore comments and strings before looking for a direct base type.
        private static readonly Regex CommentsAndStrings = new Regex(
            @"//[^\r\n]*|/\*[\s\S]*?\*/|@""(?:""""|[^""])*""|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'",
            RegexOptions.Compiled);
        private static readonly Regex BehaviourClass = new Regex(
            @"\bclass\s+\w+(?:\s*<[^>{}]*>)?\s*:\s*[^{};]*\bUdonSharpBehaviour\b",
            RegexOptions.Compiled);
        private static readonly Regex MetaGuid = new Regex(@"(?m)^\s*guid:\s*([0-9a-fA-F]{32})\s*$");
        private static readonly Regex SourceGuid = new Regex(@"(?m)^\s*sourceCsScript:\s*\{[^\r\n}]*\bguid:\s*([0-9a-fA-F]{32})\b");

        public static List<string> FindProblems(string packageRoot)
        {
            var problems = new List<string>();
            string runtime = Path.Combine(packageRoot, "Runtime");
            foreach (string name in new[] { "QvPalette.asmdef", "QvPalette.asmdef.meta", "QvPalette.asset", "QvPalette.asset.meta" })
                RequireFile(Path.Combine(runtime, name), problems);

            string scripts = Path.Combine(runtime, "Scripts");
            if (!Directory.Exists(scripts))
            {
                problems.Add("Missing scripts directory: " + scripts);
                return problems;
            }

            foreach (string script in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                string code = CommentsAndStrings.Replace(File.ReadAllText(script), " ");
                if (!BehaviourClass.IsMatch(code)) continue;

                string meta = script + ".meta";
                string asset = Path.ChangeExtension(script, ".asset");
                bool hasMeta = RequireFile(meta, problems);
                bool hasAsset = RequireFile(asset, problems);
                RequireFile(asset + ".meta", problems);
                if (!hasMeta || !hasAsset) continue;

                Match scriptGuid = MetaGuid.Match(File.ReadAllText(meta));
                Match sourceGuid = SourceGuid.Match(File.ReadAllText(asset));
                if (!scriptGuid.Success || !sourceGuid.Success ||
                    !string.Equals(scriptGuid.Groups[1].Value, sourceGuid.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                    problems.Add("Missing or mismatched sourceCsScript GUID: " + asset + " (expected GUID from " + meta + ")");
            }
            return problems;
        }

        private static bool RequireFile(string path, List<string> problems)
        {
            if (File.Exists(path)) return true;
            problems.Add("Missing file: " + path);
            return false;
        }
    }
}
