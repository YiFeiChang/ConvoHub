# ConvoHub

ConvoHub 是供企業內部使用的簡易 Windows 通訊軟體，採 WPF Client、ASP.NET Core Service 與共享 Models。

## 目前功能

- 使用 `WindowsIdentity.GetCurrent()` 顯示登入 Windows 桌面的帳戶名稱，訊息作者以 Windows 帳戶識別。
- 透過 SignalR 即時廣播 Markdown 文字訊息。
- 使用 `Markdig` 解析 Markdown，並以接近 GitHub 的樣式呈現標題、段落、粗體、斜體、刪除線、連結、引用、水平線、序號/無序清單、程式碼區塊與表格。
- 輸入框最高 140px，內容過長時在輸入框內捲動；輸入 Markdown 時會即時顯示預覽。
- 表格會依欄位與跨欄資訊繪製成 WPF Grid；遇到不完整或不支援的 Markdown 結構時，訊息會退回純文字顯示，不會使 Client 關閉。
- 可上傳 JPG、PNG、GIF、WebP 圖片與 MP4、WebM、MOV、AVI 影片，並直接嵌入訊息串播放/預覽。
- 對話中的圖片或影片可雙擊，選擇儲存位置下載原始媒體檔案。
- Service 以記憶體保存最近 200 則訊息；媒體檔案存放於 Service 執行目錄的 `uploads`，單檔上限 100 MB。

## 執行方式

需要 .NET 8 SDK。在方案目錄開啟兩個終端機：

```powershell
dotnet run --project ConvoHub.Service --launch-profile http
dotnet run --project ConvoHub.Client
```

Service 預設位於 `http://localhost:5025`。Client 目前連線到此位址；若要改服務位址，請修改 `ConvoHub.Client/MainWindow.xaml.cs` 的 `ServiceUrl`。

### 使用假帳號測試

Client 預設使用目前 Windows 桌面帳戶；驗證多使用者訊息顯示時，可使用 `--fake-user` 覆寫帳戶名稱：

```powershell
dotnet run --project ConvoHub.Client -- --fake-user=alice
dotnet run --project ConvoHub.Client -- --fake-user bob
```

此參數只影響本機 Client 傳送的測試識別，不會建立或登入 Windows 帳戶。可同時開啟多個 Client，分別指定不同假帳號驗證即時訊息。

## Windows 驗證部署

開發模式會以 Client 取得的 Windows 帳戶名稱傳送識別標頭，方便在本機 Kestrel 執行。正式部署請將 Service 發佈至 IIS 或啟用 HTTP.sys Windows Authentication，關閉 Anonymous Authentication，讓 ASP.NET Core 的 `User.Identity.Name` 成為服務端的信任來源；目前的標頭 fallback 僅供本機開發使用。
