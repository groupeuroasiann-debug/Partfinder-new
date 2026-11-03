import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const rawTarget = env.VITE_API_URL || 'https://backend.partfinder.store'
  const target = /^https?:\/\//i.test(rawTarget) ? rawTarget : `https://${rawTarget}`

  return {
    plugins: [react()],
    preview: {
      host: '127.0.0.1',
      port: 4173,
      strictPort: true,
    },
    server: {
      host: '127.0.0.1',
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': {
          target,
          changeOrigin: true,
        },
      },
    },
  }
})

