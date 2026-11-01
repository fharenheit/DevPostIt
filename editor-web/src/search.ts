import './style.css';

type Result = { id: string; title: string; excerpt: string; updated: string };
type Host = { postMessage(message: unknown): void; addEventListener(type: 'message', handler: (event: { data: unknown }) => void): void };
const host = window.chrome?.webview as unknown as Host | undefined;
const input = document.querySelector<HTMLInputElement>('#search-query')!;
const status = document.querySelector<HTMLElement>('#search-status')!;
const list = document.querySelector<HTMLUListElement>('#search-results')!;
let timer: number | undefined;

function send(query: string): void {
  host?.postMessage({ protocolVersion: 1, type: 'search.query', query });
}
// 검색 결과는 사용자 글이므로 HTML로 해석하지 않고 글자로만 넣습니다.
function appendMarked(target: HTMLElement, text: string, query: string): void {
  const at = query ? text.toLocaleLowerCase().indexOf(query.toLocaleLowerCase()) : -1;
  if (at < 0) { target.append(text); return; }
  const mark = document.createElement('mark');
  mark.textContent = text.slice(at, at + query.length);
  target.append(text.slice(0, at), mark, text.slice(at + query.length));
}
function show(results: Result[], query: string): void {
  list.replaceChildren(...results.map(result => {
    const button = document.createElement('button');
    button.type = 'button';
    const title = document.createElement('strong');
    appendMarked(title, result.title, query);
    const excerpt = document.createElement('span');
    appendMarked(excerpt, result.excerpt, query);
    button.append(title, excerpt);
    button.addEventListener('click', () => host?.postMessage({ protocolVersion: 1, type: 'search.open', id: result.id }));
    const item = document.createElement('li');
    item.append(button);
    return item;
  }));
  status.textContent = results.length ? (query ? `${results.length}개 찾음` : '최근 메모') : (query ? '찾은 메모가 없습니다.' : '메모가 없습니다.');
}

if (host) {
  host.addEventListener('message', event => {
    const data = event.data as { type?: unknown; query?: unknown; results?: unknown; message?: unknown } | null;
    if (!data || data.query !== input.value.trim()) return;
    if (data.type === 'search.results' && Array.isArray(data.results)) show(data.results as Result[], data.query as string);
    else if (data.type === 'search.failed' && typeof data.message === 'string') status.textContent = `검색하지 못했습니다: ${data.message}`;
  });
  input.addEventListener('input', () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => send(input.value.trim()), 150);
  });
  input.addEventListener('keydown', event => {
    if (event.key === 'ArrowDown') { event.preventDefault(); list.querySelector('button')?.focus(); }
    if (event.key === 'Enter') list.querySelector('button')?.click();
  });
  send('');
} else status.textContent = '검색은 앱에서 사용할 수 있습니다.';
input.focus();
