import DOMPurify from 'dompurify';

// Mermaid는 크므로 다이어그램이 있는 메모를 미리 볼 때만 불러옵니다.
let loading: Promise<typeof import('mermaid').default> | undefined;
function loadMermaid() {
  return loading ??= import('mermaid').then(({ default: mermaid }) => {
    // strict는 글자 안의 HTML과 클릭 동작을 막습니다. HTML 글자를 쓰지 않아 foreignObject도 필요 없습니다.
    mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: 'neutral', htmlLabels: false, flowchart: { htmlLabels: false } });
    return mermaid;
  });
}

const rendered = new Map<string, string>();
let nextId = 0;
async function render(source: string): Promise<string> {
  const known = rendered.get(source);
  if (known) return known;
  const mermaid = await loadMermaid();
  await mermaid.parse(source);
  const id = `mermaid-${++nextId}`;
  try {
    const { svg } = await mermaid.render(id, source);
    const clean = DOMPurify.sanitize(svg, { USE_PROFILES: { svg: true, svgFilters: true } });
    if (rendered.size >= 50) rendered.clear();
    rendered.set(source, clean);
    return clean;
  } finally { document.getElementById(`d${id}`)?.remove(); }
}

// 원문이 아니라 표시만 바꿉니다. 실패하면 코드 블록을 그대로 두고 까닭을 함께 보여 줍니다.
export async function renderDiagrams(preview: HTMLElement, isCurrent: () => boolean): Promise<void> {
  for (const code of preview.querySelectorAll<HTMLElement>('pre > code.language-mermaid')) {
    const block = code.parentElement!;
    try {
      const svg = await render((code.textContent ?? '').replace(/\r?\n$/, ''));
      if (!isCurrent()) return;
      const figure = document.createElement('figure');
      figure.className = 'mermaid-diagram';
      figure.innerHTML = svg;
      block.replaceWith(figure);
    } catch (error) {
      if (!isCurrent()) return;
      const message = document.createElement('p');
      message.className = 'diagram-error';
      message.textContent = `다이어그램을 그리지 못했습니다: ${String((error as Error)?.message ?? error).split('\n')[0].slice(0, 200)}`;
      block.after(message);
    }
  }
}
