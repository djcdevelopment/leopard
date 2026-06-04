import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Per docs/provider-contract.md: Ollama is the default provider. We reach it
// through a dev-server proxy (/ollama -> :11434) so the browser never makes a
// cross-origin call — no Ollama CORS config required. A packaged build will use
// a thin host instead; this proxy is dev-only.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5273,
    strictPort: true,
    proxy: {
      // The Leopard .NET host (Kestrel): lists logs, parses, serves box scores.
      '/api': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
      // The default inference provider (Ollama). Proxied so the browser makes no
      // cross-origin call — no Ollama CORS config needed.
      '/ollama': {
        target: 'http://localhost:11434',
        changeOrigin: true,
        rewrite: (p) => p.replace(/^\/ollama/, ''),
      },
    },
  },
})
