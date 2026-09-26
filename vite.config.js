import { defineConfig } from 'vite';
import { resolve } from 'node:path';

export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        main: resolve('index.html'),
        models: resolve('models-preview.html'),
        builder: resolve('builder.html'),
      },
      output: { manualChunks: { three: ['three'] } },
    },
  },
});
