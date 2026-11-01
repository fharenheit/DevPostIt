import { test, expect } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

async function sizes(page: import('@playwright/test').Page) {
  return page.evaluate(() => Object.fromEntries(['.brand strong', '#source-mode', '#font', '.document-heading h1', '#status', '.monaco-editor .view-lines', '#preview', '#preview h1', '#preview code'].map(selector => [selector, parseFloat(getComputedStyle(document.querySelector(selector)!).fontSize)])));
}

test('DevPostIt 이름과 작은 기본 크기를 표시하고 모든 글자를 함께 조절한다', async ({ page }) => {
  await openEditor(page, '# 제목\n\n```ts\nlet a = 1;\n```');
  await expect(page).toHaveTitle('DevPostIt');
  await expect(page.locator('.brand strong')).toHaveText('DevPostIt');
  await expect(page.getByLabel('기준 글자 크기')).toHaveText('15');
  await expect(page.locator('.monaco-editor .view-lines')).toHaveCSS('font-size', '13px');
  await expect(page.locator('#source-mode')).toHaveCSS('font-size', '15px');
  await expect(page.locator('#font')).toHaveCSS('font-size', '15px');
  await mode(page, 'preview');
  const before = await sizes(page);
  await page.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect(page.getByLabel('기준 글자 크기')).toHaveText('16');
  const after = await sizes(page);
  for (const selector of Object.keys(before)) expect(after[selector] / before[selector]).toBeCloseTo(selector.includes('view-lines') ? 14 / 13 : 16 / 15, 2);
  await page.getByRole('button', { name: '글자 작게', exact: true }).click();
  await mode(page, 'source');
  expect((await draft(page)).dirty).toBe(false);
  expect((await draft(page)).revision).toBe(7);
});

test('크기·글꼴·합자를 저장하고 새 브라우저 세션에서도 복원한다', async ({ page, browser }) => {
  await openEditor(page);
  await page.getByRole('button', { name: '글자 크게', exact: true }).click();
  await page.getByRole('button', { name: '글자 크게', exact: true }).click();
  await page.getByLabel('편집 글꼴').selectOption('jetbrains');
  await page.getByLabel('합자 사용').uncheck();
  await page.reload();
  await expect(page.getByLabel('기준 글자 크기')).toHaveText('17');
  const storageState = await page.context().storageState();
  const reopened = await browser.newContext({ storageState });
  const second = await reopened.newPage();
  await second.goto(page.url());
  await expect(second.getByLabel('기준 글자 크기')).toHaveText('17');
  await expect(second.getByLabel('편집 글꼴')).toHaveValue('jetbrains');
  await expect(second.getByLabel('합자 사용')).not.toBeChecked();
  await reopened.close();
});

test('크기를 10부터 24까지 제한하고 손상 설정은 기본값으로 복구한다', async ({ page }) => {
  await openEditor(page);
  for (let i = 0; i < 5; i++) await page.getByRole('button', { name: '글자 작게', exact: true }).click();
  await expect(page.getByRole('button', { name: '글자 작게', exact: true })).toBeDisabled();
  for (let i = 0; i < 14; i++) await page.getByRole('button', { name: '글자 크게', exact: true }).click();
  await expect(page.getByRole('button', { name: '글자 크게', exact: true })).toBeDisabled();
  await page.evaluate(() => localStorage.setItem('devpostit.editor.preferences.v1', '{"font":"d2","ligatures":true,"size":999}'));
  await page.reload();
  await expect(page.getByLabel('기준 글자 크기')).toHaveText('15');
});
