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
        ATTACK,              // 공격 명령
        USE_MEMBER_ABILITY,  // 멤버 특수 능력 사용
        CONCEDE,             // 항복
        MAKE_CHOICE,         // 클라이언트가 선택 결과를 보냄

        // ==========================================
        // 서버 -> 클라이언트 (S -> C) 메시지
        // ==========================================
        ACTION_RESOLUTION,         // 애니메이션 및 최종 상태 일괄 처리
        MULLIGAN_INFO,             // 멀리건 할 카드 정보
        OPPONENT_MULLIGAN_STATUS,  // 상대방 멀리건 완료 상태
        GAME_READY,                // 게임 시작
        PHASE_START,               // 페이즈 시작 (Standby, Draw, Main, End)
        UPDATE_MANA,               // 마나 갱신
        UPDATE_ENTITIES,           // 개체(필드, 체력 등) 상태 갱신
        OPPONENT_PLAY_CARD,        // 상대방이 카드를 냄
        PLAY_CARD_SUCCESS,         // 카드 사용 성공
        PLAY_CARD_FAIL,            // 카드 사용 실패
        UPDATE_HAND_CARDS,         // 손패 카드 상태(비용, 스탯 등) 갱신
        REQUEST_CHOICE,            // 서버가 클라이언트에게 선택을 요청함
        GAME_OVER,                 // 게임 종료
        ERROR                      // 서버 에러
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
        
        // (신규) 손/덱 버프를 위한 '현재 상태' 필드
        public int currentCost{ get; set; }   // 현재 비용 (버프/너프 적용됨)
        public int currentAttack{ get; set; } // 현재 공격력 (하수인 전용)
        public int currentHealth{ get; set; } // 현재 체력 (하수인 전용)
        // TODO: (고급) "enchantments" (부여된 효과 목록)를 추가할 수 있음
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
        public List<CardKeywords>? keywords = new List<CardKeywords>(); 

        public int position; 
        public bool isMember;
        public bool isLeader;
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
        DEATH,            // 개체 사망
        EFFECT_TRIGGER,   // 특수 효과 발동 연출 (전투의 함성, 죽음의 메아리 등)
        SUMMON ,           // 하수인 소환
        DRAW,              // 카드를 뽑음
        BIND,             // 속박 (빙결 대체)
        SILENCE,          // 침묵
        FORCE_ATTACK,     // 강제 공격
        GRANT_KEYWORD,    // 키워드 부여
        MANA_MOD          // 마나 조작
    }

    /// <summary>
    /// 효과가 발동하는 시점(트리거)의 종류를 정의합니다.
    /// </summary>
    public enum EffectTriggerType
    {
        NONE = 0,
        ON_PLAY,          // 카드를 낼 때 발동 (전투의 함성)
        ON_DEATH,          // 사망 시 발동 (죽음의 메아리)
        ON_TURN_START,     // 턴 시작 시
        ON_TURN_END,       // 턴 종료 시
        ON_ATTACK,        // 공격 시작 시
        ON_DAMAGE,        // 데미지를 입었을떄
        ON_HEAL,          // 회복했을떄
        ON_DRAW,          // 드로우 했을때
        ON_SUMMON,        // 소환할때
    }

    /// <summary>
    /// 게임 내에서 발생하는 하나의 '사건'을 정의합니다.
    /// 유니티의 JsonUtility 호환성을 위해 상속보다는 평탄화(Flat)된 구조를 권장합니다.
    /// </summary>
    public class GameEvent
    {
        // 1. 기존 string에서 enum으로 변경
        public GameEventType eventType; 

        public int sourceEntityId; // 사건의 주체 (누가)
        public int targetEntityId; // 사건의 대상 (누구에게)

        public int value;          // 수치 (데미지량, 힐량 등)

        // 2. 범용 문자열 데이터 (예: 카드 ID 등)
        public string? stringValue;

        // 3. (신규 권장) 효과 발동 트리거를 명확하게 구분하기 위한 속성
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
    }
    
    // [디버그] 덱 정보 요청 (C -> S)
    public class C_DebugRequestDeckInfo : BaseDebugAction
    {
        // 필드 불필요 (debugAction 값만으로 충분)
    }

    // [디버그] 덱 정보 응답 (S -> C)
    public class S_DebugResponseDeckInfo : BaseDebugAction
    {
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
    /// (C->S) 플레이어가 '멤버'의 특수 능력을 사용합니다.
    /// </summary>
    public class C_UseMemberAbility : BaseGameAction
    {
        // action = "USE_MEMBER_ABILITY"
        public int memberEntityId; // 능력을 사용하는 내 '멤버'의 ID
        public string? abilityId; // 사용할 능력의 ID (예: "Ability_Heal_1")
        public int targetEntityId; // 능력 대상 ID (대상이 없으면 0 또는 -1)
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

        // 나와 적의 리더(영웅) 정보
        public EntityData? myLeader;
        public EntityData? enemyLeader;

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
    /// (S->C) 게임이 종료되었음을 알립니다.
    /// </summary>
    public class S_GameOver : BaseGameAction
    {
        // action = "GAME_OVER"
        public string? winnerUid; // 승자 UID
        public string? reason; // 종료 사유 (예: "체력 0", "항복", "연결 끊김")
    }

    /// <summary>
    /// (S->C) 서버가 심각한 오류를 감지했을 때 보냅니다.
    /// </summary>
    public class S_Error : BaseGameAction
    {
        // action = "ERROR"
        public string? message;
    }
}