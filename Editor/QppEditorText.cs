#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Yakigura.QvPalette.Editor
{
    /// <summary>
    /// Unity の画面（Palette Editor、Setup、ダイアログ、インスペクター）の文字を、日本語・英語・韓国語で切り替える。
    /// 使い方: QppEditorText.T("日本語", "English", "한국어")。引数は、そのまま文字として使われる
    /// （$"..." の文字は、先に作ってから渡す: QppEditorText.T($"{n} 個", $"{n} items", $"{n}개")）。
    /// 言語は、設定（EditorPrefs）が「自動」なら、Unity の言語（Application.systemLanguage）に合わせる。
    /// </summary>
    public static class QppEditorText
    {
        public const int AutoSetting = 0;
        public const int Japanese = 1;
        public const int English = 2;
        public const int Korean = 3;

        private const string PrefKey = "Yakigura.QvPalette.EditorLanguage";
        private static int forced = -1;   // テスト用。-1 なら、使わない。

        /// <summary>選んだ設定（0 = 自動、1 = 日本語、2 = English、3 = 한국어）。</summary>
        public static int Setting
        {
            get { return EditorPrefs.GetInt(PrefKey, AutoSetting); }
            set { EditorPrefs.SetInt(PrefKey, Mathf.Clamp(value, AutoSetting, Korean)); }
        }

        /// <summary>実際に使う言語（1 = 日本語、2 = English、3 = 한국어）。</summary>
        public static int Effective
        {
            get
            {
                if (forced >= 0) return forced;
                int setting = Setting;
                if (setting != AutoSetting) return setting;
                return FromSystemLanguage(Application.systemLanguage);
            }
        }

        public static int FromSystemLanguage(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Japanese: return Japanese;
                case SystemLanguage.Korean: return Korean;
                default: return English;
            }
        }

        public static string T(string ja, string en, string ko)
        {
            switch (Effective)
            {
                case Japanese: return ja;
                case Korean: return ko;
                default: return en;
            }
        }

        /// <summary>テスト用: 言語を固定する（-1 で、固定を解く）。</summary>
        public static void ForceForTest(int language)
        {
            forced = language;
        }

        private static readonly string[] LanguageLabels = { "Auto / 自動 / 자동", "日本語", "English", "한국어" };

        /// <summary>言語の選択欄（Palette Editor や Setup のインスペクターの上に置く）。</summary>
        public static void DrawLanguagePopup()
        {
            int current = Setting;
            int next = EditorGUILayout.Popup("Language / 言語 / 언어", current, LanguageLabels);
            if (next != current) Setting = next;
        }
    }
}
#endif
