using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioMapTool.Converters
{
    /// <summary>
    /// 「最大音量」頁簽的音量欄位用:AudioMapRow.MaxS 是字串(跟其他欄位一致,方便存 ini),
    /// 畫面上改用 Slider(範圍 0~1)拖曳調整,這裡負責字串 &lt;-&gt; 0~1 的 double 互轉。
    /// </summary>
    public class VolumeStringConverter : IValueConverter
    {
        /// <summary>字串 → Slider.Value:剖析失敗或超出範圍一律夾在 0~1 之間,空字串當作 0。</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double result;
            if (double.TryParse(value as string, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return Math.Max(0d, Math.Min(1d, result));

            return 0d;
        }

        /// <summary>Slider.Value → 字串:固定輸出到小數點後兩位(例如 0.75)。</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double d = value is double ? (double)value : 0d;
            return d.ToString("0.00", CultureInfo.InvariantCulture);
        }
    }
}
