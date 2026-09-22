using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AudioMapTool.Fragment;
using AudioMapTool.Models;
using AudioMapTool.Services;
using AudioMapTool.Utilities;
using Microsoft.Win32;

namespace AudioMapTool
{
    /// <summary>
    /// MainWindow.xaml 的互動邏輯。
    /// 「音效檔對照表」主畫面:選單、工具列、頁簽容器(TabControl)。
    /// 「路徑」「開場片段」「循環片段」「最大音量」四個頁簽對應同一份資料(_items,元素是 AudioMapRow),
    /// 每個頁簽的 UserControl 只是顯示/編輯其中幾個欄位,同一列在四個頁簽都是同一個物件。
    /// 因為是同一份資料,復原/取消復原、目前開啟/存過的檔案都只有一份,不分頁簽。
    /// 檔案存成 ini 格式,每個功能代號一個 Section,底下依序是 FileName/SStart/SEnd/CStart/CEnd/MaxS
    /// (只存檔名,不存完整路徑;實際檔案位置 = 工具列選的「資料夾」+ 檔名,換電腦開檔也能用)。
    /// 「最大音量」頁簽的開始/暫停/停止透過 PlaybackController 實際播放音效檔,播放/暫停期間
    /// 這裡負責鎖住其他列(AudioMapRow.IsPlaybackLocked)跟整排選單/工具列(見 SetGlobalControlsEnabled)。
    /// </summary>
    public partial class MainWindow : Window
    {
        #region 變數與初始化

        /// <summary>四個頁簽共用的對照表資料。</summary>
        private readonly ObservableCollection<AudioMapRow> _items = new ObservableCollection<AudioMapRow>();

        /// <summary>目前使用者最後點進去的那一列資料(不分頁簽,哪個頁簽的儲存格取得焦點就更新這個)。</summary>
        private AudioMapRow _activeItem;

        /// <summary>目前開啟/另存的檔案路徑;尚未存過檔時為 null(此時「存檔」等同「另存新檔」)。</summary>
        private string _currentFilePath;

        /// <summary>復原堆疊,每筆是異動前的整份對照表快照(深複製,不跟 _items 共用物件)。</summary>
        private readonly Stack<List<AudioMapRow>> _undoStack = new Stack<List<AudioMapRow>>();
        /// <summary>取消復原堆疊,存放被復原掉的狀態,復原後又做新異動時會被清空。</summary>
        private readonly Stack<List<AudioMapRow>> _redoStack = new Stack<List<AudioMapRow>>();

        /// <summary>「最大音量」頁簽開始/暫停/停止背後實際的播放引擎(MediaPlayer 包裝)。</summary>
        private readonly PlaybackController _playback = new PlaybackController();

        /// <summary>
        /// 正在用程式(而不是使用者拖曳)更新底部兩條進度條的 Value 時設成 true,
        /// 讓 SliderOpeningPosition_ValueChanged/SliderLoopPosition_ValueChanged 知道要忽略這次變化,
        /// 不然每次同步畫面都會被誤判成使用者拖曳,反過來又呼叫 _playback.SeekWithin...,形成迴圈。
        /// </summary>
        private bool _isSyncingPositionSliders;

        /// <summary>目前(或最後一次)作用中頁簽的垂直捲動位置,切頁簽時套用到剛顯示出來的那個頁簽,見 TabMain_SelectionChanged。</summary>
        private double _lastScrollOffset;

        public MainWindow()
        {
            InitializeComponent();

            pathMapControl.Items = _items;
            openingSegmentControl.Items = _items;
            loopSegmentControl.Items = _items;
            maxVolumeControl.Items = _items;

            pathMapControl.ActiveItemChanged += Control_ActiveItemChanged;
            openingSegmentControl.ActiveItemChanged += Control_ActiveItemChanged;
            loopSegmentControl.ActiveItemChanged += Control_ActiveItemChanged;
            maxVolumeControl.ActiveItemChanged += Control_ActiveItemChanged;

            pathMapControl.UndoSnapshotRequested += Control_UndoSnapshotRequested;
            openingSegmentControl.UndoSnapshotRequested += Control_UndoSnapshotRequested;
            loopSegmentControl.UndoSnapshotRequested += Control_UndoSnapshotRequested;
            maxVolumeControl.UndoSnapshotRequested += Control_UndoSnapshotRequested;

            pathMapControl.ScrollPositionChanged += Control_ScrollPositionChanged;
            openingSegmentControl.ScrollPositionChanged += Control_ScrollPositionChanged;
            loopSegmentControl.ScrollPositionChanged += Control_ScrollPositionChanged;
            maxVolumeControl.ScrollPositionChanged += Control_ScrollPositionChanged;

            maxVolumeControl.PlayRequested += MaxVolumeControl_PlayRequested;
            maxVolumeControl.PauseRequested += MaxVolumeControl_PauseRequested;
            maxVolumeControl.StopRequested += MaxVolumeControl_StopRequested;
            _playback.PlaybackFailed += Playback_PlaybackFailed;
            _playback.PositionChanged += Playback_PositionChanged;
        }

        /// <summary>
        /// 「資料夾」欄位右側的「...」按鈕:跳出資料夾選擇對話框,選好後同時更新顯示的文字跟
        /// pathMapControl.FolderPath(「路徑」頁簽的「音效檔名稱」欄要等這裡有值才能用)。
        /// </summary>
        private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (!string.IsNullOrEmpty(txtFolder.Text) && Directory.Exists(txtFolder.Text))
                    dlg.SelectedPath = txtFolder.Text;

                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                    return;

                SetFolder(dlg.SelectedPath);
            }
        }

        /// <summary>把「資料夾」欄位(txtFolder 顯示 + pathMapControl.FolderPath)一起設成指定的資料夾路徑。</summary>
        private void SetFolder(string folder)
        {
            txtFolder.Text = folder;
            pathMapControl.FolderPath = folder;
        }

        /// <summary>
        /// 哪個頁簽的哪個儲存格取得焦點都會觸發這裡,更新「目前作用列」;順便呼叫 _playback.Preload
        /// 提前開啟這一列的音效檔(只有目前沒有任何列在播放/暫停時才會真的預載,見 PlaybackController.Preload),
        /// 讓使用者遊標停在某一列一陣子後再按「開始」時可以直接接手,減少切換音樂時的讀取卡頓感。
        /// </summary>
        private void Control_ActiveItemChanged(object sender, AudioMapRow item)
        {
            _activeItem = item;
            _playback.Preload(item, txtFolder.Text);
        }

        private void Control_UndoSnapshotRequested(object sender, List<AudioMapRow> snapshotBeforeChange)
        {
            PushUndoSnapshot(snapshotBeforeChange);
        }

        /// <summary>
        /// 記住目前作用頁簽的捲動位置,供切頁簽時套用到新頁簽(見 TabMain_SelectionChanged)。
        /// 不用即時同步到其他(目前看不到的)頁簽——反正使用者一次只看得到一個頁簽,
        /// 等切過去那一刻才套用最新的位置,效果沒有差別,也不用擔心隱藏頁簽版面配置還沒就緒的問題。
        /// </summary>
        private void Control_ScrollPositionChanged(object sender, double offset)
        {
            _lastScrollOffset = offset;
        }

        /// <summary>
        /// 切頁簽時,把「目前作用列」「最後捲動位置」套用到剛顯示出來的頁簽,讓四個頁簽感覺像同一份清單,
        /// 切過去還停在原本看的同一列、同一個捲動位置。SelectionChanged 會冒泡(DataGrid 本身也是 Selector),
        /// 所以要排除不是 TabControl 本身觸發的事件。
        /// </summary>
        private void TabMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, tabMain))
                return;

            TabItem selectedTab = tabMain.SelectedItem as TabItem;
            ITabSyncable syncable = selectedTab == null ? null : selectedTab.Content as ITabSyncable;
            if (syncable == null)
                return;

            syncable.SyncSelectedRow(_activeItem);

            // 剛切過去的頁簽如果是第一次顯示,版面配置(含 DataGrid 內部 ScrollViewer 的捲動範圍)
            // 這時候可能還沒算完,直接 ScrollToVerticalOffset 會被夾到 0 或無效,所以延到
            // Loaded 優先權(版面配置/算繪跑完之後)才套用,確保每次切頁簽都能同步成功。
            double targetOffset = _lastScrollOffset;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                syncable.ScrollToVerticalOffset(targetOffset);
            }), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 「最大音量」頁簽按下「開始」:未播放過(或已停止)時從開場片段重頭播,暫停中時接續播放。
        /// 播放成功才鎖其他列、鎖選單/工具列;播放失敗(檔案不存在、時間格式不對...等)維持原狀不鎖。
        /// </summary>
        private void MaxVolumeControl_PlayRequested(object sender, AudioMapRow row)
        {
            if (!_playback.Play(row, txtFolder.Text))
                return;

            LockOtherRows(row);
            SetGlobalControlsEnabled(false);
        }

        /// <summary>「最大音量」頁簽按下「暫停」:保留播放位置暫停,其他列、選單/工具列維持鎖定。</summary>
        private void MaxVolumeControl_PauseRequested(object sender, AudioMapRow row)
        {
            _playback.Pause();
        }

        /// <summary>「最大音量」頁簽按下「停止」:停止播放,解除所有列跟選單/工具列的鎖定。</summary>
        private void MaxVolumeControl_StopRequested(object sender, AudioMapRow row)
        {
            _playback.Stop();
            UnlockAllRows();
            SetGlobalControlsEnabled(true);

            // 停止後遊標通常還停在剛剛播放的那一列(不會重新觸發 GotFocus),順便補一次預載,
            // 讓「停止後馬上重播同一列」也能吃到提前開啟的好處。
            _playback.Preload(_activeItem, txtFolder.Text);
        }

        private void Playback_PlaybackFailed(object sender, string message)
        {
            MessageBox.Show(message, "播放失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// 播放位置變化時(播放中每 100ms 一次、暫停/開始/停止/拖曳進度條各一次)同步底部兩條進度條:
        /// 「開場片段」進度條顯示在開場片段內的進度,進入循環片段後固定顯示滿格;
        /// 「循環片段」進度條只有進入循環片段時才顯示進度,還沒進入時固定顯示 0。
        /// 兩條進度條是否能拖看目前是不是暫停中(播放中/沒有播放都不能拖),「循環片段」進度條額外要求
        /// 已經進入循環片段(IsInLoopStage,等同「開場片段」進度條滿了);另外,任何一段的開始時間跟
        /// 結束時間相同(長度 0、沒有範圍可拖)時,對應的那條進度條直接鎖住不能用。
        /// </summary>
        private void Playback_PositionChanged(object sender, EventArgs e)
        {
            AudioMapRow row = _playback.PlayingRow;
            if (row == null)
            {
                ResetPositionBars();
                return;
            }

            bool isPaused = row.PlaybackState == RowPlaybackState.Paused;
            bool isInLoop = _playback.IsInLoopStage;

            TimeSpan openingDuration = _playback.OpeningEnd - _playback.OpeningStart;
            TimeSpan loopDuration = _playback.LoopEnd - _playback.LoopStart;
            TimeSpan position = _playback.CurrentPosition;

            bool openingHasRange = openingDuration > TimeSpan.Zero;
            bool loopHasRange = loopDuration > TimeSpan.Zero;

            TimeSpan openingElapsed = isInLoop
                ? openingDuration
                : ClampTimeSpan(position - _playback.OpeningStart, TimeSpan.Zero, openingDuration);
            TimeSpan loopElapsed = isInLoop
                ? ClampTimeSpan(position - _playback.LoopStart, TimeSpan.Zero, loopDuration)
                : TimeSpan.Zero;

            double openingMax = Math.Max(0.001, openingDuration.TotalSeconds);
            double loopMax = Math.Max(0.001, loopDuration.TotalSeconds);

            _isSyncingPositionSliders = true;
            try
            {
                sliderOpeningPosition.Maximum = openingMax;
                sliderLoopPosition.Maximum = loopMax;

                // isInLoop 時直接把 Value 設成 Maximum(顯示滿格),不用 openingElapsed 的原始計算值——
                // 長度 0 時 openingElapsed 會是 0,但 Maximum 被墊高到 0.001,兩者對不起來會顯得「沒滿」。
                sliderOpeningPosition.Value = isInLoop ? openingMax : openingElapsed.TotalSeconds;
                sliderLoopPosition.Value = loopElapsed.TotalSeconds;

                sliderOpeningPosition.IsEnabled = isPaused && openingHasRange;
                sliderLoopPosition.IsEnabled = isPaused && isInLoop && loopHasRange;
            }
            finally
            {
                _isSyncingPositionSliders = false;
            }

            txtOpeningPosition.Text = TimecodeFormat.Format(_playback.OpeningStart + openingElapsed);
            txtLoopPosition.Text = TimecodeFormat.Format(_playback.LoopStart + loopElapsed);
        }

        /// <summary>沒有列在播放/暫停時,底部兩條進度條都反灰並歸零。</summary>
        private void ResetPositionBars()
        {
            _isSyncingPositionSliders = true;
            try
            {
                sliderOpeningPosition.IsEnabled = false;
                sliderLoopPosition.IsEnabled = false;
                sliderOpeningPosition.Value = 0;
                sliderLoopPosition.Value = 0;
            }
            finally
            {
                _isSyncingPositionSliders = false;
            }

            txtOpeningPosition.Text = "00:00:00:0000";
            txtLoopPosition.Text = "00:00:00:0000";
        }

        /// <summary>使用者拖曳「開場片段」進度條:定位到開場片段內對應的時間點(只有程式自己同步畫面時才忽略)。</summary>
        private void SliderOpeningPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncingPositionSliders)
                return;

            _playback.SeekWithinOpening(TimeSpan.FromSeconds(e.NewValue));
        }

        /// <summary>使用者拖曳「循環片段」進度條:定位到循環片段內對應的時間點(只有程式自己同步畫面時才忽略)。</summary>
        private void SliderLoopPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncingPositionSliders)
                return;

            _playback.SeekWithinLoop(TimeSpan.FromSeconds(e.NewValue));
        }

        /// <summary>
        /// 點一下底部進度條旁邊的時間文字,把目前顯示的時間(HH:mm:ss:ffff)複製到剪貼簿,
        /// 方便貼到「開場片段」「循環片段」頁簽的時間欄位。
        /// </summary>
        private void TxtPosition_Click(object sender, MouseButtonEventArgs e)
        {
            TextBlock tb = sender as TextBlock;
            if (tb == null || string.IsNullOrEmpty(tb.Text))
                return;

            try
            {
                Clipboard.SetText(tb.Text);
            }
            catch (Exception)
            {
                // 剪貼簿偶爾會被其他程式短暫佔用而丟例外,複製失敗就算了,不影響其他操作
            }
        }

        private static TimeSpan ClampTimeSpan(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        /// <summary>把除了 playingRow 以外的每一列都標成「被播放鎖定」,四個頁簽的畫面會跟著整列反灰。</summary>
        private void LockOtherRows(AudioMapRow playingRow)
        {
            foreach (AudioMapRow item in _items)
            {
                if (item != playingRow)
                    item.IsPlaybackLocked = true;
            }
        }

        /// <summary>解除所有列的播放鎖定(停止播放時呼叫)。</summary>
        private void UnlockAllRows()
        {
            foreach (AudioMapRow item in _items)
            {
                item.IsPlaybackLocked = false;
            }
        }

        /// <summary>播放/暫停期間把選單跟工具列整排鎖住(含 Ctrl+S/Z/Y,見 Window_KeyDown),避免異動到正在播放的資料。</summary>
        private void SetGlobalControlsEnabled(bool enabled)
        {
            mainMenu.IsEnabled = enabled;
            toolbarPanel.IsEnabled = enabled;
        }

        #endregion

        #region 全域快捷鍵

        /// <summary>
        /// 整個視窗共用的快捷鍵:Ctrl+S 存檔、Ctrl+Z 復原、Ctrl+Y 取消復原,行為分別跟按選單的對應項目相同。
        /// 播放/暫停中(mainMenu 被鎖住時)這些快捷鍵也要一併停用,不然會繞過選單被鎖住的畫面直接異動資料。
        /// </summary>
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (!mainMenu.IsEnabled)
                return;

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
        private List<AudioMapRow> CloneItems()
        {
            List<AudioMapRow> clone = new List<AudioMapRow>();
            foreach (AudioMapRow item in _items)
            {
                clone.Add(item.Clone());
            }

            return clone;
        }

        /// <summary>
        /// 把整份對照表換成指定的快照內容(直接沿用快照裡的物件,因為快照本身已經是專屬、沒有共用的複本)。
        /// </summary>
        private void ApplySnapshot(List<AudioMapRow> snapshot)
        {
            _items.Clear();
            foreach (AudioMapRow item in snapshot)
            {
                _items.Add(item);
            }

            // 換過整份資料後,舊的作用列參考已經不是目前清單裡的物件,清掉避免後續操作指到不存在的資料
            _activeItem = null;
        }

        /// <summary>
        /// 在一次異動動作「開始前」呼叫,把異動前的快照推進復原堆疊,並清空取消復原堆疊
        /// (標準 Undo/Redo 慣例:只要發生新的異動,先前被復原掉的分支就不再能取消復原)。
        /// </summary>
        private void PushUndoSnapshot(List<AudioMapRow> snapshotBeforeChange)
        {
            _undoStack.Push(snapshotBeforeChange);
            _redoStack.Clear();
            UpdateUndoRedoMenuState();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0)
                return;

            _redoStack.Push(CloneItems());
            ApplySnapshot(_undoStack.Pop());
            UpdateUndoRedoMenuState();
        }

        private void Redo()
        {
            if (_redoStack.Count == 0)
                return;

            _undoStack.Push(CloneItems());
            ApplySnapshot(_redoStack.Pop());
            UpdateUndoRedoMenuState();
        }

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
        /// 新增:清空對照表內容,回到「尚未開啟任何檔案」的狀態,重新開始編輯。
        /// </summary>
        private void DoNew()
        {
            PushUndoSnapshot(CloneItems());

            _items.Clear();
            _currentFilePath = null;
            Title = "音效檔對照表";
        }
        /// <summary>
        /// 開啟:選一個 ini 檔,依 "[功能代號]" + "欄位=值" 的區段格式解析後,整批取代目前的對照表內容。
        /// 「資料夾」欄位也會順便改成這個 ini 檔所在的資料夾(常見情況是音效檔跟 ini 檔放在一起),
        /// 使用者不滿意的話可以再自己用「...」按鈕重新選。
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
                AudioMapRow currentItem = null;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line))
                        continue;

                    // "[功能代號]" 這種格式代表新的 Section,開始一筆新資料
                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentItem = new AudioMapRow { Code = line.Substring(1, line.Length - 2).Trim() };
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

                    SetField(currentItem, key, value);
                }

                _currentFilePath = dlg.FileName;
                Title = "音效檔對照表 - " + Path.GetFileName(_currentFilePath);
                SetFolder(Path.GetDirectoryName(_currentFilePath));
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
        /// 把對照表內容依 ini 格式寫出到指定檔案(UTF-8):每筆資料一個 Section,
        /// "[功能代號]" 接著 FileName/SStart/SEnd/CStart/CEnd/MaxS 六個欄位。代號空白的列會被略過,不寫進檔案。
        /// FileName 只存檔名(不含資料夾路徑),「資料夾」是每台電腦各自在工具列選的,不會寫進這個檔案。
        /// </summary>
        private void SaveToFile(string filePath)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (AudioMapRow item in _items)
                {
                    if (string.IsNullOrWhiteSpace(item.Code))
                        continue;

                    sb.AppendLine("[" + item.Code + "]");
                    sb.AppendLine("FileName=" + item.FileName);
                    sb.AppendLine("SStart=" + item.SStart);
                    sb.AppendLine("SEnd=" + item.SEnd);
                    sb.AppendLine("CStart=" + item.CStart);
                    sb.AppendLine("CEnd=" + item.CEnd);
                    sb.AppendLine("MaxS=" + item.MaxS);
                    sb.AppendLine();
                }

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show("存檔失敗:" + ex.Message, "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>依 ini 檔讀到的 Key,把值設回對照表資料對應的欄位;不認得的 Key 直接略過。</summary>
        private static void SetField(AudioMapRow item, string key, string value)
        {
            if (string.Equals(key, "FileName", StringComparison.OrdinalIgnoreCase))
                item.FileName = value;
            else if (string.Equals(key, "SStart", StringComparison.OrdinalIgnoreCase))
                item.SStart = value;
            else if (string.Equals(key, "SEnd", StringComparison.OrdinalIgnoreCase))
                item.SEnd = value;
            else if (string.Equals(key, "CStart", StringComparison.OrdinalIgnoreCase))
                item.CStart = value;
            else if (string.Equals(key, "CEnd", StringComparison.OrdinalIgnoreCase))
                item.CEnd = value;
            else if (string.Equals(key, "MaxS", StringComparison.OrdinalIgnoreCase))
                item.MaxS = value;
        }
        /// <summary>
        /// 關閉:直接關掉整個視窗。
        /// </summary>
        private void DoClose()
        {
            Close();
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
        /// 全選:把所有列的「選」勾選狀態都打勾。
        /// </summary>
        private void DoSelectAll()
        {
            foreach (AudioMapRow item in _items)
            {
                item.IsSelected = true;
            }
        }
        /// <summary>
        /// 反選:把所有列的「選」勾選狀態反轉(打勾變沒打勾,反之亦然)。
        /// </summary>
        private void DoInvertSelect()
        {
            foreach (AudioMapRow item in _items)
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

            AudioMapRow selected = _activeItem;
            int insertIndex = selected == null ? _items.Count : _items.IndexOf(selected) + 1;

            _items.Insert(insertIndex, new AudioMapRow());
        }
        /// <summary>
        /// 刪除:
        /// 有打勾的列 → 詢問「是否刪除打勾項目?」,按是則全部刪除;
        /// 沒有打勾但有作用列 → 詢問「是否刪除{功能代號}?」,按是則刪除該列。
        /// </summary>
        private void DoDelete()
        {
            List<AudioMapRow> checkedItems = _items.Where(p => p.IsSelected).ToList();

            if (checkedItems.Count > 0)
            {
                MessageBoxResult result = MessageBox.Show("是否刪除打勾項目?", "確認刪除", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                    return;

                PushUndoSnapshot(CloneItems());

                foreach (AudioMapRow item in checkedItems)
                {
                    _items.Remove(item);
                }

                return;
            }

            AudioMapRow selected = _activeItem;
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
            List<AudioMapRow> checkedItems = _items.Where(p => p.IsSelected).ToList();

            if (checkedItems.Count > 0)
            {
                PushUndoSnapshot(CloneItems());

                foreach (AudioMapRow item in checkedItems)
                {
                    item.IsSelected = false;

                    AudioMapRow copy = item.Clone();
                    copy.Code = item.Code + "_COPY";
                    copy.IsSelected = false;

                    _items.Add(copy);
                }

                return;
            }

            AudioMapRow selected = _activeItem;
            if (selected != null)
            {
                PushUndoSnapshot(CloneItems());

                int insertIndex = _items.IndexOf(selected) + 1;

                AudioMapRow copy = selected.Clone();
                copy.Code = selected.Code + "_COPY";

                _items.Insert(insertIndex, copy);
            }
        }

        #endregion
    }
}
