using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameServer.Effects
{
    /// <summary>
    /// 카드가 효과를 발동할 수 있는 위치를 나타냅니다.
    /// </summary>
    public enum Zone
    {
        None = 0,
        Deck,       // 덱에 있을 때
        Hand,       // 손패에 있을 때
        Field,      // 필드에 소환되었을 때
        Graveyard,  // 무덤에 있을 때
        MemberZone, // 멤버 존에 있을 때
        SideDeck    // 사이드 덱에 있을 때
    }

    /// <summary>
    /// 효과 발동의 조건을 검사하는 인터페이스입니다.
    /// </summary>
    public interface ICondition
    {
        bool Check(GameState state, EffectContext context);

        /// <summary>
        /// 차세대 표준 행동 패킷(GameActionPacket)을 직접 검사하는 메서드입니다.
        /// </summary>
        bool Check(GameState state, GameActionPacket packet, GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            return Check(state, packet.ToEffectContext(ownerCard, ownerEntity));
        }
    }

    /// <summary>
    /// 실제 효과(데미지, 드로우, 버프 등)를 실행하는 인터페이스입니다.
    /// 기존 GameState 로직들이 비동기로 동작하므로 Task를 반환하도록 합니다.
    /// </summary>
    public interface IAction
    {
        Task ExecuteAsync(GameState state, EffectContext context);
    }

    /// <summary>
    /// 효과가 적용될 대상을 찾아내는 인터페이스입니다.
    /// </summary>
    public interface ITargetSelector
    {
        // 상황(Context)을 보고 타겟들의 리스트를 반환합니다.
        IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context);
    }
}

namespace GameServer.Effects
{
    /// <summary>
    /// 효과가 발동할 당시의 모든 상황 정보(Context)를 담는 클래스입니다.
    /// </summary>
    public class EffectContext
    {
        // 1. 효과를 발생시킨 주체 정보
        public string OwnerUid { get; set; }           // 소유자 UID
        public GameCard? SourceCard { get; set; }       // 효과를 가진 원본 카드
        public GameEntity? SourceEntity { get; set; }  // (선택) 필드에 있는 경우 주체 개체

        // 2. 효과의 대상 (타겟팅 효과일 경우)
        public GameEntity? TargetEntity { get; set; }  // (선택) 지정된 대상 개체
        public int TargetPosition { get; set; } = -1;  // (선택) 지정된 필드 위치

        // 3. 이벤트 정보
        public EffectTriggerType Trigger { get; set; } // 이 효과를 유발한 트리거
        public GameCard? TriggerCard { get; set; }     // 이벤트를 발생시킨 원본 카드 (예: 시전된 주문 카드)
        public string? TriggerOwnerUid { get; set; }   // 이벤트를 발생시킨 플레이어 UID
        public object? EventData { get; set; }         // (선택) 이벤트 관련 부가 데이터 (예: 입은 피해량)

        public EffectContext(string ownerUid, GameCard? sourceCard, EffectTriggerType trigger)
        {
            OwnerUid = ownerUid;
            SourceCard = sourceCard;
            Trigger = trigger;
        }
    }
}

namespace GameServer.Effects
{
    /// <summary>
    /// 효과의 종류: 사용 효과(Play) vs 반응형 감시 효과(Reactive)
    /// </summary>
    public enum EffectKind
    {
        Play = 0,      // 마나를 내고 카드를 낼 때 즉시 1회 발동 (주문 본체, 하수인 전투의 함성)
        Reactive = 1   // 패나 필드에 머물며 전장의 사건을 감시하다가 발동 (트리거 감시)
    }

    /// <summary>
    /// 새로운 시스템의 카드 효과 컨테이너 클래스입니다.
    /// 향후 JSON 다형성을 활용해 Firestore에서 이 구조로 데이터를 바로 파싱하게 됩니다.
    /// </summary>
    public class CardEffect
    {
        /// <summary>
        /// 효과의 종류 (Play: 본인 시전 효과, Reactive: 상황 감시 반응 효과)
        /// </summary>
        public EffectKind Kind { get; set; } = EffectKind.Play;

        /// <summary>
        /// 이 효과가 어느 위치(Zone)에 있을 때 활성화되는가? (Reactive 효과용)
        /// </summary>
        public Zone ActiveZone { get; set; } = Zone.Field;

        /// <summary>
        /// 이 효과를 발동시키는 이벤트는 무엇인가? (Reactive 효과용 - 구형 호환)
        /// </summary>
        public EffectTriggerType Trigger { get; set; } = EffectTriggerType.ON_PLAY;

        /// <summary>
        /// 차세대 표준 사건 종류 (GameActionKind)
        /// </summary>
        public GameActionKind TriggerAction { get; set; } = GameActionKind.None;

        /// <summary>
        /// 행동이 일어나는 시점 (Before: 선제/가로채기, After: 사후 반응)
        /// </summary>
        public ActionTiming Timing { get; set; } = ActionTiming.After;

        /// <summary>
        /// 표준 사건(GameActionKind)을 안전하게 반환합니다. (신규 설정 우선, 미설정 시 구형 Trigger 자동 변환)
        /// </summary>
        public GameActionKind GetTriggerAction()
        {
            if (TriggerAction != GameActionKind.None) return TriggerAction;
            return Trigger switch
            {
                EffectTriggerType.ON_PLAY => GameActionKind.PlayCard,
                EffectTriggerType.ON_SUMMON => GameActionKind.Summon,
                EffectTriggerType.ON_DEATH => GameActionKind.Death,
                EffectTriggerType.ON_DAMAGE => GameActionKind.Damage,
                EffectTriggerType.ON_HEAL => GameActionKind.Heal,
                EffectTriggerType.ON_DRAW => GameActionKind.Draw,
                EffectTriggerType.ON_TURN_START => GameActionKind.TurnStart,
                EffectTriggerType.ON_TURN_END => GameActionKind.TurnEnd,
                EffectTriggerType.ON_ATTACK => GameActionKind.Attack,
                _ => GameActionKind.None
            };
        }

        /// <summary>
        /// [타겟팅 조건] 마우스로 대상을 지정할 때 유효한 대상인지 검사하는 필터 목록 (Play 효과용)
        /// </summary>
        public List<ICondition> TargetConditions { get; set; } = new List<ICondition>();

        /// <summary>
        /// [트리거 감시 조건] 이 효과가 발동하기 위해 검사하는 상황/사건 조건 (Reactive 효과용)
        /// </summary>
        public List<ICondition> TriggerConditions { get; set; } = new List<ICondition>();

        /// <summary>
        /// 조건이 맞았을 때 순차적으로 실행될 행동들
        /// </summary>
        public List<IAction> Actions { get; set; } = new List<IAction>();

        /// <summary>
        /// 행동 기본 반복 횟수 (예: 무작위 적에게 1데미지를 '3'번 줍니다)
        /// </summary>
        public int RepeatCount { get; set; } = 1;
    }
}