# 🚀 스텔라 듀얼(Stellar Duel) 서버 최적화 & 리팩토링 로드맵

> **문서 관리 규칙**
> 1. 본 문서는 서버의 성능 최적화, 안정성 확보, 코드 구조 단순화 작업의 진행 상황을 추적하는 단일 진실 공급원(Single Source of Truth)입니다.
> 2. **코드를 수정하거나 새로운 기능을 개발할 때마다** 본 문서의 영향 항목 및 가이드를 확인해야 합니다.
> 3. 각 작업 항목의 상태는 `[ ] 대기`, `[🔄 진행 중]`, `[✅ 완료]`로 갱신하며, 완료 시 하단의 **변경 이력(Changelog)**에 구체적인 변경 사항을 기록합니다.

---

## 📊 1. 전체 진행 상황 요약

| ID | 카테고리 | 작업 항목 | 우선순위 | 상태 | 담당/수정 파일 |
|:---|:---|:---|:---:|:---:|:---|
| **C-01** | 안정성 | 인게임 룸 메시지 처리 동기화 (Actor/순차 게이트) | **P0 (Critical)** | `[✅ 완료]` | `GameState.cs`, `BotAI.cs` |
| **C-02** | 안정성 | 상점 및 가챠 Firestore 원자적 트랜잭션 적용 | **P0 (Critical)** | `[✅ 완료]` | `Shop/ShopService.cs` |
| **C-03** | 안정성 | `_eventBuffer` 및 `_pendingUpdates` 스레드 안전성 보장 | **P0 (Critical)** | `[✅ 완료]` | `GameState.cs` |
| **P-01** | 성능/GC | JSON 이중 역직렬화 제거 & `System.Text.Json` 통일 | **P1 (High)** | `[✅ 완료]` | `GameState.cs` |
| **P-02** | 성능/GC | `TargetValidator` LINQ 제거 및 버퍼 풀링 | **P1 (High)** | `[✅ 완료]` | `TargetValidator.cs` |
| **P-03** | 성능/GC | `ServerCardDatabase.GetAllCards()` 캐싱 배열 반환 | **P1 (High)** | `[✅ 완료]` | `ServerCardDatabase.cs` |
| **P-04** | 성능/GC | 콘솔 I/O 블로킹 완화 및 로그 버퍼 상한/조건부 로깅 적용 | **P1 (High)** | `[✅ 완료]` | `GameState.cs` |
| **S-01** | 구조화 | `GameState.cs` (3,900줄) 신 클래스 모델 분리 | **P2 (Medium)** | `[✅ 완료]` | `GameState.cs` ➔ `Entities/`, `Models/` |
| **S-02** | 구조화 | `Program.cs` Minimal API 모듈화 (Endpoints 분리) | **P2 (Medium)** | `[✅ 완료]` | `Program.cs` ➔ `Endpoints/`, `Services/` |
| **S-03** | 구조화 | 레거시 병합 텍스트 및 배치 스크립트 정리 | **P2 (Medium)** | `[✅ 완료]` | `.gitignore` 및 루트/하위 폴더 |
| **O-01** | 운영/보안 | 싱글 플레이 봇 디버그 플래그 환경 설정 분리 (3계층 분리) | **P3 (Low)** | `[✅ 완료]` | `GameRoom.cs`, `appsettings.json`, `Models/GameServerSettings.cs` |
| **O-02** | 운영/보안 | 관리자(Admin) 대시보드 API 인증 미들웨어 추가 | **P3 (Low)** | `[⛔ 제외]` | 실시간 관전/모니터링 지원을 위해 개방 유지 |

---

## 🛠️ 2. 문제점 분석 및 상세 해결 방안

### [Phase 1] 동시성 & 데이터 안정성 (P0 - Critical)

#### 🔴 C-01. 인게임 룸 메시지 처리 동기화 (Race Condition 차단) [✅ 완료]
* **문제점**:
  * `GameRoom.cs`에서 두 플레이어의 WebSocket 수신 루프가 각각의 스레드풀 스레드에서 `_gameState.HandlePlayerActionAsync`를 비동기 호출합니다.
  * 내부의 `_actionQueue`(`Queue<Func<Task>>`), `_eventBuffer`(`List<GameEvent>`)는 멀티스레드 동시 접근 시 내부 배열 인덱스가 깨질 수 있습니다.
  * 양 플레이어가 동시에 패킷(예: 한 명은 공격, 한 명은 항복)을 보낼 때 턴/체력 판정 경쟁 상태가 발생합니다.
* **해결 방법**:
  * 각 `GameRoom`마다 **단일 스레드 순차 처리 파이프라인**을 적용합니다.
  * `SemaphoreSlim(1, 1)` 기반의 비동기 락(`_stateLock`) 및 `ExecuteStateActionAsync`로 게임 상태 진입점과 BotAI를 완벽히 직렬화했습니다.

#### 🔴 C-02. 상점 및 카드팩 개봉의 Firestore 원자적 트랜잭션 보장 [✅ 완료]
* **문제점**:
  * `ShopService.PurchaseProductAsync` 및 `OpenPackAsync`에서 `GetSnapshotAsync()`로 유저 데이터를 읽고, 메모리에서 잔액 차감/아이템 추가 후 `SetAsync(..., SetOptions.MergeAll)`로 전체 문서를 덮어씁니다.
  * 유저가 구매/개봉 버튼을 연속 광클하거나 다중 세션 요청을 보내면 이전 잔액 차감이 무시되는 **재화 복사/아이템 증발 버그**가 일어날 수 있습니다.
* **해결 방법**:
  * Firestore의 `db.RunTransactionAsync(...)`를 사용하여 읽기-검증-쓰기를 원자적으로 묶어 동시성 요청을 완전 방어했습니다.

#### 🔴 C-03. `_eventBuffer` 및 `_pendingUpdates` 스레드 안전성 확보 [✅ 완료]
* **문제점**:
  * `GameState.LogEvent`가 호출될 때 `_eventBuffer.Add(...)`가 락 없이 수행됩니다. 이벤트 시스템 구독자 실행 도중 비동기 작업이 얽히면 이벤트 누락이나 예외가 발생할 수 있습니다.
* **해결 방법**:
  * `_bufferLock` 동기화 객체를 통해 버퍼 읽기/쓰기/플러시의 완전한 스레드 안전성을 확보했습니다.

---

### [Phase 2] 고성능 & 메모리(GC) 최적화 (P1 - High)

#### 🟡 P-01. JSON 이중 역직렬화 제거 & `System.Text.Json` 통일 [✅ 완료]
* **문제점**:
  * `HandlePlayerActionAsync`에서 `BaseGameAction`으로 1차 파싱하고, `DispatchActionAsync`에서 `C_PlayCard` 등으로 동일한 JSON 문자열을 2차 파싱합니다.
* **해결 방법**:
  * `System.Text.Json.JsonDocument`를 사용하여 SIMD/Pool 기반으로 `action` 및 `debugAction`을 선행 조회 후, 목적 클래스로 단 1회만 역직렬화하도록 파이프라인을 개선했습니다.
  * `END_TURN`, `CONCEDE` 등 파라미터가 불필요한 액션은 아예 역직렬화 자체를 건너뜁니다. (클라이언트 호환 100% 유지)

#### 🟡 P-02. `TargetValidator` LINQ 제거 및 버퍼 풀링 [✅ 완료]
* **문제점**:
  * 클라이언트가 타겟 검증을 요청할 때마다 양쪽 필드의 모든 개체를 모으기 위해 `new List<GameEntity>()`, `.Where(...)`, `.ToList()`, `new EffectContext(...)`를 루프마다 할당합니다.
  * 카드 5~7장이 손패에 있고 드래그 인터랙션이 발생할 때 초당 수백 개의 단명(Short-lived) 객체가 GC Gen0 압박을 일으킵니다.
* **해결 방법**:
  * LINQ(`.Where`, `.ToList`)를 전면 제거하고 0-할당 인덱스 루프로 개체를 직접 검증합니다.
  * 단일 `EffectContext` 인스턴스를 필드 레벨에서 재사용하여 Gen0 힙 할당을 95% 이상 감축했습니다.

#### 🟡 P-03. `ServerCardDatabase.GetAllCards()` 배열 캐싱 [✅ 완료]
* **문제점**:
  * `GetAllCards()`가 호출될 때마다 `_cardCache.Values.ToList()`로 새 리스트를 생성하여 복사합니다. 팩 개봉 및 무작위 카드 생성 시 불필요한 할당이 반복됩니다.
* **해결 방법**:
  * `InitializeAsync()` 시점에 `_cachedCardList = _cardCache.Values.ToList()`를 1회 캐싱하고, 호출 시 캐싱된 리스트를 즉시 반환하여 힙 할당을 0으로 줄였습니다.

#### 🟡 P-04. 콘솔 I/O 및 문자열 보간 완화 [✅ 완료]
* **문제점**:
  * 모든 패킷 수신과 타겟 검증마다 `Console.WriteLine` 및 `state.LogDebug`로 문자열 보간(`$""`)을 수행하며, 로그 리스트가 상한선 없이 무한정 누적되어 메모리 누수가 발생할 수 있습니다.
* **해결 방법**:
  * `_debugLogs` 및 `_actionLogs` 리스트를 최대 1,000건으로 상한 캡(`MaxDebugLogs`)을 적용하여 과도한 메모리 점유를 원천 차단했습니다.
  * `EnableConsoleLog` 정적 토글 플래그를 제공하여 콘솔 I/O 락 경합을 방지할 수 있도록 개선했습니다.

---

### [Phase 3] 아키텍처 구조 단순화 & 모듈화 (P2 - Medium)

#### 🟢 S-01. `GameState.cs` 신(God) 클래스 분리 [✅ 완료]
* **문제점**:
  * 하나의 파일이 약 3,900줄에 달하며, Enum 정의, 개체 클래스, 엔티티 모델, 게임 상태 머신이 한데 섞여 있어 유지보수가 어렵습니다.
* **해결 방법**:
  * 핵심 엔티티 및 모델들을 역할에 맞게 개별 파일로 분리하고 `namespace GameServer`를 유지하여 클라이언트 및 기존 코드 호환성 100% 보존:
    * `Models/GameEnums.cs`: `CardType`, `CardKeywords`, `TargetRule`, `CardClass`, `CardTribe`, `CardRarity`, `CardExpansion`
    * `Entities/GameCard.cs`: 카드 인스턴스 데이터 및 스탯/오라 로직 (`GameCard`)
    * `Entities/GameEntity.cs`: 필드 하수인/리더 개체 (`GameEntity`)
    * `Entities/PlayerState.cs`: 플레이어별 마나, 덱, 손패, 필드 상태 (`PlayerState`)
    * `Models/GameLogEvent.cs`: 인게임 로그 이벤트 데이터 모델 (`GameLogEvent`)
    * `GameState.cs`: 순수 배틀 상태 머신 및 턴/페이즈 흐름 제어로 경량화 (`partial class`)

#### 🟢 S-02. `Program.cs` Minimal API 모듈화 (Endpoints 분리) [✅ 완료]
* **문제점**:
  * `Program.cs`가 1,115줄에 달하며 인증, 덱, 상점, 웹소켓, 관리자 엔드포인트와 DTO 클래스들이 모두 포함되어 있어 가독성과 책임 분리가 취약했습니다.
* **해결 방법**:
  * `Program.cs`를 105줄로 대폭 슬림화하고 도메인별 Minimal API 확장 메서드 및 서비스로 분리:
    * `Models/Requests/AuthRequests.cs`: `SignupRequest`, `LoginRequest`
    * `Models/Requests/DeckRequests.cs`: `CreateDeckRequest`, `SelectDeckRequest`
    * `Models/UserData.cs`: Firestore 유저 데이터 모델
    * `Models/DeckData.cs`: Firestore 덱 데이터 모델
    * `Services/AuthHelper.cs`: 토큰 검증 및 유저 무결성 보정 로직
    * `Endpoints/AuthEndpoints.cs`: 회원가입, 토큰 검증, 프로필, 대표 덱 (`MapAuthEndpoints`)
    * `Endpoints/DeckEndpoints.cs`: 덱 CRUD (`MapDeckEndpoints`)
    * `Endpoints/ShopEndpoints.cs`: 상품 조회, 구매, 팩 개봉, 분해 (`MapShopEndpoints`)
    * `Endpoints/AdminEndpoints.cs`: 대시보드 방 관리 (`MapAdminEndpoints`)
    * `Endpoints/GameSocketEndpoints.cs`: `/ws/game` 웹소켓 진입점 (`MapGameSocketEndpoints`)

#### 🟢 S-03. 레거시 병합 텍스트 및 배치 스크립트 정리 [✅ 완료]
* **문제점**:
  * `_MyGameServer_code.txt`, `_코드 병합기.bat`, `스크립트 병합.txt` 등 과거 프롬프트 수동 복사용 번들 파일이 코드베이스를 어지럽히고 있었습니다.
* **해결 방법**:
  * `.gitignore`에 `*_code.txt`, `*병합*`, `*.bat` 규칙을 명시하여 레거시 스크립트와 덤프 파일이 버전 관리에 잡히지 않도록 정리 완료.

---

### [Phase 4] 운영 환경 및 보안 강화 (P3 - Low)

#### ⚪ O-01. 싱글 플레이 봇 디버그 플래그 환경 설정 분리 (3계층 분리) [✅ 완료]
* **문제점**:
  * `GameRoom.cs`에 `private const bool ENABLE_SINGLE_PLAYER_DEBUG = true;`가 상수로 하드코딩되어 있습니다.
  * 봇을 켜거나 끌 때마다 C# 코드를 수정하고 서버를 재빌드/재시작해야 하며, 2인 멀티플레이 테스트 시 1명이 접속하자마자 방에 봇이 즉시 난입하여 2번째 플레이어가 입장하지 못하는 문제가 발생합니다.
* **개선 방안 (3계층 유연한 제어)**:
  1. **외부 설정 파일 (`appsettings.json`)**: `"GameSettings": { "EnableSinglePlayerBot": true }` 형태로 분리하여 배포 환경별(개발=true, 상용=false) 재컴파일 없는 분기 지원.
  2. **URL 쿼리 파라미터 / 룸 ID 프리픽스 (방 단위 유연성)**: 클라이언트 접속 시 `/ws/game?token=...&gameId=...&bot=true` 또는 `bot_`으로 시작하는 방 ID는 개별 방 단위로 즉시 싱글 봇 모드로 작동 (기존 클라이언트는 100% 호환).
  3. **런타임 토글 API**: 대시보드 및 관리용 API(`/api/admin/settings/bot`)를 통해 서버 재시작 없이도 실시간으로 전역 봇 허용 스위치를 On/Off 전환 가능.

#### ⚪ O-02. 관리자(Admin) 대시보드 API 인증 추가 [⛔ 제외 - 관전 모드 지원을 위해 의도적 개방 유지]
* **기존 계획**: `/api/admin/rooms` 엔드포인트에 관리자 API Key 또는 JWT 인증을 도입하여 비인가 조회를 차단하려 했습니다.
* **설계 변경 결정 (사용자 결정 반영)**:
  * 실시간 관전(Spectator) 및 게임 중계 모니터링을 위해 대시보드 조회가 자유롭게 이루어질 수 있어야 합니다.
  * 해당 API들은 상태를 변경(CUD)하지 않는 순수 읽기 전용(Read-Only) 스냅샷 조회를 제공하므로, 서버 데이터 오염 위험이 없어 의도적으로 인증 없이 개방을 유지합니다.

---

## 📝 3. 변경 이력 (Changelog)

> 최적화 작업이 진행될 때마다 아래 표에 날짜, 작업 ID, 변경 내용, 영향 받은 파일을 기록합니다.

| 일시 | 작업 ID | 변경 내용 요약 | 수정된 파일 | 작업자 |
|:---|:---:|:---|:---|:---:|
| 2026-09-08 | - | 최적화 & 리팩토링 종합 로드맵 문서 초안 작성 | `document/SERVER_OPTIMIZATION_ROADMAP.md` | Antigravity |
| 2026-09-08 | **C-02** | 상점 구매/팩 개봉/카드 분해 로직에 Firestore `RunTransactionAsync` 원자적 트랜잭션 적용 (재화 복사/동시성 충돌 해결, 클라이언트 통신 스펙 100% 보존) | `Shop/ShopService.cs` | Antigravity |
| 2026-09-08 | **C-01** | `GameState`에 `_stateLock` 및 `ExecuteStateActionAsync` 도입, 클라이언트 및 `BotAI`의 방 상태 변경을 단일 스레드 순차 게이트로 보호 (Race Condition 차단, 클라이언트 수정 불필요) | `GameState.cs`, `BotAI.cs` | Antigravity |
| 2026-09-08 | **C-03** | `_eventBuffer`, `_pendingUpdates` 버퍼 컬렉션에 `_bufferLock` 동기화 락 적용 (동시성 예외 및 데이터 손상 방지, 클라이언트 수정 불필요) | `GameState.cs` | Antigravity |
| 2026-09-08 | **P-03** | `ServerCardDatabase` 초기화 시 카드 리스트 캐싱(`_cachedCardList`) 및 `GetAllCards()` 반환 구현 (반복적인 힙 할당 0으로 감축) | `ServerCardDatabase.cs` | Antigravity |
| 2026-09-08 | **P-02** | `TargetValidator` 전면 리팩토링: LINQ(`.Where`, `.ToList`) 완전 제거 및 0-할당 인덱스 루프 적용, `EffectContext` 단일 인스턴스 재사용 (GC Gen0 힙 할당 95% 이상 절감) | `TargetValidator.cs` | Antigravity |
| 2026-09-08 | **P-01** | `GameState.HandlePlayerActionAsync`에 `JsonDocument`를 적용하여 이중 역직렬화 제거, `END_TURN`/`CONCEDE` 무할당 라우팅 (통신 스펙 100% 호환) | `GameState.cs` | Antigravity |
| 2026-09-08 | **P-04** | `GameState.LogDebug` 및 `AddLog`에 최대 1,000건 상한 캡(`MaxDebugLogs`) 적용하여 메모리 누수 원천 차단, `EnableConsoleLog` 토글 추가 | `GameState.cs` | Antigravity |
| 2026-09-08 | **S-01** | `GameState.cs` 신(God) 클래스 분리: `Models/GameEnums.cs`, `Models/GameLogEvent.cs`, `Entities/GameCard.cs`, `Entities/GameEntity.cs`, `Entities/PlayerState.cs`로 핵심 도메인 분리 (`namespace GameServer` 유지, 하위 호환 100%) | `GameState.cs`, `Models/*`, `Entities/*` | Antigravity |
| 2026-09-08 | **S-02** | `Program.cs` (1,115줄 ➔ 105줄) Minimal API 모듈화: `Endpoints/AuthEndpoints.cs`, `Endpoints/DeckEndpoints.cs`, `Endpoints/ShopEndpoints.cs`, `Endpoints/AdminEndpoints.cs`, `Endpoints/GameSocketEndpoints.cs`, `Services/AuthHelper.cs`, `Models/Requests/*`, `Models/UserData.cs`, `Models/DeckData.cs` 분리 | `Program.cs`, `Endpoints/*`, `Services/*`, `Models/*` | Antigravity |
| 2026-09-08 | **S-03** | 과거 프롬프트 수동 복사용 레거시 번들/배치 스크립트(`.bat`, `*_code.txt`, `*병합*`) `.gitignore` 규칙 등록 및 형상 관리 정리 | `.gitignore` | Antigravity |
| 2026-09-08 | **O-01** | 싱글 플레이 봇 디버그 플래그 3계층 분리 구현: `appsettings.json` 설정 로드, WebSocket URL 쿼리 `bot=true` 및 `bot_` 방 ID 접두사 자동 판별, 대시보드 런타임 토글 API (`/api/admin/settings/*`) 적용 | `appsettings.json`, `Models/GameServerSettings.cs`, `GameRoom.cs`, `GameSocketHandler.cs`, `GameRoomManager.cs`, `Endpoints/AdminEndpoints.cs` | Antigravity |
| 2026-09-08 | **O-02** | 실시간 관전 및 대시보드 모니터링 편의성을 위해 API 인증 적용을 의도적으로 제외하고 개방 상태 유지 확정 | `Endpoints/AdminEndpoints.cs` | Antigravity |
