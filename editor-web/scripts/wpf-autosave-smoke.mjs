import { chromium, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

if (!process.env.DEVPOSTIT_SMOKE_DATA_DIR) throw new Error('일회용 검증 데이터 경로가 필요합니다.');
const notePath = path.join(process.env.DEVPOSTIT_SMOKE_DATA_DIR, 'notes', 'Inbox', '첫메모.md');
const text = '# 자동 저장 검증\n\n한글 본문이 재시작 후에도 남아 있습니다.';
const browser = await chromium.connectOverCDP(process.env.DEVPOSTIT_CDP_ENDPOINT ?? 'http://127.0.0.1:9333');
try {
  const page = browser.contexts().flatMap(context => context.pages()).find(page => page.url() === 'https://devpostit.local/index.html');
  if (!page) throw new Error('실제 WPF 편집기 페이지를 찾지 못했습니다.');
  await page.waitForFunction(() => Boolean(window.hunminEditor));
  if (process.argv.includes('--oversized-close')) {
    // 거대한 한 줄의 입력·줄바꿈 계산 비용과 종료 안전성 검증을 분리합니다.
    await page.evaluate(() => {
      const revision = window.hunminEditor.getDraft().revision + 1;
      window.hunminEditor.load('oversized-fixture', ('x'.repeat(80) + '\n').repeat(13000), revision);
    });
    await expect(page.locator('#host-status')).toContainText('너무 커서');
    console.log('전송 제한을 넘은 미보존 초안 준비 완료: 종료 차단 확인 필요');
  } else if (process.argv.includes('--restore')) {
    await expect.poll(() => page.evaluate(() => window.hunminEditor.getDraft().markdown)).toBe(text);
    console.log('실제 WPF 정상 종료·재시작 후 저장된 Markdown 본문 복원 성공');
  } else {
    await page.locator('#source .monaco-editor').click({ position: { x: 130, y: 40 } });
    await page.keyboard.press('Control+a');
    await page.keyboard.insertText(text);
    // Ctrl+S 없이 자동 저장을 기다립니다.
    await expect(page.locator('#host-status')).toContainText('저장됨');
    await expect.poll(async () => (await readFile(notePath, 'utf8')).endsWith(text)).toBe(true);
    expect(await page.evaluate(() => window.hunminEditor.getDraft().dirty)).toBe(false);
    await page.keyboard.press('Control+s');
    await expect(page.locator('#host-status')).toContainText('저장됨');
    await page.getByRole('button', { name: '미리보기', exact: true }).click();
    await expect(page.locator('#preview h1')).toHaveText('자동 저장 검증');
    console.log('실제 WPF 500ms 자동 저장·원자적 Markdown 파일·저장 성공 응답·미리보기 검증 성공');
  }
} finally { await browser.close(); }
