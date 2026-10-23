# 從 fork 移植到 upstream 1.8.91 的盤點

分支：`port/upstream-1.8.91`，建立於 upstream `v1.8.91`（`48568751`）。
fork 的 `main` 是 upstream 1.6.2 加上 fork commit；共同祖先為 `8d85c867`。upstream 之後改用 `net10.0-windows`、自有的 `engine/KillerPdf.Engine`（取代 PdfSharpCore），並將根目錄的 partial class 拆到 `Features/`、`Shell/`、`Controls/`，所以改為逐項移植，不做 `git merge`。

fork 自有 commit：`f2bc1a7` CJK 與 Edge 相容、`781558a` 發佈 workflow、`9a1a92c` 專案狀態、`46f2081` 原生 WPF 手寫簽名、`ac1e426` 辦公流程與繁中介面、`35ad4d3` workflow 改用 Node.js 24、`3f459d9` 無書籤 PDF 存檔修正、`c2d0916` 新增文字工具可拖曳已放置內容。`8d85c867..main` 其餘 commit 是 upstream 1.6.1／1.6.2 的工作，1.8.91 已包含。

狀態欄：**已涵蓋** = upstream 已有同等功能；**部分** = upstream 有一部分；**無** = upstream 沒有。

## 繁中用語

| # | 功能 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 1 | fork 改過的 11 個既有用語：頁面上移／下移、螢光筆、手寫簽名、清除註解、匯出相容 PDF 副本、旋轉／縮放、頁碼與浮水印、憑證簽署…、使用簽名、列印頁碼提示 | 無（沿用舊譯） | 移植：保留 upstream 的 key，值改為 fork 用語 |
| 2 | 雙方都有、用語不同的 10 個 key（全形標點：連結確認、移除連結、列印進度等） | 部分 | 移植 fork 用語。例外：`Str_TT_DocInfo` 保留 upstream 的 F4（快捷鍵已改）；`Str_Print_Preparing` 保留 upstream 的 `{0}／{1}` 佔位符 |
| 3 | 全形標點與「註解」等用詞一致化（fork 新字串一律用全形「：？（）」） | 部分（upstream 譯文混用半形） | 移植：對 upstream zh-TW 做一次全形標點與用詞一致化，不更動佔位符、快捷鍵與檔案篩選格式 |
| 4 | 約 60 個 fork 新增的 key，用來在地化寫死的英文（簽署對話框、列印、文字編輯狀態列等） | 已涵蓋（upstream 用自己的 key 在地化同一批文字） | 不移植 key；只把 fork 用語套到 upstream 對應的 key |
| 5 | 依 Windows 顯示語言預設繁中 | 已涵蓋（`LocaleManager.LocaleForCulture`，含 zh-TW/HK/MO/Hant） | 不需移植 |

## 填寫與簽名

| # | 功能 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 6 | 插入日期：預設西元 `yyyy/MM/dd`，可勾選民國年 `115/10/03` | 無 | 移植 `OfficeFill`（日期對話框＋預設文字放置），加單元測試 |
| 7 | 插入 ✓ 勾選記號 | 無 | 移植（與日期共用放置流程） |
| 8 | 原生 `InkCanvas` 手寫簽名：保留觸控筆取樣、點一下也留點、固定筆寬、關閉長按／輕拂手勢 | 無（仍是滑鼠事件畫 `Polyline`） | 移植 `SignatureInkCanvas.cs`；`build/test-signature-ink.ps1` 改寫成 xUnit 測試 |
| 9 | 簽名視窗：復原上一筆、取消、較大畫布與按鈕 | 無 | 移植到 `Shell/Signing.cs` |
| 10 | 簽名預設不存到本機：勾選「儲存在這台電腦供下次使用」才保存；匯入圖片時詢問 | 無（一律保存） | 移植 |
| 11 | 「新增文字」工具：拖曳已放置的文字、日期、✓、簽名即可移動；雙擊重新編輯；只有點空白處才新增；編輯框上方的移動把手；單純點選不標記為未儲存 | 無（每次點擊都新增文字框） | 移植到 `Controls/Viewer/PdfViewer.Annotations.cs`、`PdfViewer.TextEditing.cs` |
| 12 | 注音等輸入法組字時，Enter／Esc／方向鍵交給輸入法，不會提前完成或取消編輯 | 無 | 移植（`TextCompositionManager` 事件） |
| 13 | Enter 換行、Ctrl+Enter 完成 | 已涵蓋（`EditableTextShortcutPolicy`） | 不需移植 |
| 14 | 保留文字前後空白（不 `Trim`） | 無 | 移植 |
| 15 | 表單：Tab 依版面由上而下、空白鍵／Enter 切換勾選與選項、唯讀欄位不可聚焦、「提示填表欄位」底色開關、依 `/Q` 對齊 | 無 | 移植到 `PdfViewer.Forms.cs` |
| 16 | 表單欄位寫入 Unicode 值與外觀（中文姓名、地址） | 已涵蓋（引擎嵌入字型寫入 Unicode 值） | 不移植；以繁中表單實測確認 |
| 17 | CJK 文字註解先轉成圖片再燒入，避免 PdfSharpCore 亂碼 | 已涵蓋（引擎以正黑體等字型 fallback 輸出可選取的向量文字） | 不移植（舊作法會讓文字無法選取）；實測確認 |

## 儲存、匯出與簽署

| # | 功能 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 18 | 原子化存檔（先寫暫存檔再替換，目標被占用時保留原檔） | 已涵蓋（`PdfEngineIntegration.ReplaceFileAtomically`，有測試） | 不需移植 |
| 19 | 存檔／另存／列印／匯出共用輸出快照；失敗時保留目前編輯與未儲存狀態；不重複燒入註解 | 架構不同（引擎改寫輸出流程） | 不移植程式碼；以 upstream 測試與實測確認行為 |
| 20 | 無書籤 PDF 編輯後無法儲存（`3f459d9`，空書籤根節點造成 xref 錯誤） | 不適用（PdfSharpCore 專屬問題） | 不移植；以無書籤 PDF 實測 |
| 21 | 偵測 Edge／Adobe 易顯示空白的來源 PDF、清除 PDF/X 標記、一般存檔前改建議匯出相容副本 | 無 | 暫不移植：成因是 PdfSharpCore 丟掉 OutputIntents 但保留 PDF/X 標記。需要用原本在 Edge 變空白的 PDF 測 upstream 引擎，仍有問題再移植 |
| 22 | 「平面化」改為「匯出相容 PDF 副本」：先說明後果、預設檔名加 `-compatible`、不得覆寫目前檔案、不清除未儲存狀態 | 部分（有 Save Flattened） | 移植用語、說明與不覆寫原檔的檢查 |
| 23 | 工具列「儲存」直接存回原檔（不再詢問覆寫或另存） | 無（upstream 會詢問） | 隨辦公工具列移植：工具列「儲存」與 Ctrl+S 同一路徑；保留 upstream 的「另一窗格也有未儲存修改」警告 |
| 24 | 憑證簽署：有未儲存修改時先另存待簽文件、輸出不得覆寫來源、覆寫前確認、對話框可捲動 | 無 | 移植到 `Controls/SignDocumentDialog.cs`、`Shell/DirtyTracking.cs` |

## 列印

| # | 功能 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 25 | 頁碼範圍接受全形數字、全形逗號與連字號、頓號「、」；反向、超界、缺端點或多餘逗號都視為無效並禁止列印 | 部分（已拒絕無相符頁，但不接受全形，反向會自動對調、超界會截斷） | 移植 `Services/PrintPageRange.cs` 與測試，`PrintPreviewWindow` 改用它 |
| 26 | 預覽在取不到紙張時以 A4 為準；繁中介面邊界以毫米顯示 | 部分（已顯示紙張名稱與尺寸；後備為 Letter，邊界只用英吋） | 移植 A4 後備與毫米邊界標籤 |

## 介面

| # | 功能 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 27 | 依工作分組的辦公工具列：檔案、閱讀、填寫與簽名、註解、整理頁面；窄視窗放進「更多」；底部顯示「已儲存／尚未儲存」 | 無 | 移植 `OfficeToolbar.cs`，改接 upstream 的指令，並把 upstream 新工具（圖形、量測、表單欄位、比較等）放進對應分組 |
| 28 | Esc 不會關閉主視窗 | 部分（有工具時回到選取；已是選取時仍關閉程式） | 移植 |
| 29 | 首次啟動預設淺色主題、藍色重點色、不加顆粒紋理 | 無（深色、綠色、顆粒開啟；已可在設定切換顆粒） | 移植，只影響沒有設定檔的新使用者 |
| 30 | 全域放大字級與按鈕（14pt、最小高度 36）、側欄切換條 36px、設定面板 300px | 部分（upstream 已重新設計介面，並有介面縮放） | 不移植全域尺寸；辦公工具列本身使用大按鈕 |
| 31 | 縮圖頁碼標籤在地化 | 已涵蓋（`Str_PageLabel`） | 不需移植 |

## 專案與發佈

| # | 項目 | upstream 1.8.91 | 移植計畫 |
|---|---|---|---|
| 32 | GPLv3 署名與 fork 說明：README 的 fork 說明、CHANGELOG fork 條目、`OFFICE_UX.md`、`OFFICE_UX_VALIDATION.md`、`PROJECT_STATUS.md` | 無 | 移植並改寫為 1.8.91 基底；保留 upstream LICENSE 與歷史紀錄 |
| 33 | `build-release.yml`：推 tag 時建置並發佈 GitHub Release | 無（upstream 有自己的 release-sync） | 改寫為 .NET 10 建置；upstream 的 chocolatey、winget、nuget-engine、release-sync workflow 加上 `github.repository == 'SteveTheKiller/KillerPDF'`，避免在 fork 執行 |
| 34 | 版本號 `1.6.3-office-ux` | — | 改為 `1.8.91-office-ux`（檔案版本 `1.8.91.0`） |
| 35 | `build/test-office-workflow.ps1`、`build/test-signature-ink.ps1` | — | 舊腳本綁定 net48 與 PdfSharpCore，改寫成 `KillerPDF.Tests` 的 xUnit 測試 |

## 需要使用者決定

1. **.NET 10**：一般安裝版需要 .NET 10 Desktop Runtime（安裝程式會提示）；可攜版自帶 runtime。建置需要 .NET 10 SDK（`global.json` 指定 10.0.400），本機目前只有 SDK 8.0.426。
2. **不移植的 fork 項目**：#17 CJK 轉圖片、#19 輸出快照程式碼、#20 xref 修正、#21 Edge 偵測（待實測）、#30 全域放大字級。
3. **辦公工具列**：取代 upstream 工具列成為預設，並在設定提供「使用原版工具列」切換；或完全取代。
4. **自動更新**：upstream 的更新檢查與 About 連結指向 SteveTheKiller/KillerPDF。fork 版若跟著提示更新，會被換成官方版而失去客製化。fork 1.6.3 也有同樣情況，本次先不改，另行決定。

## 移植順序

1. zh-TW 用語覆寫（#1–3）
2. 小功能：日期與 ✓（#6–7）、列印範圍與 A4／毫米（#25–26）、Esc（#28）、首次啟動預設（#29）、保留空白與輸入法防護（#12、#14）
3. 手寫簽名（#8–10）、憑證簽署防護（#24）、相容副本（#22）
4. 新增文字工具拖曳（#11）、表單鍵盤與提示開關（#15）
5. 辦公工具列（#27、#23）
6. 文件與 workflow（#32–35）
7. 建置並執行 `KillerPDF.Tests`、引擎測試與新測試

## 移植結果

（移植完成後填寫每一項的實際結果與 commit。）
