import { editorTarget, noteAssetUrl } from './platform';
import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';
import { parseImageAlt } from './image-size';

const markdown = new MarkdownIt({ html: false, linkify: false, typographer: false });
// 웹과 메일 링크만 누를 수 있습니다. 누르면 페이지가 옮겨 가지 않고 앱이 확인 후 기본 브라우저로 엽니다.
const openable = (href: unknown) => /^(?:https?:\/\/|mailto:)/i.test(String(href ?? ''));
markdown.renderer.rules.link_open = (tokens, index) => {
  const href = tokens[index].attrGet('href');
  if (!openable(href)) return '<span class="preview-link">';
  const value = markdown.utils.escapeHtml(String(href));
  return `<a class="preview-link" href="${value}" title="${value}">`;
};
markdown.renderer.rules.link_close = (tokens, index) => {
  for (let at = index - 1, depth = 0; at >= 0; at--) {
    if (tokens[at].type === 'link_close') depth++;
    else if (tokens[at].type === 'link_open' && depth-- === 0) return openable(tokens[at].attrGet('href')) ? '</a>' : '</span>';
  }
  return '</span>';
};
// 앱이 저장한 붙여넣기 이미지만 표시합니다. 다른 이미지는 요청하지 않고 글자로 둡니다.
const noteImage = /^(?:\.\.\/)*assets\/([0-9a-f]{32}\/[0-9a-f]{32}\.(?:png|drawio\.svg))$/;
markdown.renderer.rules.image = (tokens, index) => {
  const { alt: text, width } = parseImageAlt(tokens[index].content);
  const alt = markdown.utils.escapeHtml(text);
  const match = noteImage.exec(String(tokens[index].attrGet('src') ?? ''));
  if (!match) return `[이미지: ${alt}]`;
  const { line, end, order } = tokens[index].meta ?? {};
  const position = Number.isInteger(line) ? ` data-line="${line}" data-end="${end}" data-index="${order}"` : '';
  return `<img src="${noteAssetUrl(match[1])}" alt="${alt}"${width ? ` width="${width}"` : ''}${position}>`;
};
// 미리보기에서 크기를 고치려면 이미지가 원문의 어느 문단에 있는지 알아야 합니다.
markdown.core.ruler.after('inline', 'image-positions', state => {
  for (const token of state.tokens) {
    if (token.type !== 'inline' || !token.map) continue;
    let order = 0;
    for (const child of token.children ?? []) if (child.type === 'image') child.meta = { line: token.map[0], end: token.map[1], order: order++ };
  }
});
markdown.core.ruler.after('inline', 'task-checkboxes', state => {
  for (let index = 2; index < state.tokens.length; index++) {
    const token = state.tokens[index];
    if (token.type !== 'inline' || state.tokens[index - 1].type !== 'paragraph_open' ||
        state.tokens[index - 2].type !== 'list_item_open' || !token.children?.length) continue;
    const first = token.children[0];
    const match = first.type === 'text' ? /^\[([ xX])\] /.exec(first.content) : null;
    if (!match) continue;
    first.content = first.content.slice(match[0].length);
    const checkbox = new state.Token('html_inline', '', 0);
    checkbox.content = `<input type="checkbox" data-line="${token.map?.[0] ?? ''}" aria-label="할 일 완료"${match[1].toLowerCase() === 'x' ? ' checked' : ''}> `;
    token.children.unshift(checkbox);
  }
});
// 표 칸은 한 줄로 적어야 하므로 줄바꿈 표시인 <br>만 받습니다. 코드, 이스케이프한 글자, 다른 HTML은 글자로 둡니다.
markdown.core.ruler.after('inline', 'table-line-breaks', state => {
  state.tokens.forEach((token, index) => {
    if (token.type !== 'inline' || !/^t[dh]_open$/.test(state.tokens[index - 1]?.type ?? '')) return;
    token.children = (token.children ?? []).flatMap(child => {
      if (child.type !== 'text') return [child];
      return child.content.split(/<br\s*\/?>/i).flatMap((content, at) => {
        const text = new state.Token('text', '', 0);
        text.content = content;
        text.level = child.level;
        if (at === 0) return [text];
        const line = new state.Token('hardbreak', 'br', 0);
        line.level = child.level;
        return [line, text];
      });
    });
  });
});

// DOMPurify 기본값은 앱 이미지의 사용자 지정 스킴을 지웁니다. 이 규칙은 data-line 같은 일반 속성 값에도 적용되므로 스킴이 없는 값은 그대로 허용합니다.
const macUris = /^(?:devpostit-assets:\/\/notes\/|https?:|mailto:|[^a-z]|[a-z+.\-]+(?:[^a-z+.\-:]|$))/i;

export function renderMarkdown(source: string): string {
  return DOMPurify.sanitize(markdown.render(source), {
    ALLOWED_TAGS: ['p', 'br', 'hr', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'strong', 'em', 's', 'blockquote', 'ul', 'ol', 'li', 'pre', 'code', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span', 'input', 'img', 'a'],
    ALLOWED_ATTR: ['class', 'type', 'checked', 'start', 'align', 'src', 'alt', 'data-line', 'data-end', 'data-index', 'aria-label', 'href', 'title', 'width'],
    ALLOW_DATA_ATTR: false,
    ...(editorTarget === 'macos' ? { ALLOWED_URI_REGEXP: macUris } : {}),
  });
}
