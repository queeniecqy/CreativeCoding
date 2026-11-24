# Creative Coding VR 展覽模板

這是一個在 **Meta Quest** 上觀看的 VR／MR 展場。展場分成 **Group 1、Group 2、Group 3** 三組，
一次只會顯示一組；觀眾抓取展場中的三張「Group N」海報就能切換組別。

每一組都有幾個空白的作品位置（`作品A`、`作品B`…），每個位置有一塊 **展板**（放作品）和一塊 **說明牌**（放作品介紹）。
這份說明會告訴你怎麼把自己的作品放進去。

> 🏛 **展場實景參考**：[`docs/venue/index.html`](docs/venue/index.html) 是展覽實際場地的環景檢視器，
> 可以拖曳環顧、縮放，設計作品配置時參考牆面、窗戶與柱子的位置。
> GitHub 不會直接顯示 HTML，請下載 repo 後**雙擊開啟**這個檔案（不需網路）。
> 原始照片：[`docs/venue/venue-panorama.jpg`](docs/venue/venue-panorama.jpg)
>
> 這是手機拍的全景，水平約 315°，最後約 45° 沒有拍到（轉到那裡會顯示「此方向未拍攝」）；天花板正上方與地板正下方也沒有拍到。

---

## 目錄

1. [準備工具](#1-準備工具)
2. [下載專案並打開展場](#2-下載專案並打開展場)
3. [認識展場結構](#3-認識展場結構)
4. [放入你的作品](#4-放入你的作品)
   - [方法 A：上傳網頁（推薦）](#方法-a上傳網頁推薦)
   - [方法 B：在 Unity 裡手動放](#方法-b在-unity-裡手動放)
5. [把作品交回 GitHub](#5-把作品交回-github)
6. [在 Quest 上測試](#6-在-quest-上測試)
7. [檔案規格與常見問題](#7-檔案規格與常見問題)

---

## 1. 準備工具

| 工具 | 說明 |
|---|---|
| **Unity Hub** + **Unity 6000.0.49f1** | 版本請完全一致。安裝時勾選 **Android Build Support**（含 OpenJDK、Android SDK & NDK） |
| **Git** | 下載 / 上傳專案用。不熟指令可以用 [GitHub Desktop](https://desktop.github.com/) |
| GitHub 帳號 | 請把帳號告訴助教，助教會把你加入這個 repo 的協作者 |

> 只在展覽現場用「上傳網頁」交作品的同學，**不需要**安裝 Unity 與 Git，有瀏覽器就可以。

## 2. 下載專案並打開展場

```bash
git clone https://github.com/queeniecqy/CreativeCoding.git
```

1. 打開 Unity Hub → **Add** → 選剛下載的 `CreativeCoding` 資料夾。
2. 第一次開啟會花幾分鐘重建 `Library`，請耐心等候。
3. 打開場景：`Assets/ExhibitionTemplate/ExhibitionTemplate.unity`。

## 3. 認識展場結構

```
exhibition
├─ group1 / group2 / group3        ← 三個組別（一次只顯示一組）
│   ├─ 主視覺                       ← 該組的主視覺海報
│   ├─ 作品A
│   │   ├─ 展板                     ← 你的作品（圖片或影片）
│   │   ├─ 展板 2                   ← 部分位置有背面
│   │   └─ 說明牌                   ← 作品名稱、作者、說明
│   └─ 作品B …
└─ posts / 1, 2, 3                 ← 切換組別的海報
```

- 每個作品都有**自己專屬的材質**，放在 `Assets/ExhibitionTemplate/Materials/groupN/`，改你的不會影響別人。
- 你上傳的檔案會放在 `Assets/Works/groupN/作品X/`。
- 每件作品物件上有 **Exhibit Work** 元件，記錄作品名稱、作者與說明。

> ⚠️ 請不要改動 `group1~3`、`posts`、`switchGroup`、`GrabTriggerControllerG1~3` 的名稱或結構，切換組別的功能依賴它們。

---

## 4. 放入你的作品

先跟助教確認你分到的是 **哪一組、哪個作品位置**（例如 Group 2 的作品C）。

### 方法 A：上傳網頁（推薦）

上傳網頁會**直接修改 Unity 專案**：匯入檔案、套用到展板、依比例調整展板大小並存檔。

**在展覽現場（助教的電腦）**

1. 助教在 Unity 開啟 **Tools → 展覽模板 → 作品上傳網頁伺服器**，按「▶ 啟動伺服器」。
2. 助教會給你一個網址（例如 `http://192.168.x.x:8787/`）。
3. 用手機或筆電（要連同一個 Wi-Fi）打開網址。

**在自己的電腦**

1. 用 Unity 打開專案與 `ExhibitionTemplate.unity` 場景。
2. 開啟 **Tools → 展覽模板 → 作品上傳網頁伺服器** → 啟動 → 按 `http://localhost:8787/` 旁的「開啟」。

**網頁操作**

1. 點上方的組別（Group 1 / 2 / 3）。
2. 點你的作品位置；如果需要新的位置，點「＋ 新增作品位置」（之後請助教調整它在牆上的位置）。
3. 填寫 **作品名稱、作者、作品說明**。
4. 把作品的圖片或影片拖進「作品檔案」區。
5. 說明牌有兩種做法：
   - **依文字自動產生**：網頁會用你填的文字排版成說明牌，右邊可以即時預覽。
   - **上傳自己的圖**：上傳你自己設計的說明牌圖片。
6. 按 **「儲存到 Unity」**，看到「已更新到 Unity ✔」就完成了。

> 上傳時 Unity 不能在 Play 模式。如果網頁顯示錯誤，請確認 Unity 開著展場場景，並且已經按停止 Play。

### 方法 B：在 Unity 裡手動放

1. 把圖片拖進 `Assets/Works/groupN/作品X/`（資料夾不存在就自己建立）。
2. 到 `Assets/ExhibitionTemplate/Materials/groupN/` 找到 `groupN_作品X_展板.mat`。
3. 在 Inspector 把圖片拖到 **Base Map** 的小方格，並把旁邊的顏色設成白色。
4. 說明牌同理，改 `groupN_作品X_說明牌.mat`。
5. 如果圖片比例和展板不同，選 `展板`，調整 **Scale X / Y**（Z 不要動）。例如 16:9 的作品可以設 X = 1.6、Y = 0.9。
6. **影片**：在 `展板` 上 **Add Component → Video Player**，Source 選你的影片，Render Mode 選 **Material Override**，Renderer 指向這塊展板，Material Property 選 `_BaseMap`。
7. **3D 作品**：把模型拖到你的 `作品X` 底下，不需要的話可以關掉展板。
8. 在 Hierarchy 選取某一個 group，再用 **Tools → 展覽模板 → 新增作品到選取的 group**，可以新增一個空白作品位置。

做完記得 **Ctrl + S** 存檔。

---

## 5. 把作品交回 GitHub

很多人會同時修改同一個場景，請照下面的步驟，以減少衝突：

```bash
# 1. 開始前先取得最新版本
git checkout main
git pull

# 2. 開一個自己的分支（名稱：組別-作品位置-姓名）
git checkout -b group2-作品C-王小明

# 3. 在 Unity 放入作品並存檔（見第 4 節）

# 4. 提交並上傳
git add -A
git commit -m "group2 作品C：作品名稱（王小明）"
git push -u origin group2-作品C-王小明
```

5. 到 GitHub 頁面按 **Compare & pull request**，送出 Pull Request，助教確認後會合併。

**請注意**

- 只修改**你自己的作品位置**，不要移動或刪除別人的物件。
- 一件作品送一個 Pull Request。
- `git add` 之前可以用 `git status` 檢查：應該只有你的 `Assets/Works/...` 檔案、你的材質，以及 `ExhibitionTemplate.unity`。
- 如果出現 merge conflict，不要自己硬改場景檔，請找助教處理。

---

## 6. 在 Quest 上測試

1. **File → Build Profiles**，平台選 **Android** 並切換（Switch Platform）。
2. 確認 Scene List 第一個是 `Assets/ExhibitionTemplate/ExhibitionTemplate.unity`。
3. Quest 開啟開發者模式並用 USB 接上電腦 → **Build And Run**。
4. 在頭顯中抓取「Group N」海報，切換到你的組別檢查作品。

## 7. 檔案規格與常見問題

| 項目 | 建議 |
|---|---|
| 圖片 | PNG / JPG，長邊 ≤ 4096 px（Unity 會自動壓到 2048） |
| 影片 | **H.264 MP4**、1080p 以下、**小於 100 MB**（GitHub 單一檔案上限）。MOV、WEBM 在 Quest 可能無法播放 |
| 說明牌 | 去背 PNG 也可以，說明牌材質預設為透明模式 |
| 檔名 | 上傳網頁會自動命名為 `展板.png`、`說明牌.png`，不用自己改 |

**Q：上傳網頁會不會改到別人的作品？**
A：網頁沒有帳號或密碼，知道網址的人都可以修改任何作品位置。請只點自己的位置，「清空這個作品」也只能用在自己的作品上。

**Q：手機打不開助教給的網址？**
A：確認手機和助教的電腦連同一個 Wi-Fi。助教第一次啟動伺服器時，Windows 防火牆會詢問 Unity 能不能連網，要按「允許」。

**Q：影片在 Unity 裡看不到畫面？**
A：Material Override 的影片只會在 Play 模式或 Quest 上播放，編輯模式下展板顯示白色是正常的。

**Q：我的作品比例跑掉了？**
A：上傳網頁預設會「依作品比例調整展板」，以原本展位的大小為上限。手動放的話，請調整展板的 Scale X / Y。

---

### 給助教

| 選單（Tools → 展覽模板） | 用途 |
|---|---|
| 作品上傳網頁伺服器 | 開啟上傳網頁，可設定 Port、是否允許區網連線、是否自動存檔。網頁沒有密碼保護，收完作品請停止伺服器 |
| 新增作品到選取的 group | 在 Scene 視窗中央新增一個空白作品位置 |
| 1. 預覽轉換計畫 / 2. 產生空白展覽模板場景 | 從舊的展場場景重新產生模板（一般不需要再執行） |
| 3. 清理與展覽無關的資源 | 把模板用不到的資源移到專案外的 `_unused_backup/` |

工具原始碼位於 `Assets/ExhibitionTemplate/Editor/`。
