import { Editor } from './editor';
import { fonts, readFontSettings } from './fonts';
import { connectEditor, type EditorConnection } from './bridge';
import { formatToolbarHtml } from './format-toolbar';
import type { MarkdownAction } from './markdown-format';
import './style.css';

declare global { interface Window { hunminEditor: Editor } }
document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
  <section class="workspace" aria-label="Markdown 편집기">
    <header class="note-header">
      <div class="document-heading"><h1 title="더블클릭하면 이름을 변경합니다">나의 첫 메모</h1></div>
      <div class="modes" role="group" aria-label="보기 방식"><button id="source-mode" aria-pressed="true">소스 편집</button><button id="preview-mode" aria-pressed="false">미리보기</button></div>
    </header>
    ${formatToolbarHtml}
    <dialog id="rename-dialog"><form id="rename-form"><h2>이름 변경</h2><label for="note-name">메모 이름</label><input id="note-name" maxlength="200" required><p id="rename-error" role="status"></p><div class="dialog-actions"><button type="button" id="rename-cancel">취소</button><button type="submit" class="primary">확인</button></div></form></dialog>
    <div class="editor-area"><div id="source"></div><article id="preview" class="preview" aria-label="Markdown 미리보기" hidden></article></div>
    <footer class="note-bar">
      <div class="note-status"><span id="status" role="status">편집 준비</span><span id="host-status" role="status">변경 내용은 이 창에서만 유지됩니다.</span><span id="format-status" role="status"></span></div>
      <div class="code-tools"><label><span class="visually-hidden">코드 언어</span><select id="code-language" title="코드 언어"><option value="">블록 언어 자동</option><option value="json">JSON</option><option value="yaml">YAML</option><option value="java">Java</option><option value="js">JavaScript</option><option value="ts">TypeScript</option><option value="html">HTML</option><option value="css">CSS</option></select></label><button id="format-code" title="커서가 있는 코드 블록 정리 (Ctrl+Alt+F)">코드 정리</button></div>
    </footer>
  </section>`;

const sourceButton = document.querySelector<HTMLButtonElement>('#source-mode')!;
const previewButton = document.querySelector<HTMLButtonElement>('#preview-mode')!;
const status = document.querySelector<HTMLElement>('#status')!;
const formatButton = document.querySelector<HTMLButtonElement>('#format-code')!;
const languageSelect = document.querySelector<HTMLSelectElement>('#code-language')!;
const formatToolbar = document.querySelector<HTMLElement>('.format-toolbar')!;
const toolbarButtons = formatToolbar.querySelectorAll('button');
let formatting = false;
function syncCodeTools(): void {
  const previewing = previewButton.getAttribute('aria-pressed') === 'true';
  formatButton.disabled = formatting || previewing;
  languageSelect.disabled = previewing;
  toolbarButtons.forEach(button => { button.disabled = previewing; });
}
let connection: EditorConnection | undefined;
const editor = new Editor(document.querySelector('#source')!, document.querySelector('#preview')!, (mode, draft) => {
  sourceButton.setAttribute('aria-pressed', String(mode === 'source'));
  previewButton.setAttribute('aria-pressed', String(mode === 'preview'));
  syncCodeTools();
  status.textContent = `${draft.dirty ? '편집됨' : '원문 유지'} · ${draft.markdown.length.toLocaleString('ko-KR')}자`;
  document.querySelector('h1')!.textContent = editor.getTitle();
  document.title = editor.getTitle();
  connection?.changed();
});
sourceButton.addEventListener('click', () => editor.setMode('source'));
previewButton.addEventListener('click', () => editor.setMode('preview'));
// 버튼을 눌러도 편집기의 선택과 포커스를 유지합니다.
formatToolbar.addEventListener('mousedown', event => event.preventDefault());
formatToolbar.addEventListener('click', event => {
  const action = (event.target as Element).closest<HTMLButtonElement>('button')?.dataset.format;
  if (action) editor.applyFormat(action as MarkdownAction);
});
const renameDialog = document.querySelector<HTMLDialogElement>('#rename-dialog')!;
function openRename(): void {
  if (renameDialog.open) return;
  document.querySelector<HTMLInputElement>('#note-name')!.value = editor.getTitle();
  document.querySelector('#rename-error')!.textContent = '';
  renameDialog.showModal();
}
document.querySelector('h1')!.addEventListener('dblclick', openRename);
document.querySelector('#rename-cancel')!.addEventListener('click', () => renameDialog.close());
document.querySelector('#rename-form')!.addEventListener('submit', event => {
  event.preventDefault();
  try { editor.rename(document.querySelector<HTMLInputElement>('#note-name')!.value); renameDialog.close(); }
  catch (error) { document.querySelector('#rename-error')!.textContent = (error as Error).message; }
});
async function formatCode(): Promise<void> {
  formatting = true;
  syncCodeTools();
  document.querySelector('#format-status')!.textContent = await editor.formatCodeBlock(languageSelect.value);
  formatting = false;
  syncCodeTools();
}
formatButton.addEventListener('click', () => void formatCode());
window.addEventListener('keydown', event => {
  if (event.ctrlKey && event.altKey && event.key.toLowerCase() === 'f') { event.preventDefault(); void formatCode(); }
});

function applySettings(): void {
  const { font, ligatures, size } = readFontSettings();
  document.documentElement.style.fontSize = `${size}px`;
  editor.setTextSize(size - 2);
  editor.setFont(fonts[font], ligatures);
  if (font !== 'system') void document.fonts.load(`16px "${font === 'd2' ? 'D2 Ligature' : 'JetBrains Mono'}"`).then(() => {
    const latest = readFontSettings();
    editor.setFont(fonts[latest.font], latest.ligatures);
  });
}
window.addEventListener('storage', applySettings);
applySettings();
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
window.addEventListener('keydown', event => {
  if (event.ctrlKey && event.key === ',') { event.preventDefault(); connection?.command('window.settingsRequested'); }
});
