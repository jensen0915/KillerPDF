# KillerPDF 專案狀態交接

更新日期：2026-07-22

## 目前狀態

- 本機專案路徑：`D:\_codex_\KillerPDF`
- Git remote：`https://github.com/jensen0915/KillerPDF.git`
- 目前分支：`main`
- 目前狀態：`main` 已同步到 `origin/main`
- 最近相關提交：
  - `781558a Add GitHub release build workflow`
  - `f2bc1a7 Fix CJK PDF text and Edge compatibility`

## 這版主要修正

這版主要是為了解決 KillerPDF 在 PDF 儲存、列印預覽、匯出時遇到中文 / CJK 文字顯示亂碼的問題。

已完成的重點：

- CJK 文字註解改用 WPF 字型 fallback 先 rasterize 成 PNG，再寫回 PDF，避免 `PDFsharp` 在中文、日文、韓文等字元上缺字或亂碼。
- 一般英文 / ASCII 文字仍維持原本 vector text 輸出。
- 開啟需要密碼的 PDF 時，輸入 owner password 後會先透過 PDFium 解密到暫存檔，再進入可修改狀態。
- 偵測 Edge 內建 Adobe PDF viewer 可能顯示空白的來源 PDF，包含 Microsoft Print to PDF、Skia/PDF、HeadlessChrome、部分 PDF/X metadata。
- 對可能讓 Edge/Adobe 顯示空白的 PDF，正常 Save / Save As 會提示改用：
  - `File > Save Flattened PDF...`
- `Save Flattened PDF...` 會輸出 Edge 相容的扁平化 PDF。
- README 已補充此 fork/build 的用途與中文顯示修正說明。

## GitHub Actions

已新增 `.github/workflows/build-release.yml`。

觸發方式：

- 推 tag：
  ```powershell
  git tag v1.6.2-cjk-fix
  git push origin v1.6.2-cjk-fix
  ```
- 或到 GitHub：
  - `Actions`
  - `Build Release`
  - `Run workflow`
  - 輸入 tag，例如 `v1.6.2-cjk-fix`

Actions 會做的事：

- 在 GitHub Windows runner 上 build KillerPDF。
- 產生 release 用的 `KillerPDF.exe`。
- 產生 `SHA256SUMS.txt`。
- 建立或更新 GitHub Release，並上傳檔案。

另外也調整了既有 workflow：

- `.github/workflows/chocolatey-release.yml`
- `.github/workflows/winget-release.yml`

這兩個 workflow 只會在 upstream repo `SteveTheKiller/KillerPDF` 執行，避免 fork 自己發 release 時因權限或套件發布設定失敗。

## 本機已產生 EXE

目前本機已 build 出：

```text
D:\_codex_\KillerPDF\bin\Release\net48\publish\KillerPDF.exe
```

SHA256：

```text
A33811D6D2409EEF3C34A8D786D0351E0A786AB77C2C12848A989E69C0A876ED
```

## 已執行驗證

- `dotnet test KillerPDF.Tests\KillerPDF.Tests.csproj --no-restore`
  - 結果：21 tests passed
- `dotnet build KillerPDF.sln --no-restore -p:OutputPath=bin\CodexVerify\`
  - 結果：build passed
  - 備註：有既有 Costura / Fody warning
- `dotnet publish KillerPDF.csproj /p:PublishProfile=FolderProfile1 -c Release`
  - 結果：publish succeeded，EXE 已產生
  - 備註：sandbox 內 nuget vulnerability data 無法取得，所以出現 NU1900 warning；source bundle script 也因 git safe.directory 提示過 warning，但 EXE 已正常產生

## 後續建議操作

如果要建立 GitHub Release，建議使用 tag 觸發 Actions：

```powershell
git tag v1.6.2-cjk-fix
git push origin v1.6.2-cjk-fix
```

推完後到 GitHub repository 的：

```text
Actions > Build Release
```

確認 workflow 成功，再到：

```text
Releases
```

確認是否出現對應 tag 的 release 與 `KillerPDF.exe`。

如果換帳號或換新 Codex task，建議先檢查：

```powershell
git status --short --branch
git log -3 --oneline
```

再確認 GitHub Actions / Releases 是否已經跑完。
