import './style.css';
import './tools.css';
import { applyTypography, readFontSettings } from './fonts';
import { applyHolidayData, holidaysOn, holidaysOf } from './korean-holidays';
import { onHostMessage } from './tool-host';
import { addDays, addMinutes, ask, dateText, dateTimeText, dayLabel, element, occurrences, parseLocal, PlannerClient, repeatNames, startOfDay, startOfWeek, timeText, today, weekdayNames, type CalendarEvent, type Occurrence, type Repeat, type Todo } from './planner-core';

applyTypography(readFontSettings());
const client = new PlannerClient();
const $ = <T extends HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const body = $<HTMLElement>('#cal-body');
const status = $<HTMLElement>('#cal-status');
client.error = message => { status.textContent = message; };
type View = 'month' | 'week' | 'day' | 'list';
const saved = (() => { try { return localStorage.getItem('devpostit.calendar.view'); } catch { return null; } })();
let view: View = saved === 'week' || saved === 'day' || saved === 'list' ? saved : 'month';
let cursor = startOfDay(new Date());
const hourHeight = 48, snapMinutes = 15;

const repeatSelect = $<HTMLSelectElement>('#ev-repeat');
for (const [value, name] of Object.entries(repeatNames)) repeatSelect.append(new Option(name, value));

function setView(next: View): void {
  view = next;
  try { localStorage.setItem('devpostit.calendar.view', next); } catch { /* 저장하지 못해도 동작합니다. */ }
  render();
}
function move(direction: number): void {
  if (view === 'month' || view === 'list') cursor = new Date(cursor.getFullYear(), cursor.getMonth() + direction, 1);
  else cursor = addDays(cursor, direction * (view === 'week' ? 7 : 1));
  render();
}
const todosOn = (day: string): Todo[] => client.state.todos.filter(todo => todo.due === day);

function chip(item: Occurrence, day: string): HTMLElement {
  const label = item.event.allDay ? item.event.title : `${item.start.getDate() === parseLocal(day).getDate() ? `${timeText(item.start)} ` : ''}${item.event.title}`;
  const node = element('div', { className: `cal-chip color-${item.event.color}${item.event.allDay ? ' all-day' : ''}`, title: item.event.title, textContent: label, tabIndex: 0, dataset: { id: item.event.id, day: item.day } });
  node.setAttribute('role', 'button');
  attachDayDrag(node, item);
  return node;
}
function todoChip(todo: Todo): HTMLElement {
  const node = element('button', { type: 'button', className: `cal-todo${todo.completedAt ? ' done' : ''}`, textContent: `${todo.completedAt ? '☑' : '☐'} ${todo.title}`, title: `할 일: ${todo.title}` });
  node.setAttribute('aria-pressed', String(Boolean(todo.completedAt)));
  node.addEventListener('click', event => { event.stopPropagation(); client.toggleTodo(todo); });
  return node;
}
function holidayLabels(day: string): HTMLElement[] {
  return holidaysOn(day).map(holiday => element('span', { className: 'cal-holiday', textContent: holiday.name, title: holiday.name }));
}
// 일정 칩을 다른 날로 끌어 옮깁니다. 반복 일정은 전체를 같은 날짜만큼 옮깁니다.
function attachDayDrag(node: HTMLElement, item: Occurrence): void {
  node.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    event.stopPropagation();
    const startX = event.clientX, startY = event.clientY;
    let dragging = false;
    let target: HTMLElement | null = null;
    const moveHandler = (moving: PointerEvent) => {
      if (!dragging && Math.hypot(moving.clientX - startX, moving.clientY - startY) < 5) return;
      dragging = true;
      node.classList.add('dragging');
      target?.classList.remove('drop-target');
      target = document.elementFromPoint(moving.clientX, moving.clientY)?.closest<HTMLElement>('[data-date]') ?? null;
      target?.classList.add('drop-target');
    };
    const up = () => {
      window.removeEventListener('pointermove', moveHandler);
      window.removeEventListener('pointerup', up);
      node.classList.remove('dragging');
      target?.classList.remove('drop-target');
      if (!dragging) { openDialog(item.event, item.day); return; }
      if (target?.dataset.date) {
        const days = Math.round((parseLocal(target.dataset.date).getTime() - parseLocal(item.day).getTime()) / 86400000);
        if (days) shiftEvent(item.event, days * 1440);
      }
    };
    window.addEventListener('pointermove', moveHandler);
    window.addEventListener('pointerup', up);
  });
}
function shiftEvent(event: CalendarEvent, minutes: number, endOnly = false): void {
  const format = (text: string) => event.allDay ? dateText(addMinutes(parseLocal(text), minutes)) : dateTimeText(addMinutes(parseLocal(text), minutes));
  client.put('events', { ...event, start: endOnly ? event.start : format(event.start), end: format(event.end) });
}

function renderMonth(): void {
  $('#cal-title').textContent = `${cursor.getFullYear()}년 ${cursor.getMonth() + 1}월`;
  const first = startOfWeek(new Date(cursor.getFullYear(), cursor.getMonth(), 1));
  const last = addDays(first, 41);
  const events = occurrences(client.state.events, first, addDays(last, 1));
  const grid = element('div', { className: 'cal-month' });
  for (const [index, name] of weekdayNames.entries()) grid.append(element('div', { className: `cal-weekday${index === 0 ? ' sunday' : index === 6 ? ' saturday' : ''}`, textContent: name }));
  const perCell = Math.max(1, Math.floor((Math.max(body.clientHeight, 360) / 6 - 26) / 21));
  for (let at = 0; at < 42; at++) {
    const date = addDays(first, at);
    const day = dateText(date);
    const holidays = holidayLabels(day);
    const cell = element('div', { className: `cal-day${date.getMonth() !== cursor.getMonth() ? ' other' : ''}${day === today() ? ' today' : ''}${date.getDay() === 0 || holidays.length ? ' sunday' : date.getDay() === 6 ? ' saturday' : ''}`, dataset: { date: day } });
    const number = element('button', { type: 'button', className: 'cal-number', textContent: String(date.getDate()), ariaLabel: `${dayLabel(date)} 일간 보기` });
    number.addEventListener('click', event => { event.stopPropagation(); cursor = date; setView('day'); });
    cell.append(element('div', { className: 'cal-day-head' }, number, ...holidays));
    const entries: HTMLElement[] = [
      ...events.filter(item => dateText(item.start) <= day && dateText(item.end) >= day).map(item => chip(item, day)),
      ...todosOn(day).map(todoChip),
    ];
    cell.append(...entries.slice(0, perCell));
    if (entries.length > perCell) {
      const more = element('button', { type: 'button', className: 'cal-more', textContent: `+${entries.length - perCell}개 더` });
      more.addEventListener('click', event => { event.stopPropagation(); cursor = date; setView('day'); });
      cell.append(more);
    }
    cell.addEventListener('dblclick', event => { if (!(event.target as Element).closest('.cal-chip, .cal-todo')) openNew(new Date(date.getFullYear(), date.getMonth(), date.getDate(), 9), true); });
    grid.append(cell);
  }
  body.replaceChildren(grid);
}

type Placed = { item: Occurrence; column: number; columns: number; start: Date; end: Date };
// 겹치는 일정은 나란히 놓습니다.
function placeDay(items: Occurrence[], date: Date): Placed[] {
  const dayStart = startOfDay(date), dayEnd = addDays(dayStart, 1);
  const placed: Placed[] = items.map(item => ({ item, column: 0, columns: 1, start: item.start < dayStart ? dayStart : item.start, end: item.end > dayEnd ? dayEnd : item.end }));
  const groups: Placed[][] = [];
  for (const entry of placed.sort((a, b) => a.start.getTime() - b.start.getTime())) {
    const group = groups.at(-1);
    if (group && group.some(other => other.end > entry.start)) group.push(entry);
    else groups.push([entry]);
  }
  for (const group of groups) {
    const columns: Date[] = [];
    for (const entry of group) {
      let column = columns.findIndex(end => end <= entry.start);
      if (column < 0) { column = columns.length; columns.push(entry.end); } else columns[column] = entry.end;
      entry.column = column;
    }
    for (const entry of group) entry.columns = columns.length;
  }
  return placed;
}
function renderTimeGrid(days: Date[]): void {
  const from = days[0], to = addDays(days.at(-1)!, 1);
  const events = occurrences(client.state.events, from, to);
  const crossesDays = (item: Occurrence) => item.event.allDay;
  const head = element('div', { className: 'cal-grid-head', style: `grid-template-columns: 3.5rem repeat(${days.length}, 1fr)` } as object);
  head.append(element('div', { className: 'cal-gutter', textContent: '종일' }));
  for (const date of days) {
    const day = dateText(date);
    const holidays = holidayLabels(day);
    const cell = element('div', { className: `cal-allday${date.getDay() === 0 || holidays.length ? ' sunday' : date.getDay() === 6 ? ' saturday' : ''}${day === today() ? ' today' : ''}`, dataset: { date: day } });
    const number = element('button', { type: 'button', className: 'cal-number', textContent: `${date.getDate()} (${weekdayNames[date.getDay()]})`, ariaLabel: `${dayLabel(date)} 일간 보기` });
    number.addEventListener('click', () => { cursor = date; setView('day'); });
    cell.append(element('div', { className: 'cal-day-head' }, number, ...holidays));
    cell.append(...events.filter(item => crossesDays(item) && dateText(item.start) <= day && dateText(item.end) >= day).map(item => chip(item, day)), ...todosOn(day).map(todoChip));
    cell.addEventListener('dblclick', event => { if (!(event.target as Element).closest('.cal-chip, .cal-todo, .cal-number')) openNew(new Date(date.getFullYear(), date.getMonth(), date.getDate(), 9), true); });
    head.append(cell);
  }
  const scroll = element('div', { className: 'cal-scroll' });
  const grid = element('div', { className: 'cal-times', style: `grid-template-columns: 3.5rem repeat(${days.length}, 1fr); height: ${hourHeight * 24}px` } as object);
  const gutter = element('div', { className: 'cal-gutter-times' });
  for (let hour = 0; hour < 24; hour++) gutter.append(element('div', { className: 'cal-hour-label', textContent: `${String(hour).padStart(2, '0')}:00`, style: `top: ${hour * hourHeight}px` } as object));
  grid.append(gutter);
  for (const date of days) {
    const day = dateText(date);
    const column = element('div', { className: `cal-column${day === today() ? ' today' : ''}`, dataset: { column: day } });
    for (const entry of placeDay(events.filter(item => !item.event.allDay && dateText(item.start) <= day && dateText(item.end) >= day && item.end > startOfDay(date)), date)) column.append(timedEvent(entry, date));
    attachRangeCreate(column, date);
    if (day === today()) {
      const now = new Date();
      column.append(element('div', { className: 'cal-now', style: `top: ${(now.getHours() + now.getMinutes() / 60) * hourHeight}px` } as object));
    }
    grid.append(column);
  }
  scroll.append(grid);
  body.replaceChildren(head, scroll);
  scroll.scrollTop = hourHeight * 8;
}
const minutesFrom = (column: HTMLElement, clientY: number) => {
  const box = column.getBoundingClientRect();
  return Math.max(0, Math.min(1440, Math.round((clientY - box.top) / hourHeight * 60 / snapMinutes) * snapMinutes));
};
function attachRangeCreate(column: HTMLElement, date: Date): void {
  column.addEventListener('pointerdown', event => {
    if (event.button !== 0 || event.target !== column) return;
    const startMinute = minutesFrom(column, event.clientY);
    const preview = element('div', { className: 'cal-range' });
    column.append(preview);
    let endMinute = startMinute;
    const draw = () => { const a = Math.min(startMinute, endMinute), b = Math.max(startMinute, endMinute, a + 30); preview.style.top = `${a / 60 * hourHeight}px`; preview.style.height = `${(b - a) / 60 * hourHeight}px`; };
    draw();
    const moveHandler = (moving: PointerEvent) => { endMinute = minutesFrom(column, moving.clientY); draw(); };
    const up = () => {
      window.removeEventListener('pointermove', moveHandler);
      window.removeEventListener('pointerup', up);
      preview.remove();
      const a = Math.min(startMinute, endMinute), b = Math.max(startMinute, endMinute);
      const base = startOfDay(date);
      openNew(addMinutes(base, a), false, b - a >= snapMinutes ? addMinutes(base, b) : addMinutes(base, a + 60));
    };
    window.addEventListener('pointermove', moveHandler);
    window.addEventListener('pointerup', up);
  });
}
function timedEvent(entry: Placed, date: Date): HTMLElement {
  const dayStart = startOfDay(date);
  const top = (entry.start.getTime() - dayStart.getTime()) / 3600000 * hourHeight;
  const height = Math.max(20, (entry.end.getTime() - entry.start.getTime()) / 3600000 * hourHeight);
  const width = 100 / entry.columns;
  const node = element('div', { className: `cal-event color-${entry.item.event.color}`, title: entry.item.event.title, style: `top: ${top}px; height: ${height}px; left: ${entry.column * width}%; width: calc(${width}% - 2px)`, tabIndex: 0, dataset: { id: entry.item.event.id } } as object,
    element('strong', { textContent: entry.item.event.title }), element('span', { textContent: `${timeText(entry.item.start)}~${timeText(entry.item.end)}` }), entry.item.event.location ? element('span', { textContent: entry.item.event.location }) : null);
  node.setAttribute('role', 'button');
  const handle = element('div', { className: 'cal-resize', title: '끌어서 끝 시각 변경' });
  node.append(handle);
  node.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    event.stopPropagation();
    const resizing = event.target === handle;
    const startX = event.clientX, startY = event.clientY;
    const originColumn = node.parentElement as HTMLElement;
    let dragging = false, minutes = 0;
    const moveHandler = (moving: PointerEvent) => {
      if (!dragging && Math.hypot(moving.clientX - startX, moving.clientY - startY) < 5) return;
      dragging = true;
      node.classList.add('dragging');
      const vertical = Math.round((moving.clientY - startY) / hourHeight * 60 / snapMinutes) * snapMinutes;
      if (resizing) { minutes = vertical; node.style.height = `${Math.max(20, height + vertical / 60 * hourHeight)}px`; return; }
      const over = document.elementFromPoint(moving.clientX, moving.clientY)?.closest<HTMLElement>('[data-column]');
      const days = over?.dataset.column ? Math.round((parseLocal(over.dataset.column).getTime() - parseLocal(originColumn.dataset.column!).getTime()) / 86400000) : 0;
      minutes = vertical + days * 1440;
      node.style.transform = `translate(${days ? moving.clientX - startX : 0}px, ${vertical / 60 * hourHeight}px)`;
    };
    const up = () => {
      window.removeEventListener('pointermove', moveHandler);
      window.removeEventListener('pointerup', up);
      node.classList.remove('dragging');
      if (!dragging) { openDialog(entry.item.event, entry.item.day); return; }
      if (!minutes) { render(); return; }
      if (resizing && parseLocal(entry.item.event.end).getTime() + minutes * 60000 <= parseLocal(entry.item.event.start).getTime()) { render(); return; }
      shiftEvent(entry.item.event, minutes, resizing);
    };
    window.addEventListener('pointermove', moveHandler);
    window.addEventListener('pointerup', up);
  });
  return node;
}
function renderList(): void {
  const from = view === 'list' ? new Date(cursor.getFullYear(), cursor.getMonth(), 1) : cursor;
  const to = new Date(from.getFullYear(), from.getMonth() + 3, 0);
  $('#cal-title').textContent = `${from.getFullYear()}년 ${from.getMonth() + 1}월부터 3개월`;
  const events = occurrences(client.state.events, from, addDays(to, 1));
  const list = element('div', { className: 'cal-list' });
  for (let date = from; date <= to; date = addDays(date, 1)) {
    const day = dateText(date);
    const entries = events.filter(item => dateText(item.start) <= day && dateText(item.end) >= day);
    const todos = todosOn(day);
    const holidays = holidaysOn(day);
    if (!entries.length && !todos.length && !holidays.length) continue;
    const group = element('section', { className: `cal-list-day${day === today() ? ' today' : ''}`, dataset: { date: day } });
    group.append(element('h2', { className: date.getDay() === 0 || holidays.length ? 'sunday' : date.getDay() === 6 ? 'saturday' : '', textContent: `${date.getFullYear() === new Date().getFullYear() ? '' : `${date.getFullYear()}년 `}${dayLabel(date)}` }));
    for (const holiday of holidays) group.append(element('div', { className: 'cal-list-row' }, element('span', { className: 'cal-list-time', textContent: '공휴일' }), element('span', { className: 'cal-holiday', textContent: holiday.name })));
    for (const item of entries) {
      const row = element('button', { type: 'button', className: 'cal-list-row' }, element('span', { className: 'cal-list-time', textContent: item.event.allDay ? '종일' : `${timeText(item.start)}~${timeText(item.end)}` }), element('span', { className: `cal-dot color-${item.event.color}` }), element('span', { textContent: item.event.title }), item.event.location ? element('span', { className: 'cal-list-location', textContent: item.event.location }) : null);
      row.addEventListener('click', () => openDialog(item.event, item.day));
      group.append(row);
    }
    for (const todo of todos) group.append(element('div', { className: 'cal-list-row' }, element('span', { className: 'cal-list-time', textContent: '할 일' }), todoChip(todo)));
    list.append(group);
  }
  if (!list.children.length) list.append(element('p', { className: 'cal-empty', textContent: '이 기간에는 일정이 없습니다.' }));
  body.replaceChildren(list);
}
function render(): void {
  document.querySelectorAll<HTMLButtonElement>('#cal-views button').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.view === view)));
  body.dataset.view = view;
  if (view === 'month') renderMonth();
  else if (view === 'list') renderList();
  else if (view === 'day') { $('#cal-title').textContent = `${cursor.getFullYear()}년 ${dayLabel(cursor)}`; renderTimeGrid([cursor]); }
  else {
    const first = startOfWeek(cursor);
    const days = Array.from({ length: 7 }, (_, index) => addDays(first, index));
    $('#cal-title').textContent = `${first.getFullYear()}년 ${dayLabel(first)} ~ ${dayLabel(days[6])}`;
    renderTimeGrid(days);
  }
}

const dialog = $<HTMLDialogElement>('#event-dialog');
const field = <T extends HTMLElement>(id: string) => $<T>(`#ev-${id}`);
const title = field<HTMLInputElement>('title'), allDay = field<HTMLInputElement>('all-day'), startDate = field<HTMLInputElement>('start-date'), startTime = field<HTMLInputElement>('start-time'), endDate = field<HTMLInputElement>('end-date'), endTime = field<HTMLInputElement>('end-time');
const location_ = field<HTMLInputElement>('location'), color = field<HTMLSelectElement>('color'), reminder = field<HTMLSelectElement>('reminder'), until = field<HTMLInputElement>('until'), notes = field<HTMLTextAreaElement>('notes');
let editing: CalendarEvent | null = null;
let editingDay: string | null = null;
function syncAllDay(): void { dialog.classList.toggle('all-day', allDay.checked); }
allDay.addEventListener('change', syncAllDay);
function fillDialog(event: CalendarEvent | null, start: Date, end: Date, wholeDay: boolean): void {
  editing = event;
  $('#event-heading').textContent = event ? '일정 편집' : '새 일정';
  title.value = event?.title ?? '';
  allDay.checked = event?.allDay ?? wholeDay;
  startDate.value = dateText(start); startTime.value = timeText(start);
  endDate.value = dateText(end); endTime.value = timeText(end);
  location_.value = event?.location ?? '';
  color.value = event?.color ?? 'blue';
  reminder.value = event?.reminderMinutes === null || event?.reminderMinutes === undefined ? '' : String(event.reminderMinutes);
  repeatSelect.value = event?.repeat ?? 'none';
  until.value = event?.repeatUntil ?? '';
  notes.value = event?.notes ?? '';
  field('error').textContent = '';
  field('delete').hidden = !event;
  syncAllDay();
  dialog.showModal();
  title.focus();
}
function openNew(start: Date, wholeDay = false, end = addMinutes(start, 60)): void {
  editingDay = null;
  fillDialog(null, start, end, wholeDay);
}
function openDialog(event: CalendarEvent, day: string): void {
  editingDay = day;
  fillDialog(event, parseLocal(event.start), parseLocal(event.end), event.allDay);
}
$('#event-form').addEventListener('submit', submit => {
  submit.preventDefault();
  const name = title.value.trim();
  const error = field('error');
  if (!name) { error.textContent = '제목을 입력해 주세요.'; title.focus(); return; }
  const start = allDay.checked ? startDate.value : `${startDate.value}T${startTime.value || '00:00'}`;
  const end = allDay.checked ? endDate.value : `${endDate.value}T${endTime.value || '00:00'}`;
  if (!startDate.value || !endDate.value || end < start) { error.textContent = '끝이 시작보다 빠를 수 없습니다.'; return; }
  client.put('events', { id: editing?.id ?? crypto.randomUUID(), title: name, start, end, allDay: allDay.checked, location: location_.value.trim(), notes: notes.value, color: color.value, repeat: repeatSelect.value as Repeat, repeatUntil: until.value || null, reminderMinutes: reminder.value === '' ? null : Number(reminder.value), exceptions: editing?.exceptions ?? [] });
  dialog.close();
});
$('#ev-cancel').addEventListener('click', () => dialog.close());
$('#ev-delete').addEventListener('click', async () => {
  const event = editing;
  if (!event) return;
  dialog.close();
  if (event.repeat === 'none') {
    if (await ask(`‘${event.title}’ 일정을 삭제할까요? 되돌릴 수 없습니다.`, '삭제') === 0) client.delete('events', event.id);
    return;
  }
  const choice = await ask(`‘${event.title}’은 반복 일정입니다. 어떻게 삭제할까요?`, '이 날만 삭제', '반복 전체 삭제');
  if (choice === 0 && editingDay) client.put('events', { ...event, exceptions: [...event.exceptions, editingDay] });
  else if (choice === 1) client.delete('events', event.id);
});
$('#cal-new').addEventListener('click', () => { const base = view === 'month' || view === 'list' ? (dateText(cursor) === today() || view === 'list' ? new Date() : cursor) : cursor; openNew(new Date(base.getFullYear(), base.getMonth(), base.getDate(), Math.max(9, new Date().getHours() + 1) % 24)); });
$('#cal-today').addEventListener('click', () => { cursor = startOfDay(new Date()); render(); });
$('#cal-prev').addEventListener('click', () => move(-1));
$('#cal-next').addEventListener('click', () => move(1));
document.querySelectorAll<HTMLButtonElement>('#cal-views button').forEach(button => button.addEventListener('click', () => setView(button.dataset.view as View)));
window.addEventListener('keydown', event => {
  if (dialog.open || document.querySelector('dialog[open]') || event.ctrlKey || event.altKey || event.metaKey || (event.target as Element).matches('input, textarea, select')) return;
  const key = event.key.toLowerCase();
  if (key === 'm') setView('month');
  else if (key === 'w') setView('week');
  else if (key === 'd') setView('day');
  else if (key === 'l') setView('list');
  else if (key === 't') { cursor = startOfDay(new Date()); render(); }
  else if (key === 'n') { event.preventDefault(); $<HTMLButtonElement>('#cal-new').click(); }
  else if (event.key === 'ArrowLeft') move(-1);
  else if (event.key === 'ArrowRight') move(1);
});
window.addEventListener('resize', () => { if (view === 'month') render(); });
holidaysOf(cursor.getFullYear());
onHostMessage(message => { if (message.type === 'holidays.state') { applyHolidayData(message.years); render(); } });
client.subscribe(render);
render();
