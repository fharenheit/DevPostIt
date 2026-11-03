import { expect, test } from '@playwright/test';
import { isPrimary, noteAssetUrl } from '../src/platform';

test('Windows는 Ctrl, macOS는 ⌘을 기본 수정 키로 쓴다', () => {
  expect(isPrimary({ ctrlKey: true, metaKey: false }, 'windows')).toBe(true);
  expect(isPrimary({ ctrlKey: false, metaKey: true }, 'windows')).toBe(false);
  expect(isPrimary({ ctrlKey: false, metaKey: true }, 'macos')).toBe(true);
  expect(isPrimary({ ctrlKey: true, metaKey: false }, 'macos')).toBe(false);
});

test('이미지 주소는 빌드 대상의 자산 주소를 쓴다', () => {
  expect(noteAssetUrl('a/b.png', 'windows')).toBe('https://devpostit-assets.local/a/b.png');
  expect(noteAssetUrl('a/b.png', 'macos')).toBe('devpostit-assets://notes/a/b.png');
});
