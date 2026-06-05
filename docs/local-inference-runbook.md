# Local inference runbook — Ollama on the second B70

How to bring up the local model Leopard talks to. Specialized inference (which GPU,
which backend) is a runtime/lab concern that sits *below* Leopard's seam — Leopard just
proxies `http://localhost:11434` (see `src/leopard-host/Program.cs`). This file records
the exact incantation so it isn't rediscovered.

## Launch Ollama pinned to the second B70 (32GB VRAM)

```powershell
$env:OLLAMA_MODELS = "D:\work\models"       # where the model blobs live
$env:GGML_VK_VISIBLE_DEVICES = "1"          # Vulkan backend, device 1 = the 2nd B70
ollama serve
# then, in another shell (or after serve is up):
ollama run mistral-small-24b-64k-b70
```

## Why these settings

- **`GGML_VK_VISIBLE_DEVICES=1`** — Ollama runs on the **Vulkan** backend (not ROCm).
  `1` is the *second* card. Pinning the model to the second B70 leaves the primary GPU
  free for the game, so inference runs **without affecting gameplay** — the thing that
  makes "ask your own data locally, while playing" actually viable.
- **`OLLAMA_MODELS=D:\work\models`** — keeps the model blobs on D: instead of the
  default profile location.
- **Model:** `mistral-small-24b-64k-b70` — Mistral Small 24B, tagged for a **64k context
  window**, which loads consistently on the 32GB card. (Verified 2026-06-03.)

## Sanity checks

```powershell
# Is the model up and enumerable? (Leopard's Setup tab hits this same endpoint.)
curl http://localhost:11434/api/tags
```
