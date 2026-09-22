using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioMapTool.Models;
using AudioMapTool.Utilities;
using Microsoft.Win32;

namespace AudioMapTool.Fragment
{
    /// <summary>
    /// PathMapControl.xaml 的互動邏輯。
    /// 「路徑」頁簽:選/功能代號/音效檔名稱。四個頁簽(路徑/開場片段/循環片段/最大音量)共用同一份
    /// ObservableCollection&lt;AudioMapRow&gt;(由 MainWindow 指定給 Items),每一列在四個頁簽都是同一個
    /// AudioMapRow 物件,只是顯示不同欄位——這裡編輯的 Code/FileName 在其他頁簽也是同一份資料。
    /// 復原/取消復原堆疊由 MainWindow 統一管理:這裡的儲存格編輯只在真的造成異動時,
    /// 透過 UndoSnapshotRequested 事件把「異動前」的快照丟給 MainWindow 記錄。
    /// 實作 ITabSyncable,支援切頁簽時同步「目前選取的列」跟「垂直捲動位置」。
    /// </summary>
    public partial class PathMapControl : UserControl, ITabSyncable
    {
        /// <summary>
        /// 音效檔的基準資料夾(由 MainWindow 工具列的「資料夾」選取後指定)。
        /// 「音效檔名稱」欄只存檔名,實際檔案位置 = FolderPath + 該列的 FileName。
        /// 用 DependencyProperty 而不是一般屬性,讓 XAML 的 {Binding FolderPath, RelativeSource=...} 能感知變化,
        /// 沒選資料夾(空字串/null)之前,「音效檔名稱」欄整欄不能用。
        /// </summary>
        public static readonly DependencyProperty FolderPathProperty =
            DependencyProperty.Register("FolderPath", typeof(string), typeof(PathMapControl), new PropertyMetadata(null));

        public string FolderPath
        {
            get { return (string)GetValue(FolderPathProperty); }
            set { SetValue(FolderPathProperty, value); }
        }

        private ObservableCollection<AudioMapRow> _items;

        /// <summary>四個頁簽共用的資料來源,由 MainWindow 在建立視窗時指定(四個頁簽拿到同一個實體)。</summary>
        public ObservableCollection<AudioMapRow> Items
        {
            get { return _items; }
            set
            {
                _items = value;
                dgAudioMap.ItemsSource = value;
            }
        }

        /// <summary>目前使用者最後點進去的那一列資料變動時觸發,供 MainWindow 更新「目前作用列」。</summary>
        public event EventHandler<AudioMapRow> ActiveItemChanged;

        /// <summary>儲存格編輯真的造成異動時觸發,帶出「異動前」的整份快照,由 MainWindow 記錄進復原堆疊。</summary>
        public event EventHandler<List<AudioMapRow>> UndoSnapshotRequested;

        /// <summary>使用者捲動這個頁簽時觸發(ITabSyncable)。</summary>
        public event EventHandler<double> ScrollPositionChanged;

        /// <summary>「功能代號」欄位取得焦點當下的原始值,LostFocus 時拿來跟目前值比對,只有真的異動過才驗證重複。</summary>
        private string _codeValueOnFocus;
        /// <summary>「音效檔名稱」欄位取得焦點當下的原始值,LostFocus 時比對是否真的異動過,判斷要不要記錄復原快照。</summary>
        private string _fileNameValueOnFocus;
        /// <summary>進入「功能代號」或「音效檔名稱」欄位編輯前的整份對照表快照,離開欄位時如果值真的變了才會回報。</summary>
        private List<AudioMapRow> _editSnapshot;

        public PathMapControl()
        {
            InitializeComponent();
        }

        private List<AudioMapRow> CloneItems()
        {
            List<AudioMapRow> clone = new List<AudioMapRow>();
            foreach (AudioMapRow item in Items)
            {
                clone.Add(item.Clone());
            }

            return clone;
        }

        private void RaiseActiveItemChanged(AudioMapRow item)
        {
            EventHandler<AudioMapRow> handler = ActiveItemChanged;
            if (handler != null)
                handler(this, item);
        }

        private void RaiseUndoSnapshotRequested(List<AudioMapRow> snapshotBeforeChange)
        {
            EventHandler<List<AudioMapRow>> handler = UndoSnapshotRequested;
            if (handler != null)
                handler(this, snapshotBeforeChange);
        }

        #region DataGrid 儲存格功能(目前作用列追蹤/功能代號重複驗證/音效檔查詢)

        /// <summary>
        /// 「功能代號」「音效檔路徑」兩欄的 TextBox 共用:只要使用者點進(取得焦點)該列任一欄位,
        /// 就回報該列資料,供外部(MainWindow 工具列的新增/刪除/拷貝)在「沒有打勾、只點了某一列」時使用。
        /// </summary>
        private void TxtRow_GotFocus(object sender, RoutedEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            if (fe == null)
                return;

            RaiseActiveItemChanged(fe.DataContext as AudioMapRow);
        }

        private void TxtCode_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapRow item = tb == null ? null : tb.DataContext as AudioMapRow;
            _codeValueOnFocus = item == null ? null : item.Code;
            _editSnapshot = CloneItems();
        }

        private void TxtFileName_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapRow item = tb == null ? null : tb.DataContext as AudioMapRow;
            _fileNameValueOnFocus = item == null ? null : item.FileName;
            _editSnapshot = CloneItems();
        }

        /// <summary>
        /// 「功能代號」欄位游標離開時的驗證:異動後若不為空,檢查其他列有沒有相同代號。
        /// 有重複就跳出提示,按確定後把焦點/選取範圍設回同一個 TextBox,方便使用者原地修改。
        /// </summary>
        private void TxtCode_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapRow currentItem = tb.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            string newCode = currentItem.Code;
            if (newCode == _codeValueOnFocus)
                return;

            if (_editSnapshot != null)
                RaiseUndoSnapshotRequested(_editSnapshot);

            if (string.IsNullOrEmpty(newCode))
                return;

            bool duplicate = Items.Any(p => p != currentItem && p.Code == newCode);
            if (!duplicate)
                return;

            MessageBox.Show("已存在相同功能代號", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

            Dispatcher.BeginInvoke(new Action(delegate
            {
                DataGridCellInfo cellInfo = new DataGridCellInfo(currentItem, colCode);
                dgAudioMap.CurrentCell = cellInfo;
                dgAudioMap.SelectedCells.Clear();
                dgAudioMap.SelectedCells.Add(cellInfo);

                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Background);
        }

        private void TxtFileName_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapRow currentItem = tb.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            if (currentItem.FileName == _fileNameValueOnFocus)
                return;

            if (_editSnapshot != null)
                RaiseUndoSnapshotRequested(_editSnapshot);
        }

        /// <summary>
        /// 「音效檔名稱」欄位右側查詢按鈕:跳出 OpenFileDialog 選音效檔,預設定位到 FolderPath,
        /// 選好後只把「檔名.副檔名」(不含資料夾路徑)寫回該列的 FileName,讓 ini 檔換電腦也能用。
        /// </summary>
        private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            AudioMapRow item = btn == null ? null : btn.DataContext as AudioMapRow;
            if (item == null)
                return;

            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "音效檔 (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|所有檔案 (*.*)|*.*";

            if (!string.IsNullOrEmpty(FolderPath) && Directory.Exists(FolderPath))
            {
                dlg.InitialDirectory = FolderPath;

                if (!string.IsNullOrEmpty(item.FileName))
                {
                    string fullPath = Path.Combine(FolderPath, item.FileName);
                    if (File.Exists(fullPath))
                        dlg.FileName = fullPath;
                }
            }

            if (dlg.ShowDialog() == true)
            {
                RaiseUndoSnapshotRequested(CloneItems());
                item.FileName = Path.GetFileName(dlg.FileName);
            }
        }

        /// <summary>
        /// 快捷鍵:「功能代號」「音效檔名稱」任一欄位游標所在時按 Enter,
        /// 在目前這一列後面自動插入一筆空白列(四個頁簽都會同步看到),並把游標移到新那一列的「功能代號」欄位。
        /// </summary>
        private void TxtRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapRow currentItem = tb.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            // 攔下 Enter,避免多行輸入或系統警示音
            e.Handled = true;

            // 播放/暫停中(這一列自己在播放/暫停,或被別列的播放鎖定)不能新增列,
            // 跟工具列「新增」按鈕被鎖住時的規則一致。
            if (currentItem.PlaybackState != RowPlaybackState.None || currentItem.IsPlaybackLocked)
                return;

            RaiseUndoSnapshotRequested(CloneItems());

            int insertIndex = Items.IndexOf(currentItem) + 1;
            AudioMapRow newItem = new AudioMapRow();
            Items.Insert(insertIndex, newItem);

            Dispatcher.BeginInvoke(new Action(delegate
            {
                FocusCodeTextBox(newItem);
            }), DispatcherPriority.Background);
        }

        private void FocusCodeTextBox(AudioMapRow item)
        {
            dgAudioMap.UpdateLayout();
            dgAudioMap.ScrollIntoView(item);
            dgAudioMap.UpdateLayout();

            DataGridCellInfo cellInfo = new DataGridCellInfo(item, colCode);
            dgAudioMap.CurrentCell = cellInfo;
            dgAudioMap.SelectedCells.Clear();
            dgAudioMap.SelectedCells.Add(cellInfo);

            DataGridRow row = dgAudioMap.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
            if (row == null)
                return;

            TextBox txtCode = FindVisualChildByName<TextBox>(row, "txtCode");
            if (txtCode != null)
                txtCode.Focus();
        }

        private static T FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                T result = child as T;
                if (result != null && result.Name == name)
                    return result;

                result = FindVisualChildByName<T>(child, name);
                if (result != null)
                    return result;
            }

            return null;
        }

        #endregion

        #region ITabSyncable(跨頁簽同步選取列/捲動位置)

        /// <summary>把選取狀態設到指定列(功能代號欄),只更新視覺反藍,不呼叫 Focus() 搶走鍵盤焦點。</summary>
        public void SyncSelectedRow(AudioMapRow item)
        {
            if (item == null)
                return;

            DataGridCellInfo cellInfo = new DataGridCellInfo(item, colCode);
            dgAudioMap.CurrentCell = cellInfo;
            dgAudioMap.SelectedCells.Clear();
            dgAudioMap.SelectedCells.Add(cellInfo);
        }

        public double GetVerticalOffset()
        {
            ScrollViewer scrollViewer = VisualTreeUtilities.FindVisualChild<ScrollViewer>(dgAudioMap);
            return scrollViewer == null ? 0 : scrollViewer.VerticalOffset;
        }

        public void ScrollToVerticalOffset(double offset)
        {
            ScrollViewer scrollViewer = VisualTreeUtilities.FindVisualChild<ScrollViewer>(dgAudioMap);
            if (scrollViewer != null)
                scrollViewer.ScrollToVerticalOffset(offset);
        }

        /// <summary>DataGrid 內部 ScrollViewer 的 ScrollChanged 事件會冒泡到這裡(見 XAML 的 ScrollViewer.ScrollChanged)。</summary>
        private void DataGrid_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            EventHandler<double> handler = ScrollPositionChanged;
            if (handler != null)
                handler(this, e.VerticalOffset);
        }

        #endregion
    }
}
