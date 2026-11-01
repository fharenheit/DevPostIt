export type MarkdownAction = 'h1' | 'h2' | 'h3' | 'bold' | 'italic' | 'strike' | 'code' | 'codeBlock' | 'quote' | 'bullet' | 'ordered' | 'task' | 'link' | 'table' | 'rule';
export type TextRange = { startLine: number; startColumn: number; endLine: number; endColumn: number };
export type TextDocument = { lineCount: number; line(number: number): string; text(range: TextRange): string; eol: string };
export type FormatPlan = { edits: { range: TextRange; text: string }[]; selection: TextRange };

const point = (line: number, column: number): TextRange => ({ startLine: line, startColumn: column, endLine: line, endColumn: column });
const span = (line: number, start: number, end: number): TextRange => ({ startLine: line, startColumn: start, endLine: line, endColumn: end });

// 문서를 직접 고치지 않고 편집 범위와 이후 선택만 계산합니다. 호출자가 한 번의 실행 취소 단위로 적용합니다.
export function planFormat(action: MarkdownAction, document: TextDocument, selection: TextRange): FormatPlan {
  switch (action) {
    case 'bold': return wrapInline(document, selection, '**');
    case 'italic': return wrapInline(document, selection, '*');
    case 'strike': return wrapInline(document, selection, '~~');
    case 'code': return wrapInline(document, selection, '`');
    case 'h1': return prefixLines(document, selection, headingPrefix(1));
    case 'h2': return prefixLines(document, selection, headingPrefix(2));
    case 'h3': return prefixLines(document, selection, headingPrefix(3));
    case 'quote': return prefixLines(document, selection, quotePrefix);
    case 'bullet': return prefixLines(document, selection, listPrefix('bullet'));
    case 'ordered': return prefixLines(document, selection, listPrefix('ordered'));
    case 'task': return prefixLines(document, selection, listPrefix('task'));
    case 'codeBlock': return codeBlock(document, selection);
    case 'link': return link(document, selection);
    case 'table': return insertBlock(document, selection, ['| 제목 | 제목 |', '| --- | --- |', '| 내용 | 내용 |'], 3, 2);
    case 'rule': return insertBlock(document, selection, ['---'], 4, 0);
  }
}

function isEmpty(range: TextRange): boolean { return range.startLine === range.endLine && range.startColumn === range.endColumn; }
function endOf(line: number, column: number, text: string): { line: number; column: number } {
  const parts = text.split(/\r\n|\r|\n/);
  return parts.length === 1 ? { line, column: column + text.length } : { line: line + parts.length - 1, column: parts[parts.length - 1].length + 1 };
}
function runBefore(text: string, index: number, character: string): number {
  let count = 0;
  while (index - count - 1 >= 0 && text[index - count - 1] === character) count++;
  return count;
}
function runAfter(text: string, index: number, character: string): number {
  let count = 0;
  while (index + count < text.length && text[index + count] === character) count++;
  return count;
}
// `*`는 `**` 안에서도 켜고 끌 수 있도록 표시 개수의 홀짝으로 구분합니다.
function hasMarker(before: number, after: number, marker: string): boolean {
  if (marker === '*') return before % 2 === 1 && after % 2 === 1;
  if (marker === '**') return before >= 2 && after >= 2;
  return before >= marker.length && after >= marker.length;
}

function wrapInline(document: TextDocument, selection: TextRange, marker: string): FormatPlan {
  const text = document.text(selection);
  const first = document.line(selection.startLine), last = document.line(selection.endLine);
  const before = runBefore(first, selection.startColumn - 1, marker[0]), after = runAfter(last, selection.endColumn - 1, marker[0]);
  if (hasMarker(before, after, marker)) {
    const range = { startLine: selection.startLine, startColumn: selection.startColumn - marker.length, endLine: selection.endLine, endColumn: selection.endColumn + marker.length };
    const end = endOf(range.startLine, range.startColumn, text);
    return { edits: [{ range, text }], selection: { startLine: range.startLine, startColumn: range.startColumn, endLine: end.line, endColumn: end.column } };
  }
  const leading = runAfter(text, 0, marker[0]), trailing = runBefore(text, text.length, marker[0]);
  if (text.length >= marker.length * 2 && hasMarker(leading, trailing, marker)) {
    const inner = text.slice(marker.length, text.length - marker.length);
    const end = endOf(selection.startLine, selection.startColumn, inner);
    return { edits: [{ range: selection, text: inner }], selection: { startLine: selection.startLine, startColumn: selection.startColumn, endLine: end.line, endColumn: end.column } };
  }
  const startColumn = selection.startColumn + marker.length;
  const end = endOf(selection.startLine, startColumn, text);
  return { edits: [{ range: selection, text: marker + text + marker }], selection: { startLine: selection.startLine, startColumn, endLine: end.line, endColumn: end.column } };
}

type LinePrefix = { current(line: string): string; toggleOff(lines: string[]): boolean; next(line: string, index: number, off: boolean): string };

function headingPrefix(level: number): LinePrefix {
  const current = (line: string) => /^#{1,6}[ \t]+/.exec(line)?.[0] ?? '';
  return {
    current,
    toggleOff: lines => lines.every(line => current(line).trimEnd() === '#'.repeat(level)),
    next: (_line, _index, off) => (off ? '' : `${'#'.repeat(level)} `),
  };
}
const quotePrefix: LinePrefix = {
  current: line => /^ {0,3}>[ ]?/.exec(line)?.[0] ?? '',
  toggleOff: lines => lines.every(line => /^ {0,3}>/.test(line)),
  next: (line, _index, off) => (off ? '' : `${/^ {0,3}>[ ]?/.exec(line)?.[0] ?? ''}> `),
};
function listPrefix(kind: 'bullet' | 'ordered' | 'task'): LinePrefix {
  const task = /^(\s*)[-*+][ \t]+\[[ xX]\][ \t]+/, bullet = /^(\s*)[-*+][ \t]+/, ordered = /^(\s*)\d+[.)][ \t]+/;
  const match = (line: string) => task.exec(line) ?? ordered.exec(line) ?? bullet.exec(line);
  const isKind = (line: string) => (kind === 'task' ? task.test(line) : kind === 'ordered' ? ordered.test(line) : !task.test(line) && bullet.test(line));
  return {
    current: line => match(line)?.[0] ?? /^\s*/.exec(line)![0],
    toggleOff: lines => lines.every(isKind),
    next: (line, index, off) => {
      const indent = match(line)?.[1] ?? /^\s*/.exec(line)![0];
      if (off) return indent;
      return indent + (kind === 'task' ? '- [ ] ' : kind === 'ordered' ? `${index + 1}. ` : '- ');
    },
  };
}

function selectedLines(selection: TextRange): { first: number; last: number } {
  const last = selection.endColumn === 1 && selection.endLine > selection.startLine ? selection.endLine - 1 : selection.endLine;
  return { first: selection.startLine, last };
}

function prefixLines(document: TextDocument, selection: TextRange, prefix: LinePrefix): FormatPlan {
  const { first, last } = selectedLines(selection);
  const numbers: number[] = [];
  for (let line = first; line <= last; line++) if (first === last || document.line(line).trim()) numbers.push(line);
  const off = prefix.toggleOff(numbers.map(line => document.line(line)));
  const changes = new Map<number, { oldLength: number; newLength: number }>();
  const edits = numbers.map((line, index) => {
    const text = document.line(line);
    const oldPrefix = prefix.current(text);
    const newPrefix = prefix.next(text, index, off);
    changes.set(line, { oldLength: oldPrefix.length, newLength: newPrefix.length });
    return { range: span(line, 1, oldPrefix.length + 1), text: newPrefix };
  });
  const collapsed = isEmpty(selection);
  const move = (line: number, column: number) => {
    const change = changes.get(line);
    if (!change || (column === 1 && !collapsed)) return column;
    return column <= change.oldLength + 1 ? change.newLength + 1 : column + change.newLength - change.oldLength;
  };
  return { edits, selection: { startLine: selection.startLine, startColumn: move(selection.startLine, selection.startColumn), endLine: selection.endLine, endColumn: move(selection.endLine, selection.endColumn) } };
}

function codeBlock(document: TextDocument, selection: TextRange): FormatPlan {
  const { first, last } = selectedLines(selection);
  if (first === last && !document.line(first).trim()) {
    const length = document.line(first).length;
    return { edits: [{ range: span(first, 1, length + 1), text: `\`\`\`${document.eol}${document.eol}\`\`\`` }], selection: point(first + 1, 1) };
  }
  let longest = 2;
  for (let line = first; line <= last; line++) longest = Math.max(longest, /^\s*(`+)/.exec(document.line(line))?.[1].length ?? 0);
  const fence = '`'.repeat(longest + 1);
  const end = document.line(last).length + 1;
  return {
    edits: [{ range: point(first, 1), text: fence + document.eol }, { range: point(last, end), text: document.eol + fence }],
    selection: { startLine: first + 1, startColumn: 1, endLine: last + 1, endColumn: end },
  };
}

function link(document: TextDocument, selection: TextRange): FormatPlan {
  const text = document.text(selection);
  const label = text || '글자';
  const end = endOf(selection.startLine, selection.startColumn + 1, label);
  const edit = { range: selection, text: `[${label}](url)` };
  if (!text) return { edits: [edit], selection: { startLine: selection.startLine, startColumn: selection.startColumn + 1, endLine: end.line, endColumn: end.column } };
  return { edits: [edit], selection: span(end.line, end.column + 2, end.column + 5) };
}

// 앞줄에 붙은 `---`가 제목으로 해석되지 않도록 블록을 빈 줄로 분리합니다.
function insertBlock(document: TextDocument, selection: TextRange, lines: string[], startColumn: number, selectLength: number): FormatPlan {
  const line = selection.endLine;
  const text = document.line(line);
  const blank = !text.trim();
  const before = blank ? (line > 1 && document.line(line - 1).trim() ? document.eol : '') : document.eol + document.eol;
  const after = line < document.lineCount && document.line(line + 1).trim() ? document.eol : '';
  const range = blank ? span(line, 1, text.length + 1) : point(line, text.length + 1);
  const target = blank ? line + (before ? 1 : 0) : line + 2;
  const finish = selectLength ? startColumn + selectLength : startColumn;
  return { edits: [{ range, text: before + lines.join(document.eol) + after }], selection: span(target, startColumn, finish) };
}
