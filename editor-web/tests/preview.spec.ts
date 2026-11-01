import { test, expect } from '@playwright/test';
import { openEditor, draft, mode } from './helpers';

test('위험 HTML·링크·원격 이미지를 실행하지 않고 원문을 보존한다', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', request => { if (!request.url().startsWith('http://127.0.0.1:')) requests.push(request.url()); });
  const markdown = '<script>window.attacked=true</script>\n<img src="https://example.com/track.png" onerror="window.attacked=true">\n\n[외부](https://example.com)\n![추적](https://example.com/image.png)\n[실행](javascript:alert(1))';
  await openEditor(page, markdown);
  await mode(page, 'preview');
  await expect(page.locator('#preview')).toContainText('<script>');
  expect(await page.locator('#preview script, #preview img').count()).toBe(0);
  // 웹 링크는 누를 때 앱이 확인 후 엽니다. 스크립트 주소는 링크가 되지 않습니다.
  await expect(page.locator('#preview a[href]')).toHaveCount(1);
  await expect(page.locator('#preview a[href]')).toHaveAttribute('href', 'https://example.com');
  expect(await page.evaluate(() => 'attacked' in window)).toBe(false);
  expect(requests).toEqual([]);
  expect((await draft(page)).markdown).toBe(markdown);
});
