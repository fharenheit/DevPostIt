import { test, expect } from '@playwright/test';
import { openEditor, draft, openSettings } from './helpers';

test('먼저 요청한 글꼴이 늦게 로딩되어도 최신 선택을 덮어쓰지 않는다', async ({ page }) => {
  let release!: () => void;
  const fontReleased = new Promise<void>(resolve => { release = resolve; });
  await page.route('**/D2Codingligature-Regular.ttf', async route => { await fontReleased; await route.continue(); });
  await page.goto('/', { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  const settings = await openSettings(page);
  await settings.getByLabel('편집 글꼴').selectOption('jetbrains');
  await settings.getByLabel('합자 사용').uncheck();
  release();
  await page.evaluate(() => document.fonts.ready);
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-family', /^"JetBrains Mono"/);
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-feature-settings', /"liga" 0/);
});

test('오프라인 글꼴 두 개가 실제 로딩되고 선택·합자 설정이 적용된다', async ({ page }) => {
  await openEditor(page, '한글 => != ===');
  await page.evaluate(async () => {
    await document.fonts.load('16px "D2 Ligature"');
    await document.fonts.load('16px "JetBrains Mono"');
    await document.fonts.ready;
  });
  const fonts = await page.evaluate(() => [...document.fonts].map(font => ({ family: font.family, status: font.status })));
  expect(fonts).toEqual(expect.arrayContaining([
    expect.objectContaining({ family: 'D2 Ligature', status: 'loaded' }),
    expect.objectContaining({ family: 'JetBrains Mono', status: 'loaded' }),
  ]));
  const settings = await openSettings(page);
  await expect(settings.getByLabel('편집 글꼴')).toHaveValue('d2');
  await settings.getByLabel('편집 글꼴').selectOption('jetbrains');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-family', /^"JetBrains Mono"/);
  await expect(settings.getByLabel('합자 사용')).toBeChecked();
  await settings.getByLabel('합자 사용').uncheck();
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-feature-settings', /"liga" 0/);
  expect((await draft(page)).dirty).toBe(false);
  await settings.reload();
  await expect(settings.getByLabel('편집 글꼴')).toHaveValue('jetbrains');
  await expect(settings.getByLabel('합자 사용')).not.toBeChecked();
});
