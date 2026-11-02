import './style.css';
import './tools.css';
import { applyTypography, readFontSettings } from './fonts';
import { ask, element } from './planner-core';
import { hasHost, onHostMessage, sendToHost } from './tool-host';
import { clone, contains, depthOf, findNode, freshIds, fromOutline, layoutMap, newMap, newNode, toOutline, walk, type MapNode, type MapSummary, type MindMap, type Placed } from './mindmap-core';

applyTypography(readFontSettings());
const $ = <T extends HTMLElement>(selector: string) => document.querySelector<T>(selector)!;
const canvas = $<HTMLElement>('#mm-canvas');
const stage = $<HTMLElement>('#mm-stage');
const nodesLayer = $<HTMLElement>('#mm-nodes');
const edges = document.querySelector<SVGSVGElement>('#mm-edges')!;
const status = $<HTMLElement>('#mm-status');
const titleInput = $<HTMLInputElement>('#mm-title');
const colorInput = $<HTMLInputElement>('#mm-color');
const iconSelect = $<HTMLSelectElement>('#mm-icon');
const layoutSelect = $<HTMLSelectElement>('#mm-layout');
const linkInput = $<HTMLInputElement>('#mm-link');
const noteInput = $<HTMLTextAreaElement>('#mm-note');
const findBox = $<HTMLElement>('#mm-find');
const findInput = $<HTMLInputElement>('#mm-find-input');
const icons: Record<string, string> = { done: '✔', important: '❗', question: '❓', star: '★', idea: '💡' };
const branchColors = ['#c0392b', '#1565c0', '#2e7d32', '#8e44ad', '#d35400', '#00838f', '#ad1457', '#5d4037'];
const maxDepth = 100, maxHistory = 100, saveDelay = 500, minZoom = 0.2, maxZoom = 3;

let maps: MapSummary[] = [];
let map: MindMap | null = null;
let selectedId = '';
let editingId: string | null = null;
let discardIfEmpty = false;
let undoStack: string[] = [];
let redoStack: string[] = [];
let dirty = false;
let saveTimer = 0;
let zoom = 1, panX = 0, panY = 0;
let placed: Placed[] = [];
let clipboard: { outline: string; node: MapNode } | null = null;
let matches: string[] = [];
let matchIndex = -1;
let wantOutlineCopy: string | null = null;

const say = (message: string) => { status.textContent = message; };
const selected = () => (map ? findNode(map.root, selectedId) : null);
const remember = (id: string) => { try { localStorage.setItem('devpostit.mindmap.last', id); } catch { /* 저장하지 못해도 동작합니다. */ } };
const lastOpened = () => { try { return localStorage.getItem('devpostit.mindmap.last'); } catch { return null; } };

// 저장 -----------------------------------------------------------------
function markDirty(): void {
  dirty = true;
  window.clearTimeout(saveTimer);
  saveTimer = window.setTimeout(saveNow, saveDelay);
}
function saveNow(): void {
  window.clearTimeout(saveTimer);
  if (!map || !dirty) return;
  dirty = false;
  sendToHost('mindmap.save', { map });
}
// 창을 닫을 때 앱이 호출합니다. 아직 보내지 않은 변경이 있으면 돌려줍니다.
Object.assign(window, { devpostitPending: () => { commitEdit(); commitPanel(); if (!map || !dirty) return null; dirty = false; window.clearTimeout(saveTimer); return JSON.stringify(map); } });
function change(mutate: () => void): void {
  if (!map) return;
  undoStack.push(JSON.stringify(map.root));
  if (undoStack.length > maxHistory) undoStack.shift();
  redoStack = [];
  mutate();
  markDirty();
  render();
}
function restore(from: string[], to: string[]): void {
  if (!map || !from.length) return;
  commitEdit();
  to.push(JSON.stringify(map.root));
  map.root = JSON.parse(from.pop()!);
  if (!findNode(map.root, selectedId)) selectedId = map.root.id;
  markDirty();
  render();
}

// 목록 -----------------------------------------------------------------
function renderMaps(): void {
  const words = $<HTMLInputElement>('#mm-map-search').value.trim().toLowerCase();
  $('#mm-maps').replaceChildren(...maps.filter(item => item.title.toLowerCase().includes(words)).map(item => {
    const open = element('button', { type: 'button', className: `mm-map${item.id === map?.id ? ' selected' : ''}`, title: item.title }, element('span', { className: 'mm-map-name', textContent: item.id === map?.id ? map?.title ?? item.title : item.title }), element('span', { className: 'mm-map-meta', textContent: `주제 ${item.nodes}개` }));
    open.addEventListener('click', () => { if (item.id !== map?.id) { flushCurrent(); sendToHost('mindmap.open', { id: item.id }); } });
    const copy = element('button', { type: 'button', textContent: '복제', ariaLabel: `${item.title} 복제` });
    copy.addEventListener('click', () => duplicateMap(item.id));
    const remove = element('button', { type: 'button', textContent: '삭제', ariaLabel: `${item.title} 삭제` });
    remove.addEventListener('click', async () => {
      if (await ask(`‘${item.title}’ 마인드맵을 삭제할까요? 되돌릴 수 없습니다.`, '삭제') !== 0) return;
      if (item.id === map?.id) { window.clearTimeout(saveTimer); dirty = false; map = null; }
      sendToHost('mindmap.delete', { id: item.id });
    });
    return element('li', {}, open, element('div', { className: 'mm-map-actions' }, copy, remove));
  }));
}
function flushCurrent(): void { commitEdit(); commitPanel(); saveNow(); }
let duplicateNext = false;
function duplicateMap(id: string): void {
  flushCurrent();
  duplicateNext = true;
  sendToHost('mindmap.open', { id });
}
function createMap(title = uniqueTitle('새 마인드맵')): void {
  flushCurrent();
  showMap(newMap(title));
  dirty = true;
  saveNow();
  maps = [{ id: map!.id, title, updated: new Date().toISOString(), nodes: 1 }, ...maps];
  renderMaps();
  beginEdit(map!.root.id, true);
}
function uniqueTitle(base: string): string {
  const names = new Set(maps.map(item => item.title));
  if (!names.has(base)) return base;
  for (let number = 2; ; number++) if (!names.has(`${base} ${number}`)) return `${base} ${number}`;
}
function showMap(value: MindMap): void {
  map = value;
  selectedId = value.root.id;
  undoStack = []; redoStack = [];
  titleInput.value = value.title;
  layoutSelect.value = value.layout;
  remember(value.id);
  render();
  fit();
  canvas.focus();
  renderMaps();
}
onHostMessage(message => {
  switch (message.type) {
    case 'mindmap.list':
      maps = message.maps as MapSummary[];
      if (!map) {
        const last = maps.find(item => item.id === lastOpened()) ?? maps[0];
        if (last) sendToHost('mindmap.open', { id: last.id });
        else createMap('새 마인드맵');
      }
      renderMaps();
      break;
    case 'mindmap.opened': {
      const opened = message.map as MindMap;
      if (duplicateNext) {
        duplicateNext = false;
        const copy: MindMap = { ...opened, id: crypto.randomUUID(), title: uniqueTitle(`${opened.title} 복사본`.slice(0, 200)), root: freshIds(opened.root) };
        showMap(copy);
        dirty = true;
        saveNow();
        maps = [{ id: copy.id, title: copy.title, updated: new Date().toISOString(), nodes: 0 }, ...maps];
        sendToHost('mindmap.list');
      } else showMap(opened);
      break;
    }
    case 'mindmap.saved': {
      const item = maps.find(entry => entry.id === message.id);
      const current = map;
      if (item && current && current.id === message.id) { item.title = current.title; let count = 0; walk(current.root, () => { count++; }); item.nodes = count; renderMaps(); }
      break;
    }
    case 'mindmap.exported': say('PNG로 저장했습니다.'); break;
    case 'mindmap.error': say(String(message.message)); break;
  }
});

// 그리기 ---------------------------------------------------------------
const measureCache = new Map<string, { width: number; height: number }>();
const cacheKey = (node: MapNode, isRoot: boolean) => `${isRoot ? 'r' : 'n'}|${node.bold ? 'b' : ''}|${node.icon ?? ''}|${node.note ? 'n' : ''}|${node.link ? 'l' : ''}|${node.children.length ? 'c' : ''}|${node.text}`;
const nodeContent = (node: MapNode, isRoot: boolean): HTMLElement => {
  const box = element('div', { className: `mm-node${isRoot ? ' root' : ''}${node.bold ? ' bold' : ''}`, dataset: { id: node.id } });
  if (node.icon) box.append(element('span', { className: 'mm-icon', textContent: icons[node.icon] ?? '' }));
  box.append(element('span', { className: 'mm-text', textContent: node.text || ' ' }));
  if (node.note) box.append(element('span', { className: 'mm-mark', textContent: '✎', title: node.note.slice(0, 200) }));
  if (node.link) box.append(element('span', { className: 'mm-mark', textContent: '🔗', title: node.link }));
  return box;
};
function sizeOf(node: MapNode): { width: number; height: number } {
  const isRoot = node.id === map!.root.id;
  const key = cacheKey(node, isRoot);
  const known = measureCache.get(key);
  if (known) return known;
  const probe = nodeContent(node, isRoot);
  probe.style.visibility = 'hidden';
  nodesLayer.append(probe);
  const size = { width: Math.ceil(probe.offsetWidth), height: Math.ceil(probe.offsetHeight) };
  probe.remove();
  if (measureCache.size > 5000) measureCache.clear();
  measureCache.set(key, size);
  return size;
}
const branchColor = (item: Placed): string => item.node.color ?? (item.branch < 0 ? '#7a5c00' : branchColors[item.branch % branchColors.length]);
function colorFor(item: Placed): string {
  // 색을 지정하지 않은 주제는 가장 가까운 윗주제의 색을 따릅니다.
  let parent = item.parent;
  let color = item.node.color;
  while (!color && parent && parent.id !== map!.root.id) {
    color = parent.color;
    parent = findNode(map!.root, parent.id)?.parent ?? null;
  }
  return color ?? branchColor(item);
}
function edgePath(from: Placed, to: Placed): string {
  const right = to.side === 'right';
  const x1 = from.side === 'root' ? (right ? from.x + from.width - 8 : from.x + 8) : right ? from.x + from.width : from.x;
  const y1 = from.y + from.height / 2;
  const x2 = right ? to.x : to.x + to.width;
  const y2 = to.y + to.height / 2;
  const middle = (x1 + x2) / 2;
  return `M ${x1} ${y1} C ${middle} ${y1}, ${middle} ${y2}, ${x2} ${y2}`;
}
function render(): void {
  if (!map) { nodesLayer.replaceChildren(); edges.replaceChildren(); return; }
  placed = layoutMap(map, sizeOf);
  const byId = new Map(placed.map(item => [item.node.id, item]));
  const fragment = document.createDocumentFragment();
  for (const item of placed) {
    const box = nodeContent(item.node, item.side === 'root');
    const color = colorFor(item);
    box.style.left = `${item.x}px`;
    box.style.top = `${item.y}px`;
    box.style.setProperty('--branch', color);
    box.setAttribute('role', 'treeitem');
    box.setAttribute('aria-label', item.node.text);
    box.setAttribute('aria-selected', String(item.node.id === selectedId));
    if (item.node.id === selectedId) box.classList.add('selected');
    if (matches.includes(item.node.id)) box.classList.add('match');
    fragment.append(box);
    if (item.node.children.length) {
      const toggle = element('button', { type: 'button', className: 'mm-toggle', textContent: item.node.collapsed ? String(item.node.children.length) : '−', ariaLabel: item.node.collapsed ? '펼치기' : '접기', dataset: { toggle: item.node.id } });
      const rootSide = item.side === 'root';
      toggle.style.top = `${item.y + item.height / 2 - 9}px`;
      toggle.style.left = `${rootSide ? item.x + item.width / 2 - 9 : item.side === 'right' ? item.x + item.width + 2 : item.x - 20}px`;
      if (rootSide) toggle.style.top = `${item.y + item.height + 2}px`;
      toggle.style.setProperty('--branch', color);
      fragment.append(toggle);
    }
  }
  nodesLayer.replaceChildren(fragment);
  const svg = 'http://www.w3.org/2000/svg';
  const paths: SVGPathElement[] = [];
  for (const item of placed) {
    if (!item.parent) continue;
    const parent = byId.get(item.parent.id);
    if (!parent) continue;
    const path = document.createElementNS(svg, 'path');
    path.setAttribute('d', edgePath(parent, item));
    path.setAttribute('stroke', colorFor(item));
    paths.push(path);
  }
  edges.replaceChildren(...paths);
  applyTransform();
  syncTools();
  if (editingId) showEditor();
}
function applyTransform(): void {
  stage.style.transform = `translate(${panX}px, ${panY}px) scale(${zoom})`;
}
function syncTools(): void {
  const current = selected()?.node;
  if (!current) return;
  iconSelect.value = current.icon ?? '';
  colorInput.value = current.color ?? '#7a5c00';
  if (document.activeElement !== linkInput) linkInput.value = current.link ?? '';
  if (document.activeElement !== noteInput) noteInput.value = current.note;
  $<HTMLButtonElement>('#mm-open-link').disabled = !current.link;
  document.querySelector<HTMLButtonElement>('[data-action="undo"]')!.disabled = !undoStack.length;
  document.querySelector<HTMLButtonElement>('[data-action="redo"]')!.disabled = !redoStack.length;
  document.querySelector<HTMLButtonElement>('[data-action="delete"]')!.disabled = current.id === map!.root.id;
}
function bounds(): { left: number; top: number; right: number; bottom: number } {
  return { left: Math.min(...placed.map(item => item.x)), top: Math.min(...placed.map(item => item.y)), right: Math.max(...placed.map(item => item.x + item.width)), bottom: Math.max(...placed.map(item => item.y + item.height)) };
}
function fit(): void {
  if (!placed.length) return;
  const box = bounds();
  const area = canvas.getBoundingClientRect();
  const margin = 48;
  zoom = Math.max(minZoom, Math.min(1.2, Math.min(area.width / (box.right - box.left + margin * 2), area.height / (box.bottom - box.top + margin * 2))));
  panX = area.width / 2 - (box.left + box.right) / 2 * zoom;
  panY = area.height / 2 - (box.top + box.bottom) / 2 * zoom;
  applyTransform();
}
function zoomAt(next: number, x = canvas.clientWidth / 2, y = canvas.clientHeight / 2): void {
  const value = Math.max(minZoom, Math.min(maxZoom, next));
  panX = x - (x - panX) * value / zoom;
  panY = y - (y - panY) * value / zoom;
  zoom = value;
  applyTransform();
}
function reveal(id: string): void {
  const item = placed.find(entry => entry.node.id === id);
  if (!item) return;
  const margin = 32;
  const left = item.x * zoom + panX, top = item.y * zoom + panY, right = left + item.width * zoom, bottom = top + item.height * zoom;
  if (left < margin) panX += margin - left;
  else if (right > canvas.clientWidth - margin) panX -= right - canvas.clientWidth + margin;
  if (top < margin) panY += margin - top;
  else if (bottom > canvas.clientHeight - margin) panY -= bottom - canvas.clientHeight + margin;
  applyTransform();
}
function select(id: string): void {
  commitPanel();
  selectedId = id;
  render();
  reveal(id);
}

// 편집 -----------------------------------------------------------------
let editor: HTMLTextAreaElement | null = null;
function showEditor(): void {
  const item = placed.find(entry => entry.node.id === editingId);
  if (!item || !editor) return;
  editor.style.left = `${item.x}px`;
  editor.style.top = `${item.y}px`;
  editor.style.minWidth = `${Math.max(item.width, 120)}px`;
  editor.style.minHeight = `${item.height}px`;
  nodesLayer.append(editor);
  growEditor();
  if (document.activeElement !== editor) editor.focus();
}
function growEditor(): void {
  if (!editor) return;
  editor.style.height = 'auto';
  editor.style.height = `${editor.scrollHeight + 2}px`;
  const lines = editor.value.split('\n');
  const longest = Math.max(...lines.map(line => line.length), 4);
  editor.style.width = `${Math.min(320, 28 + longest * 15)}px`;
}
function beginEdit(id: string, isNew = false, initial?: string): void {
  const found = map && findNode(map.root, id);
  if (!found) return;
  commitEdit();
  editingId = id;
  selectedId = id;
  discardIfEmpty = isNew;
  editor = element('textarea', { className: 'mm-editor', value: initial ?? found.node.text, rows: 1, maxLength: 4000, ariaLabel: '주제 내용' });
  editor.addEventListener('input', growEditor);
  editor.addEventListener('keydown', editorKey);
  editor.addEventListener('pointerdown', event => event.stopPropagation());
  render();
  editor.focus();
  if (initial === undefined) editor.select();
  else editor.setSelectionRange(editor.value.length, editor.value.length);
}
function closeEditor(): void {
  editor?.remove();
  editor = null;
  editingId = null;
  discardIfEmpty = false;
}
function commitEdit(): void {
  if (!editingId || !editor || !map) return;
  const id = editingId;
  const text = editor.value.trim();
  const removeEmpty = discardIfEmpty && !text && id !== map.root.id;
  closeEditor();
  const found = findNode(map.root, id);
  if (!found) return;
  if (removeEmpty) { removeWithoutHistory(found.node, found.parent); render(); return; }
  if (text !== found.node.text && text) change(() => { found.node.text = text; });
  else render();
}
function cancelEdit(): void {
  if (!editingId || !map) return;
  const id = editingId;
  const removeNew = discardIfEmpty && id !== map.root.id;
  closeEditor();
  const found = findNode(map.root, id);
  if (found && removeNew && !found.node.text) removeWithoutHistory(found.node, found.parent);
  render();
  canvas.focus();
}
// 내용 없이 취소한 새 주제는 추가 기록까지 함께 되돌립니다.
function removeWithoutHistory(node: MapNode, parent: MapNode | null): void {
  if (!parent) return;
  const at = parent.children.indexOf(node);
  parent.children.splice(at, 1);
  selectedId = (parent.children[at] ?? parent.children[at - 1] ?? parent).id;
  undoStack.pop();
  markDirty();
}
function editorKey(event: KeyboardEvent): void {
  event.stopPropagation();
  if (event.isComposing) return;
  if (event.key === 'Escape') { event.preventDefault(); cancelEdit(); }
  else if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); const continueWith = discardIfEmpty; commitEdit(); canvas.focus(); void continueWith; }
  else if (event.key === 'Tab') { event.preventDefault(); commitEdit(); addChild(); }
}
function addChild(): void {
  const found = selected();
  if (!found || !map) return;
  if (depthOfPath(found.node.id) >= maxDepth) { say('더 깊게 만들 수 없습니다.'); return; }
  const node = newNode();
  change(() => { found.node.collapsed = false; found.node.children.push(node); selectedId = node.id; });
  reveal(node.id);
  beginEdit(node.id, true);
}
function depthOfPath(id: string): number {
  let depth = 0;
  if (map) walk(map.root, (node, _parent, level) => { if (node.id === id) depth = level; });
  return depth;
}
function addSibling(before = false): void {
  const found = selected();
  if (!found) return;
  if (!found.parent) { addChild(); return; }
  const node = newNode();
  const parent = found.parent;
  node.side = found.node.side;
  change(() => { parent.children.splice(parent.children.indexOf(found.node) + (before ? 0 : 1), 0, node); selectedId = node.id; });
  reveal(node.id);
  beginEdit(node.id, true);
}
function deleteSelected(): void {
  const found = selected();
  if (!found?.parent) { say('중심 주제는 삭제할 수 없습니다.'); return; }
  const parent = found.parent;
  const at = parent.children.indexOf(found.node);
  change(() => { parent.children.splice(at, 1); selectedId = (parent.children[at] ?? parent.children[at - 1] ?? parent).id; });
}
function toggleCollapse(id = selectedId): void {
  const found = map && findNode(map.root, id);
  if (!found || !found.node.children.length) return;
  change(() => { found.node.collapsed = !found.node.collapsed; if (found.node.collapsed && contains(found.node, selectedId)) selectedId = found.node.id; });
}
function setAllCollapsed(collapsed: boolean): void {
  if (!map) return;
  change(() => {
    walk(map!.root, node => { if (node.id !== map!.root.id && node.children.length) node.collapsed = collapsed; });
    if (collapsed) { const keep = findVisible(selectedId); selectedId = keep; }
  });
  fit();
}
function findVisible(id: string): string {
  if (!map) return id;
  let result = map.root.id;
  const visit = (node: MapNode): boolean => {
    if (node.id === id) { result = id; return true; }
    for (const child of node.children) {
      if (visit(child)) { if (node.collapsed) result = node.id; return true; }
    }
    return false;
  };
  visit(map.root);
  return result;
}
// 방향키 이동: 화면 방향을 따릅니다.
function navigate(key: string): void {
  const item = placed.find(entry => entry.node.id === selectedId);
  if (!item || !map) return;
  const parent = item.parent;
  const outward = (side: 'left' | 'right') => {
    const candidates = placed.filter(entry => entry.parent?.id === item.node.id && entry.side === side);
    if (candidates.length) {
      const center = item.y + item.height / 2;
      select(candidates.reduce((best, entry) => Math.abs(entry.y + entry.height / 2 - center) < Math.abs(best.y + best.height / 2 - center) ? entry : best).node.id);
    } else if (item.node.collapsed && item.node.children.length && item.side === side) toggleCollapse();
  };
  if (key === 'ArrowRight') { if (item.side === 'left') { if (parent) select(parent.id); } else outward('right'); return; }
  if (key === 'ArrowLeft') { if (item.side === 'right') { if (parent) select(parent.id); } else outward('left'); return; }
  const down = key === 'ArrowDown';
  if (item.side === 'root') {
    const first = placed.find(entry => entry.parent?.id === item.node.id);
    if (first) select(first.node.id);
    return;
  }
  // 같은 쪽의 보이는 주제 중 세로로 가장 가까운 주제로 갑니다. 같은 부모의 주제를 먼저 찾습니다.
  const center = item.y + item.height / 2;
  const sameSide = placed.filter(entry => entry.side === item.side && entry.node.id !== item.node.id && (down ? entry.y + entry.height / 2 > center : entry.y + entry.height / 2 < center));
  const siblings = sameSide.filter(entry => entry.parent?.id === parent?.id);
  const pool = siblings.length ? siblings : sameSide;
  if (!pool.length) return;
  select(pool.reduce((best, entry) => {
    const score = (value: Placed) => Math.abs(value.y + value.height / 2 - center) + Math.abs(value.x - item.x) / 4;
    return score(entry) < score(best) ? entry : best;
  }).node.id);
}
function moveNode(key: string): void {
  const found = selected();
  if (!found?.parent || !map) return;
  const { node, parent } = found;
  const siblings = parent.children;
  const at = siblings.indexOf(node);
  const item = placed.find(entry => entry.node.id === node.id);
  const inward = (key === 'ArrowLeft' && item?.side === 'right') || (key === 'ArrowRight' && item?.side === 'left');
  const outward = (key === 'ArrowRight' && item?.side === 'right') || (key === 'ArrowLeft' && item?.side === 'left');
  if (key === 'ArrowUp' && at > 0) change(() => { siblings.splice(at, 1); siblings.splice(at - 1, 0, node); });
  else if (key === 'ArrowDown' && at < siblings.length - 1) change(() => { siblings.splice(at, 1); siblings.splice(at + 1, 0, node); });
  else if (inward) {
    const grand = findNode(map.root, parent.id)?.parent;
    if (grand) change(() => { siblings.splice(at, 1); grand.children.splice(grand.children.indexOf(parent) + 1, 0, node); });
  } else if (outward && at > 0) change(() => { const target = siblings[at - 1]; siblings.splice(at, 1); target.collapsed = false; target.children.push(node); });
}
function commitPanel(): void {
  const found = selected();
  if (!found) return;
  const link = linkInput.value.trim() || null;
  const note = noteInput.value;
  if (found.node.link !== link || found.node.note !== note) change(() => { found.node.link = link; found.node.note = note; });
}

// 끌어 옮기기 ----------------------------------------------------------
type Drop = { target: MapNode; mode: 'child' | 'before' | 'after'; side: 'left' | 'right' | null };
function dropAt(clientX: number, clientY: number, dragged: MapNode): Drop | null {
  const hit = document.elementFromPoint(clientX, clientY)?.closest<HTMLElement>('.mm-node:not(.ghost)');
  if (!hit?.dataset.id || !map) return null;
  const target = findNode(map.root, hit.dataset.id);
  if (!target || contains(dragged, target.node.id)) return null;
  const box = hit.getBoundingClientRect();
  const ratio = (clientY - box.top) / box.height;
  const side = target.node.id === map.root.id ? (clientX < box.left + box.width / 2 ? 'left' : 'right') : null;
  if (!target.parent || (ratio > 0.25 && ratio < 0.75)) return { target: target.node, mode: 'child', side };
  return { target: target.node, mode: ratio <= 0.25 ? 'before' : 'after', side: null };
}
function applyDrop(dragged: MapNode, drop: Drop): void {
  if (!map) return;
  const root = map.root;
  change(() => {
    const from = findNode(root, dragged.id)?.parent;
    if (!from) return;
    from.children.splice(from.children.indexOf(dragged), 1);
    if (drop.mode === 'child') {
      drop.target.collapsed = false;
      drop.target.children.push(dragged);
      dragged.side = drop.target.id === root.id ? drop.side : null;
    } else {
      const parent = findNode(root, drop.target.id)!.parent!;
      parent.children.splice(parent.children.indexOf(drop.target) + (drop.mode === 'after' ? 1 : 0), 0, dragged);
      dragged.side = parent.id === root.id ? drop.target.side : null;
    }
    selectedId = dragged.id;
  });
}
canvas.addEventListener('pointerdown', event => {
  const target = event.target as Element;
  if (event.button === 1 || (event.button === 0 && !target.closest('.mm-node, .mm-toggle'))) { startPan(event); return; }
  if (event.button !== 0) return;
  const toggle = target.closest<HTMLElement>('.mm-toggle');
  if (toggle?.dataset.toggle) { commitEdit(); toggleCollapse(toggle.dataset.toggle); return; }
  const box = target.closest<HTMLElement>('.mm-node');
  if (!box?.dataset.id || !map) return;
  event.preventDefault();
  commitEdit();
  canvas.focus();
  const id = box.dataset.id;
  if (id !== selectedId) select(id);
  const found = findNode(map.root, id);
  if (!found?.parent) return;
  startDrag(event, found.node);
});
function startPan(event: PointerEvent): void {
  commitEdit();
  canvas.focus();
  const x = event.clientX - panX, y = event.clientY - panY;
  canvas.classList.add('panning');
  const moving = (next: PointerEvent) => { panX = next.clientX - x; panY = next.clientY - y; applyTransform(); };
  const up = () => { window.removeEventListener('pointermove', moving); window.removeEventListener('pointerup', up); canvas.classList.remove('panning'); };
  window.addEventListener('pointermove', moving);
  window.addEventListener('pointerup', up);
}
function startDrag(event: PointerEvent, dragged: MapNode): void {
  const startX = event.clientX, startY = event.clientY;
  let ghost: HTMLElement | null = null;
  let drop: Drop | null = null;
  const clear = () => document.querySelectorAll('.drop-child, .drop-before, .drop-after').forEach(item => item.classList.remove('drop-child', 'drop-before', 'drop-after'));
  const moving = (next: PointerEvent) => {
    if (!ghost) {
      if (Math.hypot(next.clientX - startX, next.clientY - startY) < 5) return;
      ghost = element('div', { className: 'mm-drag-ghost', textContent: dragged.text });
      document.body.append(ghost);
    }
    ghost.style.left = `${next.clientX + 10}px`;
    ghost.style.top = `${next.clientY + 10}px`;
    clear();
    drop = dropAt(next.clientX, next.clientY, dragged);
    if (drop) nodesLayer.querySelector(`[data-id="${drop.target.id}"]`)?.classList.add(`drop-${drop.mode}`);
  };
  const up = () => {
    window.removeEventListener('pointermove', moving);
    window.removeEventListener('pointerup', up);
    ghost?.remove();
    clear();
    if (ghost && drop) applyDrop(dragged, drop);
  };
  window.addEventListener('pointermove', moving);
  window.addEventListener('pointerup', up);
}
canvas.addEventListener('dblclick', event => {
  const box = (event.target as Element).closest<HTMLElement>('.mm-node');
  if (box?.dataset.id) beginEdit(box.dataset.id);
});
canvas.addEventListener('wheel', event => {
  event.preventDefault();
  const area = canvas.getBoundingClientRect();
  zoomAt(zoom * (event.deltaY < 0 ? 1.1 : 1 / 1.1), event.clientX - area.left, event.clientY - area.top);
}, { passive: false });

// 키보드 ---------------------------------------------------------------
canvas.addEventListener('keydown', event => {
  if (!map || editingId) return;
  const control = event.ctrlKey || event.metaKey;
  const key = event.key;
  const handled = () => { event.preventDefault(); event.stopPropagation(); };
  if (event.altKey && key.startsWith('Arrow')) { handled(); moveNode(key); return; }
  if (!control && !event.altKey) {
    if (key === 'Tab' || key === 'Insert') { handled(); addChild(); return; }
    if (key === 'Enter') { handled(); addSibling(event.shiftKey); return; }
    if (key === 'F2') { handled(); beginEdit(selectedId); return; }
    if (key === 'Delete' || key === 'Backspace') { handled(); deleteSelected(); return; }
    if (key === ' ') { handled(); toggleCollapse(); return; }
    if (key.startsWith('Arrow')) { handled(); navigate(key); return; }
    if (key === 'F1') { handled(); $<HTMLDialogElement>('#mm-help').showModal(); return; }
    // 글자를 치면 바로 편집을 시작합니다. 한글 조합도 편집칸에서 이어집니다.
    if (key.length === 1 || key === 'Process') { beginEdit(selectedId, false, ''); return; }
    return;
  }
  if (!control) return;
  switch (key.toLowerCase()) {
    case 'z': handled(); if (event.shiftKey) restore(redoStack, undoStack); else restore(undoStack, redoStack); break;
    case 'y': handled(); restore(redoStack, undoStack); break;
    case 'b': handled(); toggleBold(); break;
    case 'd': handled(); duplicateNode(); break;
    case 'f': handled(); openFind(); break;
    case '=': case '+': handled(); zoomAt(zoom * 1.2); break;
    case '-': handled(); zoomAt(zoom / 1.2); break;
    case '0': handled(); fit(); break;
  }
});
function toggleBold(): void { const found = selected(); if (found) change(() => { found.node.bold = !found.node.bold; }); }
function duplicateNode(): void {
  const found = selected();
  if (!found?.parent) return;
  const copy = freshIds(found.node);
  const parent = found.parent;
  change(() => { parent.children.splice(parent.children.indexOf(found.node) + 1, 0, copy); selectedId = copy.id; });
}
const typing = () => (document.activeElement as Element | null)?.matches('input, textarea, select') ?? false;
document.addEventListener('copy', event => {
  if (wantOutlineCopy !== null) { event.clipboardData?.setData('text/plain', wantOutlineCopy); event.preventDefault(); wantOutlineCopy = null; return; }
  const found = selected();
  if (typing() || !found) return;
  clipboard = { outline: toOutline(found.node), node: clone(found.node) };
  event.clipboardData?.setData('text/plain', clipboard.outline);
  event.preventDefault();
  say('주제를 복사했습니다.');
});
document.addEventListener('cut', event => {
  const found = selected();
  if (typing() || !found?.parent) return;
  clipboard = { outline: toOutline(found.node), node: clone(found.node) };
  event.clipboardData?.setData('text/plain', clipboard.outline);
  event.preventDefault();
  deleteSelected();
});
// 앱에서 복사한 주제는 서식까지, 다른 곳의 글은 들여쓰기대로 주제를 만듭니다.
document.addEventListener('paste', event => {
  const found = selected();
  if (typing() || !found) return;
  const text = event.clipboardData?.getData('text/plain') ?? '';
  const nodes = clipboard && clipboard.outline.replace(/\r\n/g, '\n') === text.replace(/\r\n/g, '\n') ? [freshIds(clipboard.node)] : fromOutline(text);
  if (!nodes.length) return;
  event.preventDefault();
  if (depthOfPath(found.node.id) + Math.max(...nodes.map(depthOf)) > maxDepth) { say('더 깊게 붙여넣을 수 없습니다.'); return; }
  change(() => { found.node.collapsed = false; found.node.children.push(...nodes); selectedId = nodes[0].id; });
});

// 도구 -----------------------------------------------------------------
function openFind(): void { findBox.hidden = false; findInput.select(); findInput.focus(); runFind(); }
function closeFind(): void { findBox.hidden = true; matches = []; matchIndex = -1; render(); canvas.focus(); }
function runFind(): void {
  const words = findInput.value.trim().toLowerCase();
  matches = [];
  if (words && map) {
    walk(map.root, node => { if (`${node.text}\n${node.note}`.toLowerCase().includes(words)) matches.push(node.id); });
  }
  matchIndex = -1;
  $('#mm-find-count').textContent = words ? `${matches.length}개` : '';
  render();
}
function nextMatch(): void {
  if (!matches.length || !map) return;
  matchIndex = (matchIndex + 1) % matches.length;
  const id = matches[matchIndex];
  // 접힌 가지 안에 있으면 펼칩니다.
  const open = (node: MapNode): boolean => {
    if (node.id === id) return true;
    for (const child of node.children) if (open(child)) { node.collapsed = false; return true; }
    return false;
  };
  open(map.root);
  select(id);
  $('#mm-find-count').textContent = `${matchIndex + 1}/${matches.length}`;
}
findInput.addEventListener('input', runFind);
findInput.addEventListener('keydown', event => {
  if (event.isComposing) return;
  if (event.key === 'Enter') { event.preventDefault(); nextMatch(); }
  else if (event.key === 'Escape') closeFind();
});
$('#mm-find-close').addEventListener('click', closeFind);
function exportPng(): void {
  if (!map || !placed.length) return;
  const box = bounds();
  const margin = 32;
  const scale = Math.min(2, 16000 / Math.max(box.right - box.left + margin * 2, box.bottom - box.top + margin * 2));
  const out = element('canvas', { width: Math.ceil((box.right - box.left + margin * 2) * scale), height: Math.ceil((box.bottom - box.top + margin * 2) * scale) });
  const context = out.getContext('2d')!;
  context.fillStyle = '#fffdf5';
  context.fillRect(0, 0, out.width, out.height);
  context.scale(scale, scale);
  context.translate(margin - box.left, margin - box.top);
  const byId = new Map(placed.map(item => [item.node.id, item]));
  context.lineWidth = 2;
  for (const item of placed) {
    const parent = item.parent && byId.get(item.parent.id);
    if (!parent) continue;
    context.strokeStyle = colorFor(item);
    context.stroke(new Path2D(edgePath(parent, item)));
  }
  const font = getComputedStyle(document.body).fontFamily;
  for (const item of placed) {
    const color = colorFor(item);
    const isRoot = item.side === 'root';
    context.fillStyle = isRoot ? color : '#ffffff';
    context.strokeStyle = color;
    context.beginPath();
    context.roundRect(item.x, item.y, item.width, item.height, 6);
    context.fill();
    context.stroke();
    context.fillStyle = isRoot ? '#ffffff' : '#2f2a1f';
    context.font = `${item.node.bold || isRoot ? '600 ' : ''}14px ${font}`;
    context.textBaseline = 'middle';
    const text = `${item.node.icon ? `${icons[item.node.icon]} ` : ''}${item.node.text}`;
    const lines: string[] = [];
    for (const paragraph of text.split('\n')) {
      let line = '';
      for (const character of paragraph) {
        if (context.measureText(line + character).width > item.width - 20 && line) { lines.push(line); line = character; } else line += character;
      }
      lines.push(line);
    }
    lines.forEach((line, index) => context.fillText(line, item.x + 10, item.y + item.height / 2 + (index - (lines.length - 1) / 2) * 19));
  }
  const data = out.toDataURL('image/png').split(',')[1];
  if (data.length > 15_000_000) { say('그림이 너무 커서 저장할 수 없습니다.'); return; }
  sendToHost('mindmap.exportPng', { name: map.title, data });
}
document.querySelector('#mm-toolbar')!.addEventListener('click', event => {
  const action = (event.target as Element).closest<HTMLElement>('[data-action]')?.dataset.action;
  if (!action) return;
  commitEdit();
  switch (action) {
    case 'child': addChild(); return;
    case 'sibling': addSibling(); return;
    case 'delete': deleteSelected(); break;
    case 'undo': restore(undoStack, redoStack); break;
    case 'redo': restore(redoStack, undoStack); break;
    case 'bold': toggleBold(); break;
    case 'clear-color': { const found = selected(); if (found) change(() => { found.node.color = null; }); break; }
    case 'collapse-all': setAllCollapsed(true); break;
    case 'expand-all': setAllCollapsed(false); break;
    case 'zoom-in': zoomAt(zoom * 1.2); break;
    case 'zoom-out': zoomAt(zoom / 1.2); break;
    case 'fit': fit(); break;
    case 'find': openFind(); return;
    case 'copy-outline': if (map) { wantOutlineCopy = toOutline(map.root); document.execCommand('copy'); say('Markdown 개요를 복사했습니다.'); } break;
    case 'export-png': exportPng(); break;
    case 'help': $<HTMLDialogElement>('#mm-help').showModal(); return;
  }
  canvas.focus();
});
$('#mm-help-close').addEventListener('click', () => { $<HTMLDialogElement>('#mm-help').close(); canvas.focus(); });
colorInput.addEventListener('change', () => { const found = selected(); if (found) change(() => { found.node.color = colorInput.value; }); });
iconSelect.addEventListener('change', () => { const found = selected(); if (found) change(() => { found.node.icon = iconSelect.value || null; }); canvas.focus(); });
layoutSelect.addEventListener('change', () => { if (!map) return; map.layout = layoutSelect.value as MindMap['layout']; markDirty(); render(); fit(); canvas.focus(); });
const commitTitle = () => {
  if (!map) return;
  const value = titleInput.value.trim();
  if (!value) { titleInput.value = map.title; return; }
  if (value !== map.title) { map.title = value; const item = maps.find(entry => entry.id === map!.id); if (item) item.title = value; markDirty(); renderMaps(); }
};
titleInput.addEventListener('change', commitTitle);
titleInput.addEventListener('keydown', event => { if (event.isComposing) return; if (event.key === 'Enter') { commitTitle(); canvas.focus(); } });
linkInput.addEventListener('change', commitPanel);
noteInput.addEventListener('change', commitPanel);
$('#mm-open-link').addEventListener('click', () => { const link = selected()?.node.link; if (link) sendToHost('mindmap.openLink', { url: link }); });
$('#mm-new-map').addEventListener('click', () => createMap());
$('#mm-map-search').addEventListener('input', renderMaps);
window.addEventListener('keydown', event => {
  if (event.ctrlKey && event.key.toLowerCase() === 'n') { event.preventDefault(); createMap(); }
});
window.addEventListener('beforeunload', flushCurrent);
window.addEventListener('resize', applyTransform);
void hasHost;
sendToHost('mindmap.list');
