import { test, expect } from '@playwright/test';
import { openEditor, draft, mode, openSettings } from './helpers';

async function sizes(page: import('@playwright/test').Page) {
  return page.evaluate(() => Object.fromEntries(['#source-mode', '.document-heading h1', '#status', '.monaco-editor .view-lines', '#preview', '#preview h1', '#preview code'].map(selector => [selector, parseFloat(getComputedStyle(document.querySelector(selector)!).fontSize)])));
}

test('제품 표시 없이 작은 기본 크기를 유지하고 설정 창에서 전체 크기를 조절한다', async ({ page }) => {
  await openEditor(page, '# 제목\n\n```ts\nlet a = 1;\n```');
  await expect(page).toHaveTitle('나의 첫 메모');
  await expect(page.locator('.brand')).toHaveCount(0);
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  await expect(page.locator('#source-mode')).toHaveCSS('font-size', '13.125px');
  await mode(page, 'preview');
  const before = await sizes(page);
  const settings = await openSettings(page);
  await expect(settings.getByLabel('기준 글자 크기')).toHaveText('15');
  await expect(settings.locator('#font')).toHaveCSS('font-size', '14px');
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect(settings.getByLabel('기준 글자 크기')).toHaveText('16');
  await expect(settings.locator('#font')).toHaveCSS('font-size', '14px');
  await expect.poll(() => page.evaluate(() => document.documentElement.style.fontSize)).toBe('16px');
  const after = await sizes(page);
  for (const selector of Object.keys(before)) expect(after[selector] / before[selector]).toBeCloseTo(selector.includes('view-lines') || selector.startsWith('#preview') ? 14 / 13 : 16 / 15, 2);
  await settings.getByRole('button', { name: '글자 작게', exact: true }).click();
  await mode(page, 'source');
  expect((await draft(page)).dirty).toBe(false);
  expect((await draft(page)).revision).toBe(7);
});

test('크기·글꼴·합자를 저장하고 새 브라우저 세션에서도 복원한다', async ({ page, browser }) => {
  await openEditor(page);
  const settings = await openSettings(page);
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await settings.getByLabel('편집 글꼴').selectOption('jetbrains');
  await settings.getByLabel('합자 사용').uncheck();
  await settings.reload();
  await expect(settings.getByLabel('기준 글자 크기')).toHaveText('17');
  await page.reload();
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '15px');
  const storageState = await page.context().storageState();
  const reopened = await browser.newContext({ storageState });
  const second = await reopened.newPage();
  await second.goto(settings.url());
  await expect(second.getByLabel('기준 글자 크기')).toHaveText('17');
  await expect(second.getByLabel('편집 글꼴')).toHaveValue('jetbrains');
  await expect(second.getByLabel('합자 사용')).not.toBeChecked();
  await reopened.close();
});

test('크기를 10부터 24까지 제한하고 손상 설정은 기본값으로 복구한다', async ({ page }) => {
  await openEditor(page);
  const settings = await openSettings(page);
  for (let i = 0; i < 5; i++) await settings.getByRole('button', { name: '글자 작게', exact: true }).click();
  await expect(settings.getByRole('button', { name: '글자 작게', exact: true })).toBeDisabled();
  for (let i = 0; i < 14; i++) await settings.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect(settings.getByRole('button', { name: '글자 크게', exact: true })).toBeDisabled();
  await settings.evaluate(() => localStorage.setItem('devpostit.editor.preferences.v1', '{"font":"d2","ligatures":true,"size":999}'));
  await settings.reload();
  await expect(settings.getByLabel('기준 글자 크기')).toHaveText('15');
});
