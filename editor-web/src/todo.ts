import './style.css';
import './tools.css';
import { applyTypography, readFontSettings } from './fonts';
import { addDays, ask, dateText, dateTimeText, defaultList, element, parseLocal, PlannerClient, relativeDay, repeatNames, today, type Repeat, type Todo } from './planner-core';

applyTypography(readFontSettings());
const client = new PlannerClient();
const $ = <T extends HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const input = $<HTMLInputElement>('#todo-input');
const search = $<HTMLInputElement>('#todo-search');
const sort = $<HTMLSelectElement>('#todo-sort');
const items = $<HTMLUListElement>('#todo-items');
const completed = $<HTMLUListElement>('#todo-completed');
const completedToggle = $<HTMLButtonElement>('#todo-completed-toggle');
const detail = $<HTMLElement>('#todo-detail');
const status = $<HTMLElement>('#todo-status');
client.error = message => { status.textContent = message; };

type Smart = 'myDay' | 'important' | 'planned' | 'all';
const smartLists: { id: Smart; name: string; icon: string }[] = [{ id: 'myDay', name: '오늘 할 일', icon: '☀' }, { id: 'important', name: '중요', icon: '★' }, { id: 'planned', name: '계획된 일정', icon: '▦' }, { id: 'all', name: '전체', icon: '∞' }];
let selectedList: string = localStorageGet('devpostit.todo.list') ?? 'myDay';
let selectedTodo: string | null = null;
let showCompleted = false;
function localStorageGet(key: string): string | null { try { return localStorage.getItem(key); } catch { return null; } }
function localStorageSet(key: string, value: string): void { try { localStorage.setItem(key, value); } catch { /* 저장하지 못해도 동작합니다. */ } }

const isSmart = (id: string): id is Smart => smartLists.some(list => list.id === id);
const listName = (id: string) => id === defaultList ? '작업' : client.state.lists.find(list => list.id === id)?.name ?? '작업';
const inList = (todo: Todo, id: string): boolean => {
  if (id === 'myDay') return todo.myDay === today();
  if (id === 'important') return todo.important;
  if (id === 'planned') return todo.due !== null;
  if (id === 'all') return true;
  return todo.listId === id;
};
function compare(a: Todo, b: Todo): number {
  switch (sort.value) {
    case 'due': return (a.due ?? '9999').localeCompare(b.due ?? '9999');
    case 'important': return Number(b.important) - Number(a.important);
    case 'title': return a.title.localeCompare(b.title, 'ko');
    case 'created': return b.created.localeCompare(a.created);
    default: return selectedList === 'planned' ? (a.due ?? '').localeCompare(b.due ?? '') : a.order - b.order;
  }
}
function visible(): { active: Todo[]; done: Todo[] } {
  const words = search.value.trim().toLowerCase();
  const matches = client.state.todos.filter(todo => words ? `${todo.title}\n${todo.notes}\n${todo.steps.map(step => step.title).join('\n')}`.toLowerCase().includes(words) : inList(todo, selectedList)).sort(compare);
  return { active: matches.filter(todo => !todo.completedAt), done: matches.filter(todo => todo.completedAt).sort((a, b) => (b.completedAt ?? '').localeCompare(a.completedAt ?? '')) };
}

// 입력칸은 다시 그리지 않으므로 등록 뒤에도 커서가 그대로 남습니다.
function addTodo(): void {
  const title = input.value.trim();
  input.value = '';
  input.focus();
  if (!title) return;
  const id = crypto.randomUUID();
  const orders = client.state.todos.map(todo => todo.order);
  const todo: Todo = { id, title, notes: '', listId: isSmart(selectedList) ? defaultList : selectedList, important: selectedList === 'important', myDay: selectedList === 'myDay' ? today() : null, due: selectedList === 'planned' ? today() : null, reminder: null, repeat: 'none', completedAt: null, steps: [], created: dateTimeText(new Date()), order: (orders.length ? Math.min(...orders) : 0) - 1 };
  search.value = '';
  client.put('todos', todo);
}
// 한글을 조합하는 중에 누른 Enter는 조합을 끝낸 직후에 등록합니다.
let enterWhileComposing = false;
input.addEventListener('keydown', event => {
  if (event.key !== 'Enter') return;
  event.preventDefault();
  if (event.isComposing) { enterWhileComposing = true; return; }
  enterWhileComposing = false;
  addTodo();
});
input.addEventListener('compositionend', () => {
  window.setTimeout(() => { if (enterWhileComposing) { enterWhileComposing = false; addTodo(); } }, 0);
});

function metaOf(todo: Todo): string[] {
  const meta: string[] = [];
  if (selectedList !== todo.listId && !(selectedList === 'all' && todo.listId === defaultList)) meta.push(listName(todo.listId));
  if (todo.myDay === today() && selectedList !== 'myDay') meta.push('☀ 오늘 할 일');
  if (todo.steps.length) meta.push(`${todo.steps.filter(step => step.done).length}/${todo.steps.length}단계`);
  if (todo.due) meta.push(`▦ ${relativeDay(todo.due)}`);
  if (todo.reminder) meta.push('🔔');
  if (todo.repeat !== 'none') meta.push(`↻ ${repeatNames[todo.repeat]}`);
  if (todo.notes) meta.push('메모');
  return meta;
}
function row(todo: Todo): HTMLLIElement {
  const check = element('input', { type: 'checkbox', checked: Boolean(todo.completedAt), ariaLabel: `${todo.title} 완료` });
  check.addEventListener('change', () => client.toggleTodo(todo));
  const star = element('button', { type: 'button', className: `todo-star${todo.important ? ' on' : ''}`, textContent: todo.important ? '★' : '☆', ariaLabel: `${todo.title} 중요` });
  star.setAttribute('aria-pressed', String(todo.important));
  star.addEventListener('click', event => { event.stopPropagation(); client.put('todos', { ...todo, important: !todo.important }); });
  const overdue = !todo.completedAt && todo.due !== null && todo.due < today();
  const li = element('li', { className: `todo-item${todo.id === selectedTodo ? ' selected' : ''}${overdue ? ' overdue' : ''}`, draggable: !todo.completedAt, dataset: { id: todo.id } },
    check, element('div', { className: 'todo-text' }, element('span', { className: 'todo-name', textContent: todo.title }), element('span', { className: 'todo-meta', textContent: metaOf(todo).join(' · ') })), star);
  li.addEventListener('click', event => {
    if ((event.target as Element).closest('input')) return;
    selectedTodo = todo.id;
    render();
  });
  li.addEventListener('dragstart', event => { event.dataTransfer?.setData('text/devpostit-todo', todo.id); li.classList.add('dragging'); });
  li.addEventListener('dragend', () => li.classList.remove('dragging'));
  li.addEventListener('dragover', event => { event.preventDefault(); li.classList.add('drop-target'); });
  li.addEventListener('dragleave', () => li.classList.remove('drop-target'));
  li.addEventListener('drop', event => {
    event.preventDefault();
    li.classList.remove('drop-target');
    const moved = client.state.todos.find(item => item.id === event.dataTransfer?.getData('text/devpostit-todo'));
    if (moved && moved.id !== todo.id) moveBefore(moved, todo);
  });
  return li;
}
function moveBefore(moved: Todo, target: Todo): void {
  sort.value = 'manual';
  const list = visible().active.filter(item => item.id !== moved.id);
  const at = list.findIndex(item => item.id === target.id);
  const before = at > 0 ? list[at - 1].order : target.order - 2;
  client.put('todos', { ...moved, order: (before + target.order) / 2 });
}
function renderNav(): void {
  const active = client.state.todos.filter(todo => !todo.completedAt);
  const entry = (id: string, name: string, icon: string) => {
    const count = active.filter(todo => inList(todo, id)).length;
    const button = element('button', { type: 'button', className: selectedList === id && !search.value ? 'selected' : '' }, element('span', { className: 'todo-nav-icon', textContent: icon }), element('span', { className: 'todo-nav-name', textContent: name }), element('span', { className: 'todo-count', textContent: count ? String(count) : '' }));
    button.addEventListener('click', () => { selectedList = id; localStorageSet('devpostit.todo.list', id); search.value = ''; selectedTodo = null; render(); input.focus(); });
    const li = element('li', {}, button);
    if (!isSmart(id)) {
      li.addEventListener('dragover', event => { event.preventDefault(); button.classList.add('drop-target'); });
      li.addEventListener('dragleave', () => button.classList.remove('drop-target'));
      li.addEventListener('drop', event => {
        event.preventDefault();
        button.classList.remove('drop-target');
        const moved = client.state.todos.find(item => item.id === event.dataTransfer?.getData('text/devpostit-todo'));
        if (moved) client.put('todos', { ...moved, listId: id });
      });
    }
    return li;
  };
  $('#todo-smart').replaceChildren(...smartLists.map(list => entry(list.id, list.name, list.icon)));
  $('#todo-lists').replaceChildren(entry(defaultList, '작업', '☰'), ...[...client.state.lists].sort((a, b) => a.order - b.order).map(list => entry(list.id, list.name, '☰')));
}
function renderDetail(): void {
  const todo = client.state.todos.find(item => item.id === selectedTodo);
  detail.hidden = !todo;
  if (!todo) { detail.replaceChildren(); return; }
  // 입력 중인 칸은 다시 그리지 않습니다.
  if (detail.dataset.id === todo.id && detail.contains(document.activeElement) && document.activeElement !== detail) return;
  detail.dataset.id = todo.id;
  const update = (change: Partial<Todo>) => client.put('todos', { ...todo, ...change });
  const title = element('textarea', { className: 'todo-detail-title', value: todo.title, rows: 2, maxLength: 500, ariaLabel: '할 일 이름' });
  title.addEventListener('change', () => { const value = title.value.trim(); if (value) update({ title: value }); else title.value = todo.title; });
  const steps = element('ul', { className: 'todo-steps' });
  for (const step of todo.steps) {
    const box = element('input', { type: 'checkbox', checked: step.done, ariaLabel: `${step.title} 완료` });
    box.addEventListener('change', () => update({ steps: todo.steps.map(item => item.id === step.id ? { ...item, done: box.checked } : item) }));
    const remove = element('button', { type: 'button', textContent: '×', ariaLabel: `${step.title} 단계 삭제` });
    remove.addEventListener('click', () => update({ steps: todo.steps.filter(item => item.id !== step.id) }));
    steps.append(element('li', {}, box, element('span', { textContent: step.title, className: step.done ? 'done' : '' }), remove));
  }
  const stepInput = element('input', { type: 'text', placeholder: '+ 다음 단계', ariaLabel: '새 단계', maxLength: 500 });
  stepInput.addEventListener('keydown', event => {
    if (event.key !== 'Enter' || event.isComposing || !stepInput.value.trim()) return;
    const value = stepInput.value.trim();
    stepInput.value = '';
    client.put('todos', { ...todo, steps: [...todo.steps, { id: crypto.randomUUID(), title: value, done: false }] });
    detail.dataset.id = '';
    render();
    detail.querySelector<HTMLInputElement>('input[aria-label="새 단계"]')?.focus();
  });
  const myDay = element('button', { type: 'button', textContent: todo.myDay === today() ? '☀ 오늘 할 일에서 제거' : '☀ 오늘 할 일에 추가' });
  myDay.addEventListener('click', () => { detail.dataset.id = ''; update({ myDay: todo.myDay === today() ? null : today() }); });
  const due = element('input', { type: 'date', value: todo.due ?? '', ariaLabel: '기한' });
  due.addEventListener('change', () => { detail.dataset.id = ''; update({ due: due.value || null }); });
  const quick = (label: string, offset: number) => {
    const button = element('button', { type: 'button', textContent: label });
    button.addEventListener('click', () => { detail.dataset.id = ''; update({ due: dateText(addDays(new Date(), offset)) }); });
    return button;
  };
  const reminder = element('input', { type: 'datetime-local', value: todo.reminder ?? '', ariaLabel: '알림' });
  reminder.addEventListener('change', () => update({ reminder: reminder.value ? dateTimeText(parseLocal(reminder.value)) : null }));
  const repeat = element('select', { ariaLabel: '반복' });
  for (const [value, name] of Object.entries(repeatNames)) repeat.append(new Option(name, value));
  repeat.value = todo.repeat;
  repeat.addEventListener('change', () => { detail.dataset.id = ''; update({ repeat: repeat.value as Repeat, due: todo.due ?? (repeat.value === 'none' ? null : today()) }); });
  const list = element('select', { ariaLabel: '목록' });
  list.append(new Option('작업', defaultList));
  for (const item of client.state.lists) list.append(new Option(item.name, item.id));
  list.value = todo.listId;
  list.addEventListener('change', () => update({ listId: list.value }));
  const notes = element('textarea', { className: 'todo-notes', value: todo.notes, rows: 6, placeholder: '메모', ariaLabel: '메모', maxLength: 20000 });
  notes.addEventListener('change', () => update({ notes: notes.value }));
  const close = element('button', { type: 'button', textContent: '닫기' });
  close.addEventListener('click', () => { selectedTodo = null; render(); input.focus(); });
  const remove = element('button', { type: 'button', textContent: '삭제', className: 'danger' });
  remove.addEventListener('click', async () => {
    if (await ask(`‘${todo.title}’ 할 일을 삭제할까요? 되돌릴 수 없습니다.`, '삭제') !== 0) return;
    selectedTodo = null;
    client.delete('todos', todo.id);
    input.focus();
  });
  const field = (name: string, ...controls: Node[]) => element('div', { className: 'todo-field' }, element('span', { textContent: name }), ...controls);
  detail.replaceChildren(title, steps, stepInput, myDay,
    field('기한', due, quick('오늘', 0), quick('내일', 1), quick('다음 주', 7)),
    field('알림', reminder), field('반복', repeat), field('목록', list), notes,
    element('div', { className: 'tool-dialog-actions' }, close, remove),
    element('p', { className: 'todo-created', textContent: `만든 날짜: ${relativeDay(todo.created.slice(0, 10))}` }));
}
function render(): void {
  renderNav();
  const searching = search.value.trim().length > 0;
  $('#todo-title').textContent = searching ? `‘${search.value.trim()}’ 검색 결과` : smartLists.find(list => list.id === selectedList)?.name ?? listName(selectedList);
  const owned = !searching && !isSmart(selectedList) && selectedList !== defaultList;
  $('#todo-rename-list').hidden = !owned;
  $('#todo-delete-list').hidden = !owned;
  const { active, done } = visible();
  items.replaceChildren(...active.map(row));
  if (!active.length && !done.length) items.append(element('li', { className: 'todo-empty', textContent: searching ? '찾는 할 일이 없습니다.' : '할 일이 없습니다.' }));
  completedToggle.hidden = done.length === 0;
  $('#todo-clear-completed').hidden = done.length === 0;
  completedToggle.textContent = `${showCompleted ? '▾' : '▸'} 완료됨 ${done.length}`;
  completedToggle.setAttribute('aria-expanded', String(showCompleted));
  completed.hidden = !showCompleted;
  completed.replaceChildren(...(showCompleted ? done.map(row) : []));
  renderDetail();
}
completedToggle.addEventListener('click', () => { showCompleted = !showCompleted; render(); });
// 지금 보고 있는 목록이나 검색 결과의 완료 항목만 지웁니다.
$('#todo-clear-completed').addEventListener('click', async () => {
  const { done } = visible();
  if (!done.length || await ask(`완료된 할 일 ${done.length}개를 삭제할까요? 되돌릴 수 없습니다.`, '삭제') !== 0) return;
  if (done.some(todo => todo.id === selectedTodo)) selectedTodo = null;
  for (const todo of done) client.delete('todos', todo.id);
  input.focus();
});
search.addEventListener('input', render);
sort.value = localStorageGet('devpostit.todo.sort') ?? 'manual';
sort.addEventListener('change', () => { localStorageSet('devpostit.todo.sort', sort.value); render(); });
const newList = $<HTMLInputElement>('#todo-new-list');
newList.addEventListener('keydown', event => {
  if (event.key !== 'Enter' || event.isComposing || !newList.value.trim()) return;
  const id = crypto.randomUUID();
  const name = newList.value.trim();
  newList.value = '';
  selectedList = id;
  localStorageSet('devpostit.todo.list', id);
  client.put('lists', { id, name, order: client.state.lists.length + 1 });
  input.focus();
});
$('#todo-delete-list').addEventListener('click', async () => {
  const count = client.state.todos.filter(todo => todo.listId === selectedList).length;
  if (await ask(`‘${listName(selectedList)}’ 목록과 할 일 ${count}개를 삭제할까요? 되돌릴 수 없습니다.`, '삭제') !== 0) return;
  const id = selectedList;
  selectedList = defaultList;
  client.delete('lists', id);
});
$('#todo-rename-list').addEventListener('click', () => {
  const list = client.state.lists.find(item => item.id === selectedList);
  if (!list) return;
  const title = $('#todo-title');
  const field = element('input', { type: 'text', value: list.name, maxLength: 200, ariaLabel: '목록 이름' });
  title.replaceChildren(field);
  field.select();
  const finish = (save: boolean) => { const value = field.value.trim(); if (save && value) client.put('lists', { ...list, name: value }); else render(); };
  field.addEventListener('keydown', event => { if (event.isComposing) return; if (event.key === 'Enter') finish(true); else if (event.key === 'Escape') finish(false); });
  field.addEventListener('blur', () => finish(true), { once: true });
  field.focus();
});
window.addEventListener('keydown', event => {
  if (event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); input.focus(); }
  if (event.ctrlKey && event.key.toLowerCase() === 'f') { event.preventDefault(); search.focus(); }
});
client.subscribe(render);
render();
input.focus();
