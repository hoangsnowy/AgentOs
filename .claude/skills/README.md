# Project skills — agentic-sdlc-net

Custom skills cho prototype luận văn này. Ship trong repo nên mọi session Claude Code mở project tự load.

| Skill | Trigger | Khi nào |
|---|---|---|
| **agent-scaffold** | `/agent-scaffold X`, "scaffold agent X" | Phase 3-4: thêm agent mới (Requirement / Coding / Testing / QA / Orchestrator / custom). Sinh interface + impl + DI + test + fixture stub. |
| **fixture-record** | `/fixture-record`, "record fixture for X" | Khi cần freeze response thật vào `tests/fixtures/llm/<hash>.json` cho MockLlmClient. |
| **phase-bump** | `/phase-bump N`, "tick phase N" | Đánh dấu phase N DONE trong README, sinh `docs/PHASE_N.md`, commit theo convention. |
| **kc-bench** | `/kc-bench KC1`, "chạy KC1-KC5" | Phase 5: benchmark luận văn Mục 2.5, xuất `.xlsx` + `.md` report. |
| **prompt-tune** | `/prompt-tune {Name}Agent`, "tune prompt for X" | Eval prompt A vs B, batch fixture, report drift. Auto khi pass-rate < 70%. |
| **cost-report** | `/cost-report week`, "báo cáo chi phí" | Aggregate log → `.xlsx` cho luận văn Chương 4 (so sánh hybrid LLM). |

## Format

Mỗi skill = directory với `SKILL.md` (YAML frontmatter `name` + `description` + body markdown). Claude Code đọc tự động khi mở project root.

## Sửa skill

Sửa `SKILL.md` trực tiếp. `description` field quyết định khi nào skill auto-trigger — viết cụ thể để match cao.

## Thêm skill

```bash
mkdir .claude/skills/<new-name>
# Tạo SKILL.md với frontmatter + body
```

Hoặc invoke `anthropic-skills:skill-creator` (built-in) cho guided creation.
