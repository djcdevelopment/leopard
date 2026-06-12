import { describe, it, expect } from 'vitest'
import { normalizeApi, parseModels, extractStreamToken, chatRequest } from './provider.js'

// The pure half of the provider layer: protocol selection, model-list mapping, and the
// per-line stream token extractors for both wire formats (Ollama NDJSON / OpenAI SSE).
// The fetch/reader plumbing is exercised live; these pin the parsing rules.

describe('provider protocol layer', () => {
  it('normalizeApi defaults to ollama and only accepts openai as the alternative', () => {
    expect(normalizeApi(undefined)).toBe('ollama')
    expect(normalizeApi(null)).toBe('ollama')
    expect(normalizeApi('ollama')).toBe('ollama')
    expect(normalizeApi('OpenAI')).toBe('openai')
    expect(normalizeApi('vllm')).toBe('ollama') // unknown values fall back to the default
  })

  it('parseModels maps each api shape to plain names', () => {
    expect(parseModels('ollama', { models: [{ name: 'qwen2.5:14b-instruct' }, { name: 'gpt-oss:20b' }] }))
      .toEqual(['qwen2.5:14b-instruct', 'gpt-oss:20b'])
    expect(parseModels('openai', { data: [{ id: 'mistral-b70' }, { id: 'vllama-critic' }] }))
      .toEqual(['mistral-b70', 'vllama-critic'])
    expect(parseModels('ollama', {})).toEqual([])
    expect(parseModels('openai', {})).toEqual([])
  })

  it('extractStreamToken reads ollama NDJSON lines', () => {
    expect(extractStreamToken('ollama', '{"message":{"content":"Hel"},"done":false}')).toBe('Hel')
    expect(extractStreamToken('ollama', '{"message":{"content":""},"done":true}')).toBeNull()
    expect(extractStreamToken('ollama', '')).toBeNull()
    expect(extractStreamToken('ollama', 'not json')).toBeNull()
  })

  it('extractStreamToken reads openai SSE lines and ignores [DONE] + non-data lines', () => {
    expect(extractStreamToken('openai', 'data: {"choices":[{"delta":{"content":"Hel"}}]}')).toBe('Hel')
    expect(extractStreamToken('openai', 'data: {"choices":[{"delta":{}}]}')).toBeNull() // role-only first chunk
    expect(extractStreamToken('openai', 'data: [DONE]')).toBeNull()
    expect(extractStreamToken('openai', ': keep-alive')).toBeNull()
    expect(extractStreamToken('openai', '')).toBeNull()
    // an NDJSON-style bare JSON line must NOT parse as openai (protocol discipline)
    expect(extractStreamToken('openai', '{"choices":[{"delta":{"content":"X"}}]}')).toBeNull()
  })

  it('chatRequest shapes the body per protocol', () => {
    const args = { model: 'm', messages: [{ role: 'user', content: 'q' }], temperature: 0.2 }
    const o = chatRequest('ollama', args)
    expect(o.path).toBe('/api/chat')
    expect(o.body).toEqual({ model: 'm', messages: args.messages, stream: true, options: { temperature: 0.2 } })
    const a = chatRequest('openai', args)
    expect(a.path).toBe('/v1/chat/completions')
    expect(a.body).toEqual({ model: 'm', messages: args.messages, stream: true, temperature: 0.2 })
  })
})
