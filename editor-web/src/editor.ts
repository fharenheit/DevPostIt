import * as monaco from 'monaco-editor/editor/editor.api';
import 'monaco-editor/languages/definitions/markdown/register';
import DOMPurify from 'dompurify';
import { preferenceOptions, sharedEditorOptions } from './monaco-setup';
import { findLanguageId } from './code-languages';
import { defaultEditorPreferences, type EditorPreferences } from './fonts';
import { planFormat, planInsertLines, type FormatPlan, type MarkdownAction, type TextDocument, type TextRange } from './markdown-format';
import { markdownTableLines, tableFromClipboardHtml } from './paste-table';
import { renderMarkdown } from './markdown';
import { imageAltEdit } from './image-size';
import { renderDiagrams } from './diagrams';
import { formatBlock, CodeBlockError } from './code-format';

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
  private preferences = defaultEditorPreferences;
  private highlighting: Promise<void> = Promise.resolve();
  imagePasteRequested?: () => void;
  linkRequested?: (url: string) => void;
  diagramEditRequested?: (file: string) => void;
  private readonly imageEditButton = document.createElement('button');
  private assetVersion = Date.now();
  private readonly imageMenu = document.createElement('div');
  private imageMenuTarget: HTMLImageElement | null = null;

  constructor(
    private readonly source: HTMLElement,
    private readonly preview: HTMLElement,
    private readonly changed: (mode: EditorMode, draft: Draft) => void,
  ) {
    this.view = monaco.editor.create(source, { ...sharedEditorOptions, language: 'markdown', ariaLabel: 'Markdown 본문' });
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
    this.buildImageMenu();
    preview.addEventListener('click', event => {
      const target = event.target as Element;
      const link = target.closest<HTMLAnchorElement>('a[href]');
      if (link) {
        event.preventDefault();
        this.closeImageMenu();
        this.linkRequested?.(link.getAttribute('href')!);
        return;
      }
      const image = target.closest<HTMLImageElement>('img[data-line]');
      if (image) this.openImageMenu(image);
      else this.closeImageMenu();
    });
    preview.addEventListener('scroll', () => this.closeImageMenu());
    window.addEventListener('keydown', event => { if (event.key === 'Escape') this.closeImageMenu(); });
    document.addEventListener('click', event => {
      const target = event.target as Node;
      if (!preview.contains(target) && !this.imageMenu.contains(target)) this.closeImageMenu();
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
    this.model.updateOptions({ bracketColorizationOptions: { enabled: this.preferences.bracketColors, independentColorPoolPerBracketType: false } });
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
    this.closeImageMenu();
    this.mode = mode;
    this.highlightRun++;
    if (mode === 'preview') {
      this.preview.innerHTML = renderMarkdown(this.draft.markdown);
      this.showDiagramVersion();
      this.addCopyButtons();
      const run = this.highlightRun;
      this.highlighting = this.highlightPreview(run).then(() => renderDiagrams(this.preview, () => run === this.highlightRun && this.mode === 'preview'));
    }
    this.source.hidden = mode !== 'source';
    this.preview.hidden = mode !== 'preview';
    if (mode === 'source') this.view.layout();
    this.notify();
  }

  setPreferences(preferences: EditorPreferences): void {
    this.preferences = preferences;
    this.view.updateOptions(preferenceOptions(preferences));
    this.model?.updateOptions({ bracketColorizationOptions: { enabled: preferences.bracketColors, independentColorPoolPerBracketType: false } });
  }
  runEditorAction(id: string): void {
    if (!this.model || this.mode !== 'source' || this.composing) return;
    this.view.focus();
    void this.view.getAction(id)?.run();
  }
  setFont(fontFamily: string, ligatures: boolean): void {
    this.view.updateOptions({ fontFamily, fontLigatures: ligatures });
    monaco.editor.remeasureFonts();
  }

  getMode(): EditorMode { return this.mode; }
  // 인쇄 전에 하이라이트까지 끝난 미리보기를 준비합니다. 한글 조합 중이면 false입니다.
  async renderForPrint(): Promise<boolean> {
    if (this.composing) return false;
    this.setMode('preview');
    await this.highlighting;
    await Promise.all(Array.from(this.preview.querySelectorAll('img')).map(image => image.decode().catch(() => undefined)));
    return true;
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
  private buildImageMenu(): void {
    const menu = this.imageMenu;
    menu.id = 'image-menu';
    menu.hidden = true;
    menu.setAttribute('role', 'group');
    menu.setAttribute('aria-label', '이미지 크기');
    for (const [label, width] of [['25%', '25%'], ['50%', '50%'], ['75%', '75%'], ['원래 크기', null]] as const) {
      const button = document.createElement('button');
      button.type = 'button';
      button.textContent = label;
      button.addEventListener('click', () => {
        const image = this.imageMenuTarget;
        this.closeImageMenu();
        if (image) this.resizeImage(Number(image.dataset.line), Number(image.dataset.end), Number(image.dataset.index), new URL(image.src).pathname.split('/').pop() ?? '', width);
      });
      menu.append(button);
    }
    const edit = this.imageEditButton;
    edit.type = 'button';
    edit.textContent = 'draw.io로 편집';
    edit.addEventListener('click', () => {
      const image = this.imageMenuTarget;
      this.closeImageMenu();
      if (image) this.diagramEditRequested?.(new URL(image.src).pathname.slice(1));
    });
    menu.append(edit);
    document.body.append(menu);
  }
  private openImageMenu(image: HTMLImageElement): void {
    this.imageMenuTarget = image;
    this.imageEditButton.hidden = !new URL(image.src).pathname.endsWith('.drawio.svg');
    this.imageMenu.hidden = false;
    const box = image.getBoundingClientRect();
    const size = this.imageMenu.getBoundingClientRect();
    this.imageMenu.style.left = `${Math.max(4, Math.min(box.left, window.innerWidth - size.width - 4))}px`;
    this.imageMenu.style.top = `${Math.max(4, Math.min(box.top + 4, window.innerHeight - size.height - 4))}px`;
  }
  // draw.io에서 저장해도 주소가 같으면 이전 그림이 보이므로 버전을 붙여 다시 읽습니다.
  refreshDiagrams(): void {
    this.assetVersion = Date.now();
    if (this.mode === 'preview') this.showDiagramVersion();
  }
  private showDiagramVersion(): void {
    for (const image of this.preview.querySelectorAll<HTMLImageElement>('img[src*=".drawio.svg"]')) image.src = `${image.src.split('?')[0]}?v=${this.assetVersion}`;
  }
  private closeImageMenu(): void {
    this.imageMenu.hidden = true;
    this.imageMenuTarget = null;
  }
  // 체크박스처럼 원문의 해당 부분만 고칩니다. 그 사이 원문이 바뀌어 찾지 못하면 그대로 둡니다.
  resizeImage(line: number, end: number, index: number, file: string, width: string | null): void {
    const model = this.model;
    if (!model || this.composing || !Number.isInteger(line) || !Number.isInteger(end) || line < 0 || end <= line || end > model.getLineCount()) return;
    const lines = Array.from({ length: end - line }, (_, offset) => model.getLineContent(line + offset + 1));
    const edit = imageAltEdit(lines, index, file, width);
    if (!edit) return;
    const scroll = this.preview.scrollTop;
    const row = line + edit.line + 1;
    this.view.pushUndoStop();
    this.view.executeEdits('image-size', [{ range: new monaco.Range(row, edit.start + 1, row, edit.end + 1), text: edit.text }]);
    this.view.pushUndoStop();
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
  setTextSize(size: number, lineHeight = 1.7): void {
    this.view.updateOptions({ fontSize: size, lineHeight: Math.round(size * lineHeight) });
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
