import { defineConfig } from 'vite';

const macos = process.env.EDITOR_TARGET === 'macos';

export default defineConfig(({ command }) => ({
  base: './',
  define: { 'import.meta.env.VITE_EDITOR_TARGET': JSON.stringify(macos ? 'macos' : 'windows') },
  // wasm 도구는 자기 폴더의 wasm 파일을 찾으므로 미리 묶지 않습니다. 나머지는 테스트 중 페이지가 다시 읽히지 않게 미리 묶습니다.
  optimizeDeps: {
    exclude: ['@wasm-fmt/clang-format', '@wasm-fmt/gofmt', '@wasm-fmt/ruff_fmt', '@wasm-fmt/shfmt'],
    include: ['sql-formatter', '@prettier/plugin-xml', '@prettier/plugin-php/standalone', 'mermaid'],
  },
  build: { outDir: macos ? 'dist-macos' : 'dist', rollupOptions: { input: { main: 'index.html', settings: 'settings.html', search: 'search.html', formatter: 'formatter.html', mindmap: 'mindmap.html', todo: 'todo.html', calendar: 'calendar.html' } } },
  plugins: [{
    name: 'local-editor-csp',
    transformIndexHtml(html) {
      const connect = command === 'serve' ? `'self' ws://127.0.0.1:${process.env.EDITOR_TEST_PORT ?? 5173}` : "'self'";
      const policy = `default-src 'none'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; font-src 'self'; img-src 'self' data: ${macos ? 'devpostit-assets:' : 'https://devpostit-assets.local'}; worker-src 'self' blob:; connect-src ${connect}; base-uri 'none'; form-action 'none'`;
      return html.replace('<head>', `<head><meta http-equiv="Content-Security-Policy" content="${policy}">`);
    },
  }],
}));
