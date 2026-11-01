import { chromium, expect } from '@playwright/test';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

const root = process.env.DEVPOSTIT_SMOKE_DATA_DIR;
if (!root) throw new Error('별도 검증 데이터 폴더가 필요합니다.');
const endpoint = process.env.DEVPOSTIT_SMOKE_ENDPOINT ?? 'http://127.0.0.1:9337';
await expect.poll(async () => { try { return (await fetch(`${endpoint}/json/version`)).ok; } catch { return false; } }, { timeout: 30000 }).toBe(true);
const browser = await chromium.connectOverCDP(endpoint);
const pages = () => browser.contexts().flatMap(context => context.pages()).filter(page => page.url() === 'https://devpostit.local/index.html');
async function rename(page, title) {
  await page.locator('.document-heading h1').dblclick();
  await page.getByLabel('메모 이름', { exact: true }).fill(title);
  await page.getByRole('button', { name: '확인', exact: true }).click();
  await expect(page.locator('.document-heading h1')).toHaveText(title);
}
async function replace(page, text) {
  await page.getByRole('button', { name: '소스 편집', exact: true }).click();
  await page.locator('#source .monaco-editor').click({ position: { x: 100, y: 40 } });
  await page.keyboard.press('Control+a');
  await page.keyboard.insertText(text);
}
try {
  await expect.poll(() => pages().length).toBeGreaterThan(0);
  const first = pages()[0];
  await expect(first.locator('#host-status')).toContainText(/앱 연결됨|저장됨/);
  if (process.argv.includes('--restore')) {
    await expect(first.locator('.document-heading h1')).toHaveText(/검증/);
    console.log('재시작 후 제목 및 메모 목록 파일 보존 확인');
  } else {
    await rename(first, '트레이 검증 첫 메모');
    await replace(first, '```java\nclass Main{public static void main(String[] args){System.out.println("한글");}}\n```');
    await first.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(first.locator('#format-status')).toContainText('정리했습니다');
    await expect(first.locator('#host-status')).toContainText('저장됨');
    const currentFiles = await readdir(path.join(root, 'notes', 'Inbox'));
    const currentDocuments = await Promise.all(currentFiles.filter(file => file.endsWith('.md')).map(file => readFile(path.join(root, 'notes', 'Inbox', file), 'utf8')));
    const firstId = /^id: ["']?([0-9a-f-]{36})/im.exec(currentDocuments.find(text => text.includes('트레이 검증 첫 메모')))?.[1];
    if (!firstId) throw new Error('검증 메모 ID가 없습니다.');
    // 생성 버튼을 표시하지 않고 실제 호스트 생성 프로토콜을 검증합니다. 트레이 메뉴는 WPF 하네스에서 검증합니다.
    await first.evaluate(noteId => window.chrome.webview.postMessage({ protocolVersion: 1, type: 'note.newRequested', requestId: crypto.randomUUID(), noteId, revision: window.hunminEditor.getDraft().revision, payload: {} }), firstId);
    await expect.poll(() => pages().length).toBe(2);
    const second = pages().find(page => page !== first);
    await rename(second, '트레이 검증 둘째 메모');
    await replace(second, '```json\n{"a":1,"한글":true}\n```');
    await second.getByRole('button', { name: '코드 정리', exact: true }).click();
    await expect(second.locator('#format-status')).toContainText('정리했습니다');
    await expect(second.locator('#host-status')).toContainText('저장됨');
    const files = await readdir(path.join(root, 'notes', 'Inbox'));
    const documents = await Promise.all(files.filter(file => file.endsWith('.md')).map(file => readFile(path.join(root, 'notes', 'Inbox', file), 'utf8')));
    expect(documents).toHaveLength(2);
    expect(documents.some(text => text.includes('트레이 검증 첫 메모') && text.includes('class Main {'))).toBe(true);
    expect(documents.some(text => text.includes('트레이 검증 둘째 메모') && text.includes('{ "a": 1, "한글": true }'))).toBe(true);
    console.log('실제 WPF: Java·JSON 정리, 두 메모 독립 저장 및 제목 메타데이터 확인');
  }
} finally { await browser.close(); }
