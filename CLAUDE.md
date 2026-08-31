# 專案：台灣 16 張麻將單機原型

接案用的展示原型，兩週內要交出可線上試玩的網頁版麻將。
目的是向客戶證明規則引擎能力，不是完整產品。

- Unity 2022 LTS，目標平台 WebGL
- 部署至 Firebase Hosting
- 開發者主力語言 C#，其他語言依賴 AI 協助

---

## 工作流程規範

- **模型分工（省 token，預設執行、不需使用者每次交代）**：主模型（Fable）只負責「規劃任務規格」與「最終驗收」（讀 diff、核對引用、跑編譯/自測、抓邏輯洞）；**實際程式修改一律用 Agent tool 交給 Opus 5 子代理執行**（subagent_type: general-purpose, model: opus，規格需寫明檔案錨點與專案規範）。例外：改動極小（幾行內）或任務特別複雜高風險（規則引擎核心判定、需要主對話大量上下文的除錯）才由主模型直接改。**若當前主模型本身即為 Opus 5，則不需分工，規劃、實作、驗收全部由 Opus 5 從頭做到尾。**
- **完成修改程式後，務必幫使用者 commit 到 GitHub**（除非使用者明確表示不需要）
- 請用繁體中文與使用者對話
- **每次修改程式後，將本次變更補充至 `docs/CHANGELOG.md`**，格式包含：問題描述、根本原因、修改的檔案與內容；此檔案透過 git 同步，供多台電腦查閱歷史紀錄

### .gitignore（Unity 專案專用，務必確認）

```
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Uu]ser[Ss]ettings/
*.csproj
*.sln
*.user
*.unityproj
.vs/
.idea/
*.pidb
*.booproj
sysinfo.txt
.env
```

**Library/ 一定要排除**——Unity 會在裡面塞數 GB 的快取，誤入版控會讓 repo 直接爆掉。
WebGL 的 Build 輸出同理，不進 git，部署時另行處理。

### 依賴管理

Unity 套件透過 Package Manager 管理，變更會反映在 `Packages/manifest.json`，該檔案要進版控。
**不要引入外部 NuGet 套件或 DLL**——WebGL 相容性風險高，原型階段一律用 Unity 內建功能解決。

---

## 📐 核心架構原則

> 適用所有專案的通用設計準則，與程式語言無關。新功能請遵照執行；既有程式碼在不破壞功能的前提下逐步靠攏。

### 1. 職責分離 (Separation of Concerns)
UI 層只負責顯示與事件捕捉，不寫商業邏輯。核心邏輯模組不依賴特定框架或介面，確保未來可獨立測試或替換前端。

**本專案的具體落實**：`Assets/Scripts/Core/` 底下禁止 `using UnityEngine`。
這些邏輯之後要搬到伺服器端做權威判定，必須是純 C#。
- 隨機數用 `System.Random`，不用 `UnityEngine.Random`
- 不使用 `Debug.Log`，需要輸出改用回傳值或事件
- 不使用 `MonoBehaviour`、`Coroutine`、`Vector3` 等 Unity 型別

### 2. 型別先行 (Type-First)
新增跨模組傳遞的資料結構前，先用該語言對應的型別工具明確定義（C# 用 class / struct / enum），再寫邏輯。函式簽名標注輸入輸出型別。

**本專案的具體落實**：動作、牌型、台數項目一律用 enum，不用 magic string。

### 3. 配置驅動，避免魔術數字 (Data-Driven)
業務參數、規則數值不寫死在邏輯中，集中放在設定檔或透過參數注入。調整規則只改設定，不動核心程式碼。

**本專案的具體落實**：台數一律透過 `FanTable` 設定，程式中不得出現台數數字。
客戶各地規則差異極大，這是報價與合約的核心論點——「台數表列出的照做，沒列的不實作」，
架構上必須真的做得到。

### 4. 最小化可變共享狀態 (Minimize Shared Mutable State)
計算類輔助函式盡量寫成純函式（輸入 → 輸出，無副作用）。多執行緒或非同步場景下，避免直接讀寫共用變數，優先用訊息傳遞（callback、queue）溝通。

**本專案的具體落實**：`WinChecker` 的方法雖然會暫時改動傳入的計數陣列，但結束時必須完整還原。
AI 模擬一律用 `GameState.Clone()`，不得直接改動真實局面。
**WebGL 不支援多執行緒**，非同步一律用 coroutine，且只能出現在 View 層。

---

## 🛠 程式碼品質要求

- **異常處理：** API 呼叫與 I/O 操作必須有錯誤捕捉，錯誤訊息需說明發生位置與原因，不可靜默崩潰。
- **命名語意化：** 變數與函式名稱清楚表達意圖，避免 `data`、`tmp`、`x` 等無意義命名。
- **函式單一職責：** 單一函式以 50 行為參考上限，過長時優先用子函式拆解職責。
- **測試同步：** 每新增一個 Core 類別，同時在 `MahjongTests.cs` 加對應測試。規則引擎的改動一律以測試全綠為驗收標準。

---

## 本專案專屬硬性規則

1. **不要修改 MahjongCore.cs 的公開 API**
   `TileDef`、`WinChecker`、`Meld`、`Wall`、`HandPattern`、`SetInfo` 的簽章已固定並經過驗證。
   需要新功能請新增檔案或新增方法，不要改既有簽章。

2. **畫面全部用程式碼建立，不依賴 Inspector**
   場景裡什麼都不用放——`Bootstrap` 以 `RuntimeInitializeOnLoadMethod` 自動啟動，
   自己生出攝影機、燈光、3D 牌桌與 2D 介面。
   所有 Canvas、Button、Text 與 3D 物件都用 `new GameObject()` 在程式中生成。
   不要要求使用者到 Unity 編輯器拖拉引用或建立 Prefab。

3. **牌用 3D 物件，材質與貼圖全部程式生成，不使用外部圖片素材**
   一張牌 = 縮放過的立方體（白色象牙牌身）+ 正反兩片面片（牌面／綠色牌背）。
   全部封裝在 `Assets/Scripts/View/Board/`：
   - `TileAssets.cs` 尺寸、共用材質、牌面貼圖
   - `TileObject.cs` 單張牌
   - `BoardLayout.cs` 世界座標配置
   - `TableBoard.cs` 整張牌桌

   **牌面貼圖在開場先烘焙**：Unity 內建的 3D 文字（`TextMesh`）用 GUI/Text Shader，
   那支著色器寫死 `ZTest Always`，文字會穿透擋在前面的物件顯示。
   所以開場用一台離屏攝影機把 42 種牌面各拍成一張貼圖，之後就是普通 3D 材質，深度排序正確。

   外部麻將牌素材多為 CC BY-SA 授權，商業營運有 share-alike 風險，一律不採用。

   > 早期版本是用 uGUI 圖層疊出偽立體的 2D 牌，堆到後來仍不像實體牌，已於 2026-08-31 改為 3D。

4. **WebGL 限制**
   不使用 `System.IO`、不使用多執行緒、不使用 `System.Net`。
   另：材質是執行期用 `Shader.Find("Standard")` 建的，
   **WebGL 建置前必須把 Standard 加入 Project Settings → Graphics → Always Included Shaders**，
   否則著色器會被剝離，牌會變成紫色。

5. **範圍控制（重要）**
   本階段是免費原型，時間上限 40 小時。
   **不做**：連線對戰、帳號系統、賽制管理、金流、精緻美術、手機直式版面。
   若使用者要求超出此範圍的功能，先提醒這屬於付費階段項目，確認後再動手。

---

### 規則引擎測試

改動 `Assets/Scripts/Core/` 後，**不需開 Unity** 即可驗證：

```
cd tools/CoreTests && dotnet run
```

全綠才算通過。`tools/` 在 `Assets/` 之外，Unity 不會編譯到，不影響 WebGL 建置。

---

## 規則備忘（台灣 16 張）

- 144 張：數牌字牌各 4 張，花牌 8 張各 1 張
- 莊家 17 張，閒家 16 張
- 胡牌型：5 組面子 + 1 對將
- 摸到花牌自動補花，從牌山尾端補
- 吃只能下家；優先權：胡 > 碰/槓 > 吃
- 多家可胡時，逆時針方向離打牌者近者優先
- 七對子預設不成立（除非客戶台數表另訂）

---

## 目前進度

- [x] MahjongCore.cs（牌定義、胡牌判定、聽牌、牌山）
- [x] ScoreCalculator.cs（台數計算）
- [x] MahjongTests.cs（單元測試，104 項全綠）
- [x] 規則定案（見 `docs/RULES.md`）
- [x] Unity 2022.3.22f1 專案骨架 + WebGL 建置目標
- [x] GameState.cs
- [x] TurnEngine.cs
- [x] ShantenCalculator.cs / AIPlayer.cs
- [x] 3D 牌桌 `View/Board/`（TileAssets / TileObject / BoardLayout / TableBoard）
- [x] 2D 介面 Bootstrap.cs / ActionButtons.cs / ResultView.cs / AnnouncementView.cs
- [ ] 洗牌發牌與摸牌動畫（改 3D 後需重做）
- [ ] 中文字型 Assets/Resources/MahjongFont.ttf（WebGL 必需，見 docs/CHANGELOG.md）
- [ ] Standard 著色器加入 Always Included Shaders（WebGL 必需）
- [ ] WebGL build 與 Firebase 部署
