import { defineConfig } from '@playwright/test';

const production = process.env.EDITOR_PRODUCTION === '1';
const port = Number(process.env.EDITOR_TEST_PORT ?? (production ? 4173 : 5173));
export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  workers: 2,
  use: { baseURL: `http://127.0.0.1:${port}`, browserName: process.env.EDITOR_BROWSER === 'webkit' ? 'webkit' : 'chromium', viewport: { width: 1280, height: 850 } },
  webServer: {
    command: production ? `npm run preview -- --port ${port}` : `npm run dev -- --port ${port} --strictPort`,
    url: `http://127.0.0.1:${port}`,
    reuseExistingServer: false,
  },
});
