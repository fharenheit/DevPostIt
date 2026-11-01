import MarkdownIt from 'markdown-it';
import DOMPurify from 'dompurify';

const markdown = new MarkdownIt({ html: false, linkify: false, typographer: false });
// 미리보기는 읽기 전용입니다. 외부 탐색과 이미지 요청은 호스트 정책이 연결될 때까지 만들지 않습니다.
markdown.renderer.rules.link_open = () => '<span class="preview-link">';
markdown.renderer.rules.link_close = () => '</span>';
markdown.renderer.rules.image = (tokens, index) => `[이미지: ${markdown.utils.escapeHtml(tokens[index].content)}]`;
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
    checkbox.content = `<input type="checkbox" disabled${match[1].toLowerCase() === 'x' ? ' checked' : ''}> `;
    token.children.unshift(checkbox);
  }
});

export function renderMarkdown(source: string): string {
  return DOMPurify.sanitize(markdown.render(source), {
    ALLOWED_TAGS: ['p', 'br', 'hr', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'strong', 'em', 's', 'blockquote', 'ul', 'ol', 'li', 'pre', 'code', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'span', 'input'],
    ALLOWED_ATTR: ['class', 'type', 'checked', 'disabled', 'start', 'align'],
    ALLOW_DATA_ATTR: false,
  });
}
