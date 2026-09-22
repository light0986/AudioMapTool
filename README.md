# AudioMapTool(音效檔對照表)

維護「功能代號 → 音效檔」對照表的獨立小工具。應用程式呼叫 `MediaPlayer` 播放音效時,只要知道功能代號跟目前機器的音效檔資料夾,就能查到實際要播放的檔案,不需要在程式碼裡到處寫死路徑。對照表只存**檔名**,不存完整路徑,同一份 `.ini` 檔換一台電腦開也能用(見「音效檔資料夾」)。

這是完全獨立的 WPF 專案,不依賴任何外部共用框架或元件。

## 環境需求

- .NET Framework 4.0(Client Profile)
- WPF(`PresentationCore`/`PresentationFramework`/`WindowsBase`)
- 開發用 Visual Studio 2010 以上;也可以直接用 `dotnet build`/`MSBuild.exe` 建置驗證,不一定要開 IDE

## 建置方式

```bash
"C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe" AudioMapTool\AudioMapTool.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x86
```

建置完成的執行檔在 `AudioMapTool\bin\Debug\AudioMapTool.exe`。

## 畫面配置

```
┌──────────────────────────────────────────────────────┐
│ 檔案(F)  編輯(E)                                        │  ← 選單
├──────────────────────────────────────────────────────┤
│ 全選  反選  新增  刪除  拷貝      資料夾 [___________][...] │  ← 工具列
├──────────────────────────────────────────────────────┤
│ 路徑  開場片段  循環片段  最大音量                         │  ← 頁簽
├────┬────────────┬────────────────────────────────────┤
│ 選 │  功能代號    │  (欄位依頁簽而不同)                    │  ← 對照表(DataGrid,四個頁簽是同一份資料的不同欄位視圖)
├────┼────────────┼────────────────────────────────────┤
│ ☐  │  123        │  ...                                │
│ ☐  │  456        │  ...                                │
└────┴────────────┴────────────────────────────────────┘
```

**四個頁簽對應同一份資料**:同一個功能代號(同一列)在「路徑」「開場片段」「循環片段」「最大音量」四個頁簽裡是同一筆資料,只是每個頁簽顯示/編輯其中幾個欄位(見下方「對照表欄位」)。新增/刪除/拷貝/復原/取消復原、開啟/存檔也都是對這一份共用資料操作,不分頁簽。

**音效檔資料夾**:工具列右側的「資料夾」是音效檔的基準資料夾,點「...」選一個本機資料夾。「路徑」頁簽的「音效檔名稱」欄只存**檔名**(例如 `click.wav`),實際檔案位置 = 這個資料夾 + 檔名。資料夾本身**不會**存進 `.ini` 檔(每台電腦的資料夾位置不同,本來就要各自選),所以同一份 `.ini` 換電腦開,只要重新選一次資料夾就能用。沒選資料夾之前,「音效檔名稱」欄整欄反灰不能用。開啟 `.ini` 檔時,「資料夾」會自動先帶入該 `.ini` 檔所在的資料夾(方便音效檔跟 `.ini` 放在同一層的情況),不符合的話再手動重選即可。

**連動反灰**:「路徑」頁簽某一列的「音效檔名稱」是空字串時,同一筆資料在「開場片段」「循環片段」「最大音量」頁簽對應的那一列會整列反灰、不能操作,直到「路徑」頁簽把該列的音效檔名稱填上為止。

**播放(「最大音量」頁簽)**:每一列有「開始」「暫停」「停止」三個按鈕,實際播放這一列的音效檔(資料夾 + 音效檔名稱),音量套用這一列的 `MaxS`,先播「開場片段」(只播一次),播完自動接「循環片段」(反覆循環播放)。播放/暫停期間,**其他所有列**(四個頁簽)都會整列反灰;**正在播放的那一列**本身,只留「暫停」「停止」可點,其他欄位都鎖定;**暫停中**則額外開放「開場片段」「循環片段」的 4 個時間欄位可以編輯(方便邊聽邊調時間點),其餘仍鎖定;按「停止」後才會全部恢復。播放中選單(復原/取消復原等)跟工具列(含 Ctrl+S/Z/Y 快捷鍵)也會整排鎖住,避免播放中誤觸新增/刪除/開檔把正在播放的資料換掉。詳細規則見下方「播放」段落。

## 功能說明

### 檔案選單

| 項目 | 說明 |
|---|---|
| 新增 | 清空目前對照表內容,回到空白狀態重新編輯 |
| 開啟 | 選擇一個 `.ini` 檔,依格式解析後整批取代目前內容;同時把工具列的「資料夾」自動改成這個 `.ini` 檔所在的資料夾(常見情況是音效檔跟 `.ini` 檔放在一起),不符合的話可以再用「...」按鈕重新選 |
| 存檔 | 已經開過/存過檔就直接寫回原檔;否則等同「另存新檔」 |
| 另存新檔 | 跳出存檔對話框,將目前對照表輸出成 `.ini` |
| 關閉 | 關閉視窗 |

### 編輯選單 / 復原機制

| 項目 | 快捷鍵 | 說明 |
|---|---|---|
| 復原 | Ctrl+Z | 復原上一步異動 |
| 取消復原 | Ctrl+Y | 取消復原(重做) |

可復原/取消復原的範圍:新增、刪除、拷貝(工具列按鈕與 Enter 後插新列)、功能代號/音效檔名稱等欄位的內容編輯、音效檔查詢按鈕選檔、選單的「新增」「開啟」。**單純的「選」勾選狀態變化、工具列的「資料夾」選擇不列入復原範圍**。無內容可復原/取消復原時,選單項目會自動反灰。

### 工具列按鈕

| 按鈕 | 說明 |
|---|---|
| 全選 | 把所有列的「選」勾選狀態打勾 |
| 反選 | 把所有列的「選」勾選狀態反轉 |
| 新增 | 以目前作用列為基準,在其後插入一筆空白列;沒有作用列則加到最後。新增出來的空白列音量預設是 1(滿音量),開場片段預設 `00:00:00:0000`~`00:00:00:0000`(長度 0,預設不播開場片段),循環片段預設 `00:00:00:0000`~`99:99:99:9999`(結束時間是刻意超出任何檔案長度的占位值,播放時會自動夾回音效檔實際長度,等於預設整首循環) |
| 刪除 | 有打勾的列 → 詢問是否刪除打勾項目,全部刪除;沒打勾但有作用列 → 詢問是否刪除該列 |
| 拷貝 | 有打勾的列 → 複製所有勾選列到清單最後(代號加上 `_COPY` 後綴);沒打勾但有作用列 → 在該列後插入一筆拷貝 |

### 對照表(DataGrid)欄位

每一列資料共有 7 個欄位(`Models/AudioMapRow.cs`),四個頁簽各自顯示其中幾個:

| 欄位(程式內名稱) | 中文意義 | 顯示在哪個頁簽 |
|---|---|---|
| `Code` | 功能代號(ID) | 四個頁簽都有 |
| `FileName` | 音效檔名稱(僅檔名+副檔名,不含資料夾路徑) | 路徑 |
| `SStart` / `SEnd` | 開場開始/結束時間 | 開場片段 |
| `CStart` / `CEnd` | 循環開始/結束時間 | 循環片段 |
| `MaxS` | 最大音量 | 最大音量 |

| 頁簽 | 欄位 | 說明 |
|---|---|---|
| 路徑 | 選 / 功能代號 / 音效檔名稱 | 音效檔名稱可直接輸入,也可點選欄位右側的「...」按鈕跳出檔案選擇視窗(只取回「檔名.副檔名」,不含資料夾);要先在工具列選好「資料夾」這欄才能用。這個頁簽的音效檔名稱是否為空,決定其他三個頁簽對應列要不要反灰(見上方「連動反灰」) |
| 開場片段 | 選 / 功能代號 / 開始時間 / 結束時間 | 開始時間、結束時間格式固定「時:分:秒:碼」(`HH:mm:ss:ffff`),見下方說明 |
| 循環片段 | 選 / 功能代號 / 開始時間 / 結束時間 | 同上,跟「開場片段」欄位結構相同,綁定的是各自的 `CStart`/`CEnd` |
| 最大音量 | 選 / 功能代號 / 音量 / 開始 / 停止 / 暫停 | 音量是拖曳式滑桿(Slider),範圍固定 0~1,見下方說明;「開始」「停止」「暫停」實際播放這一列的音效檔,見下方「播放」段落 |

「功能代號」不可為空、不可與其他列重複(游標離開該欄位時會自動驗證,四個頁簽規則相同,因為驗證的是同一份資料)。

**開始/結束時間格式**:「開場片段」「循環片段」的開始時間、結束時間固定是「時:分:秒:碼」(`HH:mm:ss:ffff`,例如 `00:01:23:0456`),規則見 `Utilities/TimecodeFormat.cs`:

- 輸入時只能打數字跟冒號(`:`),打其他字元會被擋掉;欄位長度上限 13 字元(剛好是 `HH:mm:ss:ffff` 的長度)。
- 游標離開欄位時驗證格式,空字串算合法(代表還沒填);有值但格式不符,會跳出提示訊息並把焦點/選取範圍留在原欄位,方便直接修正。

**音量欄位**:「最大音量」頁簽的音量不能直接打字輸入,改用 Slider 拖曳調整,範圍固定 0~1(`Minimum="0" Maximum="1"`,結構上就不可能超出範圍),右側會即時顯示目前數值(到小數點後兩位)。底層 `AudioMapRow.MaxS` 還是字串(跟其他欄位一致,方便存 ini),由 `Converters/VolumeStringConverter.cs` 負責字串 `<->` 0~1 的 `double` 互轉,**新增(工具列「新增」按鈕、Enter 插入空白列)出來的列預設是 1(滿音量)**。放開滑桿、值真的變動時才會記一筆復原快照,不會每拖一下就記一筆。**這個欄位播放中/暫停中都不會反灰**,拖動時如果這一列正在播放,`PlaybackController` 會即時把新音量套用到正在播放的聲音上(見下方「播放」)。

### 播放

「最大音量」頁簽每一列的「開始」「暫停」「停止」實際播放音效檔,由 `Services/PlaybackController.cs`(包裝 `System.Windows.Media.MediaPlayer`)負責:

1. **開始**:播放「資料夾 + 這一列的音效檔名稱」,音量套用這一列的 `MaxS`。先播「開場片段」(`SStart`~`SEnd`,只播一次),播到結束時間就自動接著播「循環片段」(`CStart`~`CEnd`,反覆循環,播到結束時間就跳回開始時間重播),直到按下暫停或停止。
   - 開始播放前會檢查:開場/循環片段的 4 個時間欄位都要符合 `HH:mm:ss:ffff` 格式、工具列的「資料夾」要選好、這一列的音效檔名稱指到的檔案要存在,任何一項不符合都會跳出提示、不會開始播放。
   - 音效檔實際開啟後(才會知道音樂實際總長度),如果 4 個時間欄位裡有任何一個比音樂總長度還大,那個欄位會直接被改成音樂總長度(畫面上「開場片段」「循環片段」頁簽的欄位、底部進度條都會跟著更新),避免定位到音樂根本沒播到的時間點——新增列時循環片段結束時間預設的 `99:99:99:9999` 就是靠這個機制,第一次播放就會自動夾成音效檔實際長度。
2. **暫停**:保留播放位置暫停。再按「開始」(此時按鈕變成「接續播放」的意思)會從暫停的位置繼續播,不會重頭來。暫停中「開場片段」「循環片段」頁簽的 4 個時間欄位可以編輯(見上方狀態表),改了之後 `PlaybackController` 會重新剖析並套用新的片段邊界(四個時間都要能剖析成功才會套用),目前播放位置如果落在新邊界外面(例如把結束時間往前調到目前位置之前)會自動夾回邊界內,底部兩條進度條的長度/位置也會跟著更新。
3. **停止**:停止播放。再按「開始」會從開場片段的開始時間重新播放。

開始播放時,`PlaybackController` 訂閱了這一列的 `PropertyChanged`(停止播放時取消訂閱),兩種欄位變動會即時反映到播放上:播放中(或暫停中)拖動音量滑桿,收到 `MaxS` 變動就直接設定 `MediaPlayer.Volume`;暫停中編輯開場/循環片段的開始/結束時間,收到 `SStart`/`SEnd`/`CStart`/`CEnd` 變動就重新套用片段邊界(見上面「暫停」的說明)。

播放狀態(`Models/RowPlaybackState.cs`:`None`/`Playing`/`Paused`)存在 `AudioMapRow.PlaybackState`,決定畫面上哪些控件能用(`AudioMapRow` 裡一系列 `IsPathTabRowEnabled`/`IsSegmentTabRowEnabled`/`IsMaxVolumeTabRowEnabled`/`IsEditableWhenIdle`/`CanPlay`/`CanPause`/`CanStop` 計算屬性,各頁簽 XAML 直接綁這些屬性):

| 狀態 | 其他列(四個頁簽) | 本列「路徑」頁簽 | 本列「開場片段」「循環片段」頁簽 | 本列「最大音量」頁簽 | 選單 / 工具列 |
|---|---|---|---|---|---|
| 播放中 | 整列反灰 | 整列反灰 | 整列反灰 | 選/功能代號鎖定,音量可即時調整,只留「暫停」「停止」可點 | 整排鎖定(含 Ctrl+S/Z/Y) |
| 暫停中 | 整列反灰 | 整列反灰 | 選/功能代號鎖定,4 個時間欄位可編輯 | 選/功能代號鎖定,音量可即時調整,只留「開始」「停止」可點 | 整排鎖定(含 Ctrl+S/Z/Y) |
| 沒有播放 | 正常(依「連動反灰」規則) | 正常 | 正常 | 正常,「開始」可點、「暫停」「停止」不可點 | 正常 |

「別的列」的鎖定(`AudioMapRow.IsPlaybackLocked`)由 `MainWindow` 在開始播放時對其餘每一列設成 `true`,按停止時全部設回 `false`(`LockOtherRows`/`UnlockAllRows`)。同一時間只會有一列在播放/暫停,因為其他列在那期間全部反灰、點不到「開始」。

暫停中「開場片段」「循環片段」頁簽的時間欄位雖然開放編輯,但這幾個 TextBox 同時也是 Enter 鍵插入空白列的觸發點;四個頁簽的 `TxtRow_KeyDown` 因此都會先檢查 `currentItem.PlaybackState != RowPlaybackState.None || currentItem.IsPlaybackLocked`,播放中或暫停中(不管是這一列自己在播放/暫停,還是被別列鎖定)一律不新增列,跟工具列「新增」按鈕被鎖住時的規則保持一致(不然工具列明明反灰,暫停時卻能用 Enter 偷插一筆)。

`PlaybackController` 用一個 `DispatcherTimer`(目前間隔 20ms,見 `PollInterval`)輪詢播放位置,判斷是否該從開場片段切到循環片段、或循環片段該跳回開始時間重播;底部兩條播放進度條也是同一個計時器驅動,所以「時間軸」跟「音樂斷點」的落差就是這個輪詢間隔造成的,調短間隔可以讓兩者更接近同步。**這個間隔有天花板**:`MediaPlayer.Position` 本身的更新粒度、壓縮格式(例如 MP3)只能跳到最接近的音框邊界(常見約 20~30ms 一個音框)這兩個限制不會因為間隔調更短而消失,想要逐取樣精準大概要換掉 `MediaPlayer`、改用其他播放引擎(例如 NAudio),是更大的改動。時間欄位最後的「碼」(`ffff`)目前當成**萬分之一秒**處理(`0456` = 0.0456 秒),不是影格數;如果實際上是要換算成某個幀率(fps)的影格,再回來調整 `Utilities/TimecodeFormat.cs` 的 `TryParse` 就好。

**提前預先開啟(減少切歌卡頓)**:`MediaPlayer.Open()` 是非同步的,讀檔、初始化解碼器需要時間,兩首歌之間切換時常常會有一下卡頓感就是在等這個。`PlaybackController` 額外維護一顆「待命播放器」(`_standbyPlayer`):`MainWindow` 在任一頁簽的儲存格取得焦點時(`Control_ActiveItemChanged`,四個頁簽共用的事件)、以及按下「停止」之後,都會呼叫 `Preload(目前作用列, 資料夾)`——只有在**目前沒有任何列在播放/暫停**時才會真的預載,提前把那一列的音效檔開好、但不播放。等使用者真的按下「開始」,如果剛好是同一列、待命播放器也已經開完(`MediaOpened` 已觸發),`Play()` 就直接把待命播放器接手成正式的 `_player`,省掉再等一次 `Open()` 的時間,直接定位、開始播放;如果不是同一列、或還沒開完,就照原本的流程(重新 `Open()` 再等 `MediaOpened`)。預載失敗(檔案不存在、格式不對...等)不會跳出任何提示——這只是投機性的背景動作,真正播放失敗時 `Play()` 自己會回報。

**播放進度條**:視窗最下方有兩條進度條(`Slider`),上面對應「開場片段」的總時長,下面對應「循環片段」的總時長,方便暫停後精準抓取時間點,旁邊各有一個 `HH:mm:ss:ffff` 格式的時間顯示(`TimecodeFormat.Format`)。**這兩個時間文字點一下會把目前顯示的時間複製到剪貼簿**(`MainWindow.xaml.cs` 的 `TxtPosition_Click`,滑鼠移上去游標會變成手型),方便直接貼到「開場片段」「循環片段」頁簽的時間欄位裡。

- **播放中**:兩條都反灰不能拖,但會隨播放位置自動移動——「開場片段」進度條顯示目前在開場片段內的進度,進入循環片段後固定顯示滿格;「循環片段」進度條進入循環片段後才開始顯示進度(反覆循環,播到結束就跳回開頭),還沒進入循環片段前固定顯示 0。
- **暫停中**:「開場片段」進度條隨時可以拖;「循環片段」進度條**只有「開場片段」進度條是滿的(代表已經進入循環片段)才能拖**,否則反灰不能用。**任一段的開始時間跟結束時間相同(長度 0,沒有範圍可拖)時,對應那條進度條直接鎖住不能用**,不管是不是暫停中。
- **沒有任何列在播放/暫停時**:兩條都反灰並歸零。
- 拖曳「開場片段」進度條一定會把播放位置定位到開場片段內對應的時間點(即使原本已經在循環片段,拖了就等於要回到開場片段那個時間點);拖曳「循環片段」進度條則定位到循環片段內對應的時間點。
- **接續播放時撥放哪一段,由「開場片段進度條是否滿了」決定**:暫停時如果開場片段進度條沒滿(還在開場片段內,不論是自然播放到暫停還是使用者拖曳過),再按「開始」會從開場片段那個時間點繼續播;如果開場片段進度條是滿的(已經進入循環片段),再按「開始」會從循環片段那個時間點繼續播。這其實是 `MediaPlayer.Position` 本身的行為——`PlaybackController` 只要正確記錄使用者停在哪個時間點(拖曳進度條或自然播放暫停),`Play()` 內建的「從暫停位置接續播放」邏輯就會自動接對。
- 開場片段的開始時間跟結束時間相同(長度 0)時,一開始播放就會立刻進入循環片段,這時「開場片段」進度條會直接顯示滿格(而不是卡在 0),但因為長度是 0(沒有範圍可拖),這條進度條本身仍然是鎖住的;「循環片段」進度條則依它自己的長度是不是 0 決定能不能拖。

實作上,`PlaybackController` 新增了 `CurrentPosition`/`OpeningStart`/`OpeningEnd`/`LoopStart`/`LoopEnd`/`IsInLoopStage` 幾個唯讀屬性、`PositionChanged` 事件(播放中每 100ms、暫停/開始/停止/拖曳進度條時各觸發一次)、`SeekWithinOpening(TimeSpan)`/`SeekWithinLoop(TimeSpan)` 兩個方法(分別對應拖曳兩條進度條);`MainWindow.xaml.cs` 訂閱 `PositionChanged` 同步兩條 `Slider` 的 `Value`/`Maximum`/`IsEnabled` 跟旁邊的時間文字,並用 `_isSyncingPositionSliders` 旗標避免「程式同步畫面」跟「使用者拖曳」互相觸發造成無窮迴圈。

`MediaPlayer.Open()` 是非同步的,音效檔實際總長度(`NaturalDuration`)要等 `MediaOpened` 事件觸發才知道,所以 `Play()` 現在只先開檔、記下想要的片段邊界,真正定位播放位置、啟動計時器都延後到 `Player_MediaOpened`——這裡順便呼叫 `ClampRowTimesToNaturalDuration`,把超過音樂總長度的開始/結束時間夾回音樂總長度,並寫回 `AudioMapRow` 對應欄位。

### 快捷鍵

| 按鍵 | 功能 |
|---|---|
| Enter(游標在功能代號或音效檔名稱等欄位時) | 在目前列後面插入一筆空白列,游標移到新列的功能代號欄位 |
| Ctrl+S | 存檔 |
| Ctrl+Z | 復原 |
| Ctrl+Y | 取消復原 |

## 檔案格式

存檔輸出/開啟讀取都是 ini 格式,UTF-8 編碼,每筆資料(每個功能代號)一個 Section,底下固定是 `FileName`/`SStart`/`SEnd`/`CStart`/`CEnd`/`MaxS` 六個欄位(因為四個頁簽是同一份資料,只有一份檔案,不分頁簽):

```ini
[123]
FileName=click.wav
SStart=00:00:00:0000
SEnd=00:00:05:0000
CStart=00:00:05:0000
CEnd=00:00:30:0000
MaxS=0.80

[456]
FileName=beep.wav
SStart=
SEnd=
CStart=
CEnd=
MaxS=
```

- `[功能代號]` 為 Section 名稱
- `FileName` 只存檔名+副檔名,**不含資料夾路徑**——資料夾是每台電腦各自在工具列選的,不會寫進這個檔案,所以同一份檔案換電腦開也能用
- `SStart`/`SEnd`/`CStart`/`CEnd` 固定是「時:分:秒:碼」格式(`HH:mm:ss:ffff`);`MaxS` 固定是 0~1 的數字
- 六個欄位固定都會寫出,即使是空字串(例如尚未在「開場片段」頁簽填資料)
- 功能代號空白的列存檔時會被自動略過
- 全部頁簽共用同一個檔案、同一份復原/取消復原歷史,不分頁簽各自存檔

## 專案結構

```
AudioMapTool/
├── AudioMapTool.sln
└── AudioMapTool/
    ├── App.xaml / App.xaml.cs                    # 應用程式進入點;共用樣式資源(DataGrid 儲存格/TextBox/CheckBox/TabItem 外觀)、反灰用的轉換器資源
    ├── MainWindow.xaml / .xaml.cs                 # 主畫面(選單、工具列、頁簽容器 TabControl、底部兩條播放進度條),擁有唯一一份共用資料、復原/取消復原堆疊
    ├── Fragment/
    │   ├── PathMapControl.xaml / .xaml.cs         # 「路徑」頁簽:選/功能代號/音效檔名稱(Code/FileName),另有 FolderPath 依附屬性
    │   ├── OpeningSegmentControl.xaml / .xaml.cs  # 「開場片段」頁簽:選/功能代號/開始/結束時間(Code/SStart/SEnd)
    │   ├── LoopSegmentControl.xaml / .xaml.cs     # 「循環片段」頁簽:選/功能代號/開始/結束時間(Code/CStart/CEnd)
    │   └── MaxVolumeControl.xaml / .xaml.cs       # 「最大音量」頁簽:選/功能代號/音量/開始/停止/暫停(Code/MaxS)
    ├── Models/
    │   ├── AudioMapRow.cs                        # 一筆資料:Code/FileName/SStart/SEnd/CStart/CEnd/MaxS/IsSelected,四個頁簽共用同一份物件;另有播放狀態與一系列畫面用的計算屬性
    │   └── RowPlaybackState.cs                   # 列的播放狀態列舉:None/Playing/Paused
    ├── Services/
    │   └── PlaybackController.cs                 # 「開始/暫停/停止」背後的播放引擎(包裝 MediaPlayer),開場片段播一次接循環片段反覆播放,提供暫停中拖曳底部進度條用的 Seek 方法,並用待命播放器提前預先開啟下一個可能要播的檔案
    ├── Converters/
    │   ├── NullOrEmptyToBooleanConverter.cs      # 字串是否為空 → 布林值,「資料夾」未選時停用音效檔名稱欄
    │   └── VolumeStringConverter.cs              # 音量欄字串 <-> 0~1 的 double 互轉,給 Slider.Value 用
    └── Utilities/
        └── TimecodeFormat.cs                     # 「時:分:秒:碼」(HH:mm:ss:ffff)格式規則與剖析,開場/循環片段的開始/結束時間、播放定位共用
```

### MainWindow(選單/工具列/頁簽容器,擁有唯一一份資料)

`TabControl`(`x:Name="tabMain"`)裡有 4 個 `TabItem`,各掛一個對應的 Fragment UserControl(`pathMapControl`/`openingSegmentControl`/`loopSegmentControl`/`maxVolumeControl`)。

`MainWindow` 自己持有唯一一份 `ObservableCollection<AudioMapRow> _items`,建立視窗時把同一個實體指定給四個頁簽控制項的 `Items` 屬性,所以四個 DataGrid 顯示的是同一份資料——在任一頁簽新增/刪除/編輯一列,其他頁簽會立刻同步。因為只有一份資料,`_activeItem`(目前作用列)、`_currentFilePath`(目前檔案)、復原/取消復原堆疊(`_undoStack`/`_redoStack`)也都只有一份,不分頁簽。

`MainWindow.xaml.cs` 依功能分成以下幾個 `#region`:

- 變數與初始化:建立 `_items`,指定給四個頁簽控制項的 `Items`;訂閱每個控制項的 `ActiveItemChanged`(更新 `_activeItem`)與 `UndoSnapshotRequested`(推進復原堆疊)事件,以及 `maxVolumeControl` 的 `PlayRequested`/`PauseRequested`/`StopRequested`(轉呼叫 `_playback`,見「播放」);`BtnBrowseFolder_Click` 用 `System.Windows.Forms.FolderBrowserDialog` 選音效檔資料夾;`SetFolder(folder)` 統一更新 `txtFolder.Text` 跟 `pathMapControl.FolderPath`,選資料夾按鈕、開啟 `.ini` 檔(見下)都是呼叫這個方法;`LockOtherRows`/`UnlockAllRows`/`SetGlobalControlsEnabled` 是播放期間鎖定其他列跟選單/工具列用的
- 全域快捷鍵(`Window_KeyDown` 一開始先檢查 `mainMenu.IsEnabled`,播放/暫停期間選單被鎖住時 Ctrl+S/Z/Y 也一併停用)
- 復原/取消復原:`CloneItems()`/`ApplySnapshot()`/`PushUndoSnapshot()`/`Undo()`/`Redo()`,跟資料一樣只有一份,不分頁簽
- 選單按鈕(檔案:新增/開啟/存檔/另存新檔/關閉,一律操作 `_items`;存檔/開啟固定讀寫 `FileName`/`SStart`/`SEnd`/`CStart`/`CEnd`/`MaxS` 六個欄位,「資料夾」不存進檔案;`DoOpen()` 成功讀完 `.ini` 後會呼叫 `SetFolder(Path.GetDirectoryName(...))`,自動把「資料夾」帶成該 `.ini` 檔所在的資料夾)
- 工具列按鈕(全選/反選/新增/刪除/拷貝,一律操作 `_items`/`_activeItem`)

### Fragment 底下的頁簽 UserControl

四個 UserControl 都是「瘦」的 DataGrid 視圖,不各自持有資料、不各自管理復原堆疊:

- `Items`:外部(`MainWindow`)指定的共用 `ObservableCollection<AudioMapRow>`,直接綁給內部 DataGrid。
- `ActiveItemChanged` 事件:儲存格取得焦點時觸發,回報該列資料。
- `UndoSnapshotRequested` 事件:儲存格編輯真的造成異動時觸發,帶出異動前的整份快照,由 `MainWindow` 記錄進復原堆疊。

各自的欄位結構、DataGrid 定義不共用(`OpeningSegmentControl`/`LoopSegmentControl` 欄位結構相同但綁定不同屬性,所以還是各自一個類別),只共用 `App.xaml` 的儲存格樣式跟上面兩個事件的介面形狀。目前作用列追蹤、功能代號重複驗證、Enter 插入空白列都是各自實作。

四個頁簽的 `RowStyle`/個別欄位 `IsEnabled` 都改綁 `AudioMapRow` 上的計算屬性(取代原本單純看 `FileName` 是否為空的 `NullOrEmptyToBooleanConverter` 寫法),同時處理「音效檔名稱是否為空」跟「播放狀態」兩種鎖定來源(詳見上方「播放」段落的狀態表):`PathMapControl` 用 `IsPathTabRowEnabled`;`OpeningSegmentControl`/`LoopSegmentControl` 用 `IsSegmentTabRowEnabled`(RowStyle)+ `IsEditableWhenIdle`(選/功能代號欄,暫停時仍要鎖住);`MaxVolumeControl` 用 `IsMaxVolumeTabRowEnabled`(RowStyle,不看自己的播放狀態,因為要留按鈕能點跟音量滑桿能拖)+ `IsEditableWhenIdle`(只有選/功能代號欄,音量滑桿刻意不綁這個,播放/暫停中都能即時調)+ `CanPlay`/`CanPause`/`CanStop`(三個按鈕各自)。`DataGridCellCheckBoxStyle`(`App.xaml`,四個頁簽「選」欄共用)也直接綁了 `IsEditableWhenIdle`。

`MaxVolumeControl` 的「開始」「暫停」「停止」按鈕(`BtnStart_Click`/`BtnPause_Click`/`BtnStop_Click`)只把使用者意圖包成 `PlayRequested`/`PauseRequested`/`StopRequested` 事件往外報,不直接播放——因為鎖定其他列、鎖定選單/工具列牽涉到 `_items` 全體跟 `MainWindow` 的其他控件,不是這個 UserControl 自己管得到的範圍,實際播放/鎖定邏輯在 `MainWindow` 配合 `PlaybackController` 處理(見上方「播放」)。

`PathMapControl` 另外有:

- `FolderPath`:`DependencyProperty`,由 `MainWindow` 在使用者選好「資料夾」後指定。「音效檔名稱」欄的 TextBox 跟「...」按鈕都把 `IsEnabled` 綁到 `{Binding FolderPath, RelativeSource={RelativeSource AncestorType=UserControl}, Converter={StaticResource NullOrEmptyToBooleanConverter}}`,`FolderPath` 是空的時候整欄不能用。
- 音效檔查詢按鈕(`BtnBrowseAudio_Click`):跳出 `OpenFileDialog`,預設定位到 `FolderPath`(如果該列已有 `FileName` 且檔案存在於 `FolderPath` 下,會直接定位到那個檔案);選好後只取 `Path.GetFileName(...)`(檔名+副檔名)寫回 `FileName`,不存完整路徑。

未來要新增頁簽,可以在 `Fragment` 資料夾底下新增一個新的 UserControl(公開 `Items`/`ActiveItemChanged`/`UndoSnapshotRequested`,比照現有四個),需要新欄位就加進 `Models/AudioMapRow.cs`,再到 `MainWindow.xaml` 的 `TabControl` 底下加一個 `TabItem`,`MainWindow.xaml.cs` 的 `_items`/復原機制不需要跟著修改。
