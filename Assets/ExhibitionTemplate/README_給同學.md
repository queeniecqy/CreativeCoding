# 展覽模板使用說明

場景：`Assets/ExhibitionTemplate/ExhibitionTemplate.unity`

## 場景結構

```
exhibition
├─ group1 / group2 / group3     ← 三個組，一次只會顯示一組
│   ├─ 主視覺                    ← 該組的主視覺
│   │   ├─ 展板
│   │   └─ 說明牌
│   ├─ 作品A
│   │   ├─ 展板                  ← 放作品圖（有些作品有「展板 2」＝背面）
│   │   └─ 說明牌                ← 放作品說明
│   ├─ 作品B ...
└─ posts / 1, 2, 3              ← 切換組別的海報（抓取即切換到 Group N）
```

## 放入你的作品（推薦：用上傳網頁）

助教在 Unity 開啟 **Tools → 展覽模板 → 作品上傳網頁伺服器**，按「啟動」後會顯示網址。

1. 用瀏覽器（同一個 Wi-Fi 的手機或筆電也可以）打開助教給的網址。
2. 選你的組別 → 點你的作品位置（或「＋ 新增作品位置」）。
3. 填作品名稱、作者、說明，拖入作品圖片或影片（影片請用 H.264 MP4，Quest 才能播）。
4. 說明牌可以「依文字自動產生」，或上傳自己設計的圖。
5. 按「儲存到 Unity」：Unity 會自動匯入檔案、套到展板、依比例調整大小並存檔。

檔案會放在 `Assets/Works/groupN/作品X/`；作品資訊記錄在作品物件的 **Exhibit Work** 元件上。

## 手動放入作品（換貼圖）

1. 把圖片拖進 `Assets/` 裡你自己的資料夾（例如 `Assets/Works/group1_作品A/`）。
2. 打開 `Assets/ExhibitionTemplate/Materials/groupN/`，找到 `groupN_作品X_展板.mat`。
3. 在 Inspector 把圖片拖到 **Base Map** 的小方格，並把 Base Map 旁的顏色改回白色。
4. 說明牌同理，改 `groupN_作品X_說明牌.mat`。
   - 說明牌圖片若是去背 PNG，材質的 Surface Type 保持 **Transparent** 即可。

> 每件作品有自己的材質，改你的不會影響到別人。
> 同一件作品的正反兩面（展板 / 展板 2）共用同一個材質。

## 新增一件作品

1. 在 Hierarchy 選取要加入的組（`group1` / `group2` / `group3`）。
2. 把 Scene 視窗轉到想放作品的位置。
3. 選單 **Tools → 展覽模板 → 新增作品到選取的 group**。
   會在畫面中央建立下一個編號的 `作品X`（展板 + 說明牌），並在
   `Materials/groupN/` 產生它專屬的兩個材質。
4. 移動到牆上的位置，再照上面的步驟換貼圖。

## 圖片比例

展板大小由物件的 **Scale X / Y** 決定。若你的圖片比例不同，請調整 `展板` 的 Scale X、Y
（Z 保持很薄不要動），例如 16:9 的圖 → X : Y = 1.6 : 0.9。

## 其他類型作品

- **影片**：在 `展板` 上 Add Component → Video Player，Render Mode 選 *Material Override*，
  Renderer 指向這塊展板。
- **3D 模型**：把模型放到 `作品X` 底下即可，可以把展板關掉或刪掉。
- **粒子特效**：在展板上 Add Component → *Artwork Particles*，會沿著畫框飄出光點。

## 注意

- 請不要改 `group1~3`、`posts`、`switchGroup`、`GrabTriggerControllerG1~3` 的名稱與結構，
  組別切換靠它們運作。
- 換海報文字：選 `posts/N/海報文字 (正面)`／`(背面)`，在 TextMeshPro 元件改字。
