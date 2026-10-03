# 辦公文件優化版驗證報告

日期：2026-10-03。版本：1.6.3-office-ux／1.6.3.0。基底 Git commit：46f20819；本報告記錄提交與推送前完成的本機驗證。

## 已執行並通過

| 項目 | 實際驗證內容 |
|---|---|
| Release 建置 | .NET SDK 8.0.425、net48、x64；零編譯錯誤。仍有既有 Costura.Fody IncludeAssets 建議警告。 |
| 完整測試 | 40／40 通過，包含原有搜尋、文字繪製、簽名資料相容性，以及新增的頁碼與檔案替換測試。 |
| 原生簽名 | STA 檢查中間取樣點、邊界、單點筆畫、固定粗細、手勢設定、清除。 |
| 繁中欄位 | 實際建立 PDF，填入含前後空白的姓名、多行臺北地址及靠右民國日期；重開後逐一比對 Unicode `/V`、`/FT` 與 Widget `/AP`。 |
| 連續儲存 | 同一工作階段儲存三次，經 PDFium 比對三份輸出像素完全一致；原始編輯文件物件未被替換。 |
| 未儲存列印快照 | 含新增繁中文字、日期、✓、手寫簽名與表單外觀；PDFium 渲染與儲存檔的外觀比對一致，未清除 dirty 狀態或切換目前路徑。 |
| 失敗保留 | 無效簽名圖片使快照失敗，保留註解及 dirty 狀態；已有值但缺 AP 的勾選欄位會中止輸出。 |
| 鎖定檔案 | 原子替換 helper 在目標被占用時保留舊內容並清除中間檔，解除鎖定後成功替換。sandbox 對 File.Replace 的 ACL 限制造成一次失敗；以一般執行權限重跑通過。 |
| 重載欄位 | 儲存後呼叫結構修改使用的重載流程，欄位鍵與繁中／空白值保持一致。 |
| 列印頁碼 | 半形、全形數字、逗號、頓號、重複頁；無效、反向、超界及不完整範圍拒絕。實際 WPF 列印視窗驗證全形範圍有效，超界輸入禁用列印。未送出列印工作。 |
| 介面排版 | 淺／深色各 6 組 WPF 離屏渲染；窄視窗確認工具列溢出且「更多」完整落在視窗內。長中文檔名、圖示加文字、任務分組與儲存狀態亦檢查渲染圖。 |
| 在地化 | en-US／zh-TW 各 494 個資源鍵，無重複；程式主流程使用的 Loc／S 鍵無缺漏。 |
| 靜態檢查 | Git diff whitespace 檢查通過。未新增正式環境套件。 |

Widget 外觀的渲染方式：一般儲存保留可編輯 AcroForm；驗證時另取暫存副本合併 AP，再交 PDFium 渲染。列印／相容輸出也只在其暫存副本合併 AP，避免 PDFium 一般頁面渲染忽略 Widget。這不代表已在 Adobe Reader 或 Edge 實測。

WPF 排版尺寸：1366×768（100%）、1093×614（125%，約等效 1366×768）、1280×720（150%，等效 1920×1080）、960×540（200%，等效 1920×1080）、620×480 窄視窗、1920×1080（100%）。這是 DIP 可用空間與渲染比例檢查，不是切換 Windows 系統 DPI 或跨螢幕的人工驗證。

## 尚未執行的人工驗收

- Windows 注音／倉頡候選確認、組字中的 Enter／Esc／方向鍵，以及完整鍵盤焦點順序。程式已使用 WPF composition 事件保護，仍需實際輸入法確認。
- 實體觸控筆筆感、筆壓、手掌誤觸與不同驅動程式。現有 InkCanvas 自動檢查不能取代實筆操作。
- 實體印表機送印、紙張／方向／份數與毫米邊界量測；此輪未送出任何列印工作。
- Edge／Adobe Reader 開啟、再次編輯欄位、憑證簽署及簽章驗證。
- 完整 GUI 點擊式另存／取消／切換分頁競態、檔案選擇器覆寫，以及相容輸出從按鈕到檔案的端到端人工驗收。已檢查共用快照與寫入流程，但不能將其視為以上人工流程已通過。
- 各式外來 PDF 的特殊欄位、非標準 appearance matrix、修復檔及加密檔，尚未形成完整相容性測試集。

## 重跑與檢查產物

```powershell
dotnet build KillerPDF.sln -c Release
dotnet test KillerPDF.Tests/KillerPDF.Tests.csproj -c Release
powershell -NoProfile -STA -File build/test-signature-ink.ps1
powershell -NoProfile -STA -File build/test-office-workflow.ps1 -Exe bin/OfficeUX/KillerPDF.exe
```

整合測試使用 `bin/nuget-packages` 的既有 PDFsharpCore／Docnet 參考組件；此環境使用隔離的 DOTNET_CLI_HOME、NUGET_PACKAGES、APPDATA、LOCALAPPDATA 進行建置。測試的 PDF 與排版圖位於 `bin/OfficeValidation`，不會修改個人 PDF 或執行 GitHub 操作。

## 交付

- `bin/OfficeUX/KillerPDF.exe`：獨立優化版，檔案版本 1.6.3.0。
- 同資料夾附操作說明、此報告、授權及包含新增原始碼的 source ZIP。
- 舊版 `bin/Release/net48/publish/KillerPDF.exe` 與簽名版 `bin/SignatureTest/KillerPDF.exe` 均保留（1.6.2.0）。
- 本機驗證與封裝未建立 tag 或 GitHub Release；後續原始碼提交與推送不代表已發佈 EXE。

分工：子代理協助在地化、簽名對話框與局部審查；主代理完成快照／欄位輸出、介面整合、問題修正與統一驗證。
