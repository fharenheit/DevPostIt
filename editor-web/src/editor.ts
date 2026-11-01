import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/markdown/register';
import EditorWorker from 'monaco-editor/editor/editor.worker?worker';
import { renderMarkdown } from './markdown';

globalThis.MonacoEnvironment = { getWorker: () => new EditorWorker() };

export type EditorMode = 'source' | 'preview';
export type Draft = { markdown: string; revision: number; dirty: boolean };
export class Editor {
  private readonly view: monaco.editor.IStandaloneCodeEditor;
  private model: monaco.editor.ITextModel | null = null;
  private original = '';
  private modelBaseline = '';
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
      language: 'markdown', theme: 'vs', automaticLayout: true,
      minimap: { enabled: false }, wordWrap: 'on', fontSize: 13, lineHeight: 22,
      padding: { top: 24, bottom: 24 }, scrollBeyondLastLine: false,
      ariaLabel: 'Markdown 본문', editContext: false,
      links: false,
      renderWhitespace: 'selection', tabSize: 2, quickSuggestions: false,
      unicodeHighlight: { ambiguousCharacters: false, invisibleCharacters: false },
    });
    this.view.onDidChangeModelContent(() => {
      if (!this.model) return;
      const value = this.model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true);
      // Undo로 모델의 최초 값에 복귀하면 BOM·혼합 줄바꿈을 포함한 원문을 돌려줍니다.
      const markdown = value === this.modelBaseline ? this.original : value;
      if (markdown !== this.draft.markdown) {
        this.draft = { markdown, revision: this.draft.revision + 1, dirty: markdown !== this.original };
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

  load(noteId: string, markdown: string, revision: number): void {
    const previous = this.model;
    this.generation++;
    this.composing = false;
    this.pendingMode = null;
    this.original = markdown;
    this.model = monaco.editor.createModel(markdown, 'markdown', monaco.Uri.parse(`inmemory://notes/${encodeURIComponent(noteId)}/${this.generation}.md`));
    const firstNewline = /\r\n|\n/.exec(markdown)?.[0];
    this.model.setEOL(firstNewline === '\r\n' ? monaco.editor.EndOfLineSequence.CRLF : monaco.editor.EndOfLineSequence.LF);
    this.modelBaseline = this.model.getValue(monaco.editor.EndOfLinePreference.TextDefined, true);
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
  isComposing(): boolean { return this.composing; }
  markSaved(revision: number): void {
    if (revision !== this.draft.revision || !this.model || this.composing) return;
    this.original = this.draft.markdown;
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
