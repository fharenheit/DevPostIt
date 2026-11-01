import { defineConfig } from '@playwright/test';

const production = process.env.EDITOR_PRODUCTION === '1';
const port = production ? 4173 : 5173;
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  workers: 2,
  use: { baseURL: `http://127.0.0.1:${port}`, browserName: 'chromium', viewport: { width: 1280, height: 850 } },
  webServer: {
    command: production ? 'npm run preview' : 'npm run dev -- --port 5173 --strictPort',
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: false,
  },
});
