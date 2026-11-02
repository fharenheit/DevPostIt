// 마인드맵 구조를 다루는 순수 함수입니다. 화면과 분리해 둡니다.
export type MapNode = { id: string; text: string; children: MapNode[]; collapsed: boolean; color: string | null; bold: boolean; note: string; link: string | null; icon: string | null; side: 'left' | 'right' | null };
export type MindMap = { id: string; title: string; layout: 'both' | 'right' | 'left'; updated: string; root: MapNode };
export type MapSummary = { id: string; title: string; updated: string; nodes: number };

export const newNode = (text = ''): MapNode => ({ id: crypto.randomUUID(), text, children: [], collapsed: false, color: null, bold: false, note: '', link: null, icon: null, side: null });
export const newMap = (title: string): MindMap => ({ id: crypto.randomUUID(), title, layout: 'both', updated: '', root: newNode('중심 주제') });
export const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));
export const walk = (node: MapNode, visit: (node: MapNode, parent: MapNode | null, depth: number) => void, parent: MapNode | null = null, depth = 0): void => {
  visit(node, parent, depth);
  for (const child of node.children) walk(child, visit, node, depth + 1);
};
export function findNode(root: MapNode, id: string): { node: MapNode; parent: MapNode | null } | null {
  let found: { node: MapNode; parent: MapNode | null } | null = null;
  walk(root, (node, parent) => { if (node.id === id) found = { node, parent }; });
  return found;
}
export function contains(node: MapNode, id: string): boolean {
  let result = false;
  walk(node, item => { if (item.id === id) result = true; });
  return result;
}
export const depthOf = (node: MapNode): number => 1 + Math.max(0, ...node.children.map(depthOf));
export function freshIds(node: MapNode): MapNode {
  const copy = clone(node);
  walk(copy, item => { item.id = crypto.randomUUID(); });
  return copy;
}
export function toOutline(node: MapNode, depth = 0): string {
  const text = node.text.replace(/\s*\n\s*/g, ' ');
  return [`${'  '.repeat(depth)}- ${text}`, ...node.children.map(child => toOutline(child, depth + 1))].join('\n');
}
// 들여쓴 글이나 Markdown 목록을 주제로 바꿉니다. 들여쓰기가 얕아지면 그 깊이의 부모로 돌아갑니다.
export function fromOutline(text: string): MapNode[] {
  const roots: MapNode[] = [];
  const stack: { indent: number; node: MapNode }[] = [];
  for (const raw of text.replace(/\r\n/g, '\n').split('\n')) {
    if (!raw.trim()) continue;
    const indent = raw.replace(/\t/g, '    ').search(/\S/);
    const node = newNode(raw.trim().replace(/^(?:[-*+]\s+|\d+[.)]\s+|#{1,6}\s+)/, '').slice(0, 4000));
    while (stack.length && stack.at(-1)!.indent >= indent) stack.pop();
    if (stack.length) stack.at(-1)!.node.children.push(node);
    else roots.push(node);
    stack.push({ indent, node });
  }
  return roots;
}
export type Placed = { node: MapNode; parent: MapNode | null; x: number; y: number; width: number; height: number; side: 'left' | 'right' | 'root'; branch: number };
const gapX = 56, gapY = 12;
// 왼쪽, 오른쪽으로 가지를 펼칩니다. 좌표는 주제의 왼쪽 위이며 중심 주제의 중심이 원점입니다.
export function layoutMap(map: MindMap, size: (node: MapNode) => { width: number; height: number }): Placed[] {
  const result: Placed[] = [];
  const heights = new Map<string, number>();
  const measure = (node: MapNode): number => {
    const own = size(node).height;
    const children = node.collapsed ? [] : node.children;
    const total = children.reduce((sum, child) => sum + measure(child), 0) + Math.max(0, children.length - 1) * gapY;
    const height = Math.max(own, total);
    heights.set(node.id, height);
    return height;
  };
  const place = (node: MapNode, parent: MapNode, x: number, top: number, side: 'left' | 'right', branch: number): void => {
    const box = size(node);
    const whole = heights.get(node.id)!;
    const nodeX = side === 'right' ? x : x - box.width;
    result.push({ node, parent, x: nodeX, y: top + whole / 2 - box.height / 2, width: box.width, height: box.height, side, branch });
    if (node.collapsed) return;
    const total = node.children.reduce((sum, child) => sum + heights.get(child.id)!, 0) + Math.max(0, node.children.length - 1) * gapY;
    let y = top + whole / 2 - total / 2;
    for (const child of node.children) {
      place(child, node, side === 'right' ? x + box.width + gapX : x - box.width - gapX, y, side, branch);
      y += heights.get(child.id)! + gapY;
    }
  };
  const rootBox = size(map.root);
  result.push({ node: map.root, parent: null, x: -rootBox.width / 2, y: -rootBox.height / 2, width: rootBox.width, height: rootBox.height, side: 'root', branch: -1 });
  if (map.root.collapsed) return result;
  const sideOf = (child: MapNode, index: number): 'left' | 'right' => map.layout === 'both' ? child.side ?? (index % 2 === 0 ? 'right' : 'left') : map.layout;
  map.root.children.forEach(measure);
  for (const side of ['right', 'left'] as const) {
    const group = map.root.children.map((child, index) => ({ child, index })).filter(item => sideOf(item.child, item.index) === side);
    const total = group.reduce((sum, item) => sum + heights.get(item.child.id)!, 0) + Math.max(0, group.length - 1) * gapY;
    let y = -total / 2;
    for (const { child, index } of group) {
      place(child, map.root, side === 'right' ? rootBox.width / 2 + gapX : -rootBox.width / 2 - gapX, y, side, index);
      y += heights.get(child.id)! + gapY;
    }
  }
  return result;
}
