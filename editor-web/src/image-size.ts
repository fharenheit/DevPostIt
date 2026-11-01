// 미리보기에서 고른 이미지 크기를 원문의 대체 글자 뒤에 적습니다. 다른 Markdown 앱에서는 대체 글자로만 보입니다.
export type ImageAltEdit = { line: number; start: number; end: number; text: string };

export function parseImageAlt(alt: string): { alt: string; width: string | null } {
  const match = /^(.*)\|(\d{1,4})(%?)$/s.exec(alt);
  if (!match) return { alt, width: null };
  const value = Number(match[2]);
  const valid = match[3] ? value >= 1 && value <= 100 : value >= 16 && value <= 4000;
  return valid ? { alt: match[1], width: match[2] + match[3] } : { alt, width: null };
}

// 문단의 index번째 이미지를 찾습니다. 그 사이 원문이 바뀌어 파일이 다르면 고치지 않습니다.
export function imageAltEdit(lines: readonly string[], index: number, file: string, width: string | null): ImageAltEdit | null {
  let seen = 0;
  for (const [line, text] of lines.entries()) {
    for (const match of text.matchAll(/!\[([^\]\n]*)\]\(([^()\s]+)(?:\s+"[^"\n]*")?\)/g)) {
      if (seen++ !== index) continue;
      if (!match[2].endsWith(`/${file}`)) return null;
      const name = parseImageAlt(match[1]).alt;
      const start = match.index + 2;
      return { line, start, end: start + match[1].length, text: width ? `${name}|${width}` : name };
    }
  }
  return null;
}
