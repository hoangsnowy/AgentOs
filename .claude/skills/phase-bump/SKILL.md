---
name: phase-bump
description: >
  Tick checkbox "Phase N" trong README.md roadmap của agentic-sdlc-net, sinh doc
  docs/PHASE_N.md skeleton, commit "docs: tick Phase N done" theo convention dự án.
  Update MEMORY.md project state nếu có. Use when user says "bump phase", "phase N done",
  "tick phase N", "/phase-bump N", "đánh dấu phase N xong". Auto-trigger khi tất cả task
  trong 1 phase commit xong.
---

Đánh dấu 1 phase trong roadmap luận văn là DONE + sinh doc + commit.

## Khi nào dùng

- User vừa commit xong toàn bộ scope 1 phase.
- User explicit: "tick phase N", "/phase-bump N".
- Auto: sau khi `dotnet test` pass và git status sạch trên branch phase đó.

## Input

1. **Số phase** (1-6). Nếu user không cho, đọc README section "Lộ trình", tìm phase `[x]` cuối → phase tiếp theo = candidate.
2. **Confirm**: hỏi user check 1 lần ("Phase {N} = `{tên}` — đúng phase này không?") trước khi commit.

## Steps

### 1. Verify phase chưa tick

```bash
grep -n "Phase $N" D:/LuanVan/prototype/README.md
```

Nếu đã `[x]` → từ chối, báo user.

### 2. Verify scope phase đã làm

Đọc README phase line để biết scope (vd `Phase 2 — LLM Gateway (ILlmClient + 2 impls + factory)`). Quick check:

```bash
git log --oneline | grep -i "phase $N"
dotnet test 2>&1 | tail -5
```

Test phải PASS. Nếu fail → từ chối tick, báo user fix trước.

### 3. Tick checkbox

Edit `README.md`:

```diff
- - [ ] Phase $N — ...
+ - [x] Phase $N — ...
```

### 4. Tạo doc phase (nếu chưa có)

File `docs/PHASE_$N.md`. Template:

```markdown
# Phase $N — {Tên phase}

> Status: ✅ Done — {YYYY-MM-DD}

## Mục tiêu

{Trích từ README + Mục 2.4 luận văn nếu user cho reference}

## Deliverable

- {list các file/feature chính tạo ra}

## Test coverage

- {N} unit test trong `tests/AgenticSdlc.Tests/{namespace}/`
- {coverage % nếu có report}

## Quyết định kỹ thuật

- {decision 1 — why}
- {decision 2 — why}

## Tham chiếu luận văn

- Mục {x.y}: {tên mục}
- Bảng {x.z}: {tên bảng nếu có}

## Phase tiếp theo

→ Phase {N+1} — {scope từ README roadmap}
```

Nếu user không cho thông tin, fill `TODO:` cho phần "Quyết định kỹ thuật" + "Tham chiếu luận văn".

### 5. Update MEMORY.md (nếu tồn tại)

File: `C:\Users\ADMIN\.claude\projects\D--LuanVan-prototype\memory\MEMORY.md`.

Tìm entry kiểu "phase-status" hoặc "project-roadmap"; nếu có → update phase number. Nếu chưa → thêm 1 line index trỏ tới memory file mới.

### 6. Commit

2 commit riêng (giữ history sạch):

```bash
git add README.md
git commit -m "docs: tick Phase $N done in roadmap"

git add docs/PHASE_$N.md
git commit -m "docs(phase-$N): add Phase $N summary doc"
```

Co-Author Claude theo convention chung repo.

### 7. (Optional) Tag

Nếu user xác nhận phase milestone:

```bash
git tag -a phase-$N -m "Phase $N done: {tên phase}"
# KHÔNG push tag tự động — hỏi user.
```

## Verification

```bash
grep "Phase $N" README.md          # phải show [x]
ls docs/PHASE_$N.md                # phải exist
git log -3 --oneline               # 2 commit mới
```

## Boundaries

- KHÔNG bump phase nếu test fail.
- KHÔNG tự push lên GitHub — user quyết định.
- KHÔNG skip phase (vd bump Phase 4 khi Phase 3 chưa `[x]`) trừ khi user explicit override.
- Doc phase tiếng Việt OK (project bilingual, README có English summary).
