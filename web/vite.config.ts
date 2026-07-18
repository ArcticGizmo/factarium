import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

// The SPA builds into the API's wwwroot so the single-exe and container image
// serve the same assets the dev server (with /api proxy) exercises.
export default defineConfig(() => {
  const apiProxy = process.env.VITE_API_PROXY || 'http://localhost:4600';
  return {
    plugins: [vue()],
    server: {
      port: 4601,
      proxy: {
        '/api': { target: apiProxy, changeOrigin: true }
      }
    },
    build: {
      outDir: '../src/Factarium.Api/wwwroot',
      emptyOutDir: true
    }
  };
});
