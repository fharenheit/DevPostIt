const maxHtmlLength = 4 * 1024 * 1024, maxCells = 100_000;

// Excel·Google Sheets 등은 클립보드에 HTML 표를 함께 넣습니다. 탭이 든 일반 글은 코드일 수 있어 표로 보지 않습니다.
// DOMParser 문서는 스크립트를 실행하거나 이미지를 요청하지 않습니다.
export function tableFromClipboardHtml(html: string): string[][] | null {
  if (html.length > maxHtmlLength || !/<table[\s>]/i.test(html)) return null;
  const body = new DOMParser().parseFromString(html, 'text/html').body;
  const tables = body.querySelectorAll('table');
  if (tables.length !== 1) return null;
  const table = tables[0];
  table.querySelectorAll('script, style, template').forEach(element => element.remove());
  table.querySelectorAll('br').forEach(element => element.replaceWith(' '));
  const outside = body.cloneNode(true) as HTMLElement;
  outside.querySelector('table')!.remove();
  if (outside.textContent!.trim()) return null;

  const rows: string[][] = [];
  const occupied = new Set<string>();
  let cells = 0;
  Array.from(table.rows).forEach((row, rowIndex) => {
    const values: string[] = rows[rowIndex] ?? (rows[rowIndex] = []);
    let column = 0;
    for (const cell of Array.from(row.cells)) {
      while (occupied.has(`${rowIndex}:${column}`)) column++;
      const text = cell.textContent!.replace(/\s+/g, ' ').trim();
      for (let down = 0; down < cell.rowSpan; down++) {
        for (let across = 0; across < cell.colSpan; across++) {
          if (++cells > maxCells) return;
          occupied.add(`${rowIndex + down}:${column + across}`);
          if (down === 0) values[column + across] = across === 0 ? text : '';
        }
      }
      column += cell.colSpan;
    }
  });
  if (cells > maxCells) return null;
  const width = Math.max(0, ...rows.map(row => row.length));
  const filled = rows.map(row => Array.from({ length: width }, (_, index) => row[index] ?? ''));
  while (filled.length && filled[filled.length - 1].every(cell => !cell)) filled.pop();
  return filled.length && width && filled.length * width > 1 ? filled : null;
}

export function markdownTableLines(rows: string[][]): string[] {
  const line = (cells: string[]) => `| ${cells.map(cell => cell.replaceAll('\\', '\\\\').replaceAll('|', '\\|')).join(' | ')} |`;
  return [line(rows[0]), `| ${rows[0].map(() => '---').join(' | ')} |`, ...rows.slice(1).map(line)];
}
