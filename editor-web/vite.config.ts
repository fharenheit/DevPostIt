import { defineConfig } from 'vite';

export default defineConfig(({ command }) => ({
  base: './',
  build: { rollupOptions: { input: { main: 'index.html', settings: 'settings.html', search: 'search.html' } } },
  plugins: [{
    name: 'local-editor-csp',
    transformIndexHtml(html) {
      const connect = command === 'serve' ? `'self' ws://127.0.0.1:${process.env.EDITOR_TEST_PORT ?? 5173}` : "'self'";
      const policy = `default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: https://devpostit-assets.local; worker-src 'self' blob:; connect-src ${connect}; base-uri 'none'; form-action 'none'`;
      return html.replace('<head>', `<head><meta http-equiv="Content-Security-Policy" content="${policy}">`);
    },
  }],
}));
