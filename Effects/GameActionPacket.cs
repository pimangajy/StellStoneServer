using System;
using System.Collections.Generic;

namespace GameServer.Effects
{
    /// <summary>
    /// 전장에서 일어나는 모든 행동/사건의 종류를 정의합니다.
    /// (기존의 난잡했던 TriggerType을 완전히 대체하는 단일 표준)
    /// </summary>
    public enum GameActionKind
    {
        None = 0,

        // 1. 카드 시전 및 소환
        PlayCard,           // 카드를 패에서 냄 (주문 시전, 하수인 내기)
        Summon,             // 필드에 하수인/토큰/멤버가 소환됨
        CreateCard,         // 새로운 카드가 손패/덱에 생성됨 (토큰, 복사 등)
        Discover,           // 카드를 발견하여 선택함

        // 2. 전투 및 체력/생명
        Attack,             // 공격 선언 및 타격
        Damage,             // 피해를 입힘/받음
        Heal,               // 체력을 회복함
        Death,              // 개체(하수인/멤버)가 파괴되어 무덤으로 감

        // 3. 자원 및 덱 순환
        Draw,               // 카드를 뽑음
        Discard,            // 패를 버림
        ManaChange,         // 마나 증감

        // 4. 상태 및 스탯
        Buff,               // 스탯/키워드/오라 버프 적용
        CrowdControl,       // 침묵, 속박 등 상태이상 적용
        UseMemberSkill,     // 멤버 카드 액티브 스킬 사용

        // 5. 턴 라이프사이클
        TurnStart,          // 턴 시작
        TurnEnd,            // 턴 종료
        PhaseChange         // 페이즈 변경
    }

    /// <summary>
    /// 행동이 일어나는 시점 (선제/가로채기 vs 사후 반응)
    /// </summary>
    public enum ActionTiming
    {
        Before,             // ~할 때 (행동 실행 직전 / 선제 타격 / 대신 맞기 / 카운터)
        After               // ~했을 때 (행동 실행 직후 / 사후 반응 / 사망 판정)
    }

    /// <summary>
    /// 조건문이 패킷의 어느 영역을 검사할지 명시하는 타겟 식별자
    /// </summary>
    public enum EventCheckTarget
    {
        Source,             // 행동을 한 주체 (시전자, 공격자, 가해자, 소환된 자 등)
        Target,             // 행동을 당한 대상 (피격자, 조준 대상, 회복 대상 등)
        Self,               // 효과를 가진 이 카드 본체
        GameContext         // 게임 전체 맥락 (턴 플레이어, 턴 수, 페이즈 등)
    }

    /// <summary>
    /// 연계 및 과거 이력 검사 범위
    /// </summary>
    public enum HistoryScope
    {
        Current,            // 현재 행동 패킷
        PreviousAction,     // 직전 1개 행동 패킷 (순수 연계 / Combo)
        ThisTurn,           // 이번 턴에 일어난 모든 행동
        LastTurn,           // 지난 턴에 일어난 모든 행동
        ThisGame            // 이번 게임 전체에서 일어난 모든 행동
    }

    /// <summary>
    /// 전장의 모든 행동을 육하원칙(5W1H)으로 완벽히 캡슐화한 차세대 표준 사건 패킷입니다.
    /// 전장의 모든 카드(손/필드/덱/무덤)는 이 패킷을 관찰하고 발동 조건을 판정합니다.
    /// </summary>
    public class GameActionPacket
    {
        // =====================================================================
        // [1] 어떤 행동인가? (Action & Timing)
        // =====================================================================
        public GameActionKind Action { get; set; } = GameActionKind.None;
        public ActionTiming Timing { get; set; } = ActionTiming.After;

        // =====================================================================
        // [2] 누가 / 어디서 (Source) - 행동을 일으킨 주체
        // =====================================================================
        public string SourceUid { get; set; } = "";          // 행동한 플레이어 UID
        public GameCard? SourceCard { get; set; }            // 사용/소환된 카드
        public GameEntity? SourceEntity { get; set; }        // 행동한 필드 개체 (공격자, 시전자 등)
        public Zone SourceZone { get; set; } = Zone.None;    // 출발 구역 (Hand, Field, Deck, Graveyard, MemberZone)
        public int SourceSlot { get; set; } = -1;            // 출발 필드 슬롯 번호

        // =====================================================================
        // [3] 누구에게 / 어디로 (Target) - 행동을 당한 대상
        // =====================================================================
        public string? TargetUid { get; set; }               // 대상 플레이어 UID
        public GameEntity? TargetEntity { get; set; }        // 대상 필드 개체 (피격자, 조준 대상 등)
        public GameCard? TargetCard { get; set; }            // 대상 카드 (손패 버프 대상, 생성된 카드 등)
        public Zone TargetZone { get; set; } = Zone.None;    // 도착/배치 구역
        public int TargetSlot { get; set; } = -1;            // 도착 필드 슬롯 번호

        // =====================================================================
        // [4] 얼마만큼 / 언제 (Value & Context) - 부가 상황 정보
        // =====================================================================
        public int Amount { get; set; } = 0;                 // 데미지량, 회복량, 드로우 수, 코스트 변동 등
        public string Phase { get; set; } = "";              // 페이즈 명칭 (MainPhase, BattlePhase 등)
        public int TurnNumber { get; set; }                  // 발생 턴 번호
        public string? TurnPlayerUid { get; set; }           // 현재 턴의 주인 UID

        // =====================================================================
        // [5] 결과 및 생사 상태 (Outcome) - 처치/압살/생존 판정용
        // =====================================================================
        public bool TargetDied { get; set; } = false;         // 대상이 이번 행동으로 사망했는가?
        public int TargetRemainingHealth { get; set; } = 0;   // 행동 적용 후 대상의 남은 체력
        public int OverkillAmount { get; set; } = 0;          // 초과 피해량 (압살 / 관통 효과용)

        // =====================================================================
        // [6] 반복 및 인과관계 메타데이터 (Repeat & Causality)
        // =====================================================================
        public int SequenceIndex { get; set; } = 0;           // 연타/반복 중 현재 몇 번째인가? (0, 1, 2...)
        public int TotalSequenceCount { get; set; } = 1;      // 총 반복 횟수 (예: 3연타 중 1타)
        public GameActionKind CauseAction { get; set; } = GameActionKind.None; // 이 사건을 유발한 상위 행동
        public GameActionPacket? ParentPacket { get; set; }   // 연쇄 반응 추적용 부모 패킷

        // =====================================================================
        // [7] 정적 팩토리 도우미 (Static Factory Methods)
        // =====================================================================

        /// <summary>
        /// 카드 시전 패킷 생성 (주문 또는 하수인 패 시전)
        /// </summary>
        public static GameActionPacket CreatePlayCard(string playerUid, GameCard card, GameEntity? targetEntity = null, int targetSlot = -1)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.PlayCard,
                Timing = ActionTiming.After,
                SourceUid = playerUid,
                SourceCard = card,
                SourceZone = Zone.Hand,
                TargetEntity = targetEntity,
                TargetUid = targetEntity?.OwnerUid,
                TargetSlot = targetSlot,
                TargetZone = (card.Type == CardType.하수인 || card.Type == CardType.멤버) ? Zone.Field : Zone.Graveyard
            };
        }

        /// <summary>
        /// 하수인/멤버 필드 소환 패킷 생성
        /// </summary>
        public static GameActionPacket CreateSummon(string playerUid, GameCard card, GameEntity entity, int slot, Zone fromZone = Zone.Hand)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.Summon,
                Timing = ActionTiming.After,
                SourceUid = playerUid,
                SourceCard = card,
                SourceEntity = entity,
                SourceZone = fromZone,
                TargetEntity = entity,
                TargetSlot = slot,
                TargetZone = entity.IsMember ? Zone.MemberZone : Zone.Field
            };
        }

        /// <summary>
        /// 피해(Damage) 패킷 생성 (생사 판정 및 오버킬 계산 포함)
        /// </summary>
        public static GameActionPacket CreateDamage(GameEntity? attacker, GameCard? sourceCard, string sourceUid, GameEntity victim, int damageAmount, ActionTiming timing = ActionTiming.After)
        {
            int remainingHp = Math.Max(0, victim.Health - damageAmount);
            bool isFatal = damageAmount >= victim.Health;
            int overkill = isFatal ? Math.Max(0, damageAmount - victim.Health) : 0;

            return new GameActionPacket
            {
                Action = GameActionKind.Damage,
                Timing = timing,
                SourceUid = sourceUid,
                SourceEntity = attacker,
                SourceCard = sourceCard ?? attacker?.SourceCard,
                SourceZone = attacker != null ? Zone.Field : Zone.Hand,
                TargetEntity = victim,
                TargetUid = victim.OwnerUid,
                TargetZone = Zone.Field,
                Amount = damageAmount,
                TargetDied = isFatal,
                TargetRemainingHealth = remainingHp,
                OverkillAmount = overkill
            };
        }

        /// <summary>
        /// 치유(Heal) 패킷 생성
        /// </summary>
        public static GameActionPacket CreateHeal(GameEntity? healer, GameCard? sourceCard, string sourceUid, GameEntity target, int healAmount)
        {
            int remainingHp = Math.Min(target.MaxHealth, target.Health + healAmount);

            return new GameActionPacket
            {
                Action = GameActionKind.Heal,
                Timing = ActionTiming.After,
                SourceUid = sourceUid,
                SourceEntity = healer,
                SourceCard = sourceCard ?? healer?.SourceCard,
                TargetEntity = target,
                TargetUid = target.OwnerUid,
                Amount = healAmount,
                TargetRemainingHealth = remainingHp
            };
        }

        /// <summary>
        /// 사망(Death) 패킷 생성 (살해자 및 원인 추적)
        /// </summary>
        public static GameActionPacket CreateDeath(GameEntity deadEntity, GameEntity? killerEntity = null, GameCard? killerCard = null, GameActionKind cause = GameActionKind.Damage)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.Death,
                Timing = ActionTiming.After,
                SourceEntity = killerEntity,
                SourceCard = killerCard ?? killerEntity?.SourceCard,
                SourceUid = killerEntity?.OwnerUid ?? "",
                TargetEntity = deadEntity,
                TargetCard = deadEntity.SourceCard,
                TargetUid = deadEntity.OwnerUid,
                TargetZone = Zone.Graveyard,
                CauseAction = cause,
                TargetDied = true,
                TargetRemainingHealth = 0
            };
        }

        /// <summary>
        /// 드로우(Draw) 패킷 생성
        /// </summary>
        public static GameActionPacket CreateDraw(string playerUid, GameCard drawnCard)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.Draw,
                Timing = ActionTiming.After,
                SourceUid = playerUid,
                SourceZone = Zone.Deck,
                TargetUid = playerUid,
                TargetCard = drawnCard,
                TargetZone = Zone.Hand,
                Amount = 1
            };
        }

        /// <summary>
        /// 카드 생성(CreateCard) 패킷 생성 (토큰, 복사 생성 등)
        /// </summary>
        public static GameActionPacket CreateCardGeneration(string playerUid, GameCard sourceCard, GameCard createdCard, Zone destinationZone = Zone.Hand)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.CreateCard,
                Timing = ActionTiming.After,
                SourceUid = playerUid,
                SourceCard = sourceCard,
                TargetUid = playerUid,
                TargetCard = createdCard,
                TargetZone = destinationZone,
                Amount = 1
            };
        }

        /// <summary>
        /// 발견(Discover) 선택 완료 패킷 생성
        /// </summary>
        public static GameActionPacket CreateDiscover(string playerUid, GameCard sourceCard, GameCard chosenCard)
        {
            return new GameActionPacket
            {
                Action = GameActionKind.Discover,
                Timing = ActionTiming.After,
                SourceUid = playerUid,
                SourceCard = sourceCard,
                TargetUid = playerUid,
                TargetCard = chosenCard,
                TargetZone = Zone.Hand,
                Amount = 1
            };
        }

        /// <summary>
        /// GameActionPacket을 EffectContext로 변환합니다. (호환성 및 점진적 전환 지원)
        /// </summary>
        public EffectContext ToEffectContext(GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            GameEntity? resolvedSource = ownerEntity;
            if (resolvedSource == null && Action == GameActionKind.Death && TargetEntity?.SourceCard?.InstanceId == ownerCard.InstanceId)
            {
                resolvedSource = TargetEntity;
            }
            if (resolvedSource == null)
            {
                resolvedSource = SourceEntity;
            }

            var ctx = new EffectContext(ownerCard.OwnerUid, ownerCard, EffectTriggerType.NONE)
            {
                SourceCard = ownerCard,
                SourceEntity = resolvedSource,
                TargetEntity = TargetEntity,
                TargetPosition = TargetSlot,
                TriggerCard = SourceCard,
                TriggerOwnerUid = SourceUid,
                EventData = Amount
            };
            return ctx;
        }
    }
}
