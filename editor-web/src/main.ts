import { isPrimary, shortcutLabel } from './platform';
import { Editor, normalizeTitle } from './editor';
import { applyTypography, defaultTextSize, editorTextSize, fontStack, maxTextSize, minTextSize, readFontSettings, saveFontSettings } from './fonts';
import { connectEditor, noteColors, type EditorConnection } from './bridge';
import { formatToolbarHtml } from './format-toolbar';
import { languageLabel, sourceLanguages } from './code-format';
import type { MarkdownAction } from './markdown-format';
import './style.css';

declare global { interface Window { hunminEditor: Editor } }
const colorNames: Record<string, string> = { yellow: '노랑', green: '초록', blue: '파랑', pink: '분홍', purple: '보라', gray: '회색' };
const pdfIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 2v8M5 7l3 3 3-3M3 13h10"/></svg>';
const folderIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 4h4l1.5 1.5H14V13H2z"/></svg>';
const diagramIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M5.5 2.5h5v3.5h-5zM2 10h5v3.5H2zM9 10h5v3.5H9zM8 6v2M4.5 10V8h7v2"/></svg>';
const captureIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M2 5.5h3l1-2h4l1 2h3v7.5H2zM8 7a2.2 2.2 0 1 1 0 4.4A2.2 2.2 0 0 1 8 7z"/></svg>';
const pinIcon = '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M6 2h4l-.5 5 2 2.5h-7l2-2.5zM8 9.5V14"/></svg>';
document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
  <section class="workspace" aria-label="Markdown 편집기">
    <header class="note-header">
      <div class="document-heading"><h1 title="더블클릭하면 이름을 변경합니다">나의 첫 메모</h1></div>
      <div class="view-tools">
        <div class="modes" role="group" aria-label="보기 방식"><button id="source-mode" aria-pressed="true">소스 편집</button><button id="preview-mode" aria-pressed="false">미리보기</button></div>
        <button type="button" id="note-images" aria-label="이미지 폴더 열기" title="이 메모의 이미지 폴더 열기">${folderIcon}</button>
      </div>
      <div class="note-actions">
        <button type="button" id="capture" aria-label="화면 캡처" title="화면 캡처 (${shortcutLabel('Ctrl+Shift+C')})">${captureIcon}</button>
        <button type="button" id="new-diagram" aria-label="draw.io 다이어그램 추가" title="draw.io 다이어그램 추가">${diagramIcon}</button>
        <button type="button" id="save-pdf" aria-label="PDF로 저장" title="PDF로 저장">${pdfIcon}</button>
        <button type="button" id="pin" aria-pressed="false" aria-label="항상 위" title="항상 위">${pinIcon}</button>
        <button type="button" id="note-color" aria-label="메모 색상" title="메모 색상" aria-expanded="false"><span class="swatch"></span></button>
        <div id="color-menu" role="group" aria-label="메모 색상 선택" hidden>${noteColors.map(color => `<button type="button" data-color="${color}" aria-label="${colorNames[color]}" aria-pressed="false" title="${colorNames[color]}"><span class="swatch"></span></button>`).join('')}</div>
      </div>
    </header>
    ${formatToolbarHtml}
    <dialog id="rename-dialog"><form id="rename-form"><h2>이름 변경</h2><label for="note-name">메모 이름</label><input id="note-name" maxlength="200" required><p id="rename-error" role="status"></p><div class="dialog-actions"><button type="button" id="rename-cancel">취소</button><button type="submit" class="primary">확인</button></div></form></dialog>
    <div class="editor-area"><div id="source"></div><article id="preview" class="preview" aria-label="Markdown 미리보기" hidden></article></div>
    <footer class="note-bar">
      <div class="note-status"><span id="status" role="status">편집 준비</span><span id="host-status" role="status">변경 내용은 이 창에서만 유지됩니다.</span><span id="format-status" role="status"></span></div>
      <div class="code-tools"><label><span class="visually-hidden">코드 언어</span><select id="code-language" title="코드 언어"><option value="">블록 언어 자동</option></select></label><button id="format-code" title="커서가 있는 코드 블록 정리 (${shortcutLabel('Ctrl+Alt+F')})">코드 정리</button></div>
    </footer>
  </section>`;

const sourceButton = document.querySelector<HTMLButtonElement>('#source-mode')!;
const previewButton = document.querySelector<HTMLButtonElement>('#preview-mode')!;
const status = document.querySelector<HTMLElement>('#status')!;
const formatButton = document.querySelector<HTMLButtonElement>('#format-code')!;
const languageSelect = document.querySelector<HTMLSelectElement>('#code-language')!;
for (const language of [...sourceLanguages, { value: 'markdown', label: 'Markdown' }, { value: 'mdx', label: 'MDX' }].sort((a, b) => a.label.localeCompare(b.label, 'en', { sensitivity: 'base' })))
  languageSelect.append(new Option(languageLabel(language), language.value));
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
  const button = (event.target as Element).closest<HTMLButtonElement>('button');
  if (button?.dataset.format) editor.applyFormat(button.dataset.format as MarkdownAction);
  else if (button?.dataset.editorAction) editor.runEditorAction(button.dataset.editorAction);
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
  document.querySelector('#rename-error')!.textContent = '';
  try {
    const title = normalizeTitle(document.querySelector<HTMLInputElement>('#note-name')!.value);
    if (connection) connection.rename(title);
    else { editor.rename(title); renameDialog.close(); }
  }
  catch (error) { document.querySelector('#rename-error')!.textContent = (error as Error).message; }
});
window.addEventListener('devpostit-rename-result', event => {
  const { message } = (event as CustomEvent<{ message?: string }>).detail;
  if (message) document.querySelector('#rename-error')!.textContent = message;
  else renameDialog.close();
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
  // macOS의 Option 조합은 다른 글자를 만들므로 키 위치로도 봅니다.
  if (event.ctrlKey && event.altKey && (event.key.toLowerCase() === 'f' || event.code === 'KeyF')) { event.preventDefault(); void formatCode(); }
});

function applySettings(): void {
  const settings = readFontSettings();
  const { font, ligatures, size, editor: preferences } = settings;
  editor.setPreferences(preferences);
  document.documentElement.style.fontSize = `${size}px`;
  applyTypography(settings);
  editor.setTextSize(editorTextSize(settings), settings.lineHeight);
  editor.setFont(fontStack(font), ligatures);
  const faces = font === 'd2' ? ['D2 Ligature'] : font === 'jetbrains' ? ['JetBrains Mono', 'D2 Hangul 120'] : font === 'system' ? ['D2 Hangul 110'] : ['D2 Ligature'];
  void Promise.all(faces.map(face => document.fonts.load(`16px "${face}"`, 'a가'))).then(() => {
    const latest = readFontSettings();
    editor.setFont(fontStack(latest.font), latest.ligatures);
  });
}
function changeTextSize(next: (size: number) => number): void {
  const settings = readFontSettings();
  saveFontSettings({ ...settings, size: Math.min(maxTextSize, Math.max(minTextSize, next(settings.size))) });
  applySettings();
}
// 전체 글자 크기 단축키입니다. 저장하면 다른 메모 창과 설정 창도 storage 이벤트로 따라옵니다.
window.addEventListener('keydown', event => {
  if (!isPrimary(event) || event.altKey || event.shiftKey && event.key !== '+') return;
  if (event.key === '=' || event.key === '+') changeTextSize(size => size + 1);
  else if (event.key === '-') changeTextSize(size => size - 1);
  else if (event.key === '0') changeTextSize(() => defaultTextSize);
  else return;
  event.preventDefault();
  event.stopPropagation();
}, true);
window.addEventListener('devpostit-preferences', applySettings);
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
  if (isPrimary(event) && event.key === ',') { event.preventDefault(); connection?.command('window.settingsRequested'); }
});
editor.imagePasteRequested = () => {
  if (connection) connection.command('note.imagePasteRequested');
  else document.querySelector('#host-status')!.textContent = '이미지 붙여넣기는 앱에서 사용할 수 있습니다.';
};
editor.linkRequested = url => {
  if (connection) connection.openLink(url);
  else document.querySelector('#host-status')!.textContent = '링크는 앱에서 열 수 있습니다.';
};
window.addEventListener('keydown', event => {
  if (isPrimary(event) && event.shiftKey && event.key.toLowerCase() === 'f') { event.preventDefault(); connection?.command('window.searchRequested'); }
}, true);

const pin = document.querySelector<HTMLButtonElement>('#pin')!;
const colorButton = document.querySelector<HTMLButtonElement>('#note-color')!;
const colorMenu = document.querySelector<HTMLElement>('#color-menu')!;
let appearance = { color: 'yellow', topmost: false };
function renderAppearance(): void {
  document.documentElement.dataset.noteColor = appearance.color;
  pin.setAttribute('aria-pressed', String(appearance.topmost));
  colorMenu.querySelectorAll<HTMLButtonElement>('button').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.color === appearance.color)));
}
function closeColorMenu(): void { colorMenu.hidden = true; colorButton.setAttribute('aria-expanded', 'false'); }
pin.addEventListener('click', () => {
  appearance = { ...appearance, topmost: !appearance.topmost };
  renderAppearance();
  connection?.appearance(appearance.color, appearance.topmost);
});
colorButton.addEventListener('click', () => {
  colorMenu.hidden = !colorMenu.hidden;
  colorButton.setAttribute('aria-expanded', String(!colorMenu.hidden));
});
colorMenu.addEventListener('click', event => {
  const color = (event.target as Element).closest<HTMLButtonElement>('button')?.dataset.color;
  if (!color) return;
  appearance = { ...appearance, color };
  renderAppearance();
  closeColorMenu();
  connection?.appearance(appearance.color, appearance.topmost);
});
window.addEventListener('keydown', event => { if (event.key === 'Escape') closeColorMenu(); });
window.addEventListener('devpostit-appearance', event => {
  appearance = (event as CustomEvent<{ color: string; topmost: boolean }>).detail;
  renderAppearance();
});
renderAppearance();

const savePdf = document.querySelector<HTMLButtonElement>('#save-pdf')!;
let modeBeforePdf: 'source' | 'preview' | null = null;
savePdf.addEventListener('click', async () => {
  if (!connection) { document.querySelector('#host-status')!.textContent = 'PDF 저장은 앱에서 사용할 수 있습니다.'; return; }
  if (modeBeforePdf) return;
  const previous = editor.getMode();
  if (!await editor.renderForPrint()) return;
  modeBeforePdf = previous;
  savePdf.disabled = true;
  connection.command('note.exportPdfRequested');
});
window.addEventListener('devpostit-pdf-finished', () => {
  if (modeBeforePdf) editor.setMode(modeBeforePdf);
  modeBeforePdf = null;
  savePdf.disabled = false;
});

function requestCapture(): void {
  if (connection) connection.command('window.captureRequested');
  else document.querySelector('#host-status')!.textContent = '화면 캡처는 앱에서 사용할 수 있습니다.';
}
document.querySelector('#capture')!.addEventListener('click', requestCapture);
window.addEventListener('keydown', event => {
  if (isPrimary(event) && event.shiftKey && !event.altKey && event.key.toLowerCase() === 'c') { event.preventDefault(); event.stopPropagation(); requestCapture(); }
}, true);

document.querySelector('#note-images')!.addEventListener('click', () => {
  if (connection) connection.command('note.openImagesRequested');
  else document.querySelector('#host-status')!.textContent = '이미지 폴더는 앱에서 열 수 있습니다.';
});

document.querySelector('#new-diagram')!.addEventListener('click', () => {
  if (connection) connection.command('note.newDiagramRequested');
  else document.querySelector('#host-status')!.textContent = '다이어그램은 앱에서 만들 수 있습니다.';
});
editor.diagramEditRequested = file => {
  if (connection) connection.editDiagram(file);
  else document.querySelector('#host-status')!.textContent = '다이어그램은 앱에서 편집할 수 있습니다.';
};
