using System.ComponentModel;

namespace AudioMapTool.Models
{
    /// <summary>
    /// 一筆音效設定資料。「路徑」「開場片段」「循環片段」「最大音量」四個頁簽對應同一份資料,
    /// 每個頁簽的 DataGrid 只是顯示/編輯其中幾個欄位:
    /// Code(功能代號) / FileName(音效檔名稱) / SStart,SEnd(開場開始/結束時間) / CStart,CEnd(循環開始/結束時間) / MaxS(最大音量)。
    /// 音效檔實際位置 = 使用者在工具列選的「資料夾」+ 這裡的 FileName,所以這裡只存檔名(含副檔名),
    /// 不存完整路徑,ini 檔換一台電腦開也不會壞。
    /// PlaybackState/IsPlaybackLocked 是播放時的執行期狀態(見 Services/PlaybackController.cs),
    /// 不是使用者資料,所以 Clone()/存檔/復原快照都不會帶到。
    /// </summary>
    public class AudioMapRow : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _code;
        private string _fileName;
        private string _sStart = "00:00:00:0000";
        private string _sEnd = "00:00:00:0000";
        private string _cStart = "00:00:00:0000";
        private string _cEnd = "99:99:99:9999";
        private string _maxS = "1";
        private RowPlaybackState _playbackState = RowPlaybackState.None;
        private bool _isPlaybackLocked;

        /// <summary>是否被勾選(批次操作用),四個頁簽共用同一個值。</summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        /// <summary>功能代號(ID),對照表的 key,不可為空、不可與其他列重複。</summary>
        public string Code
        {
            get { return _code; }
            set { _code = value; OnPropertyChanged("Code"); }
        }

        /// <summary>音效檔名稱(僅檔名+副檔名,不含資料夾路徑),「路徑」頁簽的欄位。</summary>
        public string FileName
        {
            get { return _fileName; }
            set
            {
                _fileName = value;
                OnPropertyChanged("FileName");
                OnPropertyChanged("IsSegmentTabRowEnabled");
                OnPropertyChanged("IsMaxVolumeTabRowEnabled");
            }
        }

        /// <summary>開場片段開始時間,「開場片段」頁簽的欄位。新增列時預設 00:00:00:0000。</summary>
        public string SStart
        {
            get { return _sStart; }
            set { _sStart = value; OnPropertyChanged("SStart"); }
        }

        /// <summary>
        /// 開場片段結束時間,「開場片段」頁簽的欄位。新增列時預設 00:00:00:0000(跟 SStart 相同,
        /// 長度 0,等於預設不播開場片段,直接進循環片段)。
        /// </summary>
        public string SEnd
        {
            get { return _sEnd; }
            set { _sEnd = value; OnPropertyChanged("SEnd"); }
        }

        /// <summary>循環片段開始時間,「循環片段」頁簽的欄位。新增列時預設 00:00:00:0000。</summary>
        public string CStart
        {
            get { return _cStart; }
            set { _cStart = value; OnPropertyChanged("CStart"); }
        }

        /// <summary>
        /// 循環片段結束時間,「循環片段」頁簽的欄位。新增列時預設 99:99:99:9999(刻意超出任何音效檔的長度,
        /// 當一個「到檔案結尾」的占位值);開始播放、知道音效檔實際總長度後,
        /// PlaybackController 會自動把它夾回音效檔的實際總長度(見 ClampRowTimesToNaturalDuration)。
        /// </summary>
        public string CEnd
        {
            get { return _cEnd; }
            set { _cEnd = value; OnPropertyChanged("CEnd"); }
        }

        /// <summary>最大音量,「最大音量」頁簽的欄位,範圍 0~1。新增列時預設是 1(滿音量)。</summary>
        public string MaxS
        {
            get { return _maxS; }
            set { _maxS = value; OnPropertyChanged("MaxS"); }
        }

        /// <summary>
        /// 這一列目前的播放狀態(None/Playing/Paused)。由 MainWindow 透過 PlaybackController 驅動,
        /// 不是使用者資料的一部分,所以不會被 Clone()/存檔/復原快照帶走。
        /// </summary>
        public RowPlaybackState PlaybackState
        {
            get { return _playbackState; }
            set
            {
                _playbackState = value;
                OnPropertyChanged("PlaybackState");
                RaisePlaybackDerivedChanged();
            }
        }

        /// <summary>
        /// 是不是「別的列」正在播放/暫停中,因而這一列整個要鎖定不能操作。
        /// 由 MainWindow 在開始播放某一列時,把其他所有列的這個屬性設成 true;停止播放時全部設回 false。
        /// </summary>
        public bool IsPlaybackLocked
        {
            get { return _isPlaybackLocked; }
            set
            {
                _isPlaybackLocked = value;
                OnPropertyChanged("IsPlaybackLocked");
                RaisePlaybackDerivedChanged();
            }
        }

        /// <summary>「路徑」頁簽這一列是否可以操作:自己沒在播放/暫停,也沒被別列的播放鎖定。</summary>
        public bool IsPathTabRowEnabled
        {
            get { return _playbackState == RowPlaybackState.None && !_isPlaybackLocked; }
        }

        /// <summary>
        /// 「開場片段」「循環片段」頁簽這一列是否可以操作:音效檔名稱要先有值,自己沒在「播放中」(暫停中還是可以,
        /// 因為暫停時間欄位要能編輯),也沒被別列的播放鎖定。
        /// </summary>
        public bool IsSegmentTabRowEnabled
        {
            get { return !string.IsNullOrEmpty(_fileName) && _playbackState != RowPlaybackState.Playing && !_isPlaybackLocked; }
        }

        /// <summary>
        /// 「最大音量」頁簽這一列(整列,含開始/暫停/停止按鈕)是否可以操作:音效檔名稱要先有值,
        /// 也沒被別列的播放鎖定。這裡不看自己的 PlaybackState,因為播放/暫停中還是要留著按鈕能點,
        /// 選/功能代號/音量另外用 IsEditableWhenIdle 個別鎖定。
        /// </summary>
        public bool IsMaxVolumeTabRowEnabled
        {
            get { return !string.IsNullOrEmpty(_fileName) && !_isPlaybackLocked; }
        }

        /// <summary>
        /// 「選」勾選欄、「功能代號」欄在四個頁簽共用:這一列自己在播放中或暫停中時都要鎖定,
        /// 只有完全沒在播放(None)才能編輯。
        /// </summary>
        public bool IsEditableWhenIdle
        {
            get { return _playbackState == RowPlaybackState.None; }
        }

        /// <summary>「最大音量」頁簽「開始」按鈕是否可點:沒被鎖定,而且不是播放中(未播放或暫停中都可以按「開始」)。</summary>
        public bool CanPlay
        {
            get { return !_isPlaybackLocked && _playbackState != RowPlaybackState.Playing; }
        }

        /// <summary>「最大音量」頁簽「暫停」按鈕是否可點:沒被鎖定,而且正在播放中。</summary>
        public bool CanPause
        {
            get { return !_isPlaybackLocked && _playbackState == RowPlaybackState.Playing; }
        }

        /// <summary>「最大音量」頁簽「停止」按鈕是否可點:沒被鎖定,而且不是完全沒播放(播放中或暫停中都可以按「停止」)。</summary>
        public bool CanStop
        {
            get { return !_isPlaybackLocked && _playbackState != RowPlaybackState.None; }
        }

        private void RaisePlaybackDerivedChanged()
        {
            OnPropertyChanged("IsPathTabRowEnabled");
            OnPropertyChanged("IsSegmentTabRowEnabled");
            OnPropertyChanged("IsMaxVolumeTabRowEnabled");
            OnPropertyChanged("IsEditableWhenIdle");
            OnPropertyChanged("CanPlay");
            OnPropertyChanged("CanPause");
            OnPropertyChanged("CanStop");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>複製一份完全獨立的資料(所有欄位都複製,不與原本共用參考),供拷貝與復原快照使用。</summary>
        public AudioMapRow Clone()
        {
            return new AudioMapRow
            {
                Code = Code,
                FileName = FileName,
                SStart = SStart,
                SEnd = SEnd,
                CStart = CStart,
                CEnd = CEnd,
                MaxS = MaxS,
                IsSelected = IsSelected
            };
        }
    }
}
