import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/markdown/register';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import { renderMarkdown } from './markdown';
import { formatBlock, CodeBlockError } from './code-format';

globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() };

// style.css의 메모지 색 토큰과 같은 값을 사용합니다.
monaco.editor.defineTheme('devpostit-paper', {
  base: 'vs', inherit: true,
  rules: [
    { token: '', foreground: '2F2A1F' },
    { token: 'keyword', foreground: '7A5C00', fontStyle: 'bold' },
    { token: 'string', foreground: '8A4B08' },
    { token: 'string.link', foreground: '7A5C00' },
    { token: 'variable', foreground: '665E4B' },
    { token: 'comment', foreground: '665E4B', fontStyle: 'italic' },
    { token: 'strong', fontStyle: 'bold' },
    { token: 'emphasis', fontStyle: 'italic' },
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
    if (mode === 'preview') this.preview.innerHTML = renderMarkdown(this.draft.markdown);
    this.source.hidden = mode !== 'source';
    this.preview.hidden = mode !== 'preview';
    if (mode === 'source') this.view.layout();
    this.notify();
  }

  setFont(fontFamily: string, ligatures: boolean): void {
    this.view.updateOptions({ fontFamily, fontLigatures: ligatures });
    monaco.editor.remeasureFonts();
  }

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
  private notify(): void { this.changed(this.mode, this.getDraft()); }
}
