import type { MarkdownAction } from './markdown-format';

type ToolbarItem = { action: MarkdownAction; label: string; icon: string; bold?: boolean };

// 오프라인에서 쓰도록 아이콘을 16×16 선 그림으로 직접 포함합니다.
const heading = 'M2 3v10M8 3v10M2 8h6';
const groups: ToolbarItem[][] = [
  [
    { action: 'h1', label: '제목 1', icon: `${heading}M11 6l2-1.5V13` },
    { action: 'h2', label: '제목 2', icon: `${heading}M10.5 6.5a1.8 1.8 0 1 1 3.2 1.2L10.5 13H14` },
    { action: 'h3', label: '제목 3', icon: `${heading}M10.5 5h3.5l-2 3a2.5 2.5 0 1 1-1.8 4.2` },
  ],
  [
    { action: 'bold', label: '굵게', icon: 'M4 3h5a2.5 2.5 0 0 1 0 5H4zM4 8h6a2.5 2.5 0 0 1 0 5H4z', bold: true },
    { action: 'italic', label: '기울임', icon: 'M7 3h5M4 13h5M10 3 6 13' },
    { action: 'strike', label: '취소선', icon: 'M11.5 5A3 2.5 0 0 0 5 5.5c0 1 .7 1.7 2 2.2M3 8h10M5 11a3 2.5 0 0 0 6.5-.5c0-.5-.1-.9-.4-1.2' },
  ],
  [
    { action: 'code', label: '인라인 코드', icon: 'M6 4 2 8l4 4M10 4l4 4-4 4' },
    { action: 'codeBlock', label: '코드 블록', icon: 'M2.5 2.5h11v11h-11zM7 6 5 8l2 2M9 6l2 2-2 2' },
  ],
  [
    { action: 'quote', label: '인용', icon: 'M3 3v10M6 5h7M6 8h7M6 11h5' },
    { action: 'bullet', label: '글머리 목록', icon: 'M6 4h8M6 8h8M6 12h8M2.5 4h.01M2.5 8h.01M2.5 12h.01' },
    { action: 'ordered', label: '번호 목록', icon: 'M7 4h7M7 8h7M7 12h7M2.5 3 3.5 2.5V6M2.5 9.5a1 1 0 1 1 1.7.7L2.5 12.5h2' },
    { action: 'task', label: '할 일 목록', icon: 'M2.5 2.5h4v4h-4zM9 4.5h5M2.5 9.5h4v4h-4zM3.5 11.5l1 1 1.2-1.8M9 11.5h5' },
  ],
  [
    { action: 'link', label: '링크', icon: 'M6.5 9.5a2.5 2.5 0 0 0 3.6 0l2.4-2.4a2.5 2.5 0 0 0-3.6-3.6L8 4.4M9.5 6.5a2.5 2.5 0 0 0-3.6 0L3.5 8.9a2.5 2.5 0 0 0 3.6 3.6L8 11.6' },
    { action: 'table', label: '표', icon: 'M2 3h12v10H2zM2 6.5h12M2 10h12M8 3v10' },
    { action: 'rule', label: '구분선', icon: 'M2 8h12' },
  ],
];

const button = ({ action, label, icon, bold }: ToolbarItem) =>
  `<button type="button" data-format="${action}" aria-label="${label}" title="${label}"${bold ? ' class="heavy"' : ''}><svg viewBox="0 0 16 16" aria-hidden="true"><path d="${icon}"/></svg></button>`;

export const formatToolbarHtml = `<div class="format-toolbar" role="toolbar" aria-label="Markdown 서식">${groups.map(group => group.map(button).join('')).join('<span class="format-separator" aria-hidden="true"></span>')}</div>`;
