# 훈민 노트 편집기

Monaco Markdown 소스 편집과 읽기 전용 미리보기입니다. D2 Ligature·JetBrains Mono를 함께 제공하며 합자는 기본 활성화입니다. 글꼴 설정만 브라우저에 보관하고 메모 본문은 아직 파일에 저장하지 않습니다.

Node.js 24.22.1, npm 11.19.1을 사용합니다. 저장소 루트의 PowerShell에서:

```powershell
$env:PATH = "$PWD\.tools\node\node-v24.22.1-win-x64;$env:PATH"
cd editor-web
npm.cmd ci
npm.cmd run dev
```

표시된 로컬 주소를 브라우저에서 열면 됩니다. WPF에서 편집기를 여는 기능은 작업 4에서 연결합니다.

```powershell
npx.cmd playwright install chromium
npm.cmd test
npm.cmd run build
$env:EDITOR_PRODUCTION = '1'
npm.cmd test
```

이 개발 환경의 Playwright 브라우저는 저장소의 `.tools/playwright`에 설치했습니다. 이를 사용하려면 `PLAYWRIGHT_BROWSERS_PATH`를 해당 절대 경로로 설정합니다. `EDITOR_PRODUCTION`을 해제하면 개발 서버로 테스트합니다.

`window.hunminEditor`는 다음 호스트 계약을 제공합니다.

- `load(noteId, markdown, revision)`: 문서와 원문 기준을 교체합니다. 이전 Undo 기록은 제거됩니다.
- `setMode('source' | 'preview')`: 조합 중이면 완료까지 전환을 지연합니다.
- `getDraft()`: `{markdown, revision, dirty}` 복사본을 반환합니다.

원문은 열기·보기 전환만으로 수정되지 않습니다. 실제 편집 후에는 최초 줄바꿈 형식을 사용하고, 줄바꿈이 없는 새 문서는 LF를 사용합니다. Undo로 최초 모델 값에 복귀하면 BOM·혼합 줄바꿈을 포함한 원문을 반환합니다. 미지원 확장 구문은 소스에 유지하며, HTML은 미리보기에서 문자로 표시합니다. 미리보기 링크는 탐색하지 않으며 이미지는 요청하지 않습니다.

폰트 원본 및 라이선스는 `public/fonts/`, 런타임 라이브러리 라이선스는 `public/licenses/`에 있습니다.
