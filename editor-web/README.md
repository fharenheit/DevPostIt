# DevPostIt 편집기

메모 창 안에서 동작하는 웹 화면입니다. Monaco Markdown 소스 편집, 읽기 전용 미리보기, Markdown 서식 툴바, 코드 정리와 별도 설정 페이지(`settings.html`)를 제공합니다. D2 Ligature·JetBrains Mono를 함께 제공하며 합자는 기본 활성화입니다.

앱을 빌드하면 이 화면도 함께 빌드되므로 앱만 실행할 때는 아래 절차가 필요 없습니다. 브라우저에서 단독으로 실행하면 글꼴 설정만 브라우저에 저장됩니다. 메모 저장, 트레이, 삭제와 데이터 폴더 열기는 앱에서만 동작합니다.

## 브라우저에서 실행

Node.js 24.22.1, npm 11.19.1을 사용합니다. 저장소 루트의 PowerShell에서 실행합니다. 첫 줄은 저장소의 `.tools`에 Node를 설치한 환경에서만 필요하며, 다른 컴퓨터에서는 같은 버전의 시스템 도구를 사용합니다.

```powershell
$env:PATH = "$PWD\.tools\node\node-v24.22.1-win-x64;$env:PATH"
Push-Location editor-web
npm.cmd ci
npm.cmd run dev
```

표시된 로컬 주소를 브라우저에서 엽니다. 설정 페이지는 같은 주소의 `/settings.html`입니다. Ctrl+C로 서버를 끈 뒤 `Pop-Location`으로 저장소 루트에 돌아옵니다.

## 테스트

`editor-web`에서 실행합니다. 개발 서버와 제작 빌드를 모두 검사합니다. 이 저장소의 `.tools/playwright`에 브라우저가 있으면 `PLAYWRIGHT_BROWSERS_PATH`를 그 경로로 지정하고, 없으면 먼저 설치합니다.

```powershell
npx.cmd playwright install chromium
$env:EDITOR_PRODUCTION = $null
npm.cmd test
npm.cmd run build
$env:EDITOR_PRODUCTION = '1'
npm.cmd test
```

## 호스트 계약

`window.hunminEditor`는 앱과 테스트가 쓰는 다음 계약을 제공합니다.

- `load(noteId, markdown, revision, title?)`: 문서와 원문 기준을 교체합니다. 이전 Undo 기록은 제거됩니다.
- `setMode('source' | 'preview')`: 조합 중이면 완료까지 전환을 지연합니다.
- `getDraft()`: `{markdown, revision, dirty}` 복사본을 반환합니다.
- `getTitle()`, `rename(title)`: 메모 이름을 읽고 바꿉니다.

## 원문 보존

원문은 열기·보기 전환만으로 수정되지 않습니다. 실제 편집 후에는 최초 줄바꿈 형식을 사용하고, 줄바꿈이 없는 새 문서는 LF를 사용합니다. Undo로 최초 모델 값에 복귀하면 BOM·혼합 줄바꿈을 포함한 원문을 반환합니다. 미지원 확장 구문은 소스에 유지하며, HTML은 미리보기에서 문자로 표시합니다. 미리보기 링크는 탐색하지 않으며 이미지는 요청하지 않습니다.

폰트 원본 및 라이선스는 `public/fonts/`, 런타임 라이브러리 라이선스는 `public/licenses/`에 있습니다.
