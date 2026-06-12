// Tiny dual-protocol mock provider for verifying the /llm config plumbing.
// Serves both shapes so either api kind can be pointed at it:
//   GET  /v1/models            -> OpenAI model list
//   POST /v1/chat/completions  -> two SSE chunks + [DONE]
//   GET  /api/tags             -> Ollama model list
// Usage: node tools/mock-provider.mjs [port]   (default 18123)
import http from 'node:http'

const port = Number(process.argv[2] || 18123)

const server = http.createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/v1/models') {
    res.writeHead(200, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify({ object: 'list', data: [{ id: 'mock-openai-model', object: 'model' }] }))
    return
  }
  if (req.method === 'GET' && req.url === '/api/tags') {
    res.writeHead(200, { 'Content-Type': 'application/json' })
    res.end(JSON.stringify({ models: [{ name: 'mock-ollama-model' }] }))
    return
  }
  if (req.method === 'POST' && req.url === '/v1/chat/completions') {
    res.writeHead(200, { 'Content-Type': 'text/event-stream' })
    res.write('data: {"choices":[{"delta":{"role":"assistant"}}]}\n\n')
    res.write('data: {"choices":[{"delta":{"content":"Hello "}}]}\n\n')
    res.write('data: {"choices":[{"delta":{"content":"from mock"}}]}\n\n')
    res.write('data: [DONE]\n\n')
    res.end()
    return
  }
  res.writeHead(404, { 'Content-Type': 'application/json' })
  res.end(JSON.stringify({ error: 'mock: no such route' }))
})

server.listen(port, '127.0.0.1', () => console.log(`mock provider on http://127.0.0.1:${port}`))
