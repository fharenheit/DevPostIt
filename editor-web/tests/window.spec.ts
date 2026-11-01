import { test, expect } from '@playwright/test';
import { openEditor } from './helpers';

test('메모 화면에는 제품명과 생성·이름 변경·글꼴 버튼을 표시하지 않는다', async ({ page }) => {
  await openEditor(page);
  await expect(page.locator('body')).not.toContainText('DevPostIt');
  await expect(page.getByRole('button', { name: '새 메모', exact: true })).toHaveCount(0);
  await expect(page.getByRole('button', { name: '이름 변경', exact: true })).toHaveCount(0);
  await expect(page.getByLabel('편집 글꼴')).not.toBeVisible();
  await expect(page.getByRole('button', { name: '글자 크게', exact: true })).not.toBeVisible();
  await expect(page.locator('#settings, dialog[aria-label="설정"]')).toHaveCount(0);
  await page.locator('h1').dblclick();
  await expect(page.getByLabel('메모 이름', { exact: true })).toBeVisible();
  await page.getByLabel('메모 이름', { exact: true }).fill('두 번 눌러 변경');
  await page.getByRole('button', { name: '확인', exact: true }).click();
  await expect(page.locator('h1')).toHaveText('두 번 눌러 변경');
  await expect(page).toHaveTitle('두 번 눌러 변경');
});
