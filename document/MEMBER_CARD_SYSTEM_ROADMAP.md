# 🌟 스텔라 듀얼(Stellar Duel) 멤버(Member) 카드 시스템 로드맵

> **문서 개요**
> 본 문서는 매직 더 개더링(MTG)의 플레인즈워커(Planeswalker) 메커니즘을 계승한 스텔라 듀얼의 핵심 차별화 시스템인 **'멤버(Member)' 카드**의 기획 사양, 아키텍처 설계, 구현 단계 및 진행 상황을 관리하는 전용 로드맵 문서입니다.

---

## 📖 1. 멤버(Member) 카드 핵심 룰 및 기획 사양

| 구분 | 규칙 및 동작 메커니즘 |
| :--- | :--- |
| **배치 슬롯** | 일반 하수인 5슬롯과 완전히 독립된 **전용 1칸 슬롯 (`MemberZone[0]`)**에만 소환 |
| **스탯 구조** | **공격력 없음 (`Attack = 0`)**, **체력(`Health`)만 존재** (체력이 MTG 충성도 카운터 역할) |
| **직접 공격 불가** | 멤버는 직접 일반 공격을 수행할 수 없음 (`CanAttack = false`) |
| **피격 가능** | 적 하수인 및 리더의 직접 공격 대상 또는 주문/효과의 단일 타겟이 될 수 있음 |
| **액티브 스킬** | 카드마다 **2 ~ 4개의 고유 액티브 스킬** 보유 |
| **스킬 코스트** | 마나 대신(또는 마나와 함께) **멤버 자신의 체력을 획득(`+HP`)하거나 소모(`-HP`)** |
| **코스트 지불 조건** | 체력 소모 스킬의 경우, **현재 체력이 소모 코스트 이상(`currentHealth >= |HealthCost|`)**이어야만 사용 가능 |
| **턴당 발동 제한** | 멤버는 **한 턴에 딱 1번만** 액티브 스킬 사용 가능 (`HasUsedSkillThisTurn`) |
| **사후 사망 판정** | 체력 소모로 체력이 `0`이 되더라도, **스킬 효과가 먼저 정상 해결된 직후 묘지(Graveyard)로 이동** |
| **교체 소환 (Replacement)** | 멤버가 이미 필드에 있을 때 새 멤버 카드를 내면, **기존 멤버는 묘지로 이동하고 새 멤버가 즉시 교체 소환** |
| **소환 즉시 사용성** | 소환 후유증 없이 **소환된 턴부터 즉시 스킬 사용 가능**<br>이전 멤버가 이미 스킬을 사용했더라도, **교체된 새 멤버는 즉시 스킬 1회 사용 가능** |

---

## 📊 2. 구현 마일스톤 및 진행 현황

| ID | 마일스톤 | 핵심 작업 내용 | 상태 | 구현/영향 파일 |
|:---:|:---|:---|:---:|:---|
| **M-01** | 데이터 모델 구축 | `MemberSkill`, `MemberSkillData`, DB 파싱, `GameCard`/`GameEntity` 모델 확장 | `[✅ 완료]` | `Models/MemberSkill.cs`<br>`ServerCardData.cs`<br>`Entities/GameCard.cs`<br>`Entities/GameEntity.cs`<br>`GameActionModels.cs` |
| **M-02** | 소환 및 교체 로직 | `MemberZone[0]` 슬롯 보정, 교체 소환 퇴장 처리(`RetireMemberToGraveyardAsync`), 턴 시작 스킬 사용권 리셋 | `[✅ 완료]` | `GameState.cs` |
| **M-03** | 스킬 사용 패킷 및 실행 엔진 | `C_ValidMemberSkillTargetsRequest`, `C_UseMemberSkill`, 타겟 계산, 체력 코스트, 사후 사망 처리(`ProcessDeathsAsync`) | `[✅ 완료]` | `GameActionModels.cs`<br>`GameState.cs`<br>`TargetValidator.cs` |
| **M-04** | 클라이언트 연동 규격 정의 및 패킷 동기화 | Unity 클라이언트 패킷 모델(`GameActionModels.cs`) 및 통신 리시버(`GameClient.cs`) 동기화 | `[✅ 완료]` | `Assets/Script/Game/GameActionModels.cs`<br>`Assets/Script/Game/GameClient.cs` |
| **M-05** | BotAI 및 타겟팅 통합 | AI 봇의 멤버 카드 소환 판단 및 최적 액티브 스킬 발동 AI 구현 | `[ ] 대기` | `BotAI.cs`<br>`TargetValidator.cs` |

---

## 🛠️ 3. 세부 단계별 구현 명세

### [M-01] 멤버 카드 데이터 모델 [✅ 완료]
- **`MemberSkill` & `MemberSkillData` (`Models/MemberSkill.cs`)**:
  - `SkillId`, `Name`, `Description`, `HealthCost`, `ManaCost`, `TargetRule`, `Effects` 정의.
  - `CanPayCost(int currentHealth)`: 체력 소모 스킬은 현재 체력이 코스트 이상일 때만 `true` 반환.
  - 클라이언트 DTO용 `canUse` 플래그 제공.
- **`ServerCardData.cs`**:
  - `[FirestoreProperty("MemberSkills")]` 문자열/배열 다형성 지원 및 `GetMemberSkills()` JSON 역직렬화 캐싱.
- **`GameCard.cs` & `GameEntity.cs`**:
  - 멤버 카드의 경우 `Attack = 0`, `CanAttack = false` 강제 적용.
  - 엔티티 레벨에서 `MemberSkills` 및 `HasUsedSkillThisTurn` 상태 관리.
  - `ToEntityData()`에서 현재 체력과 턴 사용 여부를 계산하여 클라이언트 UI 렌더링용 DTO 생성.

### [M-02] 멤버 소환 및 교체(Replacement) 로직 [✅ 완료]
- **`ProcessPlayCardAsync`**:
  - 멤버 카드는 `Position = 0`으로 자동 고정.
  - 기존 멤버가 있더라도 슬롯 중복 에러(`PLAY_CARD_FAIL`)를 무시하고 교체 소환 허용.
- **`ExecuteCardPlayWithTargetAsync` & `RetireMemberToGraveyardAsync`**:
  - 기존 멤버 존재 시: 슬롯 초기화 $\rightarrow$ 사망/퇴장 이벤트 발행 $\rightarrow$ 무덤 구역 이동(`Zone.Graveyard`) $\rightarrow$ 소유자 묘지 추가 $\rightarrow$ 전역 개체 맵 제거 $\rightarrow$ 클라이언트 퇴장 동기화.
  - 새 멤버 안착: 존을 `Zone.MemberZone`으로 갱신하고 `HasUsedSkillThisTurn = false`로 즉시 스킬 사용권 부여.
- **`RefreshEntityAttackState`**:
  - 매 턴 Standby 페이즈마다 멤버 엔티티의 `HasUsedSkillThisTurn = false`로 리셋.

### [M-03] 멤버 액티브 스킬 사용 패킷 및 실행 엔진 [✅ 완료]
- **타겟 요청 및 회신 (`C_ValidMemberSkillTargetsRequest` / `S_ValidMemberSkillTargetsResponse`)**:
  - `TargetValidator.GetValidMemberSkillTargetIds`: 멤버 스킬의 `TargetRule`과 타겟 조건을 계산하여 조준 가능한 엔티티 ID 목록 전송.
- **스킬 시전 및 사후 사망 (`C_UseMemberSkill` / `ProcessUseMemberSkillAsync`)**:
  - 턴 플레이어, 엔티티 생존, `!HasUsedSkillThisTurn`, `skill.CanPayCost(Health)`, 타겟 유효성 검증.
  - 체력 코스트 증감 (`Health += skill.HealthCost`, 충전 시 MaxHealth 확장).
  - 스킬 효과(`CardEffect.Actions`) 비동기 실행.
  - **효과 실행 완료 후 사망 판정**: 체력이 0 이하인 경우 `ProcessDeathsAsync`를 통해 묘지 이동 처리.
  - `S_UseMemberSkillSuccess` 및 `ACTION_RESOLUTION` 브로드캐스트.

### [M-04] 클라이언트(Unity) 패킷 동기화 [✅ 완료]
- **`Assets/Script/Game/GameActionModels.cs`**:
  - `GameActionType`에 멤버 스킬 5종 패킷 등록.
  - `TargetRule` Enum, `MemberSkillData`, `EntityData.memberSkills`, `EntityData.hasUsedSkillThisTurn` 추가.
  - `C_ValidMemberSkillTargetsRequest`, `C_UseMemberSkill`, `S_ValidMemberSkillTargetsResponse`, `S_UseMemberSkillSuccess`, `S_UseMemberSkillFail` 정의.
- **`Assets/Script/Game/GameClient.cs`**:
  - 수신 루프 역직렬화 switch문 등록.
  - 이벤트(`OnValidMemberSkillTargetsResponseEvent`, `OnUseMemberSkillSuccessEvent`, `OnUseMemberSkillFailEvent`) 추가.
  - 전송 헬퍼 메서드(`SendPlayMemberCardRequest`, `SendValidMemberSkillTargetsRequest`, `SendUseMemberSkill`) 추가.

---

## 📝 4. 변경 이력 (Changelog)

| 일시 | 작업 ID | 변경 내용 요약 | 영향 파일 |
|:---|:---:|:---|:---|
| 2026-09-09 | **M-01** | 멤버 카드 액티브 스킬 모델(`MemberSkill`), 클라이언트 DTO(`MemberSkillData`), Firestore 역직렬화 파서, 엔티티 데이터 연동 구현 | `Models/MemberSkill.cs`, `ServerCardData.cs`, `Entities/GameCard.cs`, `Entities/GameEntity.cs`, `GameActionModels.cs` |
| 2026-09-09 | **M-02** | 멤버 전용 슬롯(0번) 보정, 교체 소환 처리기(`RetireMemberToGraveyardAsync`), 턴 시작 스킬 사용권 초기화 구현 | `GameState.cs` |
| 2026-09-09 | **M-03** | 멤버 스킬 조준 대상 계산(`TargetValidator`), 스킬 발동 파이프라인 및 사후 사망 처리(`ProcessUseMemberSkillAsync`) 구현 | `GameState.cs`, `TargetValidator.cs`, `GameActionModels.cs` |
| 2026-09-09 | **M-04** | 클라이언트(`Assets/Script/Game/`) `GameActionModels.cs` 및 `GameClient.cs`에 멤버 스킬 통신 패킷, 이벤트, 전송 메서드 동기화 완료 | Unity `GameActionModels.cs`, Unity `GameClient.cs` |
