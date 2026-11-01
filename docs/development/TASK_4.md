# DevPostIt 표시 설정 및 작업 4 검증 기록

기준 커밋: `c754d12`, 브랜치: `codex/task-4-devpostit-host`.
사용자 요청으로 이름 변경과 크기 조절을 먼저 구현한 뒤 작업 4를 진행했습니다. 후속 지시에 따라 기본 UI 15px, 편집기 13px로 조정했습니다.

## 동작과 판단

- DevPostIt 창·웹 제목과 제품 메타데이터, 실행 파일명을 적용했습니다. 기존 내부 프로젝트·네임스페이스·계약명은 호환을 위해 유지합니다.
- `+`·`−`는 UI 기준 크기를 1px씩 바꾸고 편집기는 항상 2px 작게 유지합니다. UI 텍스트는 rem으로 크기를 공유하며 제목·보조 텍스트의 비율은 보존합니다. Windows 제목 표시줄은 OS가 관리합니다.
- 크기 범위는 UI 10~24px입니다. 손상된 설정은 기본값으로 복원하며 기존 글꼴 설정 키도 읽어 이관합니다. 저장 설정은 기본값보다 우선합니다.
- 프로필은 `%LOCALAPPDATA%/DevPostIt/WebView2`, 출처는 `https://devpostit.local/index.html`로 고정합니다. 폰트·합자·크기는 재시작 후 복원합니다.
- WebView2 SDK [1.0.4314.35](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.4314.35)를 고정했습니다. [가상 호스트 매핑](https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.corewebview2.setvirtualhostnametofoldermapping)으로 제작 빌드만 열며 외부 탐색·프레임·새 창·다운로드·권한과 외부 HTTP 요청을 차단합니다.
- C# 헤드리스 브리지는 JSON 필드·중복 키·버전·출처·ID·리비전·1MiB 크기를 검증합니다. JS 정밀도를 벗어나는 리비전도 거부합니다. 유효한 변경은 `NoteChange`로 변환합니다.
- 웹 브리지는 조합 중간값을 보내지 않고, 같은 메모의 미저장 본문을 호스트 로드로 덮지 않습니다. 저장 성공은 호스트의 정확한 리비전 응답에서만 인정합니다.
- 저장소 연결은 작업 6 이후입니다. WPF는 저장 요청에 `note.conflict` 안내를 돌려주며 저장 성공을 가장하지 않습니다.
- 사용자가 수정한 `editor-web/README.md`의 비어 있는 실행 명령은 보존하고 커밋에서 제외했습니다. 최신 WPF 실행법은 루트 README에 기록했습니다.

## TDD 및 실행 검증

- 크기 테스트의 이름·기본값 불일치 실패를 확인한 뒤 구현했습니다. 새 기본 15/13 지시도 기대값을 먼저 바꿔 실패를 확인했습니다.
- 네이티브 테스트 27개가 미구현 상태에서 전부 실패한 후 구현해 통과했습니다. JS 안전 정수를 넘는 리비전을 추가해 실패 확인 후 제한했습니다.
- 웹 브리지의 ready 미전송 실패를 확인한 뒤 양방향 연결을 구현했습니다.
- 최초 WPF 실행에서 웹 파일 복사가 누락된 것을 실제 창에서 확인했습니다. MSBuild 자산 복사를 명시적 Copy/Publish 대상으로 바꾸고 새 출력 폴더에서 실행해 해결을 확인했습니다.
- 기본 Windows UI 입력 자동화가 사용자 입력 감지를 반복해, [Playwright 공식 WebView2 CDP 방식](https://playwright.dev/docs/webview2)으로 독립 프로필의 실제 WPF 컨트롤을 검증했습니다. 일반 앱은 디버깅 포트를 열지 않으며 테스트 프로세스 환경 변수에만 지정했습니다.
- 실제 WPF에서 기본 15/13, 한글 본문 입력, Ctrl+S의 JS→C# 검증→JS 응답, Markdown 미리보기, `+`로 16/14 변경을 확인했습니다.
- JetBrains Mono·합자 해제·크기16을 선택한 뒤 정상 창 종료와 프로세스 재시작을 수행했습니다. 크기16/본문14·글꼴·합자 설정 복원을 확인했습니다.
- 최종 .NET 테스트 62개(Core 3, Storage 31, Bridge 28)와 제작 빌드 웹 테스트 19개가 통과했습니다. 패키지 잠금 복원과 npm audit 0건을 확인했습니다.
- WPF 빌드 성공, C# 경고·오류 0. Monaco 청크 크기의 Vite 경고는 기존과 같이 남아 있습니다. Debug 구성의 로컬 publish에서도 편집기·글꼴 복사를 확인했습니다. 처음 publish 검증 명령에서 Debug 빌드와 기본 Release 구성이 달라 실패한 것은 `-c Debug`를 명시해 해결했습니다.
- 별도 검토 에이전트가 출처·JSON·IME·원문·설정·빌드 복사를 확인했으며 작업 4 범위의 주요 결함을 발견하지 못했습니다.
- 자동화 스크립트: `editor-web/scripts/wpf-smoke.mjs`, 재시작 확인은 `--restore`. `WEBVIEW2_USER_DATA_FOLDER`로 별도 프로필, `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9333`로 테스트 프로세스에만 CDP를 활성화합니다.

실제 OS 한글 IME 후보 선택·장시간 조합은 수동 검증하지 않았습니다. Chromium CDP 조합과 WebView2의 한글 텍스트 입력 결과와 구분합니다. 작업 5는 시작하지 않았습니다.
