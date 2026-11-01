# 코드 정리·이름·트레이 검증

아래는 첫 구현의 기록입니다. 후속 사용자 지시로 제목 더블클릭·최소화·X·화면 제어가 변경되었습니다. 최신 동작과 검증은 [메모 창 변경 검증](NOTE_WINDOW_REFINEMENTS.md)을 참고하세요.

2026-11-01 승인된 추가 기능의 구현 기록입니다. 원본 실행 파일 관찰 결과와 사용자 설명을 혼동하지 않으며, 접기 방식은 사용자가 선택한 작은 포스트잇 아이콘 복원입니다.

## 결과

- Prettier 3.9.10, Java 플러그인 2.11.0, web-tree-sitter 0.27.0을 고정하고 파서·WASM을 로컬 번들링했습니다. JSON·YAML·Java·JavaScript·TypeScript·HTML·CSS, 일반 별칭, 명시적 언어 선택을 지원합니다.
- 현재 최상위 블록의 코드만 범위 편집합니다. 닫힘·문법·언어 오류는 원문을 유지하고 안내하며, 정리 중 모델 변경·메모 전환 시 결과를 폐기합니다. 원문 리비전별 문자열을 보존해 혼합 줄바꿈도 정리·Undo에서 유지합니다.
- 메모 이름은 본문과 같은 리비전으로 저널·파일·인덱스에 저장합니다. 이름 변경 확인·취소, 앞뒤 공백·길이·제어문자 검증, 종료 직전 제목 수집을 포함합니다.
- 앱 소유 `NoteWorkspace`가 저장 서비스·저널·데이터 임대를 공유합니다. 실제 파일 IO와 임대 책임이 있어 Storage에 배치했습니다. 개별 창을 닫아도 다른 메모나 트레이의 수명은 종료하지 않습니다.
- 트레이의 왼쪽 클릭과 새 메모 메뉴는 생성 동작을 사용합니다. 우클릭은 제목 목록·설정·전체 표시/접기·종료를 제공합니다. 메모별 창은 하나이며 제목 더블클릭과 작은 아이콘의 클릭·드래그를 연결했습니다. 작은 창은 기본 화면의 작업 영역 안에 둡니다.
- 설정 대화상자는 기존 글꼴·합자·크기 제어를 사용하며 로컬 저장소 변경 이벤트로 열린 다른 메모에도 반영합니다.

## 실패부터 확인한 검증

- 코드 정리 버튼 부재, 미지원 언어·중첩 블록 안내, Java의 WASM 보안 정책 오류를 확인한 뒤 구현했습니다. Java에는 WASM 컴파일만 허용하며 임의 JavaScript 평가와 외부 리소스를 허용하지 않습니다.
- 제목 API 부재와 웹 제목 로드 실패를 확인한 뒤 같은 리비전의 제목·본문 저장을 구현했습니다.
- 공유 저장 세션·열거 API 부재에서 시작해 두 메모의 독립 저장·재시작·복구·배타적 임대를 실제 저장소로 검증했습니다.
- 접기 요청·설정 대화상자 부재를 확인한 뒤 브리지 및 여러 창의 설정 동기화를 검증했습니다.

별도 전체 검토의 세 지적은 모두 수정했습니다.

1. 생성과 종료 경합: `OperationLifetimeTests`에서 진행 작업이 끝나기 전에 종료 장벽이 완료되지 않음, 이후 작업 거부, 취소 시 재개를 검증했습니다. WPF 하네스는 지연 생성 중 실제 종료 메뉴를 실행하고 임대가 유지되는지 확인합니다. 종료 중 열기와 종료된 저장소 재열기도 차단합니다.
2. 혼합 줄바꿈: `혼합 줄바꿈의 앞뒤 원문을 정리와 실행 취소에서 정확히 보존한다`가 실패한 뒤 통과했습니다.
3. 복구 초안 폐기 후 제목: `DiscardedRecoveryTitleIsRemovedFromList`에서 실제로 남던 제목을 재현하고 복구 선택의 상태 이벤트를 추가했습니다.

추가로 접힌 창의 미보존 초안 경고가 숨은 창에 표시되는 문제를 WPF 하네스로 재현했습니다. 경고 전에 창을 복원하고, 종료 취소 시 작은 아이콘을 제거하여 같은 메모가 두 형태로 남지 않게 했습니다. WebView 초기화 전체를 닫기 경로에서 기다리도록 했습니다.

## 최종 검증

- .NET 단위·저장소 통합 테스트 **121개**: Core 18, Bridge 32, Storage 71.
- Playwright **36개**: 개발 서버와 제작 빌드 모두 통과.
- 잠금 복원과 WPF·검증 하네스 빌드 성공.
- 실제 WPF/WebView2에서 Java·JSON 정리와 두 메모의 제목·본문 저장을 CDP로 확인했습니다.
- `HunminNotes.DesktopSmoke`는 실제 WPF·WinForms 컨트롤로 설정·새 메모·목록 재열기·전체 접기/표시·종료 메뉴와 작은 아이콘의 클릭 이벤트, 독립 창 닫기, 종료 경합·임대 해제, 접힌 미보존 초안의 종료 차단을 검증합니다. 메뉴의 `PerformClick`과 버튼 라우팅 이벤트를 사용하며 셸 알림 영역의 물리 마우스 조작과 구분합니다.
- 기존 Monaco 청크 경고와 Java 런타임의 사용하지 않는 일반 `eval` 분기에 대한 번들 경고는 남습니다. 제작 빌드·실제 WebView2의 Java 정리에는 일반 `unsafe-eval`이 필요하지 않았습니다.

## 재실행

```powershell
$env:DOTNET_ROOT = "$PWD/.tools/dotnet"
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
$env:PATH = "$PWD/.tools/node/node-v24.22.1-win-x64;$env:PATH"
& "$env:DOTNET_ROOT/dotnet.exe" restore HunminNotes.sln --locked-mode
& "$env:DOTNET_ROOT/dotnet.exe" test HunminNotes.sln --no-restore
$env:DEVPOSTIT_DESKTOP_SMOKE_ROOT = "$PWD/.tools/desktop-check-$([Guid]::NewGuid().ToString('N'))"
& "$env:DOTNET_ROOT/dotnet.exe" run --project tests/HunminNotes.DesktopSmoke --no-restore
```

웹 검증은 editor-web에서 `npm test`를 사용합니다. 제작 빌드는 `npm run build` 후 `EDITOR_PRODUCTION=1`을 지정합니다. 기존 개발 서버와 겹치면 `EDITOR_TEST_PORT`를 변경합니다. Playwright 브라우저를 로컬 도구 폴더에 설치한 환경에서는 `PLAYWRIGHT_BROWSERS_PATH`도 지정합니다.

원래 계획의 파일 감시·충돌 해결과 중앙 검색 화면은 이번 추가 기능과 별도입니다. 사용자 수정 editor-web/README.md는 보존했습니다.
