import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// The platform console (MDV-P5-040): served by Host.Platform at its root in production, same origin as its BFF (ADR-004).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Host.Platform's CSP allows fonts from 'self' only; inlined data: fonts are blocked, so every asset stays a file.
  build: { assetsInlineLimit: 0 },
  // In development the dev server forwards the console host's sign-in and API paths (changeOrigin off: the sign-in redirect comes back here).
  server: {
    port: 5174,
    proxy: Object.fromEntries(['/bff', '/v1', '/signin-oidc'].map(p => [p, { target: 'http://localhost:5090', changeOrigin: false }])),
  },
})
