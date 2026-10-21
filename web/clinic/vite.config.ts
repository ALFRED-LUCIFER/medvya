import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  // Same origin as the API in production (BFF, ADR-004); in development the dev server forwards API and sign-in paths.
  server: {
    // changeOrigin off: the API must see the dev server's host so the sign-in redirect comes back here.
    proxy: Object.fromEntries(['/bff', '/v1', '/signin-oidc'].map(p => [p, { target: 'http://localhost:5080', changeOrigin: false }])),
  },
})
