using System;
using AudioMapTool.Models;

namespace AudioMapTool.Fragment
{
    /// <summary>
    /// 四個頁簽 UserControl 都實作這個介面,支援跨頁簽同步「目前選取的列」跟「垂直捲動位置」。
    /// 因為四個頁簽顯示的是同一份資料,切頁簽時如果還停在使用者原本看的同一列、同一個捲動位置,
    /// 體驗會比每個頁簽各自回到最上面好很多。MainWindow 在切頁簽時(TabMain_SelectionChanged)
    /// 把「目前作用列」「最後捲動位置」套用到剛顯示出來的頁簽,不是即時同步看不到的頁簽——
    /// 隱藏頁簽的 DataGrid 版面配置不一定就緒,即時同步容易踩到 WPF 版面時機的問題,
    /// 而使用者本來就只看得到目前這一個頁簽,等它顯示出來才套用,效果沒有差別。
    /// </summary>
    public interface ITabSyncable
    {
        /// <summary>把選取狀態(CurrentCell/SelectedCells)設到指定列,只更新視覺上的反藍,不搶走鍵盤焦點。</summary>
        void SyncSelectedRow(AudioMapRow item);

        /// <summary>取得目前的垂直捲動位置;還沒完成版面配置(找不到內部 ScrollViewer)時回傳 0。</summary>
        double GetVerticalOffset();

        /// <summary>設定垂直捲動位置;還沒完成版面配置(找不到內部 ScrollViewer)時不做事。</summary>
        void ScrollToVerticalOffset(double offset);

        /// <summary>使用者捲動這個頁簽時觸發,帶出新的垂直捲動位置。</summary>
        event EventHandler<double> ScrollPositionChanged;
    }
}
