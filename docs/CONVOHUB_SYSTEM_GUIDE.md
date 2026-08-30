# ConvoHub 系統與功能追蹤指南

> 本文件以 `main` 的提交 `3693600` 為準（2026-08-25）。用途是讓後續模型或開發者能快速理解現有行為、資料流與修改位置；它描述已實作的功能，不代表正式部署已具備的安全性或持久化能力。

## 1. 系統定位與專案結構

ConvoHub 是企業內部用的單一聊天室原型。它由一個 Windows WPF 桌面程式與一個 ASP.NET Core 服務組成，兩端透過 HTTP 與 SignalR 溝通。

```
ConvoHub.Client (net8.0-windows / WPF)
  ├─ HTTP：讀取歷史、上傳、下載媒體
  └─ SignalR：傳送與接收即時訊息
             │
             ▼
ConvoHub.Service (net8.0 / ASP.NET Core)
  ├─ ChatHub：即時廣播 Markdown 訊息
  ├─ ChatController：歷史與媒體上傳 HTTP API
  ├─ ChatStore：記憶體中的最近 200 則訊息
  └─ uploads/：服務程序目錄下的媒體原始檔
             │
             ▼
ConvoHub.Models (net8.0)
  └─ ChatMessage、SendMessageRequest、MessageKind
```

解決方案入口為 `ConvoHub.slnx`。主要 UI 與幾乎所有客戶端行為集中在 `ConvoHub.Client/MainWindow.xaml` 和 `MainWindow.xaml.cs`；共享模型目前位於名稱沿用範本的 `ConvoHub.Models/Class1.cs`。

## 2. 已實作功能清單

| 功能 | 目前行為 | 實作位置 |
| --- | --- | --- |
| 使用者標示 | Client 預設讀取 Windows 桌面帳戶；可用 `--fake-user=alice` 或 `--fake-user alice` 覆寫，供本機多使用者測試。 | `MainWindow.xaml.cs` 的 `GetConfiguredUserName` |
| 即時 Markdown 聊天 | 送出後由 SignalR Hub 廣播給所有已連線 Client；空白內容不建立訊息。 | `ChatHub.SendMessage` |
| 聊天歷史 | Client 成功連線後以 HTTP 取得目前程序中保存的訊息，再加入畫面。 | `ChatStore`、`GET /api/chat/messages` |
| Markdown 預覽與呈現 | 輸入時即時預覽；訊息中可呈現標題、段落、粗斜體、刪除線、連結、引言、清單、水平線、程式碼區塊、表格。無法呈現時改顯示純文字。 | `RenderMarkdown` 與相關 `Render*` 方法 |
| 圖片與影片上傳 | 支援 JPG/JPEG、PNG、GIF、WebP 與 MP4、WebM、MOV、AVI；服務依副檔名判斷種類。單檔上限 100 MiB。 | `UploadMedia`、`POST /api/chat/upload` |
| 串內媒體檢視 | 圖片以 `Image` 顯示，影片以 `MediaElement` 嵌入；最大 560×360。 | `AddMessage` |
| 媒體下載 | 雙擊串內圖片或影片，選位置後透過 HTTP 下載服務端原始檔。 | `Media_MouseLeftButtonDown`、`DownloadMediaAsync` |

## 3. 端對端資料流

### Markdown 訊息

1. 使用者按「傳送」或 `Ctrl+Enter`；Client 僅在 Hub 已連線且輸入非空白時呼叫 `SendMessage`。
2. `ChatHub.SendMessage` 從受驗證的 `Context.User.Identity.Name` 取使用者名稱；若不存在，才讀取 `X-Windows-User`，最後使用 `Unknown user`。
3. Hub 將去除前後空白的內容組成 `ChatMessage`，寫入 `ChatStore`，並以 `ReceiveMessage` 廣播。
4. 每個 Client 的 handler 透過 WPF `Dispatcher` 呼叫 `AddMessage`；Markdown 再由 Markdig 解析為 WPF `FlowDocument`。

### 媒體訊息

1. 使用者按「圖片」或「影片」，Client 的檔案挑選器限制可選副檔名，並以 `multipart/form-data` 上傳。
2. `POST /api/chat/upload` 再次依副檔名白名單檢查，產生不含原始檔名的 GUID 檔名，寫入 `<Service 執行目錄>/uploads`。
3. Controller 建立 `ChatMessage`（`Content` 為 `/uploads/{guid}.{ext}`），寫入記憶體並透過 `IHubContext<ChatHub>` 廣播。
4. Client 以 `ServiceUrl + Content` 取得媒體或在雙擊後串流下載。

## 4. 通訊契約

`ServiceUrl` 目前硬編碼為 `http://localhost:5025`。Client 連線時和 HTTP 呼叫時都帶有 `X-Windows-User`。

| 類型 | 路徑／名稱 | 請求 | 回應／效果 |
| --- | --- | --- | --- |
| SignalR Hub | `/hubs/chat` | `SendMessage(SendMessageRequest)` | 廣播 `ReceiveMessage(ChatMessage)` |
| HTTP | `GET /api/chat/messages` | 無 | `ChatMessage[]`（最多 200） |
| HTTP | `POST /api/chat/upload` | multipart 欄位 `file` | 新建立的 `ChatMessage` 並廣播 |
| 靜態檔 | `GET /uploads/{guid}.{ext}` | 無 | 原始媒體內容 |

共享模型如下：

```csharp
enum MessageKind { Markdown, Image, Video }
ChatMessage { Guid Id; string UserName; string Content; MessageKind Kind; DateTimeOffset SentAt }
SendMessageRequest { string Content; MessageKind Kind }
```

`SendMessageRequest.Kind` 目前由 Hub 原樣帶入訊息；Client 只以 `Markdown` 呼叫 Hub。媒體必須走 upload API，因為它需先儲存檔案並由服務端決定 URL 與種類。

## 5. 狀態、保存與重啟行為

- `ChatStore` 是靜態 `ConcurrentQueue`，每加入一則後會移除最舊訊息，保留上限 200。
- 訊息歷史只存在 Service 記憶體，Service 重啟即清空。
- 上傳的檔案不會隨訊息佇列淘汰，也不會隨重啟清除；因此可能留下孤兒檔案。
- 沒有聊天室、頻道、私訊、帳號資料庫、已讀狀態、編輯／刪除訊息或分頁機制。

## 6. 身分識別與部署注意事項

本機開發可讓 Client 自帶 `X-Windows-User`，也可用 `--fake-user`，但該標頭可被任何呼叫端偽造，不能視為正式認證。README 建議正式環境以 IIS 或 HTTP.sys 啟用 Windows Authentication 並停用匿名驗證，讓 `User.Identity.Name` 成為可信來源。

服務目前只呼叫 `UseAuthorization()`，沒有設定 `AddAuthentication()` 或任何 `[Authorize]` 限制；在這個狀態下仍會落到標頭 fallback。若將它部署到網路上，至少要先完成認證、授權與安全的外部 URL／TLS 設定。

媒體驗證目前只看檔案副檔名，`/uploads` 也以靜態檔方式公開。若擴充為正式產品，請評估內容驗證、惡意檔案掃描、存取授權、大小與儲存配額、下載審計、清理策略，以及服務端媒體轉碼／縮圖。

## 7. Markdown 呈現的實作邊界

Markdig 使用 `UseAdvancedExtensions()` 解析，但 WPF renderer 只明確處理部分 AST 節點。特別要注意：巢狀清單、一般縮排程式碼區塊、部分延伸語法與圖片 Markdown 並未完整對應為 WPF 元件；未知區塊可能不呈現。`AddMessage` 對 renderer 例外有純文字 fallback，而輸入時的 `MessageInput_TextChanged` 沒有自己的例外處理，修改 parser 或 renderer 時應一併檢驗預覽流程。

連結被轉為 `Hyperlink`，但程式未設定點擊後以外部瀏覽器開啟的行為；未來調整連結體驗時應同時考量 URL scheme 白名單與安全性。

## 8. 開發、驗證與追蹤方式

需要 .NET 8 SDK（專案 target framework 為 `net8.0`／`net8.0-windows`）。本機可分別啟動：

```powershell
dotnet run --project ConvoHub.Service --launch-profile http
dotnet run --project ConvoHub.Client
```

測試多使用者即時行為可另開兩個 Client：

```powershell
dotnet run --project ConvoHub.Client -- --fake-user=alice
dotnet run --project ConvoHub.Client -- --fake-user=bob
```

建議每次修改後至少驗證：

1. `dotnet build ConvoHub.slnx` 是否成功。
2. 新 Client 是否可取得歷史並接收即時 Markdown。
3. 支援與不支援的媒體副檔名、0 位元組檔案、超過 100 MiB 的檔案是否符合預期。
4. 圖片／影片是否可顯示與雙擊下載；Service 重啟後歷史清空但既有 `uploads` 檔仍可存取。
5. 長 Markdown、表格、無效 Markdown 是否不會使 UI 中斷。

目前沒有自動化測試專案或 CI 設定。若要擴充，優先為 `ChatStore`、上傳白名單／上限、Hub 的空內容行為與 API 加入單元或整合測試，再為 WPF renderer 建立範例輸入的回歸測試。

## 9. 變更史與後續修改入口

| 提交 | 內容 |
| --- | --- |
| `8c4c397` | 建立三專案基礎結構與 ASP.NET Core 範本端點。 |
| `8930692` | 加入聊天、SignalR、Markdown、媒體上傳與顯示。 |
| `3693600` | 加入雙擊媒體下載。 |

功能調整的首選入口：

- **傳輸、保存或訊息種類**：先改 `ConvoHub.Models/Class1.cs`，再同步 Hub、Controller 與 Client。
- **服務路由、靜態檔或 middleware**：改 `ConvoHub.Service/Program.cs`。
- **上傳規則與歷史 API**：改 `ConvoHub.Service/Controllers/ChatController.cs`。
- **即時訊息規則**：改 `ConvoHub.Service/ChatHub.cs`。
- **視覺與互動**：改 `ConvoHub.Client/MainWindow.xaml`；其事件與呈現邏輯在對應 `.xaml.cs`。

`WeatherForecast` 類別與 Controller 是 ASP.NET 範本遺留，與聊天室無關；若未來移除，先確認沒有外部工具依賴 Swagger 的範例端點。
