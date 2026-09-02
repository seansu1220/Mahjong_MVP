# 第三方素材與授權

本專案的牌張、牌面、材質與所有介面圖形皆為程式生成，未使用任何外部圖片素材。
唯一的第三方素材是中文字型。

---

## Noto Sans TC

| | |
|---|---|
| 檔案 | `Assets/Resources/MahjongFont.ttf` |
| 來源 | [google/fonts](https://github.com/google/fonts/tree/main/ofl/notosanstc) |
| 授權 | SIL Open Font License 1.1（全文見 `docs/licenses/OFL.txt`） |
| 商業使用 | 允許 |
| 散布義務 | 隨產品散布時需附上授權全文；不得單獨販售字型；不得使用保留字型名稱 |

### 為什麼要內嵌字型

WebGL 跑在瀏覽器沙箱裡，**取不到作業系統的字型**。
開發時 `UiFont` 會退回系統的微軟正黑體，那是本機才有的東西；
建置成 WebGL 之後如果專案裡沒有字型，中文會全部變成空白方框。

### 為什麼不直接內嵌微軟正黑體

微軟正黑體隨 Windows 授權，**不得重新散布**。
把它包進要對外營運的網頁裡會構成侵權，因此改用 SIL OFL 授權的 Noto Sans TC。

### 檔案怎麼來的

原始檔是 11.9 MB 的可變字型，直接內嵌會讓 WebGL 建置肥一大圈。處理方式：

1. 用 `fontTools.varLib.instancer` 把可變軸定格在 Regular（wght=400）→ 7.1 MB
2. 用 `fontTools.subset` 只留下遊戲實際會用到的字元 → **174 KB**

字元清單是掃描 `Assets/Scripts/` 底下所有 `.cs` 的字串常值取出的，
共 483 個字元（含 ASCII）。**日後若新增中文字串，必須重新產生子集字型**，
否則新字會顯示不出來。重新產生的步驟記在本檔末。

### 重新產生子集字型

```bash
# 1. 取得原始字型（SIL OFL）
curl -L -o NotoSansTC.ttf   "https://raw.githubusercontent.com/google/fonts/main/ofl/notosanstc/NotoSansTC%5Bwght%5D.ttf"

# 2. 定格成 Regular
python -m fontTools.varLib.instancer NotoSansTC.ttf wght=400 -o NotoSansTC-400.ttf

# 3. 取出專案用到的字元後子集化
#    charset.txt = Assets/Scripts 底下所有 .cs 字串常值中的字元
python -m fontTools.subset NotoSansTC-400.ttf --text-file=charset.txt   --layout-features='*' --name-IDs='*'   --output-file=Assets/Resources/MahjongFont.ttf
```

需要 `pip install fonttools`。
