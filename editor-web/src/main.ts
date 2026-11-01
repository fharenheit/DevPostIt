import { Editor } from './editor';
import { fonts, readFontSettings, saveFontSettings, type FontKey } from './fonts';
import { connectEditor, type EditorConnection } from './bridge';
import './style.css';

declare global { interface Window { hunminEditor: Editor } }
document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
  <header class="app-header"><div class="brand"><span class="brand-mark">DP</span><div><strong>DevPostIt</strong><small>생각을 쓰고, 원문 그대로.</small></div></div><span class="stage">Markdown 노트</span></header>
  <section class="workspace" aria-label="Markdown 편집기">
    <div class="toolbar">
      <div class="modes" role="group" aria-label="보기 방식"><button id="source-mode" aria-pressed="true">소스 편집</button><button id="preview-mode" aria-pressed="false">미리보기</button></div>
      <div class="font-controls"><div class="text-size" role="group" aria-label="전체 글자 크기"><button id="smaller" aria-label="글자 작게" title="모든 글자를 한 단계 작게">−</button><output id="text-size" aria-label="기준 글자 크기">14</output><button id="larger" aria-label="글자 크게" title="모든 글자를 한 단계 크게">+</button></div><label>편집 글꼴 <select id="font"><option value="d2">D2 Ligature</option><option value="jetbrains">JetBrains Mono</option><option value="system">시스템 고정폭</option></select></label><label class="ligatures"><input id="ligatures" type="checkbox" checked> 합자 사용</label></div>
    </div>
    <div class="document-heading"><span class="document-icon">MD</span><div><h1>나의 첫 메모</h1><p>Markdown으로 작성하고 미리보기로 확인하세요.</p></div></div>
    <div class="code-tools"><label>코드 언어 <select id="code-language"><option value="">블록 언어 자동</option><option value="json">JSON</option><option value="yaml">YAML</option><option value="java">Java</option><option value="js">JavaScript</option><option value="ts">TypeScript</option><option value="html">HTML</option><option value="css">CSS</option></select></label><button id="format-code" title="커서가 있는 코드 블록 정리 (Ctrl+Alt+F)">코드 정리</button><span id="format-status" role="status"></span></div>
    <div class="editor-area"><div id="source"></div><article id="preview" class="preview" aria-label="Markdown 미리보기" hidden></article></div>
    <footer><span id="status" role="status">편집 준비</span><span id="host-status" role="status">변경 내용은 이 창에서만 유지됩니다.</span></footer>
  </section>`;

const sourceButton = document.querySelector<HTMLButtonElement>('#source-mode')!;
const previewButton = document.querySelector<HTMLButtonElement>('#preview-mode')!;
const status = document.querySelector<HTMLElement>('#status')!;
let connection: EditorConnection | undefined;
const editor = new Editor(document.querySelector('#source')!, document.querySelector('#preview')!, (mode, draft) => {
  sourceButton.setAttribute('aria-pressed', String(mode === 'source'));
  previewButton.setAttribute('aria-pressed', String(mode === 'preview'));
  status.textContent = `${draft.dirty ? '편집됨' : '원문 유지'} · ${draft.markdown.length.toLocaleString('ko-KR')}자`;
  connection?.changed();
});
sourceButton.addEventListener('click', () => editor.setMode('source'));
previewButton.addEventListener('click', () => editor.setMode('preview'));
async function formatCode(): Promise<void> {
  const button = document.querySelector<HTMLButtonElement>('#format-code')!;
  button.disabled = true;
  document.querySelector('#format-status')!.textContent = await editor.formatCodeBlock(document.querySelector<HTMLSelectElement>('#code-language')!.value);
  button.disabled = false;
}
document.querySelector('#format-code')!.addEventListener('click', () => void formatCode());
window.addEventListener('keydown', event => {
  if (event.ctrlKey && event.altKey && event.key.toLowerCase() === 'f') { event.preventDefault(); void formatCode(); }
});

const fontSelect = document.querySelector<HTMLSelectElement>('#font')!;
const ligatures = document.querySelector<HTMLInputElement>('#ligatures')!;
const settings = readFontSettings();
const smaller = document.querySelector<HTMLButtonElement>('#smaller')!;
const larger = document.querySelector<HTMLButtonElement>('#larger')!;
let textSize = settings.size;
fontSelect.value = settings.font;
ligatures.checked = settings.ligatures;
function applyFont(): void {
  const font = fontSelect.value as FontKey;
  const enabled = ligatures.checked;
  document.documentElement.style.fontSize = `${textSize}px`;
  editor.setTextSize(textSize - 2);
  document.querySelector('#text-size')!.textContent = String(textSize);
  smaller.disabled = textSize <= 10;
  larger.disabled = textSize >= 24;
  editor.setFont(fonts[font], enabled);
  saveFontSettings({ font, ligatures: enabled, size: textSize });
  if (font !== 'system') void document.fonts.load(`16px "${font === 'd2' ? 'D2 Ligature' : 'JetBrains Mono'}"`).then(() => {
    editor.setFont(fonts[fontSelect.value as FontKey], ligatures.checked);
  });
}
fontSelect.addEventListener('change', applyFont);
ligatures.addEventListener('change', applyFont);
smaller.addEventListener('click', () => { textSize = Math.max(10, textSize - 1); applyFont(); });
larger.addEventListener('click', () => { textSize = Math.min(24, textSize + 1); applyFont(); });
applyFont();
editor.load('welcome', '# 오늘의 메모\n\n작은 생각도, 잊기 전에 남겨 두세요.\n\n## 오늘 할 일\n\n- [ ] 아이디어 정리하기\n- [x] 첫 메모 시작하기\n\n> 원문은 그대로, 보기만 편하게.\n\n```ts\nconst note = "안녕하세요";\nconst ready = count >= 1;\n```\n', 0);
window.hunminEditor = editor;
connection = connectEditor(editor, message => { document.querySelector('#host-status')!.textContent = message; });
window.addEventListener('keydown', event => {
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault();
    if (connection) connection.requestSave();
    else document.querySelector('#host-status')!.textContent = '파일 저장은 앱 연결 후 지원됩니다.';
  }
});
