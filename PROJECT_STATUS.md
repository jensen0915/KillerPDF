# KillerPDF 微調版與 GitHub 發佈說明

更新日期：2026-10-23

## 來源與開源原則

本分支在原開源專案 [SteveTheKiller/KillerPDF](https://github.com/SteveTheKiller/KillerPDF) 基礎上延續修改，由 [jensen0915/KillerPDF](https://github.com/jensen0915/KillerPDF) 保存本次微調。原作者與貢獻者的成果、署名及原有授權資訊均保留。

本次 `1.8.91-office-ux` 以原專案 1.8.91 為基礎，重新移植先前 `1.6.3-office-ux` 針對台灣辦公文件所做的調整，依舊沿用 **GNU GPLv3**，提供對應原始碼與建置方式。這是衍生修改版，不是上游官方發佈；完整授權文字見 [LICENSE](LICENSE)，本次不修改授權內容。

## 這一版怎麼來的

原專案從 1.6.2 之後改用 .NET 10、自有的 PDF 引擎（KillerPdf.Engine，取代 PdfSharpCore），並重新整理程式結構，因此沒有直接合併舊分支，而是以 1.8.91 為底，把舊版的每項功能重新做上去。每一項的去留與原因記在 [docs/PORT_FROM_FORK.md](docs/PORT_FROM_FORK.md)。

## 建置與測試

需要 .NET 10 SDK（`global.json` 指定 10.0.400 系列，例如 10.0.402）。

```powershell
dotnet build KillerPDF.csproj -c Release
dotnet test KillerPDF.Tests/KillerPDF.Tests.csproj -c Release
dotnet test engine/KillerPdf.Engine.Tests/KillerPdf.Engine.Tests.csproj -c Release
```

單一可攜執行檔（附 .NET runtime，使用原專案的打包腳本，未簽章）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build/build-portable.ps1 -PackageKind Portable
```

輸出為 `bin/Release/net10.0-windows/publish/KillerPDF-Portable.exe`。腳本會先發佈自含的 payload、以 `--startup-smoke` 檢查啟動、比對 `build/payload-files.txt` 的檔案清單並產生雜湊清單，再壓進啟動器。

## 在 GitHub 發佈

[Build Release (fork)](.github/workflows/build-release.yml) 只在本分支執行，可由 `v*-office-ux*` tag 或手動執行觸發。它會先跑應用程式與引擎測試，再用上述腳本打包單一可攜執行檔，最後建立或更新 GitHub Release。附件包含：

- `KillerPDF-Portable.exe`：單一可攜執行檔，內含 .NET runtime，下載後直接執行。
- `KillerPDF-<版本>-src.zip`：對應原始碼。
- `SHA256SUMS.txt`、`LICENSE`（本專案 GPLv3）、`PDFium-LICENSE.txt`（PDFium 授權）。

本分支沒有程式碼簽章憑證，發佈的 EXE 未簽章，Windows SmartScreen 第一次執行時可能提示。原專案的 `release.ps1` 需要簽章並會同步上游的 WinGet、網站等，不適用本分支。

發佈步驟：

1. 確認要發佈的提交已推送到本分支。
2. 到 **Actions → Build Release (fork) → Run workflow**，輸入尚未存在的新 tag，例如 `v1.8.91-office-ux`；或在本機建立 tag 後推送：

```powershell
git tag v1.8.91-office-ux
git push origin v1.8.91-office-ux
```

3. 工作流程成功後，到 [Releases](https://github.com/jensen0915/KillerPDF/releases) 檢查附件與說明。

Chocolatey、WinGet、NuGet 引擎套件與 Release Sync 工作流程只在原專案 `SteveTheKiller/KillerPDF` 執行。

## 已知限制

- 程式內的更新檢查、About 連結與問題回報仍指向原專案。原專案發佈新版本時，程式可能提示更新到官方版，更新後會失去本分支的調整。要取得本分支版本，請使用本分支的 Releases。
- 有觸控筆裝置的電腦上，WPF 的平板（WISP）列舉會讓 `--startup-smoke` 關閉時卡住，原專案 1.8.91 也一樣。本分支讓啟動檢查在記錄結果 10 秒後仍未結束時以該結果強制結束，並在報告中留下 `WARN shutdown stalled`。
- 實際注音／倉頡選字、觸控筆、實體印表機，以及 Edge／Adobe Reader 開啟輸出檔案的人工驗收尚未進行，見 [OFFICE_UX_VALIDATION.md](OFFICE_UX_VALIDATION.md)。

## 下載與回報管道

- 本分支版本：[jensen0915/KillerPDF Releases](https://github.com/jensen0915/KillerPDF/releases)。
- 本分支問題回報：[jensen0915/KillerPDF Issues](https://github.com/jensen0915/KillerPDF/issues)。
