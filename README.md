# EpubReader

.NET 8 WPF 閱讀器。畫面用 DirectWrite + Direct2D 排字，**不用 HTML / JS / CefSharp**。

直排分頁在排字時斷欄，不會切字切 ruby。單／雙頁只影響插圖對開。

## 執行

需要 .NET 8 SDK。

```bat
dotnet run --project src\EpubReader.App
```

或開 `EpubReader.sln`。輸出：`src\EpubReader.App\bin\Debug\net8.0-windows\EpubReader.exe`

拖入 EPUB / CBZ，或按 `O`。

## 操作

- 點視窗上方約 20%：叫出／收合工具列
- 滾輪、PageUp / PageDown、方向鍵：翻頁（直排書左側是下一頁）
- 目次、插圖、字級、主題、單雙頁：在工具列
- `V` 直排／橫排　`P` 單／雙頁　`T` 目次　`I` 插圖　`F11` 全螢幕

進度存在 `%LocalAppData%\EpubReader\state.json`。

## 和舊版的差別

舊 AeroEpubViewer（WinForms + CefSharp 86）已不再開發。
