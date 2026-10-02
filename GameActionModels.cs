using System.Collections.Generic;

namespace GameServer
{
    // ==================================================================
    // 1. 기본 액션 클래스 (JSON 파싱용)
    // ==================================================================

    /// <summary>
    ///  디버그용 액션
    /// </summary>
    public enum DebugAction
    {
        NONE = 0,            // 기본값(안전장치)
        SpecificCardDraw,    // 특정 카드 드로우
        RequestDeckInfo,     // [신규] 클라이언트 -> 서버: 내 덱 정보 요청
        ResponseDeckInfo     // [신규] 서버 -> 클라이언트: 덱 정보 응답
    }

    /// <summary>
    /// 카드의 출처(획득 경로)를 나타냅니다.
    /// </summary>
    public enum CardOrigin
    {
        Deck = 0,        // 덱에서 정상 드로우/서치된 카드
        SideDeck = 1,    // 사이드덱에서 코스트를 지불하고 가져온 카드
        Graveyard = 2,   // 묘지에서 회수/재사용된 카드
        Created = 3      // 주문/효과/토큰으로 새로 생성된 카드
    }

    /// <summary>
    /// 클라이언트와 서버가 주고받는 모든 메시지(액션)의 종류를 정의합니다.
    /// </summary>
    public enum GameActionType
    {
        NONE = 0,

        // ==========================================
        // 클라이언트 -> 서버 (C -> S) 메시지
        // ==========================================
        MULLIGAN_DECISION,   // 멀리건 결정
        END_TURN,            // 턴 종료
        PLAY_CARD,           // 카드 사용
        SELECT_TARGET_FOR_PLAY,    // 클라이언트가 최종 선택한 타겟 전달 (또는 취소)
        VALID_TARGETS_REQUEST,// 타겟 확인
        VALID_ATTACK_TARGETS_REQUEST, // 공격가능한 대상 요청
        ATTACK,              // 공격 명령
        CONCEDE,             // 항복
        MAKE_CHOICE,         // 클라이언트가 선택 결과를 보냄
        VALID_MEMBER_SKILL_TARGETS_REQUEST, // [멤버] 스킬 조준 가능 대상 요청
        USE_MEMBER_SKILL,                   // [멤버] 스킬 사용 요청
        GET_CARD_FROM_SIDE_DECK,            // [사이드덱] 사이드덱에서 카드 가져오기 요청

        // ==========================================
        // 서버 -> 클라이언트 (S -> C) 메시지
        // ==========================================
        ACTION_RESOLUTION,         // 애니메이션 및 최종 상태 일괄 처리
        MULLIGAN_INFO,             // 멀리건 할 카드 정보
        OPPONENT_MULLIGAN_STATUS,  // 상대방 멀리건 완료 상태
        GAME_READY,                // 게임 시작
        PHASE_START,               // 페이즈 시작 (Standby, Draw, Main, End)
        DRAW_CARD,                 // 카드를 뽑음
        UPDATE_MANA,               // 마나 갱신
        UPDATE_ENTITIES,           // 개체(필드, 체력 등) 상태 갱신
        OPPONENT_PLAY_CARD,        // 상대방이 카드를 냄
        REQUEST_TARGET_FOR_PLAY,   // 서버가 클라이언트에게 "타겟 찍어줘"라고 요청
        PLAY_CARD_SUCCESS,         // 카드 사용 성공
        PLAY_CARD_FAIL,            // 카드 사용 실패
        VALID_TARGETS_RESPONSE,    // 타겟 가능한 객체 전송
        VALID_ATTACK_TARGETS_RESPONSE,  // 공격 가능한 대상 전송
        VALID_MEMBER_SKILL_TARGETS_RESPONSE, // [멤버] 스킬 조준 가능 대상 전송
        USE_MEMBER_SKILL_SUCCESS,           // [멤버] 스킬 사용 성공 브로드캐스트
        USE_MEMBER_SKILL_FAIL,              // [멤버] 스킬 사용 실패 알림
        UPDATE_HAND_CARDS,         // 손패 카드 상태(비용, 스탯 등) 갱신
        REQUEST_CHOICE,            // 서버가 클라이언트에게 선택을 요청함
        GAME_OVER,                 // 게임 종료
        ERROR,                     // 서버 에러
        GET_CARD_FROM_SIDE_DECK_SUCCESS,    // [사이드덱] 카드 가져오기 성공 브로드캐스트
        GET_CARD_FROM_SIDE_DECK_FAIL,       // [사이드덱] 카드 가져오기 실패 알림
        CARD_CREATED,                       // [신규] (S->C) 카드 생성(손패 획득) 알림
        SEND_EMOTE,                         // [신규] (C->S) 감정표현 전송 요청
        RECEIVE_EMOTE,                      // [신규] (S->C) 감정표현 수신(브로드캐스트) 알림
        NEW_LOG_EVENT                       // [신규] (S->C) 새로운 행동 기록(히스토리 타일) 알림
    }

    /// <summary>
    /// 클라이언트 -> 서버 / 서버 -> 클라이언트 모든 메시지의 기반이 되는 클래스입니다.
    /// </summary>
    public class BaseGameAction
    {
        public GameActionType action;
    }

    /// <summary>
    /// 디버그 요청 메시지의 기반이 되는 클래스입니다.
    /// 기존 action 필드 대신 debugAction 필드를 사용합니다.
    /// </summary>
    public class BaseDebugAction
    {
        public DebugAction debugAction;
    }

    // ==================================================================
    // 2. 공용 데이터 모델 (게임 상태를 표현)
    // ==================================================================

    /// <summary>
    /// 카드를 식별하는 기본 데이터입니다.
    /// </summary>
    public class CardInfo
    {
        public string? cardId{ get; set; } // 카드 원본 ID (예: "Fireball_001")
        public string? instanceId{ get; set; } // 이 게임에서 이 카드를 식별하는 고유 ID (예: "HandCard_123")
        public string? cardName{ get; set; }
        public CardOrigin origin { get; set; } = CardOrigin.Deck; // 카드의 획득 출처 (덱, 사이드덱, 묘지, 생성)
        
        // (신규) 손/덱 버프를 위한 '현재 상태' 필드
        public int currentCost{ get; set; }   // 현재 비용 (버프/너프 적용됨)
        public int currentAttack{ get; set; } // 현재 공격력 (하수인 전용)
        public int currentHealth{ get; set; } // 현재 체력 (하수인 전용)
        
        // 부여된 효과(버프/너프) 목록
        public List<EnchantmentInfo> enchantments { get; set; } = new List<EnchantmentInfo>();

        // 손패 누적 스택 수치
        public int customValue { get; set; } = 0;

        // 이 카드가 대상으로 삼을 수 있는 현재 필드의 EntityId 목록
        public List<int>? validTargetIds { get; set; } 

        // 손패 한도(10장) 초과 등으로 인해 소각(Graveyard 직행)되었는지 여부
        public bool isBurned { get; set; } = false;

        // 🚀 [신규 추가] 실시간 증폭(Aura) 및 동적 스탯 정보
        public bool isAmplified { get; set; } = false;          // 전체 증폭 상태 여부 (true면 클라이언트에서 지속 오라/이펙트 활성화)
        public bool isSpellAmplified { get; set; } = false;     // 주문 피해 증폭 여부
        public bool isBuffAmplified { get; set; } = false;      // 주문 버프 수치 증폭 여부

        // 🚀 [신규 추가] 실시간 주문 피해량 정보
        public int dynamicDamage { get; set; } = 0;         // 증폭이 적용된 최종 피해량 (예: 3 -> 4)
        public int baseDamage { get; set; } = 0;            // 증폭 전 기본 피해량 (예: 3)
        public int spellAmpBonus { get; set; } = 0;         // 적용된 순수 주문 증폭치 (+1, +2 등)

        // 🚀 [신규 추가] 실시간 주문 버프 수치 정보
        public int dynamicBuffAttack { get; set; } = 0;     // 증폭이 적용된 최종 공격력 버프량 (예: 1 -> 2)
        public int dynamicBuffHealth { get; set; } = 0;     // 증폭이 적용된 최종 체력 버프량 (예: 1 -> 2)
        public int baseBuffAttack { get; set; } = 0;        // 증폭 전 기본 공격력 버프량
        public int baseBuffHealth { get; set; } = 0;        // 증폭 전 기본 체력 버프량
        public int buffAmpAtkBonus { get; set; } = 0;       // 적용된 공격력 버프 증폭치 (+1 등)
        public int buffAmpHpBonus { get; set; } = 0;        // 적용된 체력 버프 증폭치 (+1 등)
    }

    /// <summary>
    /// 필드, 손, 덱에 있는 모든 '개체'를 나타냅니다.
    /// (플레이어 리더, 하수인, 멤버)
    /// </summary>
    public class EntityData
    {
        public int entityId { get; set; } // 이 게임의 모든 개체를 식별하는 고유 ID (예: 1=A리더, 2=B리더, 101=A하수인, 201=B하수인)
        public string? cardId { get; set; } // 원본 카드 ID
        public string? cardName { get; set; }
        public string? ownerUid { get; set; } // 이 개체의 소유자
        public int attack{ get; set; }
        public int health{ get; set; }
        public int maxHealth{ get; set; }
        public bool canAttack{ get; set; } // '돌진'이 있거나, 턴 시작 시 true
        public bool hasAttacked{ get; set; } // 이번 턴에 이미 공격했는지
        
        // List<string>으로 키워드 관리
        // (예: ["TAUNT", "POISONOUS"])
        public List<CardKeywords>? keywords { get; set; } = new List<CardKeywords>(); 

        // 이 개체가 보유한 활성 효과 트리거 목록 (예: ON_TURN_END, ON_DEATH 등)
        public List<EffectTriggerType> activeTriggers { get; set; } = new List<EffectTriggerType>();

        // 필드에 나온 개체가 받고 있는 효과 목록
        public List<EnchantmentInfo> enchantments { get; set; } = new List<EnchantmentInfo>();

        public int position { get; set; }
        public bool isMember { get; set; }
        public bool isLeader { get; set; }
        public string? skinId { get; set; } // 장착된 리더/유닛 스킨 ID

        // 멤버 카드 액티브 스킬 정보
        public List<MemberSkillData>? memberSkills { get; set; }
        public bool hasUsedSkillThisTurn { get; set; }
    }

    /// <summary>
    /// 엔티티(하수인/영웅)의 수치 변화와 최종 상태 정보를 담는 객체입니다.
    /// </summary>
    public class EntityUpdateInfo
    {
        // 대상 식별자
        public string? entityId;

        // [최종 상태] 클라이언트는 연출 종료 후 또는 즉시 이 값으로 데이터를 동기화합니다.
        public int currentHp;
        public int currentAtk;
        public bool isDead;

        // [변화량] 클라이언트가 연출(데미지 텍스트 등)을 위해 사용할 수치 데이터입니다.
        // 클라이언트는 이 값을 참조하여 -2, +5 등의 숫자를 화면에 표시합니다.
        public int hpDelta;    
        public int atkDelta;   
    }

    
    /// <summary>
    /// 게임 내에서 발생하는 사건(이벤트)의 종류를 정의합니다.
    /// </summary>
    public enum GameEventType
    {
        NONE = 0,
        ATTACK,           // 공격 선언
        DAMAGE,           // 데미지 발생
        HEAL,             // 체력 회복
        BUFF,             // 스탯 버프
        BUFF_HAND,
        BUFF_DECK,
        DEATH,            // 개체 사망
        DESTROY,          // 즉사기(처치) 발동 연출용
        EFFECT_TRIGGER,   // 특수 효과 발동 연출 (전투의 함성, 죽음의 메아리 등)
        SUMMON ,           // 하수인 소환
        SUMMON_FROM_DECK,  // 덱에서 특수 소환
        SUMMON_FROM_HAND,  // 손에서 특수 소환
        RESURRECT,          // 묘지에서 부활
        DRAW,              // 카드를 뽑음
        SEARCH_DECK,       // 덱에서 서치
        BIND,             // 속박 (빙결 대체)
        SILENCE,          // 침묵
        FORCE_ATTACK,     // 강제 공격
        GRANT_KEYWORD,    // 키워드 부여
        MANA_MOD,          // 마나 조작
        DISCARD,          // 손패에서 카드를 버림
        RETURN_TO_HAND,   // 필드 하수인을 손패로 되돌림 (바운스)
        SHUFFLE_TO_DECK,  // 필드 하수인을 덱으로 섞어 넣음
    }

    /// <summary>
    /// 효과가 발동하는 시점(트리거)의 종류를 정의합니다.
    /// </summary>
    public enum EffectTriggerType
    {
        NONE = 0,
        ON_PLAY,          // 카드를 낼 때 발동 (전투의 함성)
        ON_DEATH,          // 사망 시 발동 (죽음의 메아리 및 사망 감지)
        ON_SUMMON,         // 소환 시 발동 (소환 감지)
        ON_TURN_START,     // 턴 시작 시
        ON_TURN_END,       // 턴 종료 시
        ON_ATTACK,         // 공격 시작 시
        ON_DAMAGE,         // 데미지를 입었을 때
        ON_HEAL,           // 회복했을 때
        ON_DRAW,           // 드로우 했을 때
        ON_AURA,           // 오라 지속 효과
    }

    public class EnchantmentInfo
    {
        public int sourceEntityId { get; set; }       // 이 버프를 부여한 주체의 EntityId (오라 삭제 등 추적용)
        public string? sourceCardId { get; set; }     // 버프를 부여한 원본 카드 ID (클라이언트 아이콘 썸네일용)
        public string? sourceCardName { get; set; }   // 버프를 부여한 원본 카드 이름
        public string? description { get; set; }      // 버프 효과 상세 설명 (툴팁 텍스트용)
        public GameEventType effectType { get; set; } // BUFF, GRANT_KEYWORD, COST_MOD 등 어떤 종류의 효과인지
        public int attackMod { get; set; }            // 부여받은 공격력 수치
        public int healthMod { get; set; }            // 부여받은 체력 수치
        public int costMod { get; set; }              // 부여받은 비용 감소/증가 수치
        public string? grantedKeyword { get; set; }   // 부여받은 특수 키워드 (예: "Rush")
        public int duration { get; set; } = 0;        // 지속 턴 수 (0 = 영구 지속, > 0 = 턴 종료 시마다 차감)
        public int spellAmpMod { get; set; } = 0;
        public int spellWeaknessMod { get; set; } = 0;
        public int buffAmpAttackMod { get; set; } = 0;
        public int buffAmpHealthMod { get; set; } = 0;
    }

    /// <summary>
    /// 게임 내에서 발생하는 하나의 '사건'을 정의합니다.
    /// 유니티의 JsonUtility 호환성을 위해 상속보다는 평탄화(Flat)된 구조를 권장합니다.
    /// </summary>
    public class GameEvent
    {
        public GameEventType eventType; 

        public int sourceEntityId; // 사건의 주체 (누가)
        public int targetEntityId; // 사건의 대상 (누구에게)

        public int value;          // 주 수치 (데미지량, 힐량, 공격력 버프 등)
        public int value2;         // 부 수치 (체력 버프 등)

        public string? cardId;      // 발동된 카드 원본 에셋 ID (주문 ID 등)
        public string? stringValue; // 범용 문자열 데이터 (부여된 키워드명 등)

        public EffectTriggerType triggerType; 

        public EntityData? entityData; // 객체 데이터
    }

    // ==================================================================
    // 3. 디버그용 메세지
    // ==================================================================

    /// <summary>
    /// [디버그] 특정 카드 드로우 요청 데이터
    /// </summary>
    public class C_DebugSpecificCardDraw : BaseDebugAction
    {
        public string? targetCardId;
        public bool isOpponent; // 상대방 덱에서 드로우 여부
    }
    
    // [디버그] 덱 정보 요청 (C -> S)
    public class C_DebugRequestDeckInfo : BaseDebugAction
    {
        public bool isOpponent; // 상대방 덱 정보 요청 여부
    }

    // [디버그] 덱 정보 응답 (S -> C)
    public class S_DebugResponseDeckInfo : BaseDebugAction
    {
        public bool isOpponent; // 상대방 덱 정보 여부
        public List<CardInfo>? deckCards; // 현재 덱에 남은 카드 리스트
    }

    // ==================================================================
    // 3. 클라이언트 -> 서버 (C -> S) 메시지
    // ==================================================================

    // 2. 클라이언트 -> 서버 (C -> S) 메시지 영역에 신규 클래스 추가

    /// <summary>
    /// (C->S) 플레이어가 멀리건(시작 손패 교체) 결정을 보냅니다.
    /// </summary>
    public class C_MulliganDecision : BaseGameAction
    {
        // action = "MULLIGAN_DECISION"
        public List<string>? cardInstanceIdsToReplace; // 교체할 카드의 'instanceId' 목록
    }

    /// <summary>
    /// (C->S) 플레이어가 턴 종료 버튼을 누릅니다.
    /// </summary>
    public class C_EndTurn : BaseGameAction
    {
        // action = "END_TURN"
    }

    /// <summary>
    /// (C->S) 플레이어가 손에서 카드를 냅니다.
    /// (하수인, 마법, 멤버 공통 사용)
    /// </summary>
    public class C_PlayCard : BaseGameAction
    {
        // action = "PLAY_CARD"
        public string? handCardInstanceId; // 내가 손에서 내는 카드의 고유 ID
        public int targetEntityId; // 대상의 고유 ID (대상이 없으면 0 또는 -1)
        public int position; // 하수인을 낼 위치 (0~6)
    }

    // ==========================================
    // (C->S) (타겟 선택 완료 또는 취소)
    // ==========================================
    public class C_SelectTargetForPlay : BaseGameAction
    {
        // action = GameActionType.SELECT_TARGET_FOR_PLAY
        public string? CardEntityId { get; set; }     // 대상을 지정한 카드의 InstanceId
        public int selectedEntityId { get; set; }     // 선택한 대상의 EntityId (취소했다면 -1 또는 0 전송)
    }


    /// <summary>
    /// (C->S) 타겟팅이 필요한 카드사용시 타겟요청
    /// </summary>
    public class C_ValidTargetRequest : BaseGameAction
    {
        // action = "VALID_TARGETS_REQUEST"

        // 어떤 카드에 대한 타겟 결과인지 클라이언트가 매칭할 수 있도록 그대로 돌려줌
        public string? CardEntityId { get; set; } 
    }

    /// <summary>
    /// (C->S) 플레이어가 공격을 명령합니다.
    /// </summary>
    public class C_Attack : BaseGameAction
    {
        // action = "ATTACK"
        public int attackerEntityId; // 공격하는 내 개체(하수인/리더/멤버)의 ID
        public int defenderEntityId; // 공격받는 상대 개체(하수인/리더/멤버)의 ID
    }

    /// <summary>
    /// (C->S) 플레이어가 특정 하수인으로 공격을 시도하려고 드래그할 때, 공격 가능한 타겟 목록을 요청합니다.
    /// </summary>
    public class C_ValidAttackTargetsRequest : BaseGameAction
    {
        public C_ValidAttackTargetsRequest()
        {
            action = GameActionType.VALID_ATTACK_TARGETS_REQUEST;
        }
        public int attackerEntityId { get; set; } // 공격을 시작하려는 내 하수인의 고유 ID
    }

    /// <summary>
    /// (C->S) 필드의 멤버 카드가 특정 스킬을 사용하려 할 때 조준 가능한 타겟 목록을 요청합니다.
    /// </summary>
    public class C_ValidMemberSkillTargetsRequest : BaseGameAction
    {
        public C_ValidMemberSkillTargetsRequest()
        {
            action = GameActionType.VALID_MEMBER_SKILL_TARGETS_REQUEST;
        }
        public int entityId { get; set; }  // 멤버 EntityId
        public int skillId { get; set; }   // 스킬 번호
    }

    /// <summary>
    /// (C->S) 필드의 멤버 카드 스킬을 최종 사용합니다.
    /// </summary>
    public class C_UseMemberSkill : BaseGameAction
    {
        public C_UseMemberSkill()
        {
            action = GameActionType.USE_MEMBER_SKILL;
        }
        public int entityId { get; set; }       // 멤버 EntityId
        public int skillId { get; set; }        // 스킬 번호
        public int targetEntityId { get; set; } // 대상 EntityId (비타겟팅일 경우 0)
    }

    /// <summary>
    /// (C->S) 클라이언트가 서버의 선택 요구(REQUEST_CHOICE)에 응답할 때 사용합니다.
    /// </summary>
    public class C_MakeChoice : BaseGameAction
    {
        // action = GameActionType.MAKE_CHOICE

        // 1. 토큰 소환 위치 등을 선택했을 경우의 값 (-1이면 선택안함)
        public int selectedPosition { get; set; } = -1; 
        
        // 2. 발견(Discover) 등 특정 카드를 선택했을 경우의 값
        public string? selectedCardId { get; set; }     
        
        // 3. 특정 하수인(타겟)을 선택했을 경우의 값 (-1이면 선택안함)
        public int selectedEntityId { get; set; } = -1; 
    }

    /// <summary>
    /// (C->S) 플레이어가 항복합니다.
    /// </summary>
    public class C_Concede : BaseGameAction
    {
        // action = "CONCEDE"
    }


    // ==================================================================
    // 4. 서버 -> 클라이언트 (S -> C) 메시지
    // ==================================================================

    /// <summary>
    /// 하나의 논리적 행동(예: 공격, 카드사용)으로 인해 발생한 
    /// 모든 사건의 순차적 기록과 최종 상태를 한 번에 클라이언트에게 전달합니다.
    /// </summary>
    public class S_ActionResolution : BaseGameAction
    {
        // action = "ACTION_RESOLUTION"
        
        // 1. 애니메이션 재생을 위한 순차적 사건 기록 (대본)
        public List<GameEvent> eventLog = new List<GameEvent>();
        
        // 2. 동기화 어긋남 방지를 위한 최종 엔티티 상태 (애니메이션이 끝난 후 최종 보정용)
        public List<EntityData>? finalStateUpdates; 
    }

    /// <summary>
    /// (S->C) 게임 시작 전, 멀리건할 카드 정보를 보냅니다.
    /// </summary>
    public class S_MulliganInfo : BaseGameAction
    {
        // action = "MULLIGAN_INFO"
        public List<CardInfo>? cardsToMulligan; // 교체할 수 있는 카드 5장 목록
        public long mulliganEndTime; // 멀리건 종료 시간 (Unix timestamp)

        // 나와 적의 리더(영웅) 정보
        public EntityData? myLeader;
        public EntityData? enemyLeader;
    }

    /// <summary>
    /// (S->C) 상대방이 멀리건을 확정했을 때 알립니다.
    /// 어떤 슬롯(인덱스)의 카드를 교체했는지 정보를 포함합니다.
    /// </summary>
    public class S_OpponentMulliganStatus : BaseGameAction
    {
        // action = "OPPONENT_MULLIGAN_STATUS"
        public string? opponentUid;
        public List<int>? replacedIndices; // 교체된 카드의 슬롯 번호 (0~4)
        public int replacedCount;          // 교체된 카드 수
        public bool isReady;               // 멀리건 완료 여부
    }

    /// <summary>
    /// (S->C) 멀리건 종료 후, 게임의 최종 상태와 함께 시작을 알립니다.
    /// </summary>
    public class S_GameReady : BaseGameAction
    {
        // action = "GAME_READY"
        public string? firstPlayerUid; // 선공 플레이어의 UID
        public List<CardInfo>? finalHand; // 나의 최종 손패
        public List<CardInfo>? enermyfinalHand; // 적의 최종 손패
        public List<CardInfo>? mySideDeck; // 🌟 나의 사이드덱 카드 정보 목록
        public string? opponentName { get; set; } // 상대방 플레이어 닉네임
    }

    public enum GamePhase
    {
        STANDBY,
        DRAW,
        MAIN,
        END
    }

    /// <summary>
    /// (S->C) 새로운 턴 또는 새로운 페이즈의 시작을 알립니다.
    /// </summary>
    public class S_PhaseStart : BaseGameAction
    {
        // action = "PHASE_START"
        public string? TurnPlayerUid; // 새 턴을 시작하는 플레이어 UID
        public GamePhase phase; // "Standby", "Draw", "Main", "End"
        public CardInfo? drawnCard; // (Draw Phase 전용) 방금 뽑은 카드 (null일 수 있음)
        public bool hasDrawn; // (Draw Phase 전용) 실제로 드로우가 성공했는지 여부 (드로우 봉인 시 false)
        public long turnEndTime; // (Main Phase 전용) 턴 종료 시간 (Unix timestamp)
    }

    /// <summary>
    /// (S->C) 플레이어의 마나 상태를 갱신합니다.
    /// </summary>
    public class S_UpdateMana : BaseGameAction
    {
        // action = "UPDATE_MANA"
        public string? ownerUid; // 누구의 마나 정보인가?
        public int currentMana;
        public int maxMana;
    }

    /// <summary>
    /// (S->C) 플레이어가 카드를 드로우했음을 알립니다. (페이즈 전환 없이 순수 드로우만 처리)
    /// </summary>
    public class S_DrawCard : BaseGameAction
    {
        // action = GameActionType.DRAW_CARD
        public string? playerUid;   // 카드를 뽑은 플레이어의 UID
        public CardInfo? drawnCard; // 뽑은 카드 정보 (상대방에게 보낼 때는 Fog of War를 위해 null 처리)
    }

    /// <summary>
    /// (S->C) 새 카드가 생성되어 손패에 추가되었음을 알립니다. (토큰 창조, 효과 획득 등)
    /// 상대방에게 전송되어 일반 드로우가 아닌 '카드 생성 애니메이션'을 실행할 수 있도록 합니다.
    /// </summary>
    public class S_CardCreated : BaseGameAction
    {
        // action = GameActionType.CARD_CREATED
        public string? playerUid;      // 카드를 생성/획득한 플레이어의 UID
        public CardInfo? card;         // 생성된 카드 정보 (상대방에게는 null로 마스킹)
        public CardInfo? createdCard;  // card와 동일 (클라이언트 접근 편의성)
        public int handCount;          // 카드가 추가된 후 해당 플레이어의 총 손패 장수
    }

    /// <summary>
    /// (S->C) (가장 중요) 게임의 개체(체력, 공격력, 위치, 죽음 등) 상태가
    /// 변경되었음을 알립니다.
    /// </summary>
    public class S_UpdateEntities : BaseGameAction
    {
        // action = "UPDATE_ENTITIES"
        public List<EntityData>? updatedEntities; // 변경되거나, 생성되거나, 죽은 개체들의 목록
    }

    /// <summary>
    // (S->C) 상대방이 카드를 냈음을 알립니다.
    /// </summary>
    public class S_OpponentPlayCard : BaseGameAction
    {
        // action = "OPPONENT_PLAY_CARD"
        public CardInfo? cardPlayed; // 상대가 낸 카드
        public int handNum; // 상대손에 있을때 위치
        public int targetEntityId; // 상대가 지정한 대상
        // TODO: 애니메이션 처리를 위한 추가 정보
        

        public int position;     // 하수인이 놓일 필드 슬롯 번호
        public int entityId;     // 서버가 생성하여 부여한 고유 엔티티 ID
    }

    // ==========================================
    //  (S->C) (타겟 지정 요청)
    // ==========================================
    public class S_RequestTargetForPlay : BaseGameAction
    {
        // action = GameActionType.REQUEST_TARGET_FOR_PLAY
        public string? CardEntityId { get; set; }     // 대상을 요구하는 카드의 InstanceId
        public int position { get; set; }             // 카드가 놓일 필드 위치
        public int targetIndex { get; set; }          // (멀티 타겟 확장용) 현재가 몇 번째 타겟인가 (0, 1, 2...)
        public List<int>? ValidTargetIds { get; set; } // TargetValidator가 계산한 현재 턴의 유효한 타겟 목록 [5]
    }

    /// <summary>
    // (S->C) 타겟 가능한 객체들을 알려줍니다.
    /// </summary>
    public class S_ValidTargetResponse : BaseGameAction
    {
        // action = "VALID_TARGETS_RESPONSE"

        // 클라이언트가 "아, 이 응답은 내가 아까 드래그한 OOO 카드의 결과구나!" 하고 
        // 매칭할 수 있도록 CardEntityId를 같이 돌려주는 것이 안전합니다.
        public string? CardEntityId { get; set; } 

        // TargetValidator가 계산해낸 타겟 가능한 대상들의 EntityId 목록
        public List<int>? ValidTargetIds { get; set; } 
    }

    /// <summary>
    /// (S->C) 서버가 계산한 공격 가능한 타겟들의 EntityId 목록을 클라이언트에 회신합니다.
    /// </summary>
    public class S_ValidAttackTargetsResponse : BaseGameAction
    {
        public S_ValidAttackTargetsResponse()
        {
            action = GameActionType.VALID_ATTACK_TARGETS_RESPONSE;
        }
        public int attackerEntityId { get; set; } // 대상을 조회한 공격 하수인의 고유 ID
        public List<int> validDefenderEntityIds { get; set; } = new List<int>(); // 공격 가능한 대상들의 EntityId 목록
    }

    /// <summary>
    /// (S->C) 멤버 스킬로 조준 가능한 대상들의 EntityId 목록을 회신합니다.
    /// </summary>
    public class S_ValidMemberSkillTargetsResponse : BaseGameAction
    {
        public S_ValidMemberSkillTargetsResponse()
        {
            action = GameActionType.VALID_MEMBER_SKILL_TARGETS_RESPONSE;
        }
        public int entityId { get; set; }
        public int skillId { get; set; }
        public List<int> validTargetIds { get; set; } = new List<int>();
    }

    /// <summary>
    /// (S->C) 멤버 스킬이 성공적으로 발동되었음을 브로드캐스트합니다.
    /// </summary>
    public class S_UseMemberSkillSuccess : BaseGameAction
    {
        public S_UseMemberSkillSuccess()
        {
            action = GameActionType.USE_MEMBER_SKILL_SUCCESS;
        }
        public int memberEntityId { get; set; }
        public int skillId { get; set; }
        public int targetEntityId { get; set; }
        public int currentHp { get; set; }
    }

    /// <summary>
    /// (S->C) 멤버 스킬 발동이 실패했음을 알립니다.
    /// </summary>
    public class S_UseMemberSkillFail : BaseGameAction
    {
        public S_UseMemberSkillFail()
        {
            action = GameActionType.USE_MEMBER_SKILL_FAIL;
        }
        public int memberEntityId { get; set; }
        public int skillId { get; set; }
        public string? reason { get; set; }
    }

    /// <summary>
    /// (S->C) 게임 진행 중(효과 발동 중) 플레이어의 개입이 필요할 때 서버가 전송합니다.
    /// </summary>
    public class S_RequestChoice : BaseGameAction
    {
        // action = GameActionType.REQUEST_CHOICE

        // 어떤 종류의 선택을 요구하는지 명시 (예: "POSITION", "DISCOVER_CARD", "TARGET")
        public string? choiceType { get; set; } 
        
        // 선택해야 하는 개수 (기본 1)
        public int count { get; set; } = 1;     

        // (선택) 카드 발견 등 제한된 선택지가 있을 때 후보 목록을 보낼 수 있습니다.
        public List<CardInfo>? availableOptions { get; set; } 

        // ==========================================
        // 유저 화면 UI에 띄워줄 안내 메세지
        // ==========================================
        public string? message { get; set; } 

        //  이 선택을 요구하게 만든 주체(예: 방금 낸 하수인의 ID) 
        // -> 클라이언트가 이 대상을 밝게 하이라이트 표시할 수 있음
        public int sourceEntityId { get; set; } 
        
        // (선택) 무엇을 소환/사용할 것인지 명시 (예: "token-101")
        public string? targetDataId { get; set; }
    }

    /// <summary>
    /// (S->C) 내가 요청한 카드 내기가 서버에서 정상적으로 처리되었음을 알립니다.
    /// </summary>
    public class S_PlayCardSuccess : BaseGameAction
    {
        // action = "PLAY_CARD_SUCCESS"
        public string? serverInstanceId; // 서버에서 확인한 카드의 고유 ID
    }

    /// <summary>
    /// (S->C) 내가 요청한 카드 내기가 (규칙 위반으로) 실패했음을 알립니다.
    /// </summary>
    public class S_PlayCardFail : BaseGameAction
    {
        // action = "PLAY_CARD_FAIL"
        public string? failedCardInstanceId; // 실패한 카드의 ID
        public string? reason; // 실패 사유 (예: "마나 부족", "유효하지 않은 대상")
    }

    /// <summary>
    /// (신규) (S->C) 손(Hand)에 있는 하나 이상의 카드의 상태(비용, 스탯)가
    /// 변경되었음을 알립니다. (예: '내 손의 모든 하수인에게 +1/+1')
    /// </summary>
    public class S_UpdateHandCards : BaseGameAction
    {
        // action = "UPDATE_HAND_CARDS"
        public List<CardInfo>? updatedCards; // 상태가 변경된 카드들의 '최신 정보' 목록
    }

    /// <summary>
    /// 대전 종료 후 플레이어가 획득한 보상 및 갱신된 재화/성장 상태
    /// </summary>
    public class PlayerMatchRewardResult
    {
        public int EarnedGold { get; set; }
        public int EarnedExp { get; set; }
        public int CurrentGold { get; set; }
        public int CurrentLevel { get; set; }
        public int CurrentExp { get; set; }
        public int MaxExp { get; set; }
        public bool IsLevelUp { get; set; }
        public int ScoreChange { get; set; }
        public int CurrentScore { get; set; }
    }

    /// <summary>
    /// (S->C) 게임이 종료되었음을 알립니다.
    /// 각 플레이어에게 맞춤화된 획득 재화 및 성장 정보가 포함됩니다.
    /// </summary>
    public class S_GameOver : BaseGameAction
    {
        // action = "GAME_OVER"
        public string? winnerUid; // 승자 UID (무승부 시 "DRAW")
        public string? reason; // 종료 사유 (예: "체력 0", "항복", "연결 끊김")

        // 플레이어 맞춤 획득 보상 및 성장 정보
        public int earnedGold;     // 이번 매치로 획득한 골드
        public int earnedExp;      // 이번 매치로 획득한 경험치
        public int currentGold;    // 갱신된 보유 골드
        public int currentLevel;   // 현재 레벨
        public int currentExp;     // 현재 경험치
        public int maxExp;         // 레벨업에 필요한 경험치 (currentLevel * 100)
        public bool isLevelUp;     // 레벨업 여부
        public int scoreChange;    // 점수(랭크 포인트) 변동 (+30, -15 등)
        public int currentScore;   // 갱신된 점수
    }

    /// <summary>
    /// (S->C) 서버가 심각한 오류를 감지했을 때 보냅니다.
    /// </summary>
    public class S_Error : BaseGameAction
    {
        // action = "ERROR"
        public string? message;
    }

    // ==================================================================
    // 사이드덱 관련 통신 패킷 (C <-> S)
    // ==================================================================

    /// <summary>
    /// [C->S] 사이드덱에서 원하는 카드를 손패로 가져오겠다고 요청합니다.
    /// </summary>
    public class C_GetCardFromSideDeck : BaseGameAction
    {
        // action = "GET_CARD_FROM_SIDE_DECK"
        public string? cardInstanceId; // 가져올 카드의 인스턴스 ID (또는 cardId)
    }

    /// <summary>
    /// [S->C] 사이드덱에서 카드를 성공적으로 가져왔음을 브로드캐스트합니다.
    /// </summary>
    public class S_GetCardFromSideDeckSuccess : BaseGameAction
    {
        // action = "GET_CARD_FROM_SIDE_DECK_SUCCESS"
        public string? playerUid;              // 카드를 가져온 플레이어 UID
        public CardInfo? card;                 // 가져온 카드 정보 (본인에게는 상세 정보, 상대에게는 null)
        public int consumedCost;              // 지불한 코스트(마나)
        public int remainingMana;             // 차감 후 남은 마나
        public int remainingSideDeckCount;     // 남은 사이드덱 장수
    }

    /// <summary>
    /// [S->C] 사이드덱 카드 가져오기 요청이 규칙 위반으로 실패했음을 알립니다.
    /// </summary>
    public class S_GetCardFromSideDeckFail : BaseGameAction
    {
        // action = "GET_CARD_FROM_SIDE_DECK_FAIL"
        public string? reason; // 실패 사유 ("마나 부족", "턴 1회 제한 초과", "손패 초과" 등)
    }

    // ==================================================================
    // 감정표현(Emote) 관련 통신 패킷 (C <-> S)
    // ==================================================================

    /// <summary>
    /// [C->S] 플레이어가 감정표현을 사용할 때 서버로 전송하는 패킷입니다.
    /// </summary>
    public class C_SendEmote : BaseGameAction
    {
        // action = GameActionType.SEND_EMOTE
        public string emoteId { get; set; } = "";
        public string message { get; set; } = "";

        public C_SendEmote()
        {
            action = GameActionType.SEND_EMOTE;
        }

        public C_SendEmote(string emoteId, string message = "")
        {
            action = GameActionType.SEND_EMOTE;
            this.emoteId = emoteId;
            this.message = message;
        }
    }

    /// <summary>
    /// [S->C] 방 안의 플레이어가 감정표현을 사용했을 때 서버가 상대방(또는 양쪽)에게 브로드캐스트하는 패킷입니다.
    /// </summary>
    public class S_ReceiveEmote : BaseGameAction
    {
        // action = GameActionType.RECEIVE_EMOTE
        public string senderUid { get; set; } = "";
        public string emoteId { get; set; } = "";
        public string message { get; set; } = "";

        public S_ReceiveEmote()
        {
            action = GameActionType.RECEIVE_EMOTE;
        }
    }

    // ==================================================================
    // 실시간 인게임 행동 로그 및 히스토리 패킷 (S -> C)
    // ==================================================================

    /// <summary>
    /// [S->C] 인게임에서 새로운 행동(소환, 공격, 주문 사용, 사망 등)이 발생했을 때
    /// 하스스톤 스타일의 히스토리 타일 및 로그 UI 생성을 위해 실시간으로 전송되는 패킷입니다.
    /// </summary>
    public class S_NewLogEvent : BaseGameAction
    {
        // action = GameActionType.NEW_LOG_EVENT
        public string? actor { get; set; }          // 행동 주체 (Player UID 또는 "System")
        public string? playerUid { get; set; }      // 행동을 유발한 플레이어 UID (아군/적군 피아식별용)
        public string? actionType { get; set; }     // 행동 종류 ("SUMMON", "ATTACK", "PLAY_CARD", "DEATH", "BUFF" 등)
        public string? message { get; set; }        // 한 줄 요약 텍스트
        public string? sourceCardId { get; set; }   // 행동/버프의 원본 카드 ID (히스토리 썸네일 아이콘용)
        public string? sourceCardName { get; set; } // 행동/버프의 원본 카드 이름
        public int sourceEntityId { get; set; }     // 주체 Entity ID
        public int targetEntityId { get; set; }     // 대상 Entity ID (없으면 0)
        public string? targetCardName { get; set; } // 대상 카드/영웅 이름
        public int value { get; set; }              // 피해량, 회복량, 공격력 등의 주 수치
        public int value2 { get; set; }             // 체력 버프 등의 부 수치
        public long timestamp { get; set; }         // 발생 시각 (Unix 밀리초)
        public List<LogSubEvent>? subEvents { get; set; } // 이 행동으로 인해 파생된 세부 하위 결과 목록

        public S_NewLogEvent()
        {
            action = GameActionType.NEW_LOG_EVENT;
        }
    }

    /// <summary>
    /// 단일 액션 번들 내부에 포함되는 개별 세부 결과 이벤트 데이터입니다.
    /// </summary>
    public class LogSubEvent
    {
        public string type { get; set; } = "";        // "DAMAGE", "HEAL", "DRAW", "SUMMON", "DEATH", "BUFF" 등
        public string targetName { get; set; } = "";  // 대상 이름 (또는 슬롯 번호)
        public int value { get; set; }                // 수치 1 (피해량, 회복량, 드로우 장수 등)
        public int value2 { get; set; }               // 수치 2
    }
}