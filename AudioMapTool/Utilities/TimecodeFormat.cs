using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace AudioMapTool.Utilities
{
    /// <summary>
    /// 「開場片段」「循環片段」頁簽的開始/結束時間欄位共用:格式固定為「時:分:秒:碼」(HH:mm:ss:ffff)。
    /// </summary>
    public static class TimecodeFormat
    {
        private static readonly Regex Pattern = new Regex(@"^\d{2}:\d{2}:\d{2}:\d{4}$");

        /// <summary>欄位允許的最大字元數(HH:mm:ss:ffff = 13 個字元),給 TextBox.MaxLength 用。</summary>
        public const int MaxLength = 13;

        /// <summary>空字串視為合法(尚未填);有值的話一定要符合 HH:mm:ss:ffff。</summary>
        public static bool IsValid(string value)
        {
            return string.IsNullOrEmpty(value) || Pattern.IsMatch(value);
        }

        /// <summary>
        /// 把 HH:mm:ss:ffff 剖析成 TimeSpan,供播放時定位用。最後的「碼」目前當成萬分之一秒處理
        /// (0456 = 0.0456 秒),不是幀數;如果之後確認要用幀率(fps)換算,再回來調整這裡就好。
        /// 空字串或格式不符回傳 false。
        /// </summary>
        public static bool TryParse(string value, out TimeSpan result)
        {
            result = TimeSpan.Zero;

            if (string.IsNullOrEmpty(value) || !Pattern.IsMatch(value))
                return false;

            string[] parts = value.Split(':');
            int hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int seconds = int.Parse(parts[2], CultureInfo.InvariantCulture);
            int code = int.Parse(parts[3], CultureInfo.InvariantCulture);

            result = new TimeSpan(0, hours, minutes, seconds, code / 10);
            return true;
        }

        /// <summary>
        /// TryParse 的反向操作:把 TimeSpan 格式化成 HH:mm:ss:ffff,給播放進度條旁邊的時間顯示用。
        /// 因為 TryParse 把「碼」換算成毫秒時已經有精度損失(0456 -> 45ms,不是精確的 456),
        /// 這裡的往返轉換只求好讀,不保證跟原始輸入逐碼相同。
        /// </summary>
        public static string Format(TimeSpan value)
        {
            if (value < TimeSpan.Zero)
                value = TimeSpan.Zero;

            int code = value.Milliseconds * 10;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}:{3:0000}",
                (int)value.TotalHours, value.Minutes, value.Seconds, code);
        }

        /// <summary>TextBox.PreviewTextInput 共用:只允許數字跟冒號,擋掉其他輸入(字母、符號...等)。</summary>
        public static void FilterInput(object sender, TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != ':')
                {
                    e.Handled = true;
                    return;
                }
            }
        }
    }
}
