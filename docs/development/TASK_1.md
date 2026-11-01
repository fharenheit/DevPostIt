# 작업 1 실행 기록

계획: `docs/superpowers/plans/2026-11-01-hunmin-notes.md`

기준 커밋: `e590427`. 날짜: 2026-11-01, Windows 11 빌드 26200, x64.
승인된 설계와 계획을 정해진 경로로 내용 변경 없이 복사했습니다.

## 도구 환경

| 항목 | 실제 확인 결과 |
|---|---|
| .NET SDK | 초기 미설치. 공식 설치 스크립트의 GA 10.0 채널로 10.0.403을 `.tools/dotnet`에 설치 |
| .NET 런타임 | Core / ASP.NET Core / Windows Desktop 10.0.14 |
| 대상 프레임워크 | 도메인·테스트 `net10.0`, WPF `net10.0-windows` |
| Node.js | 기존 번들 및 별도 공식 배포본 모두 24.22.1 |
| npm | 초기 경로에 없음. 공식 Node.js 배포본에서 11.19.1 확인 |
| WebView2 | 설치 파일의 버전 156.0.4314.50 확인. 실제 호스팅은 작업 4에서 검증 |
| 독립 Windows SDK | 기본 Windows Kits 경로에 없음. 이번 순수 관리 WPF 빌드는 성공 |

공식 자료: [.NET 10 다운로드](https://dotnet.microsoft.com/en-us/download/dotnet/10.0),
[.NET LTS 발표](https://devblogs.microsoft.com/dotnet/announcing-dotnet-10/),
[Node.js 지원 현황](https://nodejs.org/en/about/previous-releases),
[WebView2 배포 및 설치 감지](https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution).

SDK는 `global.json`, Node는 `.node-version`, 테스트 패키지와 전이 의존성은
프로젝트 파일 및 `packages.lock.json`에 고정했습니다. WebView2 SDK 및 편집기 패키지는 해당 작업에서 선택합니다.
설치 도구와 다운로드 바이너리는 버전 관리에서 제외했습니다.

## 범위 및 판단

- 작업 1만 실행했습니다. 작업 2 이후는 시작하지 않았습니다.
- 현재 체크아웃에서 `codex/task-1-note-domain` 브랜치를 사용했습니다. Git 메타데이터 쓰기와 테스트 실행에는 권한 확장이 필요했습니다.
- 새 메모 생성은 명시적인 `Note.Create`로 ID와 동일한 생성·수정 시각을 부여합니다. 기존 ID를 읽어오는 위치 생성자는 계획의 계약을 유지합니다.
- `GetAsync`는 미존재 메모에 `null`을 반환하는 계약으로 정했습니다. 반환 형식을 명시하지 않은 계획의 모호성을 해소한 것으로, 저장소 구현 시 이 계약을 따라야 합니다.
- 사전 인터페이스 검토: 작업 2의 저장소는 이 `Note` 및 `INoteRepository`를 사용하고, 작업 4·6은 `NoteChange`의 ID·리비전·본문을 사용합니다. 계획 내 충돌은 발견하지 못했습니다.
- Core에 WPF·파일·DB 의존성을 추가하지 않았습니다. 저장·인덱싱·편집기 기능은 미구현입니다.

## TDD 증거

명령은 `.tools/dotnet/dotnet.exe`로 실행했습니다.

1. 테스트를 먼저 작성하고 `dotnet test HunminNotes.sln --no-restore --filter FullyQualifiedName~NoteTests` 실행: `Note` 미정의 컴파일 실패 확인.
2. 계약과 미구현 생성 함수만 추가 후 재실행: 총 3개 중 2개 실패, 1개 통과. 두 실패 모두 생성 함수의 `NotImplementedException`.
3. 생성 함수 구현 후 동일 명령: 총 3개 모두 통과, 실패 0.
4. `dotnet build HunminNotes.sln --no-restore`: WPF 포함 성공, 경고 0, 오류 0.
5. `dotnet test HunminNotes.sln --no-restore`: 전체 3개 통과, 실패 0, 건너뜀 0.
6. `dotnet restore HunminNotes.sln --locked-mode --packages .tools/nuget` 및 빌드·전체 테스트 재실행: 잠금 복원 성공, 빌드 경고·오류 0, 테스트 3개 통과.

별도 검토 에이전트가 계획과 변경 파일을 읽기 전용으로 검토했으며 중요한 문제를 발견하지 못했습니다.

검증 항목: 서로 다른 새 ID, 본문 변경 시 ID 유지, 한글 제목·태그 및 메타데이터 보존, 빈 본문 허용.
도메인 레코드의 데이터 보존 테스트는 계약을 고정하며, 생성 함수 미구현 상태에서도 통과했습니다.
WPF 창의 수동 시각 검증과 WebView2 생성은 이번 작업의 검증에 포함하지 않았습니다.

## 제공된 원본

사용자가 제공한 `C:/Users/fhare/Downloads/훈민메모패드.zip`은 읽기 전용으로 목록과 해시만 확인했습니다.

- ZIP SHA-256: `985590664754BC6AE8314AABF8660D6E735D652C48DFF6F1764DD9FC24BC57D5`
- 포함 실행 파일: `HMPad2k.exe`, 475648바이트, PE Machine `0x014C`(x86)
- 실행 파일 SHA-256: `C7865ECBF9DA523D32EE16FBB2BA7E8BD2C35C164BE59CFE2F3C151F257F1DD7`
- DLL 및 기존 메모 데이터도 포함되어 있습니다. 실행·수정·제품 저장소 복사를 하지 않았습니다.
- 원본 부재 조건은 해소되었지만, 계획의 일회용 Windows VM과 스냅샷은 제공되지 않았습니다. 작업 0의 실행 관찰·행동 명세는 보류하며 관찰 결과를 추정하지 않았습니다.
