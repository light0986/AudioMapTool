using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioMapTool.Models;
using Microsoft.Win32;

namespace AudioMapTool
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯。
    /// 「音效檔對照表」主畫面:維護「功能代號 -> 音效檔路徑」對照表,
    /// 並可將對照表存成 ini 格式的檔案(每個功能代號一個 Section,格式:
    /// [功能代號]
    /// Path=音效檔路徑
    /// ),供 MediaPlayer 依代號查路徑播放。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 變數與初始化

        /// <summary>
        /// 目前 Grid 顯示中的對照表資料,直接綁定給 dgAudioMap.ItemsSource。
        /// </summary>
        private ObservableCollection<AudioMapItem> _items;
        /// <summary>
        /// 目前開啟/另存的檔案路徑;尚未存過檔時為 null(此時「存檔」等同「另存新檔」)。
        /// </summary>
        private string _currentFilePath;
        /// <summary>
        /// 目前使用者最後點進去的那一列資料。
        /// 因為「功能代號」「音效檔路徑」都是自訂 TemplateColumn(內含永遠可編輯的 TextBox),
        /// DataGrid 內建的 CurrentCell/SelectedItem 在這種欄位上不會可靠更新,
        /// 所以改由 TextBox 的 GotFocus 事件(TxtRow_GotFocus)自己記錄「目前作用列」,
        /// 給新增/刪除/拷貝在「沒有打勾、只點了某一列」時使用。
        /// </summary>
        private AudioMapItem _activeItem;
        /// <summary>
        /// 「功能代號」欄位取得焦點當下的原始值,LostFocus 時拿來跟目前值比對,
        /// 只有真的異動過才驗證重複,避免「警告後把焦點搶回來」這個動作本身(值沒變)又觸發下一輪驗證,
        /// 兩列都重複時互搶焦點形成無窮迴圈。
        /// </summary>
        private string _codeValueOnFocus;
        /// <summary>「音效檔路徑」欄位取得焦點當下的原始值,LostFocus 時比對是否真的異動過,判斷要不要記錄復原快照。</summary>
        private string _filePathValueOnFocus;
        /// <summary>
        /// 進入「功能代號」或「音效檔路徑」欄位編輯前的整份對照表快照,離開欄位時如果值真的變了才會推進 _undoStack。
        /// </summary>
        private List<AudioMapItem> _editSnapshot;
        /// <summary>復原堆疊,每筆是異動前的整份對照表快照(深複製,不跟目前 _items 共用物件)。</summary>
        private Stack<List<AudioMapItem>> _undoStack = new Stack<List<AudioMapItem>>();
        /// <summary>取消復原堆疊,存放被復原掉的狀態,復原後又做新異動時會被清空。</summary>
        private Stack<List<AudioMapItem>> _redoStack = new Stack<List<AudioMapItem>>();

        public MainWindow()
        {
            InitializeComponent();

            // 建立空白對照表資料來源,綁定給 DataGrid 顯示
            _items = new ObservableCollection<AudioMapItem>();
            dgAudioMap.ItemsSource = _items;
        }

        #endregion

        #region 全域快捷鍵

        /// <summary>
        /// 整個視窗共用的快捷鍵:Ctrl+S 存檔、Ctrl+Z 復原、Ctrl+Y 取消復原,行為分別跟按選單的對應項目相同。
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control)
                return;

            if (e.Key == Key.S)
            {
                e.Handled = true;
                DoSave();
            }
            else if (e.Key == Key.Z)
            {
                e.Handled = true;
                Undo();
            }
            else if (e.Key == Key.Y)
            {
                e.Handled = true;
                Redo();
            }
        }

        #endregion

        #region 復原/取消復原

        /// <summary>
        /// 把目前對照表整份深複製一份(每筆資料都是新物件,不跟 _items 共用參考),當作一次快照。
        /// </summary>
        private List<AudioMapItem> CloneItems()
        {
            List<AudioMapItem> clone = new List<AudioMapItem>();
            foreach (AudioMapItem item in _items)
            {
                clone.Add(new AudioMapItem { Code = item.Code, FilePath = item.FilePath, IsSelected = item.IsSelected });
            }

            return clone;
        }

        /// <summary>
        /// 把整份對照表換成指定的快照內容(直接沿用快照裡的物件,因為快照本身已經是專屬、沒有共用的複本)。
        /// </summary>
        private void ApplySnapshot(List<AudioMapItem> snapshot)
        {
            _items.Clear();
            foreach (AudioMapItem item in snapshot)
            {
                _items.Add(item);
            }

            // 換過整份資料後,舊的作用列/編輯狀態參考已經不是目前清單裡的物件,清掉避免後續操作指到不存在的資料
            _activeItem = null;
        }

        /// <summary>
        /// 在一次異動動作「開始前」呼叫,把異動前的快照推進復原堆疊,並清空取消復原堆疊
        /// (標準 Undo/Redo 慣例:只要發生新的異動,先前被復原掉的分支就不再能取消復原)。
        /// </summary>
        private void PushUndoSnapshot(List<AudioMapItem> snapshotBeforeChange)
        {
            _undoStack.Push(snapshotBeforeChange);
            _redoStack.Clear();
            UpdateUndoRedoMenuState();
        }

        /// <summary>
        /// 復原:把目前狀態存進取消復原堆疊,再取出復原堆疊最上面那份快照套用回去。
        /// </summary>
        private void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            _redoStack.Push(CloneItems());
            ApplySnapshot(_undoStack.Pop());
            UpdateUndoRedoMenuState();
        }

        /// <summary>
        /// 取消復原:把目前狀態存進復原堆疊,再取出取消復原堆疊最上面那份快照套用回去。
        /// </summary>
        private void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            _undoStack.Push(CloneItems());
            ApplySnapshot(_redoStack.Pop());
            UpdateUndoRedoMenuState();
        }

        /// <summary>
        /// 依堆疊目前是否有內容,更新「復原」「取消復原」選單項目能不能點。
        /// </summary>
        private void UpdateUndoRedoMenuState()
        {
            miUndo.IsEnabled = _undoStack.Count > 0;
            miRedo.IsEnabled = _redoStack.Count > 0;
        }

        #endregion

        #region 選單按鈕(檔案:新增/開啟/存檔/另存新檔/關閉)

        /// <summary>
        /// 「檔案」選單底下 5 個項目共用的 Click 進入點,依 MenuItem 的 x:Name 分派到對應處理方法。
        /// </summary>
        private void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            MenuItem mi = sender as MenuItem;
            if (mi == null)
                return;

            switch (mi.Name)
            {
                case "miNew":
                    DoNew();
                    break;

                case "miOpen":
                    DoOpen();
                    break;

                case "miSave":
                    DoSave();
                    break;

                case "miSaveAs":
                    DoSaveAs();
                    break;

                case "miClose":
                    DoClose();
                    break;

                case "miUndo":
                    Undo();
                    break;

                case "miRedo":
                    Redo();
                    break;
            }
        }
        /// <summary>
        /// 新增:清空目前的對照表內容,回到「尚未開啟任何檔案」的狀態,重新開始編輯。
        /// </summary>
        private void DoNew()
        {
            PushUndoSnapshot(CloneItems());

            _items.Clear();
            _currentFilePath = null;
            Title = "音效檔對照表";
        }
        /// <summary>
        /// 開啟:選一個 ini 檔,依 "[功能代號]" + "Path=路徑" 的區段格式解析後,整批取代目前的對照表內容。
        /// </summary>
        private void DoOpen()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "INI 設定檔 (*.ini)|*.ini|所有檔案 (*.*)|*.*";

            if (dlg.ShowDialog() != true)
                return;

            try
            {
                PushUndoSnapshot(CloneItems());

                _items.Clear();

                string[] lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                AudioMapItem currentItem = null;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    // "[功能代號]" 這種格式代表新的 Section,開始一筆新資料
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentItem = new AudioMapItem { Code = line.Substring(1, line.Length - 2).Trim() };
                        _items.Add(currentItem);
                        continue;
                    }

                    if (currentItem == null)
                        continue;

                    int idx = line.IndexOf('=');
                    if (idx < 0)
                        continue;

                    string key = line.Substring(0, idx).Trim();
                    string value = line.Substring(idx + 1).Trim();

                    if (string.Equals(key, "Path", StringComparison.OrdinalIgnoreCase))
                        currentItem.FilePath = value;
                }

                _currentFilePath = dlg.FileName;
                Title = "音效檔對照表 - " + Path.GetFileName(_currentFilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("開啟檔案失敗:" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// 存檔:已經開過/存過檔就直接寫回原檔;否則(第一次存檔)改走「另存新檔」讓使用者選檔名。
        /// </summary>
        private void DoSave()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                DoSaveAs();
                return;
            }

            SaveToFile(_currentFilePath);
        }
        /// <summary>
        /// 另存新檔:跳出存檔對話框,將目前對照表輸出成 ini,並把該檔設為之後「存檔」的目標檔案。
        /// </summary>
        private void DoSaveAs()
        {
            SaveFileDialog dlg = new SaveFileDialog();
            dlg.Filter = "INI 設定檔 (*.ini)|*.ini|所有檔案 (*.*)|*.*";
            dlg.FileName = string.IsNullOrEmpty(_currentFilePath) ? "音效檔對照表.ini" : Path.GetFileName(_currentFilePath);

            if (dlg.ShowDialog() != true)
                return;

            SaveToFile(dlg.FileName);
            _currentFilePath = dlg.FileName;
            Title = "音效檔對照表 - " + Path.GetFileName(_currentFilePath);
        }
        /// <summary>
        /// 把目前對照表內容依 ini 格式寫出到指定檔案(UTF-8):每筆資料一個 Section,
        /// "[功能代號]" 接著一行 "Path=音效檔路徑"。代號空白的列會被略過,不寫進檔案。
        /// </summary>
        private void SaveToFile(string filePath)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (AudioMapItem item in _items)
                {
                    if (string.IsNullOrWhiteSpace(item.Code))
                        continue;

                    sb.AppendLine("[" + item.Code + "]");
                    sb.AppendLine("Path=" + item.FilePath);
                    sb.AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("存檔失敗:" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// 關閉:直接關掉整個視窗。
        /// </summary>
        private void DoClose()
        {
            Close();
        }

        #endregion

        #region DataGrid 儲存格功能(目前作用列追蹤/功能代號重複驗證/音效檔查詢)

        /// <summary>
        /// 「功能代號」「音效檔路徑」兩欄的 TextBox 共用:只要使用者點進(取得焦點)該列任一欄位,
        /// 就把該列資料記到 _activeItem,供工具列的新增/刪除/拷貝按鈕判斷「目前作用列」。
        /// </summary>
        private void TxtRow_GotFocus(object sender, RoutedEventArgs e)
        {
            FrameworkElement fe = sender as FrameworkElement;
            if (fe == null)
                return;

            _activeItem = fe.DataContext as AudioMapItem;
        }
        /// <summary>
        /// 「功能代號」欄位取得焦點:除了跟 TxtRow_GotFocus 一樣記錄目前作用列,
        /// 還要多記錄「取得焦點當下的原始值」,給 LostFocus 判斷這次是否真的有異動過。
        /// </summary>
        private void TxtCode_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapItem item = tb == null ? null : tb.DataContext as AudioMapItem;
            _codeValueOnFocus = item == null ? null : item.Code;
            _editSnapshot = CloneItems();
        }
        /// <summary>
        /// 「音效檔路徑」欄位取得焦點:除了跟 TxtRow_GotFocus 一樣記錄目前作用列,
        /// 還要多記錄「取得焦點當下的原始值」跟復原快照,給 LostFocus 判斷這次是否真的有異動過。
        /// </summary>
        private void TxtFilePath_GotFocus(object sender, RoutedEventArgs e)
        {
            TxtRow_GotFocus(sender, e);

            TextBox tb = sender as TextBox;
            AudioMapItem item = tb == null ? null : tb.DataContext as AudioMapItem;
            _filePathValueOnFocus = item == null ? null : item.FilePath;
            _editSnapshot = CloneItems();
        }
        /// <summary>
        /// 「功能代號」欄位游標離開時的驗證:異動後若不為空,檢查其他列有沒有相同代號。
        /// 有重複就跳出提示,按確定後把焦點/選取範圍設回同一個 TextBox,方便使用者原地修改。
        /// 只有這次真的改過值才會驗證——值沒變(例如上一輪警告後把焦點搶回來)就直接放行,
        /// 否則兩列本來就重複時,搶焦點的動作本身會不斷互相觸發驗證,形成無窮迴圈。
        /// </summary>
        private void TxtCode_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapItem currentItem = tb.DataContext as AudioMapItem;
            if (currentItem == null)
                return;

            string newCode = currentItem.Code;
            if (newCode == _codeValueOnFocus)
                return;

            // 這次真的異動過代號,記錄復原快照(用取得焦點當下捕捉的那份,代表異動前的狀態)
            if (_editSnapshot != null)
                PushUndoSnapshot(_editSnapshot);

            if (string.IsNullOrEmpty(newCode))
                return;

            // 排除自己這一列,檢查其他列是否已經有相同的功能代號
            bool duplicate = _items.Any(p => p != currentItem && p.Code == newCode);
            if (!duplicate)
                return;

            MessageBox.Show("已存在相同功能代號", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);

            // 用 Background 優先權延後執行,確保排在這次「切換焦點」的處理完成之後,
            // 才把焦點/選取範圍設回原本的 TextBox,不會又被蓋回去
            Dispatcher.BeginInvoke(new Action(delegate
            {
                // 反藍(目前儲存格)也要跟著一起指向回這一列的功能代號欄位,不然會停在切換前點的儲存格上
                DataGridCellInfo cellInfo = new DataGridCellInfo(currentItem, colCode);
                dgAudioMap.CurrentCell = cellInfo;
                dgAudioMap.SelectedCells.Clear();
                dgAudioMap.SelectedCells.Add(cellInfo);

                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Background);
        }
        /// <summary>
        /// 「音效檔路徑」欄位游標離開時:只要值真的異動過,就把取得焦點當下的快照推進復原堆疊。
        /// </summary>
        private void TxtFilePath_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapItem currentItem = tb.DataContext as AudioMapItem;
            if (currentItem == null)
                return;

            if (currentItem.FilePath == _filePathValueOnFocus)
                return;

            if (_editSnapshot != null)
                PushUndoSnapshot(_editSnapshot);
        }
        /// <summary>
        /// 「音效檔路徑」欄位右側查詢按鈕:跳出 OpenFileDialog 選音效檔,選好後把路徑寫回該列的 FilePath。
        /// </summary>
        private void BtnBrowseAudio_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            AudioMapItem item = btn == null ? null : btn.DataContext as AudioMapItem;
            if (item == null)
                return;

            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "音效檔 (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|所有檔案 (*.*)|*.*";

            // 該列原本已經有有效路徑的話,對話框預設定位到那個檔案
            if (!string.IsNullOrEmpty(item.FilePath) && File.Exists(item.FilePath))
                dlg.FileName = item.FilePath;

            if (dlg.ShowDialog() == true)
            {
                PushUndoSnapshot(CloneItems());
                item.FilePath = dlg.FileName;
            }
        }
        /// <summary>
        /// 快捷鍵:「功能代號」「音效檔路徑」任一欄位游標所在時按 Enter,
        /// 在目前這一列後面自動插入一筆空白列,並把游標移到新那一列的「功能代號」欄位。
        /// </summary>
        private void TxtRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            TextBox tb = sender as TextBox;
            if (tb == null)
                return;

            AudioMapItem currentItem = tb.DataContext as AudioMapItem;
            if (currentItem == null)
                return;

            // 攔下 Enter,避免多行輸入或系統警示音
            e.Handled = true;

            PushUndoSnapshot(CloneItems());

            int insertIndex = _items.IndexOf(currentItem) + 1;
            AudioMapItem newItem = new AudioMapItem();
            _items.Insert(insertIndex, newItem);

            // 新列的容器要等這次資料異動的版面更新跑完才會真正產生,
            // 用 Background 優先權延後執行,確保抓得到新列的 TextBox
            Dispatcher.BeginInvoke(new Action(delegate
            {
                FocusCodeTextBox(newItem);
            }), DispatcherPriority.Background);
        }

        /// <summary>
        /// 把游標移到指定列「功能代號」欄位的 TextBox(x:Name="txtCode")上。
        /// </summary>
        private void FocusCodeTextBox(AudioMapItem item)
        {
            dgAudioMap.UpdateLayout();
            dgAudioMap.ScrollIntoView(item);
            dgAudioMap.UpdateLayout();

            // 把 DataGrid 自己的目前儲存格/選取範圍也一併移過去,反藍才會跟著游標一起切換
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

        /// <summary>
        /// 依名稱在視覺樹底下遞迴尋找指定型別的子控件(用於定位 DataGrid 儲存格內、自訂 DataTemplate 產生的 x:Name 控件)。
        /// </summary>
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

        #region 工具列按鈕(全選/反選/新增/刪除/拷貝)

        /// <summary>
        /// 工具列 5 個按鈕共用的 Click 進入點,依 Button 的 x:Name 分派到對應處理方法。
        /// </summary>
        private void BtnToolbar_Click(object sender, RoutedEventArgs e)
        {
            Button btn = sender as Button;
            if (btn == null)
                return;

            switch (btn.Name)
            {
                case "btnSelectAll":
                    DoSelectAll();
                    break;

                case "btnInvertSelect":
                    DoInvertSelect();
                    break;

                case "btnAdd":
                    DoAdd();
                    break;

                case "btnDelete":
                    DoDelete();
                    break;

                case "btnCopy":
                    DoCopy();
                    break;
            }
        }
        /// <summary>
        /// 全選:把目前對照表所有列的「選」勾選狀態都打勾。
        /// </summary>
        private void DoSelectAll()
        {
            foreach (AudioMapItem item in _items)
            {
                item.IsSelected = true;
            }
        }
        /// <summary>
        /// 反選:把目前對照表所有列的「選」勾選狀態反轉(打勾變沒打勾,反之亦然)。
        /// </summary>
        private void DoInvertSelect()
        {
            foreach (AudioMapItem item in _items)
            {
                item.IsSelected = !item.IsSelected;
            }
        }
        /// <summary>
        /// 新增:以目前作用列(_activeItem)為基準,在它後面插入一筆空白資料;
        /// 沒有作用列(尚未點過任何列)則直接加到清單最後。
        /// </summary>
        private void DoAdd()
        {
            PushUndoSnapshot(CloneItems());

            AudioMapItem selected = _activeItem;
            int insertIndex = selected == null ? _items.Count : _items.IndexOf(selected) + 1;

            _items.Insert(insertIndex, new AudioMapItem());
        }
        /// <summary>
        /// 刪除:
        /// 有打勾的列 → 詢問「是否刪除打勾項目?」,按是則全部刪除;
        /// 沒有打勾但有作用列 → 詢問「是否刪除{功能代號}?」,按是則刪除該列。
        /// </summary>
        private void DoDelete()
        {
            List<AudioMapItem> checkedItems = _items.Where(p => p.IsSelected).ToList();

            if (checkedItems.Count > 0)
            {
                MessageBoxResult result = MessageBox.Show("是否刪除打勾項目?", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;

                PushUndoSnapshot(CloneItems());

                foreach (AudioMapItem item in checkedItems)
                {
                    _items.Remove(item);
                }

                return;
            }

            AudioMapItem selected = _activeItem;
            if (selected != null)
            {
                MessageBoxResult result = MessageBox.Show("是否刪除" + selected.Code + "?", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;

                PushUndoSnapshot(CloneItems());

                _items.Remove(selected);
            }
        }
        /// <summary>
        /// 拷貝:
        /// 有打勾的列 → 複製所有勾選列,新增在清單最後(代號加上 "_COPY" 後綴),並取消原列的勾選;
        /// 沒有打勾但有作用列 → 在該列後面插入一筆拷貝。
        /// </summary>
        private void DoCopy()
        {
            List<AudioMapItem> checkedItems = _items.Where(p => p.IsSelected).ToList();

            if (checkedItems.Count > 0)
            {
                PushUndoSnapshot(CloneItems());

                foreach (AudioMapItem item in checkedItems)
                {
                    item.IsSelected = false;

                    _items.Add(new AudioMapItem
                    {
                        Code = item.Code + "_COPY",
                        FilePath = item.FilePath
                    });
                }

                return;
            }

            AudioMapItem selected = _activeItem;
            if (selected != null)
            {
                PushUndoSnapshot(CloneItems());

                int insertIndex = _items.IndexOf(selected) + 1;

                _items.Insert(insertIndex, new AudioMapItem
                {
                    Code = selected.Code + "_COPY",
                    FilePath = selected.FilePath
                });
            }
        }

        #endregion
    }
}
