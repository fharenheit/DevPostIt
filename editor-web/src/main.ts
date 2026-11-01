import { Editor } from './editor';
import { fonts, readFontSettings, saveFontSettings, type FontKey } from './fonts';
import './style.css';

declare global { interface Window { hunminEditor: Editor } }
document.querySelector<HTMLDivElement>('#app')!.innerHTML = `
  <header class="app-header"><div class="brand"><span class="brand-mark">훈</span><div><strong>훈민 노트</strong><small>생각을 쓰고, 원문 그대로.</small></div></div><span class="stage">편집기 미리 체험</span></header>
  <section class="workspace" aria-label="Markdown 편집기">
    <div class="toolbar">
      <div class="modes" role="group" aria-label="보기 방식"><button id="source-mode" aria-pressed="true">소스 편집</button><button id="preview-mode" aria-pressed="false">미리보기</button></div>
      <div class="font-controls"><label>편집 글꼴 <select id="font"><option value="d2">D2 Ligature</option><option value="jetbrains">JetBrains Mono</option><option value="system">시스템 고정폭</option></select></label><label class="ligatures"><input id="ligatures" type="checkbox" checked> 합자 사용</label></div>
    </div>
    <div class="document-heading"><span class="document-icon">MD</span><div><h1>나의 첫 메모</h1><p>Markdown으로 작성하고 미리보기로 확인하세요.</p></div></div>
    <div class="editor-area"><div id="source"></div><article id="preview" class="preview" aria-label="Markdown 미리보기" hidden></article></div>
    <footer><span id="status" role="status">편집 준비</span><span>변경 내용은 이 창에서만 유지됩니다.</span></footer>
  </section>`;

const sourceButton = document.querySelector<HTMLButtonElement>('#source-mode')!;
const previewButton = document.querySelector<HTMLButtonElement>('#preview-mode')!;
const status = document.querySelector<HTMLElement>('#status')!;
const editor = new Editor(document.querySelector('#source')!, document.querySelector('#preview')!, (mode, draft) => {
  sourceButton.setAttribute('aria-pressed', String(mode === 'source'));
  previewButton.setAttribute('aria-pressed', String(mode === 'preview'));
  status.textContent = `${draft.dirty ? '편집됨' : '원문 유지'} · ${draft.markdown.length.toLocaleString('ko-KR')}자`;
});
sourceButton.addEventListener('click', () => editor.setMode('source'));
previewButton.addEventListener('click', () => editor.setMode('preview'));

const fontSelect = document.querySelector<HTMLSelectElement>('#font')!;
const ligatures = document.querySelector<HTMLInputElement>('#ligatures')!;
const settings = readFontSettings();
fontSelect.value = settings.font;
ligatures.checked = settings.ligatures;
function applyFont(): void {
  const font = fontSelect.value as FontKey;
  const enabled = ligatures.checked;
  editor.setFont(fonts[font], enabled);
  saveFontSettings({ font, ligatures: enabled });
  if (font !== 'system') void document.fonts.load(`16px "${font === 'd2' ? 'D2 Ligature' : 'JetBrains Mono'}"`).then(() => {
    editor.setFont(fonts[fontSelect.value as FontKey], ligatures.checked);
  });
}
fontSelect.addEventListener('change', applyFont);
ligatures.addEventListener('change', applyFont);
applyFont();
editor.load('welcome', '# 오늘의 메모\n\n작은 생각도, 잊기 전에 남겨 두세요.\n\n## 오늘 할 일\n\n- [ ] 아이디어 정리하기\n- [x] 첫 메모 시작하기\n\n> 원문은 그대로, 보기만 편하게.\n\n```ts\nconst note = "안녕하세요";\nconst ready = count >= 1;\n```\n', 0);
window.hunminEditor = editor;
