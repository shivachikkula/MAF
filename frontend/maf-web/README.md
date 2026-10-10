# maf-web

Angular 22 chat UI for the MAF Knowledge Assistant. It calls the Agents API
(`POST /api/chat/stream`) and renders the multi-agent run live: tool calls, each
agent's progress, and the final Markdown answer (sanitized with DOMPurify).

```bash
npm ci
npm start          # http://localhost:4200, proxies /api → http://localhost:5103 (proxy.conf.json)
npm test           # Vitest unit tests
npm run build      # production build → dist/maf-web/browser
```

Key files:

| File | Purpose |
|------|---------|
| `src/app/chat/chat-api.service.ts` | `fetch` + `ReadableStream` SSE client (EventSource can't POST) |
| `src/app/chat/sse-parser.ts` | Incremental, spec-compliant SSE parser |
| `src/app/chat/chat.store.ts` | Signal-based conversation state; maps stream events to UI state |
| `src/app/chat/chat.component.*` | Chat view, agent activity panel, composer |
| `nginx.conf` | Production server: SPA hosting, `/api` reverse proxy with SSE buffering off, security headers |
