namespace AudioMapTool.Models
{
    /// <summary>一筆資料目前的播放狀態,供「最大音量」頁簽的開始/暫停/停止按鈕與跨頁簽的欄位鎖定使用。</summary>
    public enum RowPlaybackState
    {
        /// <summary>沒有在播放(初始狀態,或按過停止之後)。</summary>
        None,
        /// <summary>播放中。</summary>
        Playing,
        /// <summary>暫停中。</summary>
        Paused
    }
}
