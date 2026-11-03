import { expect, type Page } from '@playwright/test';

// Monaco의 문서 처음·끝 이동 키는 macOS에서 다릅니다.
export const documentStart = process.platform === 'darwin' ? 'Meta+ArrowUp' : 'Control+Home';
export const documentEnd = process.platform === 'darwin' ? 'Meta+ArrowDown' : 'Control+End';
export const replaceKey = process.platform === 'darwin' ? 'Alt+Meta+f' : 'Control+h';
export const foldKey = process.platform === 'darwin' ? 'Alt+Meta+BracketLeft' : 'Control+Shift+[';
export const unfoldKey = process.platform === 'darwin' ? 'Alt+Meta+BracketRight' : 'Control+Shift+]';
// 사용자 지정 스킴의 이미지는 Playwright가 가로채지 못합니다. 실제 표시는 MacSmoke가 확인합니다.
export const macTarget = process.env.EDITOR_TARGET === 'macos';
// 앱이 직접 처리하는 단축키는 브라우저의 운영체제가 아니라 빌드 대상을 따른다(Monaco는 운영체제를 따른다).
export const assetOrigin = macTarget ? 'devpostit-assets://notes' : 'https://devpostit-assets.local';
export const appPrimary = macTarget ? 'Meta' : 'Control';

export async function openEditor(page: Page, markdown = '', revision = 7) {
  await page.goto('/');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  await page.evaluate(({ markdown, revision }) => window.hunminEditor.load('note-1', markdown, revision), { markdown, revision });
}
export async function draft(page: Page) { return page.evaluate(() => window.hunminEditor.getDraft()); }
export async function mode(page: Page, value: 'source' | 'preview') {
  await page.getByRole('button', { name: value === 'source' ? '소스 편집' : '미리보기', exact: true }).click();
}
export async function openSettings(page: Page) {
  const settings = await page.context().newPage();
  await settings.goto('/settings.html');
  await expect(settings.getByRole('main', { name: '설정', exact: true })).toBeVisible();
  return settings;
}
export async function replaceText(page: Page, text: string) {
  const input = page.getByRole('textbox', { name: 'Markdown 본문' });
  await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
  await expect(input).toBeFocused();
  await page.keyboard.press(documentStart);
  await page.keyboard.press('ControlOrMeta+a');
  await page.keyboard.insertText(text);
  await expect.poll(async () => (await draft(page)).markdown).toBe(text);
}
