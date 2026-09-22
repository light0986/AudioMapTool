using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioMapTool.Models;
using AudioMapTool.Utilities;

namespace AudioMapTool.Fragment
{
    /// <summary>
    /// OpeningSegmentControl.xaml 的互動邏輯。
    /// 「開場片段」頁簽:選/功能代號/開始時間/結束時間,綁定共用 AudioMapRow 的 SStart/SEnd。
    /// 開始/結束時間格式固定為「時:分:秒:碼」(HH:mm:ss:ffff,見 Utilities.TimecodeFormat)。
    /// 資料來源(Items)由 MainWindow 指定,跟「路徑」「循環片段」「最大音量」頁簽共用同一份。
    /// </summary>
    public partial class OpeningSegmentControl : UserControl
    {
        private ObservableCollection<AudioMapRow> _items;

        public ObservableCollection<AudioMapRow> Items
        {
            get { return _items; }
            set
            {
                _items = value;
                dgSegment.ItemsSource = value;
            }
        }

        public event EventHandler<AudioMapRow> ActiveItemChanged;
        public event EventHandler<List<AudioMapRow>> UndoSnapshotRequested;

        private string _codeValueOnFocus;
        private string _startValueOnFocus;
        private string _endValueOnFocus;
        private List<AudioMapRow> _editSnapshot;

        public OpeningSegmentControl()
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

        #region DataGrid 儲存格功能(目前作用列追蹤/功能代號重複驗證)

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

        private void TxtStart_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapRow item = tb == null ? null : tb.DataContext as AudioMapRow;
            _startValueOnFocus = item == null ? null : item.SStart;
            _editSnapshot = CloneItems();
        }

        private void TxtEnd_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapRow item = tb == null ? null : tb.DataContext as AudioMapRow;
            _endValueOnFocus = item == null ? null : item.SEnd;
            _editSnapshot = CloneItems();
        }

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
                dgSegment.CurrentCell = cellInfo;
                dgSegment.SelectedCells.Clear();
                dgSegment.SelectedCells.Add(cellInfo);

                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Background);
        }

        /// <summary>TextBox.PreviewTextInput 共用:只允許輸入數字跟冒號,符合「時:分:秒:碼」格式。</summary>
        private void TxtTimecode_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TimecodeFormat.FilterInput(sender, e);
        }

        private void TxtStart_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapRow currentItem = tb.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            if (!TimecodeFormat.IsValid(currentItem.SStart))
            {
                MessageBox.Show("開始時間格式必須為「時:分:秒:碼」(HH:mm:ss:ffff),例如 00:01:23:0456", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    DataGridCellInfo cellInfo = new DataGridCellInfo(currentItem, colStart);
                    dgSegment.CurrentCell = cellInfo;
                    dgSegment.SelectedCells.Clear();
                    dgSegment.SelectedCells.Add(cellInfo);

                    tb.Focus();
                    tb.SelectAll();
                }), DispatcherPriority.Background);

                return;
            }

            if (currentItem.SStart == _startValueOnFocus)
                return;

            if (_editSnapshot != null)
                RaiseUndoSnapshotRequested(_editSnapshot);
        }

        private void TxtEnd_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapRow currentItem = tb.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            if (!TimecodeFormat.IsValid(currentItem.SEnd))
            {
                MessageBox.Show("結束時間格式必須為「時:分:秒:碼」(HH:mm:ss:ffff),例如 00:01:23:0456", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

                Dispatcher.BeginInvoke(new Action(delegate
                {
                    DataGridCellInfo cellInfo = new DataGridCellInfo(currentItem, colEnd);
                    dgSegment.CurrentCell = cellInfo;
                    dgSegment.SelectedCells.Clear();
                    dgSegment.SelectedCells.Add(cellInfo);

                    tb.Focus();
                    tb.SelectAll();
                }), DispatcherPriority.Background);

                return;
            }

            if (currentItem.SEnd == _endValueOnFocus)
                return;

            if (_editSnapshot != null)
                RaiseUndoSnapshotRequested(_editSnapshot);
        }

        /// <summary>
        /// 快捷鍵:功能代號/開始時間/結束時間任一欄位游標所在時按 Enter,
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

            // 播放/暫停中(這一列自己在播放/暫停,或被別列的播放鎖定)不能新增列——暫停時這裡的開始/結束
            // 時間欄位雖然開放編輯,但不能趁機按 Enter 新增列,跟工具列「新增」按鈕被鎖住時的規則一致。
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
            dgSegment.UpdateLayout();
            dgSegment.ScrollIntoView(item);
            dgSegment.UpdateLayout();

            DataGridCellInfo cellInfo = new DataGridCellInfo(item, colCode);
            dgSegment.CurrentCell = cellInfo;
            dgSegment.SelectedCells.Clear();
            dgSegment.SelectedCells.Add(cellInfo);

            DataGridRow row = dgSegment.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
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
    }
}
