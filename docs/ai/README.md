# docs/ai — AI Knowledge Base

A compact, factual corpus for LLM coding agents (Claude, Cursor, Codex, …) working on
**Dots and Figures**. Its purpose: understand the game **without re-analyzing the whole
codebase**.

## How an agent should use this

1. **Read `CLAUDE.md` (repo root) first** — the always-on short brief.
2. **Then read `docs/ai/INDEX.md`** — the one-page map of this KB.
3. **Open one topic file only if the task needs it** (see the table in `INDEX.md`).
4. **Open source files only for the change at hand** — not to re-derive facts already here.

## Source-of-truth order

`Assets/Scripts/**` (code) > `CLAUDE.md` > `docs/ai/**` > `.claude/skills/dots-and-figures/SKILL.md` (partly stale).

If this KB and the code disagree, **the code wins** — and fix the KB.

## Files

| File | Open when you need… |
|------|---------------------|
| `INDEX.md` | the map: what the game is, stack, file table |
| `glossary.md` | the precise meaning of a project term |
| `architecture.md` | who owns what, who calls whom |
| `data-model.md` | exact runtime state / fields / grid-vs-world |
| `mechanics.md` | the rules as checkable invariants |
| `flows.md` | step-by-step call flows |
| `file-index.md` | per-script navigation table (start here to locate code) |
| `invariants-and-pitfalls.md` | things agents routinely break (NEVER/ALWAYS/WHY) |
| `ui-and-build.md` | scenes, canvases, sorting orders, Android/build notes |
| `project-kb.json` | machine-readable knowledge graph (RAG / tools) |

`project-kb.json` is generated from these markdown files; keep them in sync.
