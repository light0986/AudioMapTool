using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using AudioMapTool.Models;
using AudioMapTool.Utilities;

namespace AudioMapTool.Services
{
    /// <summary>
    /// 「最大音量」頁簽開始/暫停/停止背後的播放邏輯。
    /// 開始播放時:先播「開場片段」(SStart~SEnd,只播一次),播完自動接「循環片段」(CStart~CEnd,反覆循環),
    /// 直到按下暫停或停止。音量套用該列的 MaxS,而且播放/暫停期間拖動音量滑桿會即時反映到正在播放的聲音上
    /// (訂閱該列的 PropertyChanged,MaxS 變了就馬上套用到 MediaPlayer.Volume);暫停中如果使用者改了
    /// 開場/循環片段的開始/結束時間,也會重新套用新的片段邊界(見 RefreshSegmentBoundsIfPaused)。
    /// 每次按「開始」重新播放時,會等音效檔實際開啟完成、知道音樂總長度後,把超過總長度的開始/結束時間
    /// 夾回音樂總長度本身(見 Player_MediaOpened/ClampRowTimesToNaturalDuration),避免定位到一個
    /// 音樂根本沒播到的時間點。
    /// 透過 PositionChanged 事件跟 SeekWithinOpening/SeekWithinLoop,支援 MainWindow 底部的兩條播放進度條:
    /// 播放中自動跟著移動(不能拖),暫停中可以拖——開場片段進度條隨時能拖,循環片段進度條只有
    /// 「已經進入循環片段」(IsInLoopStage,即開場片段進度條已經滿了)時才能拖。
    /// 同一時間只會有一列在播放/暫停,呼叫端(MainWindow)負責在開始播放時鎖住其他列的畫面。
    /// </summary>
    public class PlaybackController
    {
        private enum Stage
        {
            Opening,
            Loop
        }

        /// <summary>用計時器輪詢目前播放位置,判斷是不是該從開場片段切到循環片段、或循環片段該重頭播——
        /// WPF 的 MediaPlayer 沒有「播到某個時間點」的事件,只能用輪詢的方式做,精準度受這個間隔限制。
        /// 間隔越短,時間軸跟音樂斷點的落差越小,但受限於 MediaPlayer.Position 本身的更新粒度、
        /// 壓縮格式(如 MP3)的音框邊界,再短也不會是逐取樣精準。</summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

        private readonly MediaPlayer _player = new MediaPlayer();
        private readonly DispatcherTimer _timer;

        private AudioMapRow _row;
        private TimeSpan _openingStart;
        private TimeSpan _openingEnd;
        private TimeSpan _loopStart;
        private TimeSpan _loopEnd;
        private Stage _stage;

        /// <summary>目前正在播放/暫停中的列;沒有播放時是 null。</summary>
        public AudioMapRow PlayingRow
        {
            get { return _row; }
        }

        /// <summary>目前播放位置(MediaPlayer.Position);沒有列在播放/暫停時沒有意義。</summary>
        public TimeSpan CurrentPosition
        {
            get { return _player.Position; }
        }

        public TimeSpan OpeningStart { get { return _openingStart; } }
        public TimeSpan OpeningEnd { get { return _openingEnd; } }
        public TimeSpan LoopStart { get { return _loopStart; } }
        public TimeSpan LoopEnd { get { return _loopEnd; } }

        /// <summary>
        /// 目前是不是已經進入循環片段(等同「開場片段進度條已經滿了」)。
        /// 播放中會隨播放進度自然變 true;暫停中則是使用者上一次操作(拖曳進度條/自然播放到)停在哪一段。
        /// </summary>
        public bool IsInLoopStage
        {
            get { return _row != null && _stage == Stage.Loop; }
        }

        /// <summary>播放失敗時觸發(檔案不存在、時間格式不對、解碼失敗...等),帶出給使用者看的訊息。</summary>
        public event EventHandler<string> PlaybackFailed;

        /// <summary>
        /// 播放位置有變化時觸發(播放中每 PollInterval 一次、暫停/開始/停止/拖曳進度條時各觸發一次),
        /// 供 MainWindow 更新底部兩條播放進度條。沒有列在播放/暫停時(PlayingRow 為 null)代表要重置畫面。
        /// </summary>
        public event EventHandler PositionChanged;

        public PlaybackController()
        {
            _timer = new DispatcherTimer { Interval = PollInterval };
            _timer.Tick += Timer_Tick;

            _player.MediaOpened += Player_MediaOpened;
            _player.MediaEnded += Player_MediaEnded;
            _player.MediaFailed += Player_MediaFailed;
        }

        /// <summary>
        /// 開始播放指定列:如果這一列正好是暫停中的那一列,直接從暫停位置接續播放;
        /// 否則(第一次播放、或上次已經停止)從開場片段的開始時間重新開始。
        /// 開場/循環片段的開始/結束時間格式不對、找不到資料夾裡的音效檔時會回傳 false 並觸發 PlaybackFailed。
        /// </summary>
        public bool Play(AudioMapRow row, string folderPath)
        {
            if (row == null)
                return false;

            if (_row == row && row.PlaybackState == RowPlaybackState.Paused)
            {
                _player.Play();
                _timer.Start();
                row.PlaybackState = RowPlaybackState.Playing;
                RaisePositionChanged();
                return true;
            }

            TimeSpan openingStart, openingEnd, loopStart, loopEnd;
            if (!TimecodeFormat.TryParse(row.SStart, out openingStart) ||
                !TimecodeFormat.TryParse(row.SEnd, out openingEnd) ||
                !TimecodeFormat.TryParse(row.CStart, out loopStart) ||
                !TimecodeFormat.TryParse(row.CEnd, out loopEnd))
            {
                RaisePlaybackFailed("請先在「開場片段」「循環片段」頁簽把這一列的開始/結束時間都填好(格式 HH:mm:ss:ffff),才能播放。");
                return false;
            }

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                RaisePlaybackFailed("請先在工具列選好「資料夾」,才能播放。");
                return false;
            }

            if (string.IsNullOrEmpty(row.FileName))
            {
                RaisePlaybackFailed("請先在「路徑」頁簽填好這一列的音效檔名稱,才能播放。");
                return false;
            }

            string fullPath = Path.Combine(folderPath, row.FileName);
            if (!File.Exists(fullPath))
            {
                RaisePlaybackFailed("找不到音效檔:" + fullPath);
                return false;
            }

            _row = row;
            _openingStart = openingStart;
            _openingEnd = openingEnd;
            _loopStart = loopStart;
            _loopEnd = loopEnd;
            _stage = Stage.Opening;

            // Position/Play()/計時器都延後到 Player_MediaOpened 才開始——MediaPlayer.Open() 是非同步的,
            // 音效檔實際總長度(NaturalDuration)要等 MediaOpened 觸發才知道,必須先等這個才能做「時間欄位
            // 超過音樂總長度就夾回去」的檢查(見 ClampRowTimesToNaturalDuration)。
            _player.Open(new Uri(fullPath));
            _player.Volume = ParseVolume(row.MaxS);
            row.PropertyChanged += Row_PropertyChanged;

            row.PlaybackState = RowPlaybackState.Playing;
            RaisePositionChanged();
            return true;
        }

        /// <summary>暫停目前播放的列(保留播放位置);沒有列在播放時不做事。</summary>
        public void Pause()
        {
            if (_row == null)
                return;

            _player.Pause();
            _timer.Stop();
            _row.PlaybackState = RowPlaybackState.Paused;
            RaisePositionChanged();
        }

        /// <summary>停止播放;下次再按「開始」會從開場片段重頭播放。沒有列在播放/暫停時不做事。</summary>
        public void Stop()
        {
            if (_row == null)
                return;

            _timer.Stop();
            _player.Stop();
            _player.Close();

            AudioMapRow row = _row;
            row.PropertyChanged -= Row_PropertyChanged;
            _row = null;
            row.PlaybackState = RowPlaybackState.None;
            RaisePositionChanged();
        }

        /// <summary>
        /// 暫停中拖曳「開場片段」進度條:定位到開場片段內指定的相對時間(從 SStart 算起,自動夾在片段長度內)。
        /// 這條進度條隨時可以拖(不管目前在開場還是循環片段),拖了就代表要回到開場片段這個時間點繼續播。
        /// 只有暫停中才能拖(播放中畫面本來就會把進度條鎖住,這裡仍加一層保護)。
        /// </summary>
        public void SeekWithinOpening(TimeSpan offsetFromStart)
        {
            if (_row == null || _row.PlaybackState != RowPlaybackState.Paused)
                return;

            TimeSpan duration = _openingEnd - _openingStart;
            _stage = Stage.Opening;
            _player.Position = _openingStart + Clamp(offsetFromStart, TimeSpan.Zero, duration);
            RaisePositionChanged();
        }

        /// <summary>
        /// 暫停中拖曳「循環片段」進度條:定位到循環片段內指定的相對時間(從 CStart 算起,自動夾在片段長度內)。
        /// 只有「開場片段進度條已經滿了」(已經進入循環片段)且暫停中才能拖,對應 IsInLoopStage。
        /// </summary>
        public void SeekWithinLoop(TimeSpan offsetFromStart)
        {
            if (_row == null || _row.PlaybackState != RowPlaybackState.Paused || _stage != Stage.Loop)
                return;

            TimeSpan duration = _loopEnd - _loopStart;
            _player.Position = _loopStart + Clamp(offsetFromStart, TimeSpan.Zero, duration);
            RaisePositionChanged();
        }

        /// <summary>
        /// 音量滑桿拖動時即時套用到正在播放的聲音;暫停中編輯開場/循環片段的開始/結束時間時,
        /// 重新套用片段邊界(見 RefreshSegmentBoundsIfPaused)。其他欄位(功能代號等)變動不用理會。
        /// </summary>
        private void Row_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "MaxS")
            {
                if (_row != null)
                    _player.Volume = ParseVolume(_row.MaxS);
            }
            else if (e.PropertyName == "SStart" || e.PropertyName == "SEnd" ||
                     e.PropertyName == "CStart" || e.PropertyName == "CEnd")
            {
                RefreshSegmentBoundsIfPaused();
            }
        }

        /// <summary>
        /// 暫停中使用者改了開場/循環片段的開始或結束時間時,重新剖析並套用新的片段邊界,
        /// 讓實際播放的時間點跟畫面上輸入的值一致(底部兩條進度條的 Maximum/Value 也會跟著更新)。
        /// 四個時間欄位都要能剖析成功才會套用,避免打到一半、格式還不完整時就套用進去;
        /// 目前播放位置如果不在新的邊界內(例如把結束時間往前調,調到目前位置之前),會夾到新的邊界內。
        /// </summary>
        private void RefreshSegmentBoundsIfPaused()
        {
            if (_row == null || _row.PlaybackState != RowPlaybackState.Paused)
                return;

            TimeSpan openingStart, openingEnd, loopStart, loopEnd;
            if (!TimecodeFormat.TryParse(_row.SStart, out openingStart) ||
                !TimecodeFormat.TryParse(_row.SEnd, out openingEnd) ||
                !TimecodeFormat.TryParse(_row.CStart, out loopStart) ||
                !TimecodeFormat.TryParse(_row.CEnd, out loopEnd))
            {
                return;
            }

            _openingStart = openingStart;
            _openingEnd = openingEnd;
            _loopStart = loopStart;
            _loopEnd = loopEnd;

            _player.Position = _stage == Stage.Opening
                ? Clamp(_player.Position, _openingStart, _openingEnd)
                : Clamp(_player.Position, _loopStart, _loopEnd);

            RaisePositionChanged();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (_row == null)
                return;

            if (_stage == Stage.Opening)
            {
                if (_player.Position >= _openingEnd)
                {
                    _stage = Stage.Loop;
                    _player.Position = _loopStart;
                }
            }
            else if (_player.Position >= _loopEnd)
            {
                _player.Position = _loopStart;
            }

            RaisePositionChanged();
        }

        /// <summary>
        /// 音效檔實際開啟完成(這時候才知道 NaturalDuration,也就是音樂總長度)。
        /// 開場/循環片段任一個開始或結束時間比音樂總長度還大,就把那個欄位直接改成音樂總長度
        /// (同時寫回 AudioMapRow,畫面上的時間欄位、底部進度條會一起更新),
        /// 接著才真正定位到開場片段開始的位置、開始播放。
        /// </summary>
        private void Player_MediaOpened(object sender, EventArgs e)
        {
            if (_row == null)
                return;

            if (_player.NaturalDuration.HasTimeSpan)
                ClampRowTimesToNaturalDuration(_player.NaturalDuration.TimeSpan);

            _player.Position = _openingStart;
            _player.Play();
            _timer.Start();
            RaisePositionChanged();
        }

        /// <summary>
        /// 把開場/循環片段的開始/結束時間夾在音樂總長度以內:超過的欄位改成音樂總長度本身,
        /// 同時更新這裡記的 _openingStart 等內部邊界跟 AudioMapRow 上的欄位(觸發 PropertyChanged,
        /// 畫面上的「開場片段」「循環片段」頁簽跟底部進度條都會跟著顯示新的值)。
        /// </summary>
        private void ClampRowTimesToNaturalDuration(TimeSpan duration)
        {
            if (_openingStart > duration)
            {
                _openingStart = duration;
                _row.SStart = TimecodeFormat.Format(duration);
            }

            if (_openingEnd > duration)
            {
                _openingEnd = duration;
                _row.SEnd = TimecodeFormat.Format(duration);
            }

            if (_loopStart > duration)
            {
                _loopStart = duration;
                _row.CStart = TimecodeFormat.Format(duration);
            }

            if (_loopEnd > duration)
            {
                _loopEnd = duration;
                _row.CEnd = TimecodeFormat.Format(duration);
            }
        }

        /// <summary>音效檔本身比設定的片段短、提早播完時的保底處理:開場片段播完就接循環片段,循環片段播完就重頭循環。</summary>
        private void Player_MediaEnded(object sender, EventArgs e)
        {
            if (_row == null)
                return;

            _stage = Stage.Loop;
            _player.Position = _loopStart;
            _player.Play();
            RaisePositionChanged();
        }

        private void Player_MediaFailed(object sender, ExceptionEventArgs e)
        {
            string message = "播放失敗:" + (e.ErrorException == null ? "未知錯誤" : e.ErrorException.Message);
            Stop();
            RaisePlaybackFailed(message);
        }

        private static double ParseVolume(string maxS)
        {
            double volume;
            if (double.TryParse(maxS, NumberStyles.Float, CultureInfo.InvariantCulture, out volume))
                return Math.Max(0d, Math.Min(1d, volume));

            return 1d;
        }

        private void RaisePlaybackFailed(string message)
        {
            EventHandler<string> handler = PlaybackFailed;
            if (handler != null)
                handler(this, message);
        }

        private void RaisePositionChanged()
        {
            EventHandler handler = PositionChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
