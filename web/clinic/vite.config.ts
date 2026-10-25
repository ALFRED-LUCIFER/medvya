import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Same origin as the API in production (BFF, ADR-004); in development the dev server forwards API and sign-in paths.
  server: {
    // changeOrigin off: the API must see the dev server's host so the sign-in redirect comes back here.
    // ws: the live board's SignalR WebSocket under /v1 (MDV-P1-029).
    proxy: Object.fromEntries(['/bff', '/v1', '/signin-oidc'].map(p => [p, { target: 'http://localhost:5080', changeOrigin: false, ws: p === '/v1' }])),
  },
})
