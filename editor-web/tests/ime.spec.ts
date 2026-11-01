import { test, expect } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

test('Chromium 한글 IME 조합 완료 후 전환해도 중복·누락이 없다', async ({ page, context }) => {
  await openEditor(page);
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
  const cdp = await context.newCDPSession(page);
  await cdp.send('Input.imeSetComposition', { text: 'ㅎ', selectionStart: 1, selectionEnd: 1 });
  await cdp.send('Input.imeSetComposition', { text: '하', selectionStart: 1, selectionEnd: 1 });
  await cdp.send('Input.imeSetComposition', { text: '한', selectionStart: 1, selectionEnd: 1 });
  await cdp.send('Input.insertText', { text: '한' });
  await expect.poll(async () => (await draft(page)).markdown).toBe('한');
  await mode(page, 'preview');
  await mode(page, 'source');
  expect((await draft(page)).markdown).toBe('한');
});

test('조합 중 API 전환 요청은 compositionend 이후에 적용한다', async ({ page }) => {
  await openEditor(page, '한글');
  await page.getByRole('textbox', { name: 'Markdown 본문' }).dispatchEvent('compositionstart', { data: 'ㅎ' });
  await page.evaluate(() => window.hunminEditor.setMode('preview'));
  await expect(page.locator('#source')).toBeVisible();
  await page.getByRole('textbox', { name: 'Markdown 본문' }).dispatchEvent('compositionend', { data: '한' });
  await expect(page.locator('#preview')).toBeVisible();
  expect((await draft(page)).markdown).toBe('한글');
});

test('조합 종료 직후 최신 보기 요청이 이전 예약 전환을 취소한다', async ({ page }) => {
  await openEditor(page, '한글');
  await page.evaluate(async () => {
    const input = document.querySelector<HTMLTextAreaElement>('#source textarea')!;
    input.dispatchEvent(new CompositionEvent('compositionstart', { bubbles: true }));
    window.hunminEditor.setMode('preview');
    input.dispatchEvent(new CompositionEvent('compositionend', { bubbles: true }));
    window.hunminEditor.setMode('source');
    await new Promise(resolve => window.setTimeout(resolve, 0));
  });
  await expect(page.locator('#source')).toBeVisible();
  await expect(page.getByRole('button', { name: '소스 편집', exact: true })).toHaveAttribute('aria-pressed', 'true');
  expect((await draft(page)).markdown).toBe('한글');
});
