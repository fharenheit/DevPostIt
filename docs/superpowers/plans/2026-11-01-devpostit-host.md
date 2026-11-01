# DevPostIt 표시 설정 및 작업 4 실행 계획

사용자 요청: DevPostIt으로 표시 이름 변경, 전체 글자 크기 +/- 조절 및 영구 기억, WPF에서 Monaco 실행, 테스트 후 커밋.
기준: `c754d12`. 실행은 `superpowers:executing-plans`와 TDD를 따릅니다.

## 설계

- 제품 표시 이름·창 제목·실행 파일명을 DevPostIt으로 변경합니다. 기존 내부 프로젝트 계약은 유지합니다.
- 글자 크기는 후속 사용자 지시에 따라 UI 기본 15px, 편집기 기본 13px입니다. 한 단계 1px, UI 기준 10~24px이며 편집기는 UI보다 2px 작게 유지합니다. 모든 웹 UI 글꼴은 rem 기준으로 바꾸고 Monaco의 글자·줄 높이를 함께 변경합니다. Windows가 그리는 제목 표시줄은 OS 설정을 따릅니다.
- 글꼴·합자·크기를 localStorage에 저장하고 기존 글꼴 설정은 이관합니다. WebView2는 `%LOCALAPPDATA%/DevPostIt/WebView2`의 안정된 사용자 프로필과 `https://devpostit.local/index.html` 출처를 사용합니다.
- WebView2는 제작 빌드 `editor-web/dist`만 로컬 가상 호스트로 로드합니다. npm 개발 서버나 외부 CDN이 없어도 실행됩니다.
- 프로토콜 v1: `editor.ready`, `note.load`, `note.changed`, `note.saveRequested`, `note.saved`, `note.conflict`. C# 브리지는 버전·타입·ID·리비전·출처·최대 1MiB JSON을 검증하고 typed NoteChange를 반환합니다. 임의 JSON 필드와 중복 키도 거부합니다.
- 편집기 준비 후 WPF가 초기 문서를 전달합니다. 본문 변경은 네이티브 브리지를 통과하며, 저장 요청은 아직 파일 저장으로 연결하지 않고 저장 기능 연결 전임을 표시합니다. 실제 저장·저널은 기존 계획의 작업 6입니다.
- 외부/새 창/프레임 탐색, 다운로드·권한·웹의 호스트 객체 접근은 차단합니다. 메시지는 신뢰된 페이지 출처에서만 받습니다.
- UI 조절 설정은 본문 dirty/revision을 변경하지 않습니다. 호스트 부재 시 브라우저 데모도 유지합니다.

## 1. 표시 설정

- [x] Playwright 테스트: 이름, 기본14, +/- 전체 글꼴 비율, 본문 무변경, 보기 전환·reload·별도 브라우저 세션 설정 유지, 범위 제한·손상 설정 복구.
- [x] 미구현 실패 확인 → 크기 설정과 전체 rem 스타일 구현 → 웹 전체 테스트·제작 빌드 → 커밋.

## 2. 작업 4 WebView2 연결

파일: `src/HunminNotes.EditorHost/{EditorMessage,EditorBridge,EditorOrigin}.cs`, `tests/HunminNotes.App.Tests/EditorBridgeTests.cs`, `editor-web/src/bridge.ts`, WPF MainWindow 및 프로젝트 빌드 설정.

- [x] C# 계약 테스트부터 작성하고 미구현 실패를 확인합니다. 정상 메시지 typed 변경, 오염된 필드·버전·ID·리비전·출처·크기·타입·탐색 거부를 검증합니다.
- [x] 브라우저 호스트 모사 테스트부터 작성하고 실패를 확인합니다. ready→load, changed, 저장 요청/응답, 잘못된 호스트 메시지 거부, 조합 중 이벤트 억제를 검증합니다.
- [x] 네이티브 브리지·웹 브리지와 WPF 호스트 구현. 빌드 시 웹 제작 산출물을 앱 출력·publish에 복사하며 필요한 경우 npm으로 생성합니다.
- [x] 전체 .NET·개발/제작 웹 테스트, 실제 WPF 실행·입력·미리보기·전체 크기 조절·종료/재시작 설정 복원을 검증합니다.
- [x] 별도 검토와 회귀 테스트 후 `feat: connect DevPostIt WPF to Monaco with validated messages` 커밋. 작업 5는 시작하지 않습니다.

## 검토 중점

설정 변경 시 본문 손상, 재시작 후 설정 유실, IME 중간 문자열 전송, 외부 페이지의 위조 메시지, 실제 WPF 자산 누락·실행 실패를 검토합니다.

## 시작 상태

`editor-web/README.md`에는 사용자 편집으로 실행 명령 일부가 비어 있습니다. 해당 변경은 보존하며 이번 커밋에 포함하지 않습니다.
