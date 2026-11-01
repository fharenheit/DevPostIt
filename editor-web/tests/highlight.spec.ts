import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

async function colorsIn(page: Page, selector: string) {
  return page.locator(selector).evaluate(element => {
    const colors = new Set<string>();
    element.querySelectorAll('span').forEach(span => { if (span.children.length === 0 && span.textContent!.trim()) colors.add(getComputedStyle(span).color); });
    return colors.size;
  });
}
const colorOf = (page: Page, scope: string, text: string) => page.locator(scope).evaluate((root, value) => {
  const span = [...root.querySelectorAll('span')].find(item => item.children.length === 0 && item.textContent!.includes(value));
  return span ? getComputedStyle(span).color : null;
}, text);

const samples: Record<string, string> = {
  ts: 'const note: string = "메모"; // 설명',
  js: 'const count = 1; // 설명',
  json: '{ "name": "메모", "count": 1, "ok": true }',
  yaml: 'name: 메모\ncount: 1 # 설명',
  java: 'public class Note { int count = 1; }',
  html: '<div class="note">메모</div>',
  css: '.note { color: red; }',
  python: 'def note(): return "메모"',
  sql: 'SELECT name FROM notes WHERE id = 1;',
  sh: 'echo "메모" # 설명',
  bash: 'echo "메모" # 설명',
  csharp: 'public class Note { int count = 1; }',
};

const moreSamples: Record<string, string> = {
  cpp: '#include <vector>\nint main() { return 0; } // 설명',
  'c++': 'class Note { int count = 1; }; // 설명',
  c: 'int main(void) { return 0; } /* 설명 */',
  go: 'func main() { x := "메모" }',
  rust: 'fn main() { let x = "메모"; }',
  kotlin: 'fun main() { val x = "메모" }',
  php: '<?php echo "메모"; ?>',
  ruby: 'def note; "메모"; end',
  swift: 'let note = "메모"',
  scala: 'object Note { val x = "메모" }',
  lua: 'local x = "메모"',
  dart: 'void main() { var x = "메모"; }',
  r: 'x <- "메모"',
  perl: 'my $x = "메모";',
  powershell: '$x = "메모"',
  dockerfile: 'FROM node:24',
  graphql: 'query { note(id: 1) { name } }',
  scss: '.note { color: red; }',
  less: '.note { color: red; }',
  bat: 'echo 메모 & rem 설명',
  vb: 'Dim x As String = "메모"',
  xml: '<note id="1">메모</note>',
  ini: '[note]\nname = 메모',
  'objective-c': 'int main() { return 0; }',
  tsx: 'const a = "메모";',
  golang: 'func main() {}',
  yml: 'name: 1',
};

for (const [language, code] of Object.entries({ ...samples, ...moreSamples })) {
  test(`미리보기에서 ${language} 코드 블록을 색으로 구분한다`, async ({ page }) => {
    await openEditor(page, `\`\`\`${language}\n${code}\n\`\`\`\n`);
    await mode(page, 'preview');
    await expect.poll(() => colorsIn(page, '#preview pre code')).toBeGreaterThan(1);
  });
}

test('미리보기 하이라이트는 코드 글자와 들여쓰기를 바꾸지 않고 초안도 건드리지 않는다', async ({ page }) => {
  const code = 'function a() {\n  return "메모  두칸";\n}';
  const markdown = `\`\`\`ts\n${code}\n\`\`\`\n`;
  await openEditor(page, markdown);
  await mode(page, 'preview');
  await expect.poll(() => colorsIn(page, '#preview pre code')).toBeGreaterThan(1);
  const text = await page.locator('#preview pre code').evaluate(element => (element as HTMLElement).innerText);
  expect(text.replace(/\n+$/, '')).toBe(code);
  expect(await draft(page)).toMatchObject({ markdown, revision: 7, dirty: false });
});

test('언어가 없거나 모르는 코드 블록은 그대로 표시한다', async ({ page }) => {
  await openEditor(page, '```\nconst plain = 1;\n```\n\n```없는언어\nconst other = 2;\n```\n');
  await mode(page, 'preview');
  await expect(page.locator('#preview pre code').nth(0)).toHaveText('const plain = 1;\n');
  await expect(page.locator('#preview pre code').nth(1)).toHaveText('const other = 2;\n');
});

test('코드 블록 안의 HTML은 하이라이트 후에도 실행하지 않는다', async ({ page }) => {
  await openEditor(page, '```html\n<img src="x" onerror="window.attacked=true"><script>window.attacked=true</script>\n```\n');
  await mode(page, 'preview');
  await expect.poll(() => colorsIn(page, '#preview pre code')).toBeGreaterThan(1);
  await expect(page.locator('#preview img, #preview script')).toHaveCount(0);
  await expect(page.locator('#preview pre code')).toContainText('<script>');
  expect(await page.evaluate(() => (window as unknown as { attacked?: boolean }).attacked)).toBeUndefined();
});

test('다른 메모로 바뀌면 늦게 끝난 하이라이트가 새 내용을 덮어쓰지 않는다', async ({ page }) => {
  await openEditor(page, '```ts\nconst before = 1;\n```\n');
  await page.evaluate(() => {
    document.querySelector<HTMLButtonElement>('#preview-mode')!.click();
    window.hunminEditor.load('note-2', '```ts\nconst after = 2;\n```\n', 1);
    document.querySelector<HTMLButtonElement>('#preview-mode')!.click();
  });
  await expect.poll(() => colorsIn(page, '#preview pre code')).toBeGreaterThan(1);
  await expect(page.locator('#preview pre code')).toContainText('after');
  await expect(page.locator('#preview pre code')).not.toContainText('before');
  await expect(page.locator('#preview pre')).toHaveCount(1);
});

test('소스 편집에서도 코드 블록을 언어에 맞게 색으로 구분한다', async ({ page }) => {
  await openEditor(page, '```ts\nconst note = 1;\n```\n');
  await expect.poll(() => colorOf(page, '#source .view-lines', 'const')).not.toBeNull();
  await expect.poll(async () => await colorOf(page, '#source .view-lines', 'const') !== await colorOf(page, '#source .view-lines', 'note')).toBe(true);
});

test('하이라이트 글자색은 코드 블록 배경에서 대비 4.5:1 이상이다', async ({ page }) => {
  await openEditor(page, '```ts\nconst note: string = "메모"; let n = 1; // 설명\n@Dec class A {}\n```\n\n```html\n<a href="x">b</a>\n```\n\n```json\n{ "k": true }\n```\n');
  await mode(page, 'preview');
  await expect.poll(() => colorsIn(page, '#preview pre:last-of-type code')).toBeGreaterThan(1);
  const lowest = await page.locator('#preview').evaluate(root => {
    const luminance = (color: string) => {
      const [r, g, b] = color.match(/[\d.]+/g)!.slice(0, 3).map(value => { const c = Number(value) / 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; });
      return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    };
    let result = 21;
    root.querySelectorAll('pre').forEach(pre => {
      const back = luminance(getComputedStyle(pre).backgroundColor);
      pre.querySelectorAll('span').forEach(span => {
        const fore = luminance(getComputedStyle(span).color);
        result = Math.min(result, (Math.max(fore, back) + 0.05) / (Math.min(fore, back) + 0.05));
      });
    });
    return result;
  });
  expect(lowest).toBeGreaterThanOrEqual(4.5);
});

test('코드 블록 끝에 빈 줄을 더하지 않는다', async ({ page }) => {
  await openEditor(page, '```ts\nconst a = 1;\n```\n\n```\nconst a = 1;\n```\n');
  await mode(page, 'preview');
  const highlighted = page.locator('#preview pre').nth(0);
  await expect.poll(() => colorsIn(page, '#preview pre:first-of-type code')).toBeGreaterThan(1);
  const plain = page.locator('#preview pre').nth(1);
  expect((await highlighted.boundingBox())!.height).toBeLessThanOrEqual((await plain.boundingBox())!.height);
});

test('언어가 달라도 같은 종류의 토큰은 같은 색을 쓰고 소스 편집은 괄호를 따로 칠하지 않는다', async ({ page }) => {
  await openEditor(page, '```ts\nconst a = "문자";\nfunction f() {}\n```\n\n```json\n{ "k": true }\n```\n\n```html\n<a href="값">b</a>\n```\n');
  await expect.poll(() => colorOf(page, '#source .view-lines', 'function')).not.toBeNull();
  await page.waitForTimeout(300);
  const painted = await page.locator('#source .view-lines .bracket-highlighting-0, #source .view-lines .bracket-highlighting-1').count();
  expect(painted).toBe(0);
  await mode(page, 'preview');
  await expect.poll(() => colorsIn(page, '#preview pre:last-of-type code')).toBeGreaterThan(1);
  expect(await colorOf(page, '#preview', 'true')).toBe(await colorOf(page, '#preview', 'const'));
  expect(await colorOf(page, '#preview', '"값"')).toBe(await colorOf(page, '#preview', '"문자"'));
});

test('소스 편집에서도 C++ 코드 블록을 색으로 구분한다', async ({ page }) => {
  await openEditor(page, '```cpp\nint note = 1;\n```\n');
  await expect.poll(async () => {
    const [keyword, name] = await Promise.all([colorOf(page, '#source .view-lines', 'int'), colorOf(page, '#source .view-lines', 'note')]);
    return Boolean(keyword && name && keyword !== name);
  }).toBe(true);
});
