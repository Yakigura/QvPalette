using VRC.SDKBase;

namespace Yakigura.QvPalette
{
    /// <summary>
    /// ワールドの中に出る文字（日本語・英語・韓国語）。
    /// 言語は、設定が「自動」なら、見ている人の VRChat の言語に合わせる（設定で、固定もできる）。
    /// Udon では静的な配列が使えないので、文字は switch で返す。
    /// </summary>
    public static class QppText
    {
        public const int LANG_AUTO = 0;
        public const int LANG_JA = 1;
        public const int LANG_EN = 2;
        public const int LANG_KO = 3;

        /// <summary>設定（0 = 自動、1 = 日本語、2 = English、3 = 한국어）から、実際に使う言語（1〜3）を決める。</summary>
        public static int Resolve(int setting)
        {
            if (setting >= LANG_JA && setting <= LANG_KO) return setting;
            return FromCode(CurrentLanguageCode());
        }

        /// <summary>見ている人の VRChat の言語コード（ja、en、ko、zh-CN など）。取れないときは空。</summary>
        public static string CurrentLanguageCode()
        {
            // ワールドの外（エディターのテストなど）では、VRChat の言語は取れない。
            if (!Utilities.IsValid(Networking.LocalPlayer)) return "";
            string code = VRCPlayerApi.GetCurrentLanguage();
            return code == null ? "" : code;
        }

        /// <summary>言語コードから言語を決める。日本語と韓国語以外は、English。</summary>
        public static int FromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return LANG_EN;
            if (code.StartsWith("ja")) return LANG_JA;
            if (code.StartsWith("ko")) return LANG_KO;
            return LANG_EN;
        }

        public static string FavoritesTitle(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "お気に入り（長押しで登録）";
                case LANG_KO: return "즐겨찾기 (길게 눌러 저장)";
                default: return "Favorites (hold to save)";
            }
        }

        public static string TagTitle(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "タグで絞り込み";
                case LANG_KO: return "태그로 필터";
                default: return "Filter by tag";
            }
        }

        public static string TagAll(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "すべて";
                case LANG_KO: return "전체";
                default: return "All";
            }
        }

        public static string TagPage(int lang, int page, int pages)
        {
            string numbers = page + "/" + pages + " →";
            switch (lang)
            {
                case LANG_JA: return "タグ " + numbers;
                case LANG_KO: return "태그 " + numbers;
                default: return "Tags " + numbers;
            }
        }

        public static string PenLabel(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "ペン";
                case LANG_KO: return "펜";
                default: return "Pen";
            }
        }

        /// <summary>言語ボタンに出す、いまの設定の名前（0 = 自動、1 = 日本語、2 = English、3 = 한국어）。</summary>
        public static string LanguageName(int setting, int lang)
        {
            switch (setting)
            {
                case LANG_JA: return "日本語";
                case LANG_EN: return "English";
                case LANG_KO: return "한국어";
                default:
                    switch (lang)
                    {
                        case LANG_JA: return "自動";
                        case LANG_KO: return "자동";
                        default: return "Auto";
                    }
            }
        }

        /// <summary>登録されていないお気に入りの枠。</summary>
        public static string EmptySlot(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "空";
                case LANG_KO: return "빔";
                default: return "Empty";
            }
        }

        /// <summary>太さの表示の頭。</summary>
        public static string WidthLabel(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "太さ";
                case LANG_KO: return "두께";
                default: return "Width";
            }
        }

        /// <summary>同期中の表示。</summary>
        public static string Syncing(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "同期中...";
                case LANG_KO: return "동기화 중...";
                default: return "SYNC...";
            }
        }

        /// <summary>ほかの人が持っているペンの表示。</summary>
        public static string InUse(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "使用中";
                case LANG_KO: return "사용 중";
                default: return "IN USE";
            }
        }

        /// <summary>切り替えの上限の表示。</summary>
        public static string Full(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "満杯 - これ以上切り替えられません";
                case LANG_KO: return "가득 참 - 더 이상 전환할 수 없습니다";
                default: return "FULL - no more style changes";
            }
        }

        /// <summary>ツールにカーソルを合わせたときの文字。</summary>
        public static string ToolGrab(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "つかんで動かす";
                case LANG_KO: return "잡고 움직이기";
                default: return "Hold to move Palette";
            }
        }

        /// <summary>ツールを持っているときに出る、開閉の操作の説明。</summary>
        public static string ToolToggle(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "トリガー（VR）/ P（Desktop）：開閉";
                case LANG_KO: return "트리거(VR) / P(Desktop): 열기·닫기";
                default: return "Trigger (VR) / P (Desktop): Toggle";
            }
        }

        public static string StandInteraction(int lang)
        {
            switch (lang)
            {
                case LANG_JA: return "長押しでツールを戻す";
                case LANG_KO: return "길게 눌러 도구를 되돌리기";
                default: return "Hold to return the tool";
            }
        }
    }
}
