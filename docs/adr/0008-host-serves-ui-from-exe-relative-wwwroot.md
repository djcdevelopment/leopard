# ADR-0008: The host serves the UI from an exe-relative wwwroot (content root = AppContext.BaseDirectory)

Status: Accepted (2026-06-11 — exercised by every launch since the fix, including the
phase-2 verification launches; no regressions)

## Context

Observed 2026-06-11: launching `leopard-host.exe` via plain `Start-Process` (or any cwd other
than the project directory) rendered **HTTP 404 for the entire UI** in Production, while the
`/api/*` endpoints worked. Root cause: `WebApplication.CreateBuilder(args)` defaults the
content root — and therefore the static web root — to `Directory.GetCurrentDirectory()`, i.e.
the *launcher's* cwd, where no `wwwroot` exists.

The failure was masked in every prior session: Development launches resolve static files
through the StaticWebAssets manifest (which points at the source `wwwroot` regardless of cwd),
and the dev workflow uses Vite on :5273 anyway. The first plain Production double-click
exposed it. A desktop app whose UI depends on the launcher's working directory is wrong for
the product's "double-click .exe" promise.

## Decision

Two changes, paired:

1. **`Program.cs`**: construct the builder with
   `new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory }` —
   the host always serves from the exe's own directory, never the launcher's cwd.
2. **`leopard-host.csproj`**: `<None Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />`
   — the Web SDK only copies `wwwroot` on *publish*; this copies it on every *build*, so the
   bin output is self-contained.

## Consequences

- The exe works from any cwd, double-click, shortcut, or `Start-Process` — launch location is
  no longer part of the contract.
- The existing discipline "every `npm run build` is followed by `dotnet build src/leopard-host`"
  is now **structural, not just cache hygiene**: the dotnet build is the step that refreshes
  the bin copy of `wwwroot`. Skipping it definitively serves stale assets.
- A running `leopard-host.exe` instance now fails the build with MSB3027 (file lock on the exe
  being replaced) — stop the app before rebuilding.
- Bin output grows by the bundle (~250 KB) per build; negligible.
- Publish behavior is unchanged (publish already copied `wwwroot`).
