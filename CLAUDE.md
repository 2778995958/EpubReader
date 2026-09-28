# EpubReader 編譯

需要 .NET 8 SDK。方案：`D:\git\AeroEpubViewer2\EpubReader.sln`

```bat
dotnet build EpubReader.sln -c Release
```

執行：

```bat
dotnet run --project src\EpubReader.App -c Release
```

輸出：`src\EpubReader.App\bin\Release\net8.0-windows\EpubReader.exe`
