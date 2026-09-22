using System.Windows;
using System.Windows.Media;

namespace AudioMapTool.Utilities
{
    /// <summary>視覺樹搜尋共用工具。</summary>
    public static class VisualTreeUtilities
    {
        /// <summary>
        /// 在視覺樹底下遞迴尋找第一個指定型別的子控件(只看型別,不看名稱),找不到回傳 null。
        /// 主要用來抓 DataGrid 內部的 ScrollViewer(DataGrid 本身沒有直接公開捲動位置的屬性/方法)。
        /// </summary>
        public static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null)
                return null;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                T result = child as T;
                if (result != null)
                    return result;

                result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
