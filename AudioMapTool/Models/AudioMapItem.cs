using System.ComponentModel;

namespace AudioMapTool.Models
{
    /// <summary>
    /// 一筆「功能代號 -> 音效檔路徑」對照資料。
    /// 對照表存檔時,每一筆會輸出成一行 "Code=FilePath"。
    /// </summary>
    public class AudioMapItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _code;
        private string _filePath;

        /// <summary>
        /// 這一列是否被勾選(對應 Grid「選」欄位的 CheckBox)。
        /// 全選/反選/刪除/拷貝等批次操作,都是依這個屬性判斷要處理哪些列。
        /// </summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set { _isSelected = value; OnPropertyChanged("IsSelected"); }
        }

        /// <summary>
        /// 功能代號,對照表的 key,存檔時不可為空、且不能與其他列重複。
        /// </summary>
        public string Code
        {
            get { return _code; }
            set { _code = value; OnPropertyChanged("Code"); }
        }

        /// <summary>
        /// 音效檔的實際路徑,提供給 MediaPlayer 播放使用。
        /// </summary>
        public string FilePath
        {
            get { return _filePath; }
            set { _filePath = value; OnPropertyChanged("FilePath"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// 屬性值變動時通知畫面(DataGrid 的 TextBox/CheckBox 綁定)更新顯示。
        /// </summary>
        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
