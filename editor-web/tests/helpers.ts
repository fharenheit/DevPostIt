import { expect, type Page } from '@playwright/test';

export async function openEditor(page: Page, markdown = '', revision = 7) {
  await page.goto('/');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  await page.evaluate(({ markdown, revision }) => window.hunminEditor.load('note-1', markdown, revision), { markdown, revision });
}
export async function draft(page: Page) { return page.evaluate(() => window.hunminEditor.getDraft()); }
export async function mode(page: Page, value: 'source' | 'preview') {
  await page.getByRole('button', { name: value === 'source' ? '소스 편집' : '미리보기', exact: true }).click();
}
export async function replaceText(page: Page, text: string) {
  const input = page.getByRole('textbox', { name: 'Markdown 본문' });
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
  await expect(input).toBeFocused();
  await page.keyboard.press('Control+Home');
  await page.keyboard.press('Control+a');
  await page.keyboard.insertText(text);
  await expect.poll(async () => (await draft(page)).markdown).toBe(text);
}
