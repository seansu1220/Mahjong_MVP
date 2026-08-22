# 變更紀錄

本檔案透過 git 同步，供多台電腦查閱歷史紀錄。

---

## 2026-08-22　規則引擎稽核與修正

### 背景

專案接手時只有 `MahjongCore.cs`、`ScoreCalculator.cs`、`MahjongTests.cs` 三個檔案（皆由 AI 產出，未經實際執行驗證）。
以 dotnet 建立離線跑道實測後，發現 15 個測試中有 1 個失敗，且該失敗掩蓋了台數計算的多個錯誤。

---

### 一、測試本身錯誤，導致台數層從未被驗證

**問題描述**
`TestScoreQingyise` 的手牌只有 15 張，加胡牌張共 16 張。

**根本原因**
台灣 16 張胡牌需 17 張（5 組面子 × 3 + 將 2）。張數不符時 `AllPatterns` 拆不出任何牌型，
`Calculate` 直接回傳空結果，測試永遠失敗。這是 `ScoreCalculator` 唯一的測試，
等於整支台數計算從未被任何測試覆蓋——以下數個錯誤都是因此漏出。

**修改內容**
`Assets/Scripts/Tests/MahjongTests.cs`：改用合法的 17 張全萬子牌型
（123萬 123萬 456萬 789萬 789萬 + 5萬5萬），並補上「不應同時計混一色」的反向斷言。

---

### 二、放槍胡的刻子被誤判為暗刻（多送 8 台）

**問題描述**
放槍胡五組刻子時，結果出現「五暗刻 8 台」。

**根本原因**
`WinChecker.CollectSets` 將 `SetInfo.Concealed` 寫死為 `true`。
胡牌張是先併入計數陣列再拆解的，因此由別人打出的那張所組成的刻子也被當成暗刻。

**修改內容**
- `Assets/Scripts/Core/ScoreCalculator.cs`：新增 `MarkWinningSetAsExposed`，
  非自摸時把含胡牌張的手中面子標記為明。順子優先標記，刻子保留暗刻身分，
  與本類別「多種拆法取台數最高」的既有慣例一致。
- 同檔案：`allSets` 改為深拷貝。`p.Sets` 的 `SetInfo` 物件會被多種拆解方式共用同一個實例，
  直接改動會汙染其他拆法的結果。
- 補上 `TestRonDoesNotCountAsConcealedTriplet` 與 `TestSelfDrawCountsAsConcealedTriplet`。

---

### 三、連莊拉莊台數重複乘算

**問題描述**
連莊 2 次算出 6 台、3 次算出 10 台（實際公式為 4N-2）。

**根本原因**
`FanTable` 中 `LianZhuang` 的 `Value` 已設為 2，程式又寫 `ctx.DealerStreak * 2 - 1`，
2 被乘了兩次。此外 `* 2 - 1` 屬於寫死在邏輯中的台數數字，
違反 CLAUDE.md「台數一律透過 FanTable 設定，程式中不得出現台數數字」。

**修改內容**
`Assets/Scripts/Core/ScoreCalculator.cs`：改為 `Add(FanId.LianZhuang, ctx.DealerStreak)`，
台數值全部交由 `FanTable` 決定（預設 2 台／次，即連 N 拉 N = 2N 台）。
補上 `TestLianZhuangTai`，斷言以台數表設定值計算而非寫死數字。

---

### 四、聽牌未扣除已無剩餘的牌（死聽）

**問題描述**
自己暗槓四張九筒後仍單吊九筒，`GetWaits` 仍將九筒列為聽張，但世上已無第五張。

**根本原因**
`GetWaits(int[], int)` 只檢查 `hand[i] >= 4`，不知道副露與場上可見牌。

**修改內容**
`Assets/Scripts/Core/MahjongCore.cs`：新增多載
`GetWaits(int[] hand, List<Meld> melds, int[] visibleCounts = null)`，
扣除自己副露與場上可見牌後才回傳聽張。
原多載簽章保持不變（CLAUDE.md 硬性規則 1）。
`ScoreCalculator` 的獨聽判定改用新多載。補上 `TestWaitsExcludeExhaustedTiles`。

註：碰掉三張的情況第四張仍可胡，屬正常聽張，不在此修正範圍。

---

### 五、牌山缺少補花／槓後補牌機制

**問題描述**
`Wall` 只有 `Draw()`，無法從牌山尾端取牌。

**根本原因**
台灣麻將規則要求補花與槓後補牌從牌山尾端取。缺少此機制，`TurnEngine` 無法實作補花流程。

**修改內容**
`Assets/Scripts/Core/MahjongCore.cs`：`Wall` 新增 `tailIndex` 與 `DrawFromTail()`，
`Remaining` 改為 `tailIndex - drawIndex + 1`（簽章不變）。
補上 `TestWallTailDraw`，頭尾交替取完 144 張驗證不重複不遺漏。

---

### 六、`seed: 0` 無法重現牌局

**問題描述**
兩次 `new Wall(seed: 0)` 產生不同牌山，無法重現客戶回報的問題局面。

**根本原因**
`rng = seed == 0 ? new Random() : new Random(seed)`——0 是合法種子卻被當成「隨機」的哨兵值，
且 `new Random()` 為時間種子，短時間內連續建立會得到相同牌山。

**修改內容**
`Assets/Scripts/Core/MahjongCore.cs`：改由靜態 `SeedSource` 產生隨機種子，
並新增唯讀屬性 `Seed` 記錄實際使用的種子，把它傳回建構子即可完整重現同一副牌。
補上 `TestWallSeedReproducible`。

---

### 七、輸入無防禦，錯誤輸入直接索引越界

**問題描述**
`WinningTile` 傳入花牌 id 時拋出 `IndexOutOfRangeException`，訊息無法指出問題所在。
另 `CanWin` 有張數檢查而 `AllPatterns` 沒有，行為不一致且靜默回空。

**根本原因**
未依 CLAUDE.md「錯誤訊息需說明發生位置與原因，不可靜默崩潰」做輸入驗證。

**修改內容**
- `MahjongCore.cs`：新增 `WinChecker.ValidateCountArray`，`CanWin`／`AllPatterns`／`GetWaits` 皆先驗證；
  `AllPatterns` 補上與 `CanWin` 一致的 3n+2 張數前提。
- `ScoreCalculator.cs`：新增 `ValidateContext`，對 null、陣列長度、胡牌張範圍拋出說明清楚的 `ArgumentException`。
- 補上 `TestInvalidWinningTileThrows`。

---

### 八、規則差異項目改為設定驅動（待客戶確認）

**問題描述**
三處各地規則差異極大的判定被寫死在 `ScorePattern` 的 `else` 分支中，改規則必須動核心程式碼。

**根本原因**
違反 CLAUDE.md「調整規則只改設定，不動核心程式碼」。

**修改內容**
`Assets/Scripts/Core/ScoreCalculator.cs`：`FanTable` 新增三個開關，
**預設值一律維持原型既有行為，尚未做任何規則決定**：

| 設定 | 預設 | 影響 |
|---|---|---|
| `ZhengHuaStacksWithFlower` | `true` | 正花是否在「每花 1 台」之外再加計一次 |
| `WindTilesCountWithBigWinds` | `false` | 大四喜／小四喜時是否仍計門風、圈風 |
| `DragonTilesCountWithBigDragons` | `false` | 大三元／小三元時是否仍計三元牌 |

另修正「平胡」的註解——原註解宣稱檢查兩面聽，實作並未檢查，已改為據實說明並標註待補。

---

### 九、工程基礎建設

- 初始化 git 版控，加入 Unity 專用 `.gitignore`（`Library/` 等一律排除）。
- 檔案搬移至 Unity 標準位置：`Assets/Scripts/Core/`、`Assets/Scripts/Tests/`。
- 新增 `tools/CoreTests/`：不需開啟 Unity 即可執行規則引擎測試的離線跑道
  （`cd tools/CoreTests && dotnet run`）。此資料夾位於 `Assets/` 之外，Unity 不會編譯，不影響 WebGL 建置。
- `MahjongTests.RunAll()` 改為回傳 `bool`，離線跑道以離開碼回報成敗，便於日後接 CI。
- 移除 `Dictionary.GetValueOrDefault`（需 .NET Standard 2.1），改用 `TryGetValue`，
  避免 Unity API 相容性層級設為 2.0 時編譯失敗。

**測試結果：27 項全數通過（修正前為 15 項中 1 項失敗）。**

---

## 2026-08-22（第二次）　規則定案、Unity 骨架與 GameState

### 一、Unity 專案骨架納入版控

**修改內容**
Unity 2022.3.22f1 專案搬入，建置目標已設為 WebGL（`webGLThreadsSupport: 0`，符合不使用多執行緒的限制）。
`.gitignore` 補上 `.vsconfig`；另修正 Unity 的 `*.csproj` 排除規則會誤傷 `tools/CoreTests` 手寫專案檔的問題。
確認 `Library/`、`Temp/`、`Logs/`、`UserSettings/` 全數排除，納管 41 個檔案。
遠端設定為 https://github.com/seansu1220/Mahjong_MVP 。

---

### 二、規則定案並補實作「兩面聽」

**問題描述**
先前標記為「待客戶確認」的四項規則差異需要定案，其中平胡的兩面聽條件從未實作。

**根本原因**
原程式的平胡註解宣稱檢查兩面聽，實際只檢查了全順子、將非字牌、非自摸三項。

**修改內容**
- 新增 `docs/RULES.md`，完整記錄本專案採用的規則與台數表，可直接作為給客戶的規格書。
- `Assets/Scripts/Core/ScoreCalculator.cs`：新增 `IsTwoSidedWait`，
  依順子中胡牌張的位置判定兩面／邊張／嵌張，單吊與對倒自然不成立。
- `FanTable` 新增 `PinghuRequiresTwoSidedWait`（預設 `true`）。
- `FanTable.ZhengHuaStacksWithFlower` 預設改為 `false`（花牌一律每張 1 台，正花不另加）。
- 大牌不重複計小牌（`WindTilesCountWithBigWinds`、`DragonTilesCountWithBigDragons` 維持 `false`）。
- 補上 `TestPinghuRequiresTwoSidedWait`、`TestPinghuRejectsEdgeWait`、`TestFlowerAndZhengHuaNotDoubled`。

---

### 三、新增 GameState.cs（牌局現況）

**修改內容**
新增 `Assets/Scripts/Core/GameState.cs`，純 C#，不含任何 UnityEngine 依賴。職責僅限「記住局面」與「純查詢」，流程推進留給 TurnEngine。

- `GamePhase`、`GameEndReason`：以 enum 定義局面階段與結束原因，不用 magic string。
- `PlayerState`：手牌計數陣列、副露、花牌、牌河，含 `ConcealedTileCount`、`IsConcealedHand`
  與會拋出明確錯誤的 `AddTile` / `RemoveTile`。
- `GameState.CreateNewHand`：發牌（莊家 17、閒家 16）並依莊家順序補花，
  補花走牌山尾端且會重複補到手上沒有花為止。
- `DrawTile` / `DrawReplacementTile`：摸牌與補牌，牌山抽乾回傳 `NoTile` 而非拋例外，
  因為流局是合法的牌局狀態，該由 TurnEngine 判定。
- `NextSeat` / `SeatDistance` / `IsNextSeatOf`：純函式，供「吃只能下家」與
  「多家可胡時逆時針近者優先」使用。
- `BuildVisibleCounts`：彙整牌河與副露，供 AI 與聽牌計算扣除已摸不到的牌。
- `Clone`：深拷貝，且刻意不複製 `Wall`（AI 模擬不應偷看牌山）。
- `MahjongCore.cs` 的 `Meld` 新增 `Clone()`，供深拷貝使用（僅新增方法，未改既有簽章）。

**新增測試**
發牌張數、補花補滿（連測 30 副牌）、總牌數守恆 144、門風排列、座位輔助函式、
`Clone` 深拷貝且不含牌山。

**測試結果：41 項全數通過。**
