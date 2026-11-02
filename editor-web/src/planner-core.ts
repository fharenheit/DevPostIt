import { hasHost, onHostMessage, sendToHost } from './tool-host';

export type TodoStep = { id: string; title: string; done: boolean };
export type Todo = { id: string; title: string; notes: string; listId: string; important: boolean; myDay: string | null; due: string | null; reminder: string | null; repeat: Repeat; completedAt: string | null; steps: TodoStep[]; created: string; order: number };
export type TodoList = { id: string; name: string; order: number };
export type CalendarEvent = { id: string; title: string; start: string; end: string; allDay: boolean; location: string; notes: string; color: string; repeat: Repeat; repeatUntil: string | null; reminderMinutes: number | null; exceptions: string[] };
export type Repeat = 'none' | 'daily' | 'weekdays' | 'weekly' | 'monthly' | 'yearly';
export type PlannerState = { lists: TodoList[]; todos: Todo[]; events: CalendarEvent[] };
type Collection = 'lists' | 'todos' | 'events';
export const repeatNames: Record<Repeat, string> = { none: '반복 안 함', daily: '매일', weekdays: '평일', weekly: '매주', monthly: '매월', yearly: '매년' };
export const defaultList = 'tasks';

const pad = (value: number) => String(value).padStart(2, '0');
export const dateText = (date: Date) => `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
export const timeText = (date: Date) => `${pad(date.getHours())}:${pad(date.getMinutes())}`;
export const dateTimeText = (date: Date) => `${dateText(date)}T${timeText(date)}`;
export function parseLocal(text: string): Date {
  const [day, time = '00:00'] = text.split('T');
  const [year, month, date] = day.split('-').map(Number);
  const [hour, minute] = time.split(':').map(Number);
  return new Date(year, month - 1, date, hour, minute);
}
export const addDays = (date: Date, days: number) => new Date(date.getFullYear(), date.getMonth(), date.getDate() + days, date.getHours(), date.getMinutes());
export const addMinutes = (date: Date, minutes: number) => new Date(date.getTime() + minutes * 60000);
export const startOfDay = (date: Date) => new Date(date.getFullYear(), date.getMonth(), date.getDate());
export const startOfWeek = (date: Date) => addDays(startOfDay(date), -date.getDay());
export const today = () => dateText(new Date());
export const weekdayNames = ['일', '월', '화', '수', '목', '금', '토'];
export const dayLabel = (date: Date) => `${date.getMonth() + 1}월 ${date.getDate()}일 (${weekdayNames[date.getDay()]})`;
export function relativeDay(text: string): string {
  const value = parseLocal(text);
  const difference = Math.round((startOfDay(value).getTime() - startOfDay(new Date()).getTime()) / 86400000);
  return difference === 0 ? '오늘' : difference === 1 ? '내일' : difference === -1 ? '어제' : `${value.getFullYear() === new Date().getFullYear() ? '' : `${value.getFullYear()}년 `}${dayLabel(value)}`;
}

// 앱의 PlannerStore.OccursOn과 같은 규칙입니다.
export function occursOn(event: CalendarEvent, day: string): boolean {
  const first = event.start.slice(0, 10);
  if (day < first || event.exceptions.includes(day) || (event.repeatUntil && day > event.repeatUntil)) return false;
  const a = parseLocal(first), b = parseLocal(day);
  switch (event.repeat) {
    case 'daily': return true;
    case 'weekdays': return b.getDay() !== 0 && b.getDay() !== 6;
    case 'weekly': return a.getDay() === b.getDay();
    case 'monthly': return a.getDate() === b.getDate();
    case 'yearly': return a.getDate() === b.getDate() && a.getMonth() === b.getMonth();
    default: return day === first;
  }
}
export type Occurrence = { event: CalendarEvent; day: string; start: Date; end: Date };
// 끝은 종일 일정이면 마지막 날의 시작 시각입니다.
export function occurrences(events: CalendarEvent[], from: Date, to: Date): Occurrence[] {
  const result: Occurrence[] = [];
  for (const event of events) {
    const start = parseLocal(event.start), end = parseLocal(event.end);
    const length = end.getTime() - start.getTime();
    const spanDays = Math.ceil(length / 86400000) + 1;
    for (let day = addDays(startOfDay(from), -spanDays); day <= to; day = addDays(day, 1)) {
      const text = dateText(day);
      if (!occursOn(event, text)) continue;
      const begin = new Date(day.getFullYear(), day.getMonth(), day.getDate(), start.getHours(), start.getMinutes());
      const finish = new Date(begin.getTime() + length);
      if (finish >= from && begin <= to) result.push({ event, day: text, start: begin, end: finish });
    }
  }
  return result.sort((a, b) => a.start.getTime() - b.start.getTime() || Number(b.event.allDay) - Number(a.event.allDay));
}

export function nextDue(due: string, repeat: Repeat): string {
  const date = parseLocal(due);
  switch (repeat) {
    case 'daily': return dateText(addDays(date, 1));
    case 'weekdays': { let next = addDays(date, 1); while (next.getDay() === 0 || next.getDay() === 6) next = addDays(next, 1); return dateText(next); }
    case 'weekly': return dateText(addDays(date, 7));
    case 'monthly': { const wanted = date.getDate(); const next = new Date(date.getFullYear(), date.getMonth() + 1, 1); const last = new Date(next.getFullYear(), next.getMonth() + 1, 0).getDate(); return dateText(new Date(next.getFullYear(), next.getMonth(), Math.min(wanted, last))); }
    case 'yearly': return dateText(new Date(date.getFullYear() + 1, date.getMonth(), date.getDate()));
    default: return due;
  }
}

export class PlannerClient {
  state: PlannerState = { lists: [], todos: [], events: [] };
  private readonly listeners: (() => void)[] = [];
  error: (message: string) => void = () => {};
  constructor() {
    onHostMessage(message => {
      if (message.type === 'planner.state' && message.state && typeof message.state === 'object') { this.state = message.state as PlannerState; this.listeners.forEach(listener => listener()); }
      else if (message.type === 'planner.error' && typeof message.message === 'string') this.error(message.message);
    });
    sendToHost('planner.load');
  }
  readonly hosted = hasHost;
  subscribe(listener: () => void): void { this.listeners.push(listener); }
  put<C extends Collection>(collection: C, item: PlannerState[C][number]): void {
    const items = this.state[collection] as { id: string }[];
    const at = items.findIndex(value => value.id === item.id);
    if (at >= 0) items[at] = item; else items.push(item);
    this.listeners.forEach(listener => listener());
    sendToHost('planner.put', { collection, item });
  }
  delete(collection: Collection, id: string): void {
    (this.state[collection] as { id: string }[]) = (this.state[collection] as { id: string }[]).filter(value => value.id !== id);
    if (collection === 'lists') this.state.todos = this.state.todos.filter(todo => todo.listId !== id);
    this.listeners.forEach(listener => listener());
    sendToHost('planner.delete', { collection, id });
  }
  // 반복하는 할 일을 끝내면 다음 차례를 새로 만듭니다.
  toggleTodo(todo: Todo): void {
    if (todo.completedAt) { this.put('todos', { ...todo, completedAt: null }); return; }
    this.put('todos', { ...todo, completedAt: dateTimeText(new Date()) });
    if (todo.repeat === 'none') return;
    const due = nextDue(todo.due ?? today(), todo.repeat);
    const gap = parseLocal(due).getTime() - parseLocal(todo.due ?? today()).getTime();
    this.put('todos', { ...todo, id: crypto.randomUUID(), due, completedAt: null, myDay: null, created: dateTimeText(new Date()), steps: todo.steps.map(step => ({ ...step, id: crypto.randomUUID(), done: false })),
      reminder: todo.reminder ? dateTimeText(new Date(parseLocal(todo.reminder).getTime() + gap)) : null });
  }
}

export function element<K extends keyof HTMLElementTagNameMap>(tag: K, properties: Partial<HTMLElementTagNameMap[K]> & { dataset?: Record<string, string> } = {}, ...children: (Node | string | null)[]): HTMLElementTagNameMap[K] {
  const node = document.createElement(tag);
  const { dataset, ...rest } = properties;
  Object.assign(node, rest);
  if (dataset) Object.assign(node.dataset, dataset);
  node.append(...children.filter((child): child is Node | string => child !== null));
  return node;
}
// 브라우저 기본 확인 창은 앱에서 꺼져 있어 페이지 안의 대화상자를 씁니다. 기본 선택은 취소입니다.
export function ask(message: string, ...choices: string[]): Promise<number> {
  return new Promise(resolve => {
    const dialog = element('dialog', { className: 'tool-confirm' });
    dialog.append(element('p', { textContent: message }));
    const row = element('div', { className: 'tool-dialog-actions' });
    choices.forEach((choice, index) => {
      const button = element('button', { type: 'button', textContent: choice });
      button.addEventListener('click', () => { dialog.close(); dialog.remove(); resolve(index); });
      row.append(button);
    });
    const cancel = element('button', { type: 'button', textContent: '취소' });
    cancel.addEventListener('click', () => { dialog.close(); });
    dialog.addEventListener('close', () => { dialog.remove(); resolve(-1); });
    row.append(cancel);
    dialog.append(row);
    document.body.append(dialog);
    dialog.showModal();
    cancel.focus();
  });
}
