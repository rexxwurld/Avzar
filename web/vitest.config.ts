import { defineConfig } from 'vitest/config';
import path from 'node:path';

export default defineConfig({
  resolve: { alias: { '@': path.resolve(__dirname, 'src') } },
  test: {
    // The app's unit tests live beside the pure view-model/helpers under src.
    // Keep the legacy test/ directory enabled for older suites as well.
    include: ['src/**/*.test.ts', 'src/**/*.test.tsx', 'test/**/*.test.ts', 'test/**/*.test.tsx'],
    coverage: { reporter: ['text', 'html'], include: ['src/lib/**/*.ts'] },
  },
});
