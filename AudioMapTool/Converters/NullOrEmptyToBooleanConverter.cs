using System;
using System.Globalization;
using System.Windows.Data;

namespace AudioMapTool.Converters
{
    /// <summary>
    /// 把字串轉成布林值:null 或空字串回傳 false,否則回傳 true。
    /// 用在「開場片段」「循環片段」「最大音量」頁簽的 DataGridRow.IsEnabled——
    /// 當該列在「路徑」頁簽的音效檔路徑(AudioMapRow.Path)是空字串時,這三個頁簽對應的那一列要反灰不能使用。
    /// </summary>
    public class NullOrEmptyToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !string.IsNullOrEmpty(value as string);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
