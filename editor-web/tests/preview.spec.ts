import { test, expect } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

test('위험 HTML·링크·원격 이미지를 실행하지 않고 원문을 보존한다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!request.url().startsWith('http://127.0.0.1:')) requests.push(request.url()); });
  const markdown = '<script>window.attacked=true</script>\n<img src="https://example.com/track.png" onerror="window.attacked=true">\n\n[외부](https://example.com)\n![추적](https://example.com/image.png)\n[실행](javascript:alert(1))';
  await openEditor(page, markdown);
  await mode(page, 'preview');
  await expect(page.locator('#preview')).toContainText('<script>');
  expect(await page.locator('#preview script, #preview img, #preview a[href]').count()).toBe(0);
  expect(await page.evaluate(() => 'attacked' in window)).toBe(false);
  expect(requests).toEqual([]);
  expect((await draft(page)).markdown).toBe(markdown);
});
