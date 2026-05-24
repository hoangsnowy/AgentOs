# Phase 7 — Agent Studio (Blazor Server, realtime UI + orchestration editor)

> Status: ✅ Build + test xanh (.NET 10.0.202, 154 pass / 4 skip live-smoke), chạy live verify trên trình duyệt OK.
> Quality Loop demo đúng: QA fail vòng 1 (0.62) → pass vòng 2 (0.92), 5 tác tử hiển thị realtime.

## Mục tiêu

Bổ sung lớp trình diễn realtime cho prototype: một UI Blazor Server cho phép nhập user story,
bấm chạy và **xem 5 tác tử phối hợp theo thời gian thực** (timeline agent + vòng QA + điểm số),
cùng panel metrics (token / cost / latency) và tab xem artefact sinh ra (Requirement / Code / Test / QA).

Phục vụ trực tiếp **Mục 2.5** (kịch bản thực nghiệm: input → output → đánh giá nhìn thấy được) và
**Mục 2.6** (số liệu hiệu quả), và là công cụ demo trực quan cho buổi bảo vệ (cho thấy pattern
Leader–Specialists–Quality Loop chạy sống).

## Kế hoạch (đã thống nhất)

- Host: **Blazor Server** (Blazor Web App, InteractiveServer render mode). Circuit chạy trên SignalR
  ⇒ realtime push "miễn phí", không cần viết Hub thủ công.
- Tích hợp: project Web **tham chiếu Infrastructure trực tiếp**, gọi thẳng `PipelineOrchestrator`.
- Cơ chế realtime: orchestrator phát sự kiện tiến trình; component lắng nghe rồi `StateHasChanged()`.

## Deliverable

### Lõi (giữ TreatWarningsAsErrors=true)

- `Domain/Pipeline/PipelineProgressEvent.cs` — record sự kiện tiến trình + enum `PipelineStage`, `PipelinePhase`.
- `Application/Pipeline/IPipelineProgressSink.cs` — cổng phát tiến trình (abstraction).
- `Infrastructure/Pipeline/NullPipelineProgressSink.cs` — bản no-op, đăng ký mặc định trong `AddAgents`
  (qua `TryAddSingleton`) ⇒ API + 80 test cũ KHÔNG đổi hành vi.
- `Infrastructure/Orchestration/PipelineOrchestrator.cs` — thêm tham số ctor **tuỳ chọn**
  `IPipelineProgressSink? progress = null` (non-breaking với 4 call-site test dùng `new PipelineOrchestrator(...6 args...)`).
  Chèn `ReportAsync` ở: Requirement start/done, mỗi vòng Coding/Testing/QA start/done, QA-completed (kèm score),
  Aggregate, và các nhánh Failed.

### Project trình diễn `src/AgenticSdlc.Web` (TreatWarningsAsErrors=false)

- `AgenticSdlc.Web.csproj`, `Program.cs`, `appsettings.json`, `Properties/launchSettings.json` (cổng 5180).
- `Components/`: `App.razor`, `Routes.razor`, `_Imports.razor`, `Layout/MainLayout.razor`,
  `Pages/PipelineStudio.razor` (trang chính, route `/`).
- `wwwroot/app.css` (theme tối).
- `Services/CircuitPipelineProgress.cs` — `IPipelineProgressSink` scoped theo circuit; chuyển sự kiện cho
  `Listener` mà component đăng ký.
- `Services/CodeHighlighter.cs` — tô màu cú pháp C# nhẹ, an toàn XSS (HTML-encode trước, regex 1 lượt).
- `Services/Demo/DemoRunContext.cs` — cờ `UseDemo` theo circuit.
- `Services/Demo/DemoLlmClient.cs` — nguồn LLM "canned" offline: nhận biết agent qua system prompt
  ("Bạn là … Agent"), trả JSON hợp lệ theo schema, **mô phỏng QA fail vòng 1 → pass vòng 2** (cấu hình
  `Demo:FailingQaRounds`, `Demo:StepDelayMs`).
- `Services/Demo/DemoAwareLlmClientFactory.cs` — override `ILlmClientFactory`: `UseDemo` ⇒ DemoLlmClient,
  ngược lại uỷ quyền `LlmClientFactory` thật (Claude/Azure theo appsettings).

### Solution

- `AgenticSdlc.sln` — đã thêm project `AgenticSdlc.Web` (GUID `...006`) vào solution folder `src`.

## Quyết định kỹ thuật

- **Vì sao không viết SignalR Hub riêng?** Blazor Server đã có circuit (SignalR) sẵn; dùng
  `IPipelineProgressSink` scoped + `InvokeAsync(StateHasChanged)` là đủ realtime, ít code hơn, idiomatic.
  (Nếu sau cần nhiều người cùng xem 1 lần chạy mới cần Hub broadcast.)
- **Vì sao có DemoLlmClient mà không dùng Mock fixture?** `MockLlmClient` hash-based, miss ⇒ "stub-response"
  (không phải JSON) ⇒ agent parse fail. Fixture lại brittle (xem Phase 5). DemoLlmClient trả JSON đúng
  schema, deterministic, và minh hoạ được Quality Loop — chạy offline không cần API key.
- **Vì sao tham số progress để optional (= null → NullPipelineProgressSink.Instance)?** Để 4 call-site test
  `new PipelineOrchestrator(...)` 6 tham số vẫn biên dịch; DI (API/Web) vẫn inject sink đã đăng ký.
- **Chuyển nguồn LLM lúc chạy:** trang resolve `IOrchestratorAgent` từ `IServiceProvider` của circuit
  SAU khi đặt `DemoRunContext.UseDemo` (vì agent đọc `factory.Create` ở constructor).

## Cách chạy

```bash
# tại D:\LuanVan\prototype
dotnet build AgenticSdlc.sln -c Release
dotnet test  AgenticSdlc.sln -c Release          # kỳ vọng: 80 test cũ vẫn xanh
dotnet run --project src/AgenticSdlc.Web         # mở http://localhost:5180
```

Nhập user story → bấm "Chạy pipeline" (mặc định chế độ Demo offline) → quan sát timeline realtime.

## Việc còn lại (TODO cho phiên sau)

- [x] Build + test xác nhận; sửa lỗi cú pháp/analyzer nhỏ nếu có (project Web đã tắt TWAE nên ít rủi ro).
- [x] Tick checkbox Phase 7 trong `README.md` mục "Lộ trình" sau khi build xanh.

> Các mục tuỳ chọn (export, test interpreter, …) đã chuyển xuống mục **Backlog** bên dưới.

### Bug đã sửa khi verify live

`DemoLlmClient` nhận diện agent bằng `Contains(sys, "Testing Agent")` — nhưng system prompt
của Testing Agent có câu *"…cho code đã được **Coding Agent** sinh ra…"*, mà nhánh `"Coding Agent"`
được kiểm tra TRƯỚC ⇒ lần gọi Testing bị route nhầm sang `CodeJson` (shape code-artifact, thiếu
`happyPathCount/edgeCaseCount/errorCaseCount`) ⇒ fail schema `test-artifact.v1`, pipeline đứt ở bước Testing.
Sửa: khớp theo dòng định danh đầy đủ `"Bạn là <X> Agent"` (đúng ý đồ ghi trong header file) thay vì suffix trần.

## Phase 7b — Orchestration Studio (editor kéo-thả kiểu Synapse)

> Status: ✅ Build + test xanh (154 pass), verify live trên trình duyệt OK.

Bổ sung editor node-graph trực quan (cảm hứng từ Synapse) thay cho timeline tuyến tính:
canvas nền tối, card từng step, nối edge có nhãn route, minimap, vòng QA vẽ thành chu trình.

- **Route**: `/` = Orchestration Studio (editor); `/timeline` = view realtime cũ (giữ nguyên).
- **Dependency mới**: `Z.Blazor.Diagrams` 3.0.4.1 (lib node-editor Blazor thuần, MIT) — chỉ trong project Web.
- **Mô hình** (`Orchestrations/`): `OrchestrationGraph` / `GraphNode` / `GraphEdge` / `StepType`;
  `OrchestrationStore` (singleton, seed + lưu `App_Data/orchestrations.json`); `StepNodeModel : NodeModel`.
- **Seed**: `5-Agent SDLC Pipeline` (ánh xạ KC1–KC5, Run chạy được) + `Strict Developer` (mô phỏng ảnh Synapse).
- **Editor**: palette "Add step" (14 loại) → thêm node; kéo di chuyển; nối port vẽ edge; inspector sửa
  tiêu đề/role/in/out/max/route; Save / New / Duplicate / Delete; selector đổi orchestration; zoom + fit.
- **Run** (`OrchestrationStudio.Run`): thông dịch đồ thị — đi từ node Start theo edge, mỗi node 1 lượt LLM
  (Demo offline), sáng node realtime + stream Run Log. Evaluator rẽ nhánh theo `isConsistent` ⇒ minh hoạ
  vòng lặp QA (fail vòng 1 → loop → pass vòng 2). Model gán theo vai trò ⇒ cost ước tính khớp hybrid LLM.
- **Quyết định**: dùng `Z.Blazor.Diagrams` (node = Razor component) thay vì nhúng React/Svelte Flow — giữ toàn
  bộ trong C#/Blazor, bind realtime dễ; canvas tái tạo qua `@key="_graph.Id"` khi đổi orchestration.

## Backlog — phát triển sau (NGOÀI scope hiện tại)

Các mục dưới đây cố ý để **stub / trang trí** trong bản này (đủ minh hoạ luận văn). Ghi lại để mở rộng sau:

### UI hiện chỉ trang trí
- [ ] **Sidebar 18 mục** (General, Build Agents, MCP Servers, Tool Builder, Repos, DB Configs, Models,
  Messaging, Integrations, Schedules, Vault, Usage, Import/Export, Logs, Memory, API Keys, Support & Docs) —
  hiện chỉ "Orchestrations" hoạt động; còn lại bấm không làm gì.
- [ ] **Build with AI** — nút disabled. Ý tưởng: nhập mô tả → LLM sinh ra đồ thị orchestration tự động.
- [ ] **Deploy as Agent** — nút disabled. Ý tưởng: export orchestration thành endpoint/agent chạy được.

### Run / thực thi (đáng làm nhất cho luận văn)
- [ ] **Run gọi LLM thật** — thêm toggle Demo/Real như trang `/timeline` (hiện orchestration chỉ chạy Demo offline).
- [ ] **Recent Runs lưu lịch sử** — mỗi lần Run lưu token/cost/latency/thời điểm; tab xem lại + so sánh.
- [ ] **Guardrails enforce** — hiện chỉ hiển thị text; chưa chặn/kiểm lúc Run.

### Semantics node nâng cao (giờ chạy như LLM generic)
- [ ] **Tool** gọi tool/hàm thật · **Parallel** fork nhánh thật · **Merge** gộp · **Transform** map dữ liệu ·
  **Extract JSON** · **Switch / If-Else** rẽ nhánh theo điều kiện thật · **Print** log giá trị.

### Khác
- [ ] Nút **export kết quả** ra `.md/.json` cho phụ lục.
- [ ] **Test** cho `PipelineOrchestrator` thứ tự sự kiện + cho `OrchestrationStudio.Run` interpreter.

## Lưu ý dọn dẹp

Trong lúc tạo code có 1 file bị ghi nhầm vào `OneDrive\...\Documents\Claude\Projects\LuanVan\prototype\src\...`
(không phải repo thật). Repo thật ở `D:\LuanVan`. Xoá thư mục thừa đó nếu còn.
