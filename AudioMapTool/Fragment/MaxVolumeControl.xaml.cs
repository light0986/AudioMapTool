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

namespace AudioMapTool.Fragment
{
    /// <summary>
    /// MaxVolumeControl.xaml 的互動邏輯。
    /// 「最大音量」頁簽:選/功能代號/音量/開始/停止/暫停,綁定共用 AudioMapRow 的 MaxS。
    /// 音量欄是 Slider(範圍 0~1,見 VolumeStringConverter),不能直接打字輸入。
    /// 資料來源(Items)由 MainWindow 指定,跟「路徑」「開場片段」「循環片段」頁簽共用同一份。
    /// 「開始」「停止」「暫停」按鈕只負責把使用者的意圖(PlayRequested/PauseRequested/StopRequested)
    /// 往外報,實際播放邏輯、鎖定其他列/工具列都是 MainWindow 配合 Services/PlaybackController 處理,
    /// 因為那些動作牽涉到「這份資料的全部列」跟「工具列」,不是這個 UserControl 自己管得到的範圍。
    /// </summary>
    public partial class MaxVolumeControl : UserControl
    {
        private ObservableCollection<AudioMapRow> _items;

        public ObservableCollection<AudioMapRow> Items
        {
            get { return _items; }
            set
            {
                _items = value;
                dgMaxVolume.ItemsSource = value;
            }
        }

        public event EventHandler<AudioMapRow> ActiveItemChanged;
        public event EventHandler<List<AudioMapRow>> UndoSnapshotRequested;

        /// <summary>使用者按下這一列的「開始」按鈕。</summary>
        public event EventHandler<AudioMapRow> PlayRequested;
        /// <summary>使用者按下這一列的「暫停」按鈕。</summary>
        public event EventHandler<AudioMapRow> PauseRequested;
        /// <summary>使用者按下這一列的「停止」按鈕。</summary>
        public event EventHandler<AudioMapRow> StopRequested;

        private string _codeValueOnFocus;
        private string _volumeValueOnFocus;
        private List<AudioMapRow> _editSnapshot;

        public MaxVolumeControl()
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

        private void SliderVolume_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            Slider slider = sender as Slider;
            AudioMapRow item = slider == null ? null : slider.DataContext as AudioMapRow;
            _volumeValueOnFocus = item == null ? null : item.MaxS;
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
                dgMaxVolume.CurrentCell = cellInfo;
                dgMaxVolume.SelectedCells.Clear();
                dgMaxVolume.SelectedCells.Add(cellInfo);

                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Background);
        }

        private void SliderVolume_LostFocus(object sender, RoutedEventArgs e)
        {
            Slider slider = sender as Slider;
            if (slider == null)
                return;

            AudioMapRow currentItem = slider.DataContext as AudioMapRow;
            if (currentItem == null)
                return;

            if (currentItem.MaxS == _volumeValueOnFocus)
                return;

            if (_editSnapshot != null)
                RaiseUndoSnapshotRequested(_editSnapshot);
        }

        /// <summary>
        /// 快捷鍵:功能代號欄位游標所在時按 Enter,
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
            dgMaxVolume.UpdateLayout();
            dgMaxVolume.ScrollIntoView(item);
            dgMaxVolume.UpdateLayout();

            DataGridCellInfo cellInfo = new DataGridCellInfo(item, colCode);
            dgMaxVolume.CurrentCell = cellInfo;
            dgMaxVolume.SelectedCells.Clear();
            dgMaxVolume.SelectedCells.Add(cellInfo);

            DataGridRow row = dgMaxVolume.ItemContainerGenerator.ContainerFromItem(item) as DataGridRow;
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

        #region 開始/停止/暫停

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            RaiseButtonRequest(sender, PlayRequested);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            RaiseButtonRequest(sender, StopRequested);
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            RaiseButtonRequest(sender, PauseRequested);
        }

        private void RaiseButtonRequest(object sender, EventHandler<AudioMapRow> handler)
        {
            Button btn = sender as Button;
            AudioMapRow item = btn == null ? null : btn.DataContext as AudioMapRow;
            if (item == null || handler == null)
                return;

            handler(this, item);
        }

        #endregion
    }
}
