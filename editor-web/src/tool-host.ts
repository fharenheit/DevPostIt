// 마인드맵, 할 일, 달력 페이지와 앱 사이의 통로입니다. 브라우저 단독 실행에서는 이 브라우저의 저장소를 대신 씁니다.
type Message = Record<string, unknown>;
type Host = { postMessage(message: unknown): void; addEventListener(type: 'message', handler: (event: { data: unknown }) => void): void };
const webview = (typeof window === 'undefined' ? undefined : window.chrome?.webview) as unknown as Host | undefined;
const handlers: ((message: Message) => void)[] = [];
const emit = (message: Message) => window.setTimeout(() => handlers.forEach(handler => handler(message)), 0);
export const hasHost = Boolean(webview);
export const onHostMessage = (handler: (message: Message) => void) => { handlers.push(handler); };
webview?.addEventListener('message', event => { if (event.data && typeof event.data === 'object') handlers.forEach(handler => handler(event.data as Message)); });

export function sendToHost(type: string, fields: Message = {}): void {
  const message = { protocolVersion: 1, type, ...fields };
  if (webview) webview.postMessage(message);
  else localBackend(type, fields);
}

const read = <T>(key: string, fallback: T): T => {
  try { return JSON.parse(localStorage.getItem(key) ?? 'null') ?? fallback; }
  catch { return fallback; }
};
const write = (key: string, value: unknown) => { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* 브라우저 저장 공간이 없으면 저장하지 않습니다. */ } };
type LocalMap = { id: string; title: string; updated: string; root: { children?: unknown[] } };
const count = (node: { children?: unknown[] }): number => 1 + (node.children ?? []).reduce<number>((sum, child) => sum + count(child as { children?: unknown[] }), 0);
function localList(): Message {
  const maps = read<Record<string, LocalMap>>('devpostit.local.mindmaps', {});
  return { type: 'mindmap.list', maps: Object.values(maps).sort((a, b) => b.updated.localeCompare(a.updated)).map(map => ({ id: map.id, title: map.title, updated: map.updated, nodes: count(map.root) })) };
}
function localBackend(type: string, fields: Message): void {
  const maps = read<Record<string, LocalMap>>('devpostit.local.mindmaps', {});
  if (type === 'mindmap.list') emit(localList());
  else if (type === 'mindmap.open') {
    const map = maps[String(fields.id)];
    emit(map ? { type: 'mindmap.opened', map } : { type: 'mindmap.error', message: '마인드맵을 찾지 못했습니다.' });
  } else if (type === 'mindmap.save') {
    const map = { ...(fields.map as LocalMap), updated: new Date().toISOString() };
    write('devpostit.local.mindmaps', { ...maps, [map.id]: map });
    emit({ type: 'mindmap.saved', id: map.id, updated: map.updated });
  } else if (type === 'mindmap.delete') {
    delete maps[String(fields.id)];
    write('devpostit.local.mindmaps', maps);
    emit(localList());
  } else if (type === 'mindmap.openLink') emit({ type: 'mindmap.error', message: '링크는 앱에서 열 수 있습니다.' });
  else if (type === 'mindmap.exportPng') emit({ type: 'mindmap.error', message: 'PNG 저장은 앱에서 할 수 있습니다.' });
  else if (type.startsWith('planner.')) localPlanner(type, fields);
}
type LocalItem = { id: string; listId?: string };
function localPlanner(type: string, fields: Message): void {
  const state = read<Record<string, LocalItem[]>>('devpostit.local.planner', { lists: [], todos: [], events: [] });
  const collection = String(fields.collection);
  if (type === 'planner.put') {
    const item = fields.item as LocalItem;
    const at = state[collection].findIndex(value => value.id === item.id);
    if (at >= 0) state[collection][at] = item;
    else state[collection].push(item);
  } else if (type === 'planner.delete') {
    state[collection] = state[collection].filter(value => value.id !== fields.id);
    if (collection === 'lists') state.todos = state.todos.filter(value => value.listId !== fields.id);
  }
  write('devpostit.local.planner', state);
  emit({ type: 'planner.state', state });
}
