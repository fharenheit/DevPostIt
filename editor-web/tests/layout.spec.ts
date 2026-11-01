import { test, expect, type Page } from '@playwright/test';
import { openEditor, mode, openSettings } from './helpers';

const longMarkdown = '# 제목\n\n본문\n\n```ts\nlet a = 1;\n```\n';

async function openAt(page: Page, width: number, height: number, size = 15) {
  await page.setViewportSize({ width, height });
  await page.addInitScript(value => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify({ font: 'd2', ligatures: true, size: value })), size);
  await openEditor(page, longMarkdown);
}
const box = (page: Page, selector: string) => page.locator(selector).first().evaluate(element => {
  const rect = element.getBoundingClientRect();
  return { left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height };
});

test('메모 영역은 창을 꽉 채우고 안내 문구와 형식 배지를 표시하지 않는다', async ({ page }) => {
  await openAt(page, 480, 640);
  const workspace = await box(page, '.workspace');
  expect(workspace).toMatchObject({ left: 0, top: 0, width: 480, height: 640 });
  await expect(page.locator('.document-icon')).toHaveCount(0);
  await expect(page.locator('body')).not.toContainText('Markdown으로 작성하고');
});

test('좁은 창에서도 본문이 창 높이의 대부분을 차지한다', async ({ page }) => {
  await openAt(page, 420, 520);
  const area = await box(page, '.editor-area');
  expect(area.height / 520).toBeGreaterThanOrEqual(0.7);
});

for (const [width, height, size] of [[304, 320, 24], [304, 320, 15], [480, 640, 24], [480, 640, 10], [1200, 850, 24]] as const) {
  test(`${width}×${height} 창과 글자 ${size}px에서 넘치거나 잘리는 컨트롤이 없다`, async ({ page }) => {
    await openAt(page, width, height, size);
    const scroll = await page.evaluate(() => ({ width: document.documentElement.scrollWidth, height: document.documentElement.scrollHeight }));
    expect(scroll.width).toBeLessThanOrEqual(width);
    expect(scroll.height).toBeLessThanOrEqual(height);
    for (const selector of ['h1', '#source-mode', '#preview-mode', '#code-language', '#format-code', '#status']) {
      const rect = await box(page, selector);
      expect(rect.left, selector).toBeGreaterThanOrEqual(0);
      expect(rect.right, selector).toBeLessThanOrEqual(width);
      expect(rect.bottom, selector).toBeLessThanOrEqual(height);
    }
    expect((await box(page, '.editor-area')).height).toBeGreaterThan(60);
  });
}

test('긴 저장 오류는 잘리지 않고 줄바꿈으로 모두 표시한다', async ({ page }) => {
  await openAt(page, 320, 480);
  await page.locator('#host-status').evaluate(element => { element.textContent = '파일이 외부에서 변경되어 저장하지 못했습니다. 내용을 별도로 보관한 뒤 다시 시도하세요. '.repeat(2); });
  const overflow = await page.locator('#host-status').evaluate(element => element.scrollWidth > element.clientWidth || element.scrollHeight > element.clientHeight);
  expect(overflow).toBe(false);
  const rect = await box(page, '#host-status');
  expect(rect.right).toBeLessThanOrEqual(320);
  expect(rect.bottom).toBeLessThanOrEqual(480);
});

test('미리보기에서는 코드 도구를 비활성화하고 소스 편집으로 돌아오면 다시 켠다', async ({ page }) => {
  await openAt(page, 480, 640);
  await mode(page, 'preview');
  await expect(page.getByRole('button', { name: '코드 정리', exact: true })).toBeDisabled();
  await expect(page.getByLabel('코드 언어')).toBeDisabled();
  await mode(page, 'source');
  await expect(page.getByRole('button', { name: '코드 정리', exact: true })).toBeEnabled();
  await expect(page.getByLabel('코드 언어')).toBeEnabled();
});

test('코드 정리가 끝날 때 미리보기로 바뀌어 있으면 코드 도구를 다시 켜지 않는다', async ({ page }) => {
  await openAt(page, 480, 640);
  await page.evaluate(() => {
    document.querySelector<HTMLButtonElement>('#format-code')!.click();
    document.querySelector<HTMLButtonElement>('#preview-mode')!.click();
  });
  await expect(page.locator('#format-status')).not.toBeEmpty();
  await expect(page.getByRole('button', { name: '코드 정리', exact: true })).toBeDisabled();
});

function luminance(color: string): number {
  const [r, g, b] = color.match(/[\d.]+/g)!.slice(0, 3).map(value => {
    const channel = Number(value) / 255;
    return channel <= 0.03928 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
async function contrast(page: Page, selector: string): Promise<number> {
  const [foreground, background] = await page.locator(selector).first().evaluate(element => {
    let target: Element | null = element;
    while (target && getComputedStyle(target).backgroundColor === 'rgba(0, 0, 0, 0)') target = target.parentElement;
    return [getComputedStyle(element).color, getComputedStyle(target ?? document.documentElement).backgroundColor];
  });
  const [light, dark] = [luminance(foreground), luminance(background)].sort((a, b) => b - a);
  return (light + 0.05) / (dark + 0.05);
}

test('글자와 컨트롤의 대비가 4.5:1 이상이다', async ({ page }) => {
  await openAt(page, 480, 640);
  for (const selector of ['h1', '#source-mode', '#preview-mode', '#status', '#host-status', '#code-language', '#format-code']) {
    expect(await contrast(page, selector), selector).toBeGreaterThanOrEqual(4.5);
  }
});

test('코드 도구는 앱의 공통 컨트롤 모양을 사용한다', async ({ page }) => {
  await openAt(page, 480, 640);
  for (const selector of ['#code-language', '#format-code']) {
    const style = await page.locator(selector).evaluate(element => { const value = getComputedStyle(element); return { appearance: value.appearance, radius: parseFloat(value.borderTopLeftRadius) }; });
    expect(style.radius, selector).toBeGreaterThan(0);
    if (selector === '#format-code') expect(style.appearance, selector).toBe('none');
  }
});

for (const [width, height, size] of [[304, 240, 24], [364, 320, 15]] as const) {
  test(`설정 창 ${width}×${height}과 글자 ${size}px에서 항목을 한 줄씩 정리해 표시한다`, async ({ page }) => {
    await openAt(page, 480, 640, size);
    const settings = await openSettings(page);
    await settings.setViewportSize({ width, height });
    await expect(settings.locator('.setting-row:visible')).toHaveCount(12);
    expect(await settings.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(width);
    for (const row of await settings.locator('.setting-row:visible').all()) {
      const rect = (await row.boundingBox())!;
      expect(rect.x + rect.width).toBeLessThanOrEqual(width);
    }
    const ligature = await settings.locator('.setting-row:visible').filter({ hasText: '합자 사용' }).locator('span').first().evaluate(element => element.getBoundingClientRect().height / parseFloat(getComputedStyle(element).lineHeight));
    expect(ligature).toBeLessThan(1.5);
  });
}

test('설정 창은 메모 편집기를 싣지 않고 공통 컨트롤 모양과 대비를 따른다', async ({ page }) => {
  await page.goto('/settings.html');
  await expect(page).toHaveTitle('설정');
  await expect(page.locator('.monaco-editor, .workspace, dialog')).toHaveCount(0);
  for (const selector of ['#larger', '#font']) {
    expect(await contrast(page, selector), selector).toBeGreaterThanOrEqual(4.5);
    const radius = await page.locator(selector).evaluate(element => parseFloat(getComputedStyle(element).borderTopLeftRadius));
    expect(radius, selector).toBeGreaterThan(0);
  }
});

test('편집기와 미리보기는 메모지 배경을 함께 쓰고 편집기 장식을 줄인다', async ({ page }) => {
  await openAt(page, 480, 640);
  const workspace = await page.locator('.workspace').evaluate(element => getComputedStyle(element).backgroundColor);
  await expect(page.locator('#source .monaco-editor-background').first()).toHaveCSS('background-color', workspace);
  await expect(page.locator('#source .decorationsOverviewRuler')).toBeHidden();
  await expect(page.locator('#source .view-overlays .current-line')).toHaveCount(1);
  await mode(page, 'preview');
  await expect(page.locator('#preview')).toHaveCSS('background-color', 'rgba(0, 0, 0, 0)');
});

test('넓은 창의 미리보기는 읽기 좋은 폭으로 가운데에 놓고 좁은 창에서는 넘치지 않는다', async ({ page }) => {
  await openAt(page, 1200, 850);
  await mode(page, 'preview');
  const wide = await box(page, '#preview h1');
  expect(wide.width).toBeLessThanOrEqual(760);
  expect(wide.left).toBeGreaterThan(100);
  await page.setViewportSize({ width: 304, height: 320 });
  const narrow = await box(page, '#preview h1');
  expect(narrow.right).toBeLessThanOrEqual(304);
  expect(narrow.left).toBeLessThanOrEqual(24);
});

async function fontSizes(page: Page, selector: string) {
  return page.locator(selector).evaluate(root => {
    const sizes = new Set<string>();
    for (const element of [root, ...root.querySelectorAll('*')]) if (element.tagName !== 'OPTION') sizes.add(getComputedStyle(element).fontSize);
    return [...sizes];
  });
}

for (const size of [10, 15, 24]) {
  test(`메모 글자 ${size}px에서도 설정과 이름 변경 화면은 모두 14px이다`, async ({ page }) => {
    await openAt(page, 480, 640, size);
    const settings = await openSettings(page);
    expect(await fontSizes(settings, '#settings')).toEqual(['14px']);
    await settings.close();
    await page.locator('h1').first().dblclick();
    await expect(page.locator('#rename-dialog')).toBeVisible();
    await page.locator('#rename-error').evaluate(element => { element.textContent = '오류'; });
    expect(await fontSizes(page, '#rename-dialog')).toEqual(['14px']);
  });
}

test('데이터 폴더 버튼은 앱 안에서만 보이고 경로 없이 열기 요청만 보낸다', async ({ page }) => {
  await page.goto('/settings.html');
  await expect(page.getByRole('button', { name: '폴더 열기', exact: true })).toBeHidden();

  await page.addInitScript(() => {
    const sent: unknown[] = [];
    Object.assign(window, { sent, chrome: { webview: { postMessage: (message: unknown) => sent.push(message), addEventListener: () => undefined } } });
  });
  await page.reload();
  await expect(page.locator('.setting-row:visible')).toHaveCount(18);
  await page.getByRole('button', { name: '폴더 열기', exact: true }).click();
  expect(await page.evaluate(() => (window as unknown as { sent: unknown[] }).sent)).toEqual([{ protocolVersion: 1, type: 'settings.openDataFolder' }]);
  expect(await fontSizes(page, '#settings')).toEqual(['14px']);
});
