import type { Editor } from './editor';

type Message = { protocolVersion: number; type: string; requestId: string; noteId: string; revision: number; payload: Record<string, unknown> };
type WebView = { postMessage(message: Message): void; addEventListener(type: 'message', handler: (event: { data: unknown }) => void): void };
declare global { interface Window { chrome?: { webview?: WebView } } }
const emptyId = '00000000-0000-0000-0000-000000000000';
const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const maxBytes = 1024 * 1024;
const bytes = (value: unknown) => new TextEncoder().encode(JSON.stringify(value)).byteLength;
const keys = (value: object, expected: string[]) => Object.keys(value).length === expected.length && expected.every(key => Object.hasOwn(value, key));

export class EditorConnection {
  private noteId = emptyId;
  private sentRevision = -1;
  private loading = false;
  private queued = false;

  constructor(private readonly editor: Editor, private readonly transport: WebView, private readonly status: (message: string) => void) {
    transport.addEventListener('message', event => this.receive(event.data));
    this.send('editor.ready', {}, 0);
  }

  changed(): void {
    if (this.loading || this.noteId === emptyId || this.queued) return;
    this.queued = true;
    queueMicrotask(() => { this.queued = false; this.publish(); });
  }

  requestSave(): void {
    if (this.noteId === emptyId || this.editor.isComposing()) return;
    this.publish();
    const draft = this.editor.getDraft();
    this.send('note.saveRequested', { markdown: draft.markdown, title: this.editor.getTitle() }, draft.revision);
  }

  private publish(): void {
    if (this.loading || this.noteId === emptyId || this.editor.isComposing()) return;
    const draft = this.editor.getDraft();
    if (draft.revision <= this.sentRevision) return;
    if (this.send('note.changed', { markdown: draft.markdown, title: this.editor.getTitle() }, draft.revision)) {
      this.sentRevision = draft.revision;
      this.status('자동 저장 대기 중…');
    }
  }

  private send(type: string, payload: Record<string, unknown>, revision: number): boolean {
    const message = { protocolVersion: 1, type, requestId: crypto.randomUUID(), noteId: this.noteId, revision, payload };
    if (bytes(message) > maxBytes) { this.status('문서가 너무 커서 앱에 전달하지 못했습니다.'); return false; }
    this.transport.postMessage(message);
    return true;
  }

  private receive(raw: unknown): void {
    if (!raw || typeof raw !== 'object' || !keys(raw, ['protocolVersion', 'type', 'requestId', 'noteId', 'revision', 'payload'])) return;
    const message = raw as Message;
    if (message.protocolVersion !== 1 || typeof message.type !== 'string' || typeof message.requestId !== 'string' ||
        !message.requestId || message.requestId.length > 128 || typeof message.noteId !== 'string' || !guid.test(message.noteId) ||
        message.noteId === emptyId || !Number.isSafeInteger(message.revision) || message.revision < 0 ||
        !message.payload || typeof message.payload !== 'object' || Array.isArray(message.payload) || bytes(message) > maxBytes) return;
    const current = this.editor.getDraft();
    if (message.type === 'note.load') {
      if (!(keys(message.payload, ['markdown']) || keys(message.payload, ['markdown', 'title'])) || typeof message.payload.markdown !== 'string' || (Object.hasOwn(message.payload, 'title') && typeof message.payload.title !== 'string')) return;
      if (message.noteId === this.noteId && (message.revision < current.revision || current.dirty)) return;
      this.loading = true;
      try {
        this.noteId = message.noteId;
        this.sentRevision = message.revision;
        this.editor.load(message.noteId, message.payload.markdown, message.revision, message.payload.title as string | undefined);
      } finally { this.loading = false; }
      this.status('앱 연결됨 · 자동 저장 사용');
    } else if (message.noteId === this.noteId && message.revision === current.revision) {
      if (message.type === 'note.saved' && keys(message.payload, [])) {
        this.editor.markSaved(message.revision);
        this.status('저장됨 · 자동 저장 사용');
      }
      else if (message.type === 'note.conflict' && keys(message.payload, ['message']) && typeof message.payload.message === 'string') this.status(message.payload.message);
    }
  }
}

export function connectEditor(editor: Editor, status: (message: string) => void): EditorConnection | undefined {
  const transport = window.chrome?.webview;
  return transport ? new EditorConnection(editor, transport, status) : undefined;
}
