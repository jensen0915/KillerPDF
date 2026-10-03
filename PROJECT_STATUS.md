# KillerPDF 微調版與 GitHub 發佈說明

更新日期：2026-10-03

## 來源與開源原則

本分支在原開源專案 [SteveTheKiller/KillerPDF](https://github.com/SteveTheKiller/KillerPDF) 基礎上延續修改，由 [jensen0915/KillerPDF](https://github.com/jensen0915/KillerPDF) 保存本次微調。原作者與貢獻者的成果、署名及原有授權資訊均保留。

本次 `1.6.3-office-ux` 針對台灣辦公文件的閱讀、填寫、手寫簽名、儲存及列印流程做後續微調，依舊沿用 **GNU GPLv3**，提供對應原始碼與建置方式。這是衍生修改版，不是上游官方發佈；完整授權文字見 [LICENSE](LICENSE)，本次不修改授權內容。

## 目前已完成

- 程式優化與本機封裝完成：`bin/OfficeUX/KillerPDF.exe`，產品版本 `1.6.3-office-ux`、檔案版本 `1.6.3.0`。
- 同資料夾提供對應原始碼 `KillerPDF-1.6.3-office-ux-src.zip`、專案 `LICENSE`、`PDFium-LICENSE.txt`、`SHA256SUMS.txt`、操作說明及驗證報告。
- 40 項自動測試、原生 InkCanvas 簽名回歸及 PDF／WPF 整合檢查通過；實筆、注音／倉頡、實體列印及 Edge／Adobe Reader 人工驗收仍待進行。
- 舊版 EXE 保留於 `bin/Release/net48/publish` 與 `bin/SignatureTest`。
- 使用方式見 [OFFICE_UX.md](OFFICE_UX.md)，完整驗證範圍與限制見 [OFFICE_UX_VALIDATION.md](OFFICE_UX_VALIDATION.md)。

本版以 `46f2081` 為基礎，程式與文件透過本分支的 `main` 提交與推送；Git 提交紀錄可用於確認版本。推送原始碼不會自動建立本版的 tag 或 GitHub Release，EXE 仍需依下方步驟另外發佈。

## 後續在 GitHub 發佈

現有 [Build Release 工作流程](.github/workflows/build-release.yml) 可由 `v*` tag 或手動執行觸發。執行後會實際建立或更新 GitHub Release，請在確認要發佈時操作。

1. 檢查並提交本次需要的程式、文件與測試檔案，推送到自己的 fork。仍在本機、尚未提交的修改不會出現在 GitHub 建置中。
2. 開啟 GitHub 專案的 **Actions → Build Release → Run workflow**，選擇包含上述變更的分支，輸入新的 tag，例如 `v1.6.3-office-ux`。
3. 等待工作流程成功，再到自己 fork 的 [Releases](https://github.com/jensen0915/KillerPDF/releases) 檢查 tag、提交版本及說明是否正確。
4. 確認附件包含 `KillerPDF.exe`、對應版本的原始碼 ZIP、`SHA256SUMS.txt`、`LICENSE` 與 `PDFium-LICENSE.txt`；確認原始碼 ZIP 包含本次修改及建置腳本。

也可以在提交並推送分支後，用 tag 觸發：

```powershell
git tag v1.6.3-office-ux
git push origin v1.6.3-office-ux
```

以上是後續操作說明，本次未執行。手動工作流程使用所選分支的提交建置；請使用尚未存在的新 tag，避免既有 tag 指向舊提交而附件來自另一版本。

## 工作流程實際內容

- 在 Windows runner 使用 .NET 8 SDK，以 `FolderProfile1` 發佈 .NET Framework 4.8／win-x64 程式。
- 輸出至 `bin/Release/net48/publish`，並產生對應版本的原始碼 ZIP。
- 保留建置輸出的 PDFium 授權為 `PDFium-LICENSE.txt`，另外附上專案的 GPLv3 `LICENSE`。
- 計算 EXE 與原始碼 ZIP 的 SHA-256，將檔案與兩份授權文件上傳為 `KillerPDF-release` Actions artifact 及 GitHub Release 附件。
- Release 說明包含本次辦公流程微調、上游來源、GNU GPLv3、對應原始碼及人工驗收限制。
- 此工作流程負責建置與發佈，**不會重跑完整測試**；發佈前須完成需要的測試與驗收。
- Chocolatey 與 WinGet 工作流程仍僅在原上游 `SteveTheKiller/KillerPDF` 執行。

## 下載與回報管道

- 本分支版本：[jensen0915/KillerPDF Releases](https://github.com/jensen0915/KillerPDF/releases)。
- 本分支問題回報：[jensen0915/KillerPDF Issues](https://github.com/jensen0915/KillerPDF/issues)。

目前程式內建更新檢查及部分問題回報連結仍指向原上游；本次文件更新不會改變程式內的目的地。需要本分支微調版時，請使用本分支的 Release。README 中的 WinGet／Chocolatey 安裝方式對應原上游版本。
