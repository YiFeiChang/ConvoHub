# ConvoHub

> 本 README 同時是專案的系統與功能追蹤指南。內容以 `main` 的提交 `3693600` 為準（2026-08-25），描述已實作功能；它不代表正式部署已具備認證、持久化或媒體安全能力。

ConvoHub 是供企業內部使用的單一聊天室原型，採 WPF Client、ASP.NET Core Service 與共享 Models。Client 與服務透過 HTTP 和 SignalR 溝通。

## 系統架構

```
ConvoHub.Client (net8.0-windows / WPF)
  ├─ HTTP：讀取歷史、上傳、下載媒體
  └─ SignalR：傳送與接收即時訊息
             │
             ▼
ConvoHub.Service (net8.0 / ASP.NET Core)
  ├─ ChatHub：即時廣播 Markdown 訊息
  ├─ ChatController：歷史與媒體上傳 API
  ├─ ChatStore：記憶體中的最近 200 則訊息
  └─ uploads/：服務程序目錄下的媒體原始檔
             │
             ▼
ConvoHub.Models (net8.0)
  └─ ChatMessage、SendMessageRequest、MessageKind
```

解決方案入口為 `ConvoHub.slnx`。主要 UI 與客戶端行為集中在 `ConvoHub.Client/MainWindow.xaml` 和 `MainWindow.xaml.cs`；共享模型目前位於沿用範本名稱的 `ConvoHub.Models/Class1.cs`。

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

開發模式會以 Client 取得的 Windows 帳戶名稱傳送 `X-Windows-User` 識別標頭，方便在本機 Kestrel 執行。此標頭可被任何呼叫端偽造，不能當成正式認證。

正式部署請將 Service 發佈至 IIS 或啟用 HTTP.sys Windows Authentication，關閉 Anonymous Authentication，讓 ASP.NET Core 的 `User.Identity.Name` 成為服務端可信的身分來源。目前服務只有 `UseAuthorization()`，尚未設定 `AddAuthentication()` 或 `[Authorize]` 限制；部署到網路前必須完成認證、授權、TLS 與安全的外部 URL 設定。

## 資料流

### Markdown 訊息

1. 使用者按「傳送」或 `Ctrl+Enter`；Client 僅在 Hub 已連線且輸入非空白時呼叫 `SendMessage`。
2. `ChatHub.SendMessage` 優先採用 `Context.User.Identity.Name`，否則讀取 `X-Windows-User`，最後才使用 `Unknown user`。
3. Hub 將去除前後空白的內容建立 `ChatMessage`，寫入 `ChatStore`，並以 `ReceiveMessage` 廣播。
4. Client 透過 WPF `Dispatcher` 呼叫 `AddMessage`，再用 Markdig 解析並呈現為 WPF `FlowDocument`。

### 媒體訊息

1. 使用者按「圖片」或「影片」，Client 以 `multipart/form-data` 上傳檔案。
2. `POST /api/chat/upload` 檢查白名單副檔名，產生 GUID 檔名並寫入 `<Service 執行目錄>/uploads`。
3. Controller 建立 `Content` 為 `/uploads/{guid}.{ext}` 的 `ChatMessage`，寫入記憶體後透過 SignalR 廣播。
4. Client 使用 `ServiceUrl + Content` 嵌入顯示；雙擊媒體時以 HTTP 串流下載原始檔。

## 通訊契約

`ServiceUrl` 目前硬編碼為 `http://localhost:5025`，Client 的 SignalR 與 HTTP 呼叫都會帶 `X-Windows-User`。

| 類型 | 路徑／名稱 | 請求 | 回應／效果 |
| --- | --- | --- | --- |
| SignalR Hub | `/hubs/chat` | `SendMessage(SendMessageRequest)` | 廣播 `ReceiveMessage(ChatMessage)` |
| HTTP | `GET /api/chat/messages` | 無 | `ChatMessage[]`（最多 200） |
| HTTP | `POST /api/chat/upload` | multipart 欄位 `file` | 新建立的 `ChatMessage` 並廣播 |
| 靜態檔 | `GET /uploads/{guid}.{ext}` | 無 | 原始媒體內容 |

```csharp
enum MessageKind { Markdown, Image, Video }
ChatMessage { Guid Id; string UserName; string Content; MessageKind Kind; DateTimeOffset SentAt }
SendMessageRequest { string Content; MessageKind Kind }
```

`SendMessageRequest.Kind` 目前由 Hub 原樣帶入訊息；Client 只用它傳送 Markdown。媒體必須走 upload API，讓服務端建立 URL 並決定媒體種類。

## 狀態與限制

- `ChatStore` 使用靜態 `ConcurrentQueue`，只保留最近 200 則；Service 重啟後歷史即清空。
- 媒體檔不會隨訊息淘汰或隨 Service 重啟清除，可能留下孤兒檔案。
- 沒有聊天室、頻道、私訊、帳號資料庫、已讀狀態、編輯／刪除訊息或分頁。
- 服務僅依副檔名驗證媒體，且 `/uploads` 是公開靜態檔。正式化前應補上內容驗證、惡意檔案掃描、存取授權、配額、下載審計與清理策略。

## Markdown 呈現邊界

Markdig 使用 `UseAdvancedExtensions()` 解析，但 WPF renderer 只明確處理部分 AST 節點。巢狀清單、一般縮排程式碼區塊、部分延伸語法與 Markdown 圖片沒有完整對應，未知區塊可能不呈現。訊息顯示會在 renderer 例外時回退為純文字；但輸入預覽沒有獨立例外處理，修改 parser 或 renderer 後要一併測試預覽。

連結會轉為 `Hyperlink`，但目前沒有設定以外部瀏覽器開啟的行為。未來調整時應加入 URL scheme 白名單。

## 維護與驗證

每次修改後至少驗證：

1. `dotnet build ConvoHub.slnx` 可成功建置。
2. 新 Client 能取得歷史並接收即時 Markdown。
3. 支援／不支援的副檔名、0 位元組與超過 100 MiB 媒體檔的行為。
4. 圖片／影片顯示與雙擊下載；Service 重啟後歷史清空、既有 uploads 檔仍可存取。
5. 長 Markdown、表格與無效 Markdown 不會中斷 UI。

目前沒有自動化測試或 CI。擴充時，優先為 `ChatStore`、上傳白名單／上限、Hub 空內容與 API 建立單元或整合測試，再為 WPF renderer 準備 Markdown 範例的回歸測試。

### 修改入口

- **傳輸、保存或訊息種類**：先改 `ConvoHub.Models/Class1.cs`，再同步 Hub、Controller 與 Client。
- **路由、靜態檔或 middleware**：改 `ConvoHub.Service/Program.cs`。
- **上傳規則與歷史 API**：改 `ConvoHub.Service/Controllers/ChatController.cs`。
- **即時訊息規則**：改 `ConvoHub.Service/ChatHub.cs`。
- **視覺與互動**：改 `ConvoHub.Client/MainWindow.xaml` 與對應 `.xaml.cs`。

`WeatherForecast` 類別與 Controller 是 ASP.NET 範本遺留，與聊天室無關；移除前請確認外部工具沒有依賴 Swagger 範例端點。

## 變更歷史

| 提交 | 內容 |
| --- | --- |
| `8c4c397` | 建立三專案基礎結構與 ASP.NET Core 範本端點。 |
| `8930692` | 加入聊天、SignalR、Markdown、媒體上傳與顯示。 |
| `3693600` | 加入雙擊媒體下載。 |
