import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/markdown/register';
import DOMPurify from 'dompurify';
import { findLanguageId } from './code-languages';
import { planFormat, planInsertLines, type FormatPlan, type MarkdownAction, type TextDocument, type TextRange } from './markdown-format';
import { markdownTableLines, tableFromClipboardHtml } from './paste-table';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import { renderMarkdown } from './markdown';
import { formatBlock, CodeBlockError } from './code-format';

globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() };

// style.css의 메모지 색 토큰과 같은 값을 사용합니다.
monaco.editor.defineTheme('devpostit-paper', {
  base: 'vs', inherit: false,
  rules: [
    { token: '', foreground: '2F2A1F' },
    { token: 'keyword.md', foreground: '7A5C00', fontStyle: 'bold' },
    { token: 'string.md', foreground: '7A5C00' },
    { token: 'string.link.md', foreground: '7A5C00' },
    { token: 'variable.md', foreground: '665E4B' },
    { token: 'comment.md', foreground: '665E4B', fontStyle: 'italic' },
    { token: 'strong', fontStyle: 'bold' },
    { token: 'emphasis', fontStyle: 'italic' },
    // 코드 블록: 대비 4.5:1 이상을 유지하는 어두운 색만 사용합니다.
    { token: 'keyword', foreground: '9A2F12' },
    { token: 'string', foreground: '2B6636' },
    { token: 'number', foreground: '0B5A85' },
    { token: 'comment', foreground: '665E4B', fontStyle: 'italic' },
    { token: 'type', foreground: '6A3F99' },
    { token: 'annotation', foreground: '6A3F99' },
    { token: 'tag', foreground: '9A2F12' },
    { token: 'attribute.name', foreground: '0B5A85' },
    { token: 'attribute.value', foreground: '2B6636' },
    { token: 'delimiter', foreground: '4F4838' },
    { token: 'variable', foreground: '0B5A85' },
    { token: 'regexp', foreground: '2B6636' },
    { token: 'metatag', foreground: '6A3F99' },
    { token: 'predefined', foreground: '6A3F99' },
  ],
  colors: {
    'editor.background': '#FFFDF5',
    'editor.foreground': '#2F2A1F',
    'editorCursor.foreground': '#7A5C00',
    'editorLineNumber.foreground': '#A89E80',
    'editorLineNumber.activeForeground': '#665E4B',
    'editor.selectionBackground': '#FFE88F',
    'editor.inactiveSelectionBackground': '#F8F2DC',
    'editor.lineHighlightBackground': '#00000000',
    'editor.lineHighlightBorder': '#00000000',
    'scrollbarSlider.background': '#C9BB8866',
    'scrollbarSlider.hoverBackground': '#C9BB88AA',
    'scrollbarSlider.activeBackground': '#C9BB88',
  },
});

export type EditorMode = 'source' | 'preview';
export type Draft = { markdown: string; revision: number; dirty: boolean };
export class Editor {
  private readonly view: monaco.editor.IStandaloneCodeEditor;
  private model: monaco.editor.ITextModel | null = null;
  private original = '';
  private title = '나의 첫 메모';
  private originalTitle = this.title;
  private modelBaseline = '';
  private readonly rawVersions = new Map<number, string>();
  private draft: Draft = { markdown: '', revision: 0, dirty: false };
  private composing = false;
  private pendingMode: EditorMode | null = null;
  private generation = 0;
  private mode: EditorMode = 'source';
  private highlightRun = 0;
  imagePasteRequested?: () => void;

  constructor(
    private readonly source: HTMLElement,
    private readonly preview: HTMLElement,
    private readonly changed: (mode: EditorMode, draft: Draft) => void,
  ) {
    this.view = monaco.editor.create(source, {
      language: 'markdown', theme: 'devpostit-paper', automaticLayout: true,
      minimap: { enabled: false }, wordWrap: 'on', fontSize: 13, lineHeight: 22,
      padding: { top: 12, bottom: 12 }, scrollBeyondLastLine: false,
      lineNumbersMinChars: 3, lineDecorationsWidth: 8, renderLineHighlight: 'none',
      overviewRulerLanes: 0, overviewRulerBorder: false, hideCursorInOverviewRuler: true,
      scrollbar: { verticalScrollbarSize: 8, horizontalScrollbarSize: 8, useShadows: false },
      ariaLabel: 'Markdown 본문', editContext: false,
      links: false,
      renderWhitespace: 'selection', tabSize: 2, quickSuggestions: false,
      unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false },
    });
    const shortcut = (keys: number, action: MarkdownAction) => this.view.addCommand(keys, () => this.applyFormat(action));
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyB, 'bold');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyI, 'italic');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyMod.Shift | monaco.KeyCode.KeyX, 'strike');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyE, 'code');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.KeyK, 'link');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Digit1, 'h1');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Digit2, 'h2');
    shortcut(monaco.KeyMod.CtrlCmd | monaco.KeyCode.Digit3, 'h3');
    preview.addEventListener('change', event => {
      const box = event.target as HTMLInputElement;
      if (box.matches('input[type=checkbox][data-line]')) this.toggleTask(Number(box.dataset.line));
    });
    this.view.onDidChangeModelContent(() => {
      if (!this.model) return;
      const value = this.model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true);
      // Undo로 모델의 최초 값에 복귀하면 BOM·혼합 줄바꿈을 포함한 원문을 돌려줍니다.
      const markdown = this.rawVersions.get(this.model.getAlternativeVersionId()) ?? (value === this.modelBaseline ? this.original : value);
      if (markdown !== this.draft.markdown) {
        this.draft = { markdown, revision: this.draft.revision + 1, dirty: markdown !== this.original || this.title !== this.originalTitle };
        this.notify();
      }
    });
    // Monaco의 붙여넣기보다 먼저 받아 표일 때만 가로챕니다. 나머지는 기본 동작을 그대로 둡니다.
    source.addEventListener('paste', event => {
      const selection = this.editableSelection();
      const html = event.clipboardData?.getData('text/html');
      const rows = selection && html ? tableFromClipboardHtml(html) : null;
      if (selection && !rows && !event.clipboardData?.getData('text/plain') && Array.from(event.clipboardData?.items ?? []).some(item => item.type.startsWith('image/'))) {
        // 이미지 내용은 메시지 크기 제한을 넘을 수 있어 앱이 클립보드에서 직접 읽습니다.
        event.preventDefault();
        event.stopPropagation();
        this.imagePasteRequested?.();
        return;
      }
      if (!selection || !rows) return;
      event.preventDefault();
      event.stopPropagation();
      this.applyPlan(planInsertLines(this.document(), selection, markdownTableLines(rows)));
    }, true);
    source.addEventListener('compositionstart', () => { this.composing = true; });
    source.addEventListener('compositionend', () => {
      this.composing = false;
      const generation = this.generation;
      window.setTimeout(() => {
        if (generation !== this.generation) return;
        const requested = this.pendingMode;
        this.pendingMode = null;
        if (requested) this.setMode(requested);
        else this.notify();
      }, 0);
    });
  }

  load(noteId: string, markdown: string, revision: number, title = '나의 첫 메모'): void {
    const previous = this.model;
    this.generation++;
    this.composing = false;
    this.pendingMode = null;
    this.original = markdown;
    this.title = this.originalTitle = title;
    this.model = monaco.editor.createModel(markdown, 'markdown', monaco.Uri.parse(`inmemory://notes/${encodeURIComponent(noteId)}/${this.generation}.md`));
    const firstNewline = /\r\n|\n/.exec(markdown)?.[0];
    // 괄호 색은 편집기 옵션이 아니라 모델 옵션을 따릅니다.
    this.model.updateOptions({ bracketColorizationOptions: { enabled: false, independentColorPoolPerBracketType: false } });
    this.model.setEOL(firstNewline === '\r\n' ? monaco.editor.EndOfLineSequence.CRLF : monaco.editor.EndOfLineSequence.LF);
    this.modelBaseline = this.model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true);
    this.rawVersions.clear();
    this.rawVersions.set(this.model.getAlternativeVersionId(), markdown);
    this.draft = { markdown, revision, dirty: false };
    this.view.setModel(this.model);
    previous?.dispose();
    this.setMode('source');
  }

  setMode(mode: EditorMode): void {
    if (this.composing) { this.pendingMode = mode; return; }
    this.pendingMode = null;
    this.mode = mode;
    this.highlightRun++;
    if (mode === 'preview') {
      this.preview.innerHTML = renderMarkdown(this.draft.markdown);
      this.addCopyButtons();
      void this.highlightPreview(this.highlightRun);
    }
    this.source.hidden = mode !== 'source';
    this.preview.hidden = mode !== 'preview';
    if (mode === 'source') this.view.layout();
    this.notify();
  }

  setFont(fontFamily: string, ligatures: boolean): void {
    this.view.updateOptions({ fontFamily, fontLigatures: ligatures });
    monaco.editor.remeasureFonts();
  }

  getMode(): EditorMode { return this.mode; }
  getDraft(): Draft { return { ...this.draft }; }
  getTitle(): string { return this.title; }
  rename(title: string): void {
    const value = title.trim();
    if (!value || value.length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(value)) throw new Error('메모 이름은 줄바꿈 없이 1~200자로 입력하세요.');
    if (value === this.title) return;
    this.title = value;
    this.draft = { ...this.draft, revision: this.draft.revision + 1, dirty: this.title !== this.originalTitle || this.draft.markdown !== this.original };
    this.notify();
  }
  async formatCodeBlock(language?: string): Promise<string> {
    if (!this.model || this.mode !== 'source' || this.composing) return '소스 편집에서 조합을 마친 뒤 실행해 주세요.';
    const model = this.model;
    const version = model.getVersionId();
    const raw = this.draft.markdown;
    try {
      const edit = await formatBlock(raw, (this.view.getPosition()?.lineNumber ?? 1) - 1, language);
      if (this.model !== model || model.isDisposed() || model.getVersionId() !== version || this.composing || this.mode !== 'source') return '편집 내용이 바뀌어 정리를 취소했습니다.';
      this.rawVersions.set(model.getAlternativeVersionId(), raw);
      this.view.pushUndoStop();
      this.view.executeEdits('format-code', [{ range: new monaco.Range(edit.startLine, 1, edit.endLine, 1), text: edit.text }]);
      this.view.pushUndoStop();
      const markdown = raw.slice(0, edit.start) + edit.text + raw.slice(edit.end);
      this.rawVersions.set(model.getAlternativeVersionId(), markdown);
      this.draft = { ...this.draft, markdown, dirty: markdown !== this.original || this.title !== this.originalTitle };
      this.notify();
      return '코드를 정리했습니다.';
    } catch (error) { return error instanceof CodeBlockError ? error.message : '정리하지 못했습니다. 블록의 언어·문법과 닫는 울타리를 확인해 주세요.'; }
  }
  insertText(text: string): void {
    const selection = this.editableSelection();
    if (!selection) return;
    this.applyPlan({ edits: [{ range: selection, text }], selection: { startLine: selection.startLine, startColumn: selection.startColumn + text.length, endLine: selection.startLine, endColumn: selection.startColumn + text.length } });
  }
  // 미리보기의 체크박스를 누르면 원문의 해당 줄만 고칩니다. 줄이 할 일 항목이 아니면 화면만 원문에 맞춥니다.
  private toggleTask(line: number): void {
    const model = this.model;
    const scroll = this.preview.scrollTop;
    if (model && Number.isInteger(line) && line >= 0 && line < model.getLineCount() && !this.composing) {
      const text = model.getLineContent(line + 1);
      const match = /^(\s*(?:>\s*)*(?:[-*+]|\d+[.)])\s+\[)([ xX])\]/.exec(text);
      if (match) {
        const column = match[1].length + 1;
        this.view.pushUndoStop();
        this.view.executeEdits('task-checkbox', [{ range: new monaco.Range(line + 1, column, line + 1, column + 1), text: match[2] === ' ' ? 'x' : ' ' }]);
        this.view.pushUndoStop();
      }
    }
    this.setMode('preview');
    this.preview.scrollTop = scroll;
  }
  private addCopyButtons(): void {
    for (const code of this.preview.querySelectorAll<HTMLElement>('pre > code')) {
      const text = (code.textContent ?? '').replace(/\r?\n$/, '');
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'copy-code';
      button.textContent = '복사';
      button.setAttribute('aria-label', '코드 복사');
      button.addEventListener('click', async () => {
        try { await navigator.clipboard.writeText(text); button.textContent = '복사됨'; }
        catch { button.textContent = '복사 실패'; }
        window.setTimeout(() => { button.textContent = '복사'; }, 1500);
      });
      code.parentElement!.append(button);
    }
  }
  applyFormat(action: MarkdownAction): void {
    const selection = this.editableSelection();
    if (selection) this.applyPlan(planFormat(action, this.document(), selection));
  }
  private editableSelection(): TextRange | null {
    const selection = this.view.getSelection();
    if (!this.model || !selection || this.mode !== 'source' || this.composing) return null;
    return { startLine: selection.startLineNumber, startColumn: selection.startColumn, endLine: selection.endLineNumber, endColumn: selection.endColumn };
  }
  private document(): TextDocument {
    const model = this.model!;
    return {
      lineCount: model.getLineCount(),
      line: number => model.getLineContent(number),
      text: range => model.getValueInRange(new monaco.Range(range.startLine, range.startColumn, range.endLine, range.endColumn)),
      eol: model.getEOL(),
    };
  }
  private applyPlan(plan: FormatPlan): void {
    const selection = new monaco.Range(plan.selection.startLine, plan.selection.startColumn, plan.selection.endLine, plan.selection.endColumn);
    this.view.pushUndoStop();
    this.view.executeEdits('markdown-toolbar', plan.edits.map(({ range, text }) => ({ range: new monaco.Range(range.startLine, range.startColumn, range.endLine, range.endColumn), text, forceMoveMarkers: true })));
    this.view.setSelection(selection);
    this.view.pushUndoStop();
    this.view.revealRangeInCenterIfOutsideViewport(selection);
    this.view.focus();
  }
  isComposing(): boolean { return this.composing; }
  markSaved(revision: number): void {
    if (revision !== this.draft.revision || !this.model || this.composing) return;
    this.original = this.draft.markdown;
    this.originalTitle = this.title;
    this.modelBaseline = this.model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true);
    this.draft = { ...this.draft, dirty: false };
    this.notify();
  }
  setTextSize(size: number): void {
    this.view.updateOptions({ fontSize: size, lineHeight: Math.round(size * 1.7) });
    this.view.layout();
  }
  // 원문이 아니라 표시만 바꿉니다. 실패하거나 다른 화면으로 바뀌면 평문 코드 블록을 그대로 둡니다.
  private async highlightPreview(run: number): Promise<void> {
    for (const code of this.preview.querySelectorAll<HTMLElement>('pre > code[class*="language-"]')) {
      const language = findLanguageId(/language-(\S+)/.exec(code.className)?.[1] ?? '');
      if (!language) continue;
      try {
        const html = await monaco.editor.colorize((code.textContent ?? '').replace(/\r?\n$/, ''), language, { tabSize: 2 });
        if (run !== this.highlightRun) return;
        code.innerHTML = DOMPurify.sanitize(html, { ALLOWED_TAGS: ['div', 'span', 'br'], ALLOWED_ATTR: ['class'], ALLOW_DATA_ATTR: false }).replaceAll('&nbsp;', ' ');
      } catch { /* 평문 코드 블록을 유지합니다. */ }
    }
  }
  private notify(): void { this.changed(this.mode, this.getDraft()); }
}
