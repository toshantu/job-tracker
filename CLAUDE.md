# Job Application Tracker: standing rules for Claude Code

Monorepo: `backend/` (ASP.NET Core, .NET 10), `frontend/` (Next.js 16, TypeScript), `tests/JobTracker.Api.Tests/` (xUnit). Vercel deploys `frontend/` and Render deploys `backend/`, both from `main`. One Neon Postgres database serves local runs and production. For frontend work, `frontend/CLAUDE.md` imports `frontend/AGENTS.md`, which says to read the bundled Next.js docs before writing frontend code.

## Your role
- Verify and report: run builds, tests and live checks, and answer PASS or FAIL for every check in the prompt.
- Never redesign. Raise design doubts as questions at the end of your report.
- A plain bug (typo, casing, syntax) may be fixed only when the prompt allows edits; show the fix as a diff. When the prompt says "verify only", change nothing and show the fix under QUESTIONS.
- Do not commit or push unless the prompt says to.

## Secrets and personal data
- Never print secret or config values: `.env*` files, `appsettings*.json` values, User Secrets, connection strings, tokens, cookies or environment variable values. Names are fine.
- CV text, job-description text and model output never go in chat, logs, commits or test output. Tests use invented texts.
- Never set `JOBTRACKER_LIVE_GROQ`. It spends tokens and needs the real key.

## Git
- Never `git add .` or `git add -A`. Stage explicit paths, and only when `git status --short` shows exactly the intended files.
- The Windows working tree uses CRLF, so a trailing-whitespace grep flags nearly every line. Do not normalise line endings or strip whitespace across files.
- `next dev` may rewrite the managed block in `frontend/AGENTS.md`. Do not revert it; report it.

## Commands (Git Bash on Windows)
- Backend tests: `dotnet test tests/JobTracker.Api.Tests/JobTracker.Api.Tests.csproj`. They need no secrets, database or network.
- Frontend, run in `frontend/`: `npm run lint`, `npm test`, `npm run build`, then `npx tsc --noEmit`. The build type-checks test files, and `next.config.ts` throws unless `BACKEND_ORIGIN` is a valid http or https URL (local value in `frontend/.env.local`).
- Stop a server by port, not by `$!`: `netstat -ano | grep LISTENING | grep ":3000 "`, then `taskkill //PID <pid> //F //T`.
- Console tests capture ids in code. A placeholder id returns 404 before any handler runs, so the test passes silently.
- Never run `npm audit fix --force`: it would downgrade `eslint-config-next`.

## Render and other platforms
- Never retry a Render 429 (`x-render-routing: hibernate-rate-limited`). A Manual Deploy clears it. Keep test bursts small, and never ping to keep the service awake.
- A push that touches `backend/` redeploys Render; a frontend-only push does not. A new Render variable goes in before the push that needs it, with double underscores in its name.
- Vercel, Render, GitHub, Google and Groq settings change often. Search before designing against them, cite what you found and say what you did not re-fetch.
