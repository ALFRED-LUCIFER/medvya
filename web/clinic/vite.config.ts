import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ command, isPreview }) => ({
  plugins: [react(), tailwindcss()],
  // Built (and previewed), the app is served by the API under /app/ (the public site owns /); the dev server stays at the root.
  base: command === 'build' || isPreview ? '/app/' : '/',
  // The API's CSP allows fonts from 'self' only; inlined data: fonts are blocked, so every asset stays a file.
  build: { assetsInlineLimit: 0 },
  // Same origin as the API in production (BFF, ADR-004); in development the dev server forwards API and sign-in paths.
  server: {
    // changeOrigin off: the API must see the dev server's host so the sign-in redirect comes back here.
    // ws: the live board's SignalR WebSocket under /v1 (MDV-P1-029).
    proxy: Object.fromEntries(['/bff', '/v1', '/signin-oidc'].map(p => [p, { target: 'http://localhost:5080', changeOrigin: false, ws: p === '/v1' }])),
  },
}))
