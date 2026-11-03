import { test, expect, type Page } from '@playwright/test';
import { openEditor, draft, mode, openSettings, replaceText, documentStart, replaceKey, foldKey, unfoldKey } from './helpers';

const markdown = async (page: Page) => (await draft(page)).markdown;
const minimapWidth = (page: Page) => page.locator('#source .minimap').evaluate(element => (element as HTMLElement).offsetWidth);
async function start(page: Page, text: string) {
  await openEditor(page);
  await replaceText(page, text);
  await page.keyboard.press(documentStart);
}

test('현재 줄을 강조하고 포커스가 없어도 유지한다', async ({ page }) => {
  await openEditor(page, '첫째\n둘째');
  await expect(page.locator('#source .view-overlays .current-line')).toHaveCount(1);
  const color = await page.locator('#source .view-overlays .current-line').evaluate(element => getComputedStyle(element).backgroundColor);
  expect(color).not.toBe('rgba(0, 0, 0, 0)');
});

test('Ctrl+F로 찾으면 일치 개수를 보여 주고 원문은 바꾸지 않는다', async ({ page }) => {
  await start(page, '사과 배\n사과');
  await page.keyboard.press('ControlOrMeta+f');
  await expect(page.locator('#source .find-widget.visible')).toBeVisible();
  await page.keyboard.insertText('사과');
  await expect(page.locator('#source .find-widget .matchesCount')).toContainText('2');
  expect(await markdown(page)).toBe('사과 배\n사과');
  await page.keyboard.press('Escape');
  await expect(page.locator('#source .find-widget.visible')).toHaveCount(0);
});

test('Ctrl+H로 모두 바꾸기를 하고 한 번에 되돌린다', async ({ page }) => {
  await start(page, '사과 배\n사과');
  await page.keyboard.press(replaceKey);
  await expect(page.locator('#source .find-widget.replaceToggled')).toBeVisible();
  await page.keyboard.insertText('사과');
  await page.keyboard.press('Tab');
  await page.keyboard.insertText('포도');
  await page.keyboard.press('ControlOrMeta+Alt+Enter');
  await expect.poll(() => markdown(page)).toBe('포도 배\n포도');
  await page.keyboard.press('Escape');
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press('ControlOrMeta+z');
  await expect.poll(() => markdown(page)).toBe('사과 배\n사과');
});

for (const [name, selector] of [['찾기', '.find-widget.visible'], ['바꾸기', '.find-widget.replaceToggled']] as const) {
  test(`툴바의 ${name} 버튼으로 연다`, async ({ page }) => {
    await start(page, '본문');
    await page.getByRole('toolbar', { name: 'Markdown 서식', exact: true }).getByRole('button', { name, exact: true }).click();
    await expect(page.locator(`#source ${selector}`)).toBeVisible();
    expect((await draft(page)).dirty).toBe(true);
  });
}

test('F1로 명령 팔레트를 열어 줄 이동을 실행한다', async ({ page }) => {
  await start(page, '첫째\n둘째\n셋째');
  await page.keyboard.press('F1');
  await expect(page.locator('.quick-input-widget')).toBeVisible();
  await page.keyboard.press('Escape');
  await page.keyboard.press('Control+g');
  await expect(page.locator('.quick-input-widget')).toBeVisible();
  await page.keyboard.insertText('3');
  await page.keyboard.press('Enter');
  await page.keyboard.insertText('끝');
  await expect.poll(() => markdown(page)).toBe('첫째\n둘째\n끝셋째');
});

test('여러 커서로 같은 글자를 함께 고친다', async ({ page }) => {
  await start(page, 'ab ab');
  await page.keyboard.press('ControlOrMeta+d');
  await page.keyboard.press('ControlOrMeta+d');
  await page.keyboard.insertText('cd');
  await expect.poll(() => markdown(page)).toBe('cd cd');
});

test('줄 이동·복제·주석 단축키가 동작한다', async ({ page }) => {
  await start(page, '첫째\n둘째');
  await page.keyboard.press('Alt+ArrowDown');
  await expect.poll(() => markdown(page)).toBe('둘째\n첫째');
  await page.keyboard.press('Shift+Alt+ArrowDown');
  await expect.poll(() => markdown(page)).toBe('둘째\n첫째\n첫째');
  await page.keyboard.press('ControlOrMeta+/');
  await expect.poll(() => markdown(page)).toContain('<!--');
});

test('Markdown 구역을 접고 펼 수 있다', async ({ page }) => {
  const source = '# 제목\n\n  들여쓴 본문\n    더 들여쓴 본문\n  다음 본문\n\n# 다음';
  await openEditor(page, source);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.keyboard.press(documentStart);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press(foldKey);
  await expect(page.locator('#source .inline-folded')).toBeAttached();
  await expect(page.locator('#source .view-line').filter({ hasText: '더 들여쓴 본문' })).toHaveCount(0);
  await page.keyboard.press(unfoldKey);
  await expect(page.locator('#source .view-line').filter({ hasText: '더 들여쓴 본문' })).toHaveCount(1);
  expect(await draft(page)).toMatchObject({ markdown: source, dirty: false });
});

test('편집 중 기능을 더해도 외부 리소스를 요청하지 않고 링크를 열지 않는다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!/^(http:\/\/127\.0\.0\.1|data:|blob:)/.test(request.url())) requests.push(request.url()); });
  await start(page, '[외부](https://example.com)');
  await page.keyboard.press('F1');
  await page.keyboard.press('Escape');
  await page.locator('#source .view-line').first().click({ modifiers: ['Control'] });
  await page.waitForTimeout(200);
  expect(requests).toEqual([]);
  await expect(page.locator('#source .detected-link')).toHaveCount(0);
});

test('설정의 편집기 옵션을 열린 메모에 바로 적용하고 다음 실행에도 유지한다', async ({ page }) => {
  await openEditor(page, Array.from({ length: 80 }, (_, index) => `줄 ${index}`).join('\n'));
  expect(await minimapWidth(page)).toBe(0);
  await expect(page.locator('#source .margin-view-overlays .line-numbers').first()).toBeVisible();
  const settings = await openSettings(page);
  await settings.getByLabel('미니맵').check();
  await expect.poll(() => minimapWidth(page)).toBeGreaterThan(0);
  await settings.getByLabel('줄 번호').uncheck();
  await expect(page.locator('#source .line-numbers')).toHaveCount(0);
  await settings.getByLabel('공백 문자 표시').check();
  await settings.getByLabel('괄호 색 구분').check();
  await settings.getByLabel('구역 제목 고정').check();
  await settings.getByLabel('자동 줄바꿈').uncheck();
  expect((await draft(page)).dirty).toBe(false);
  await page.reload();
  await expect.poll(() => minimapWidth(page)).toBeGreaterThan(0);
  await expect(page.locator('#source .line-numbers')).toHaveCount(0);
  await settings.reload();
  await expect(settings.getByLabel('미니맵')).toBeChecked();
  await expect(settings.getByLabel('자동 줄바꿈')).not.toBeChecked();
});

test('손상된 편집기 옵션은 기본값으로 읽는다', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('devpostit.editor.preferences.v1', JSON.stringify({ font: 'd2', ligatures: true, size: 15, editor: { minimap: 'yes', lineNumbers: 0 } })));
  await openEditor(page, '본문');
  expect(await minimapWidth(page)).toBe(0);
  await expect(page.locator('#source .margin-view-overlays .line-numbers').first()).toBeVisible();
});

test('미리보기에서는 찾기 버튼을 비활성화한다', async ({ page }) => {
  await openEditor(page, '본문');
  await mode(page, 'preview');
  await expect(page.getByRole('toolbar', { name: 'Markdown 서식', exact: true }).getByRole('button', { name: '찾기', exact: true })).toBeDisabled();
});

test('한글 조합 중에는 툴바의 찾기를 실행하지 않는다', async ({ page }) => {
  await openEditor(page, '본문');
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 20 } });
  await page.locator('#source').dispatchEvent('compositionstart');
  await page.getByRole('toolbar', { name: 'Markdown 서식', exact: true }).getByRole('button', { name: '찾기', exact: true }).click();
  await expect(page.locator('#source .find-widget.visible')).toHaveCount(0);
  await page.locator('#source').dispatchEvent('compositionend');
});
