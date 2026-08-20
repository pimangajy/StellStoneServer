using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text; // StringBuilder 사용 (문자열 조합)
using System.Threading.Tasks; // 비동기 작업(Task) 사용
using Google.Cloud.Firestore;
using Newtonsoft.Json; // JSON 직렬화/역직렬화

namespace GameServer
{
    public enum CardType
    {
        UNKNOWN = 0,
        하수인,
        주문,
        멤버,
        READER
    }

    public enum CardClass
    {
        Gangzi,
        Yuni,
        Huya
    }

    public enum CardTribe
    {
        무소속,
        강도단,
        아르냥,
        바쿠,
        멤버
    }

    public enum CardKeywords
    {
        Default,
        Charge,       // 돌진
        Rush,         // 속공
        Taunt,        // 도발
        DivineShield, //천보
        Poisonous,    // 독성
        Stealth,      // 은신
        Lifesteal,    // 생흡
        Windfury,     // 질풍
        Bind          // 속박
    }

    public enum TargetRule
    {
        None = 0,               // 대상 없음 (자동/광역/랜덤 등)

        // --- 단일 지정 (플레이어가 직접 선택) ---
        Target_All = 1,                 // 모든 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Minion = 2,              // 모든 하수인 중 1개 선택
        Target_Enemy_All = 3,           // 적 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Enemy_Minion = 4,        // 적 하수인 중 1개 선택
        Target_Friend_All = 5,          // 아군 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Friend_Minion = 6,       // 아군 하수인 중 1개 선택
        Target_Member = 7,              // 멤버중 하나
    }

    public enum CardRarity
    {
        common, 
        rare, 
        epic, 
        legendary

    }

    public enum CardExpansion
    {
        기본,
    }

    // ==================================================================
    // 1. 데이터 모델 (GameCard, GameEntity)
    // ==================================================================

    /// <summary>
    /// [카드 정보 클래스]
    /// 덱이나 손패에 있을 때의 '카드' 그 자체를 나타냅니다.
    /// DB에서 불러온 원본 스탯과, 게임 중 버프/너프된 현재 스탯을 모두 가집니다.
    /// </summary>
    public class GameCard
    {
        public string CardId { get; private set; } // 원본 카드 ID (예: "Fireball")
        public string InstanceId { get; set; }     // 이 게임에서의 고유 ID (예: "Hand_PlayerA_1")
        public string OwnerUid { get; set; } = ""; 
        public string CardName { get; private set; }
        
        // --- 원본 스탯 (절대 변하지 않는 기준값) ---
        public int OriginalCost { get; private set; }
        public int OriginalAttack { get; private set; }
        public int OriginalHealth { get; private set; }
        public List<CardKeywords>? OriginalKeywords { get; private set; }

        public CardType? Type;
        public CardClass? Class { get; private set; }  // 직업
        public CardTribe? Tribe { get; private set; }  // 종족 (강도단 등)
        public CardRarity? Rarity { get; private set; } 
        public bool TargetRule { get; private set; } // 타겟팅 규칙

        // 새로운 시스템용 효과 리스트와 Zone 상태
        public GameServer.Effects.Zone CurrentZone { get; private set; } = GameServer.Effects.Zone.None;
        public List<GameServer.Effects.CardEffect> NewEffects { get; set; } = new List<GameServer.Effects.CardEffect>();

        // 카드의 위치가 바뀔 때 호출되는 핵심 메서드
        public void UpdateZone(GameServer.Effects.Zone newZone, GameServer.Effects.EventSystem eventSystem)
        {
            if (CurrentZone == newZone) return; // 위치가 그대로면 무시

            // 1. 기존 Zone에서 벗어났으므로, 기존 위치에서 발동하던 효과들을 구독 해지
            foreach (var effect in NewEffects)
            {
                if (effect.ActiveZone == CurrentZone)
                {
                    eventSystem.Unsubscribe(effect);
                }
            }

            CurrentZone = newZone;

            // 2. 새로운 Zone에 진입했으므로, 새 위치에서 발동해야 할 효과들을 구독 등록
            foreach (var effect in NewEffects)
            {
                if (effect.ActiveZone == CurrentZone)
                {
                    eventSystem.Subscribe(effect, this);
                }
            }
        }

        // --- 현재 스탯 (게임 중 버프/너프에 의해 변하는 값) ---
        public int CurrentCost { get; set; }
        public int CurrentAttack { get; set; }
        public int CurrentHealth { get; set; }
        public List<CardKeywords> CurrentKeywords { get; set; }
        public List<EnchantmentInfo> Enchantments { get; set; } = new List<EnchantmentInfo>();

        // [생성자 1] 일반 카드 생성 (DB에서 데이터 로드)
        public GameCard(string cardId, string instanceId)
        {
            CardId = cardId;
            InstanceId = instanceId;
            
            // 싱글톤 DB 매니저에게서 데이터 가져오기
            ServerCardData? data = ServerCardDatabase.Instance.GetCardData(cardId);

            if (data != null)
            {
                // DB 데이터가 있으면 원본 스탯 설정
                CardName = data.Name ?? "이름 없음";
                OriginalCost = data.Cost;
                OriginalAttack = data.AttackValue;
                OriginalHealth = data.HealthValue;
                OriginalKeywords = data.Keywords != null ? new List<CardKeywords>(data.Keywords) : new List<CardKeywords>();
                
                Type = data.CardType ?? CardType.UNKNOWN;
                Class = data.Class ?? CardClass.Gangzi;
                Tribe = data.Tribe ?? CardTribe.강도단;
                Rarity = data.Rarity ?? CardRarity.common; 
                TargetRule = data.Targeting ?? false;
                NewEffects = data.GetNewParsedEffects(); 
            }
            else
            {
                // DB에 없는 카드일 경우 (에러 방지용 기본값)
                Console.WriteLine($"[GameCard] ⚠️ DB에서 카드 데이터를 찾을 수 없습니다: {cardId}");
                CardName = "알 수 없는 카드";
                OriginalCost = 1; OriginalAttack = 1; OriginalHealth = 1;
                Type = CardType.하수인; Class = CardClass.Gangzi; Tribe = CardTribe.무소속; TargetRule = false;
                OriginalKeywords = new List<CardKeywords> { CardKeywords.Default };
                NewEffects = new List<GameServer.Effects.CardEffect>();
            }

            // 초기에는 현재 스탯 = 원본 스탯
            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<CardKeywords>(OriginalKeywords);
        }

        // [생성자 2] 영웅(Leader) 카드 생성 (코드에서 직접 생성)
        public GameCard(string instanceId, CardClass playerClass, CardTribe tribe, int health = 30)
        {
            CardId = $"LEADER_{playerClass}";
            InstanceId = instanceId;
            CardName = $"{playerClass} 영웅";

            OriginalCost = 0;
            OriginalAttack = 0;
            OriginalHealth = health;
            OriginalKeywords = new List<CardKeywords> { CardKeywords.Default };

            Type = CardType.READER;
            Class = playerClass;
            Tribe = tribe;
            Rarity = CardRarity.legendary; 
            TargetRule = false;

            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<CardKeywords>(OriginalKeywords);
        }

        // 클라이언트에게 보낼 데이터(CardInfo)로 변환
        public CardInfo ToCardInfo()
        {
            return new CardInfo
            {
                cardId = this.CardId,
                instanceId = this.InstanceId,
                cardName = this.CardName,
                currentCost = this.CurrentCost,
                currentAttack = this.CurrentAttack,
                currentHealth = this.CurrentHealth,
                enchantments = new List<EnchantmentInfo>(this.Enchantments)
            };
        }
    }

    /// <summary>
    /// [필드 개체 클래스]
    /// 필드 위에 소환된 하수인이나 영웅을 나타냅니다.
    /// 'EntityId'라는 고유 번호(정수)를 가집니다.
    /// </summary>
    public class GameEntity
    {
        public int EntityId { get; private set; } // 필드 위에서의 고유 번호 (100, 101...)
        public GameCard SourceCard { get; private set; } // 이 하수인을 만든 원본 카드 정보
        public string OwnerUid { get; private set; } // 누구 소유인지
        
        // 전투 관련 스탯
        public int Attack { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public bool CanAttack { get; set; }   // 이번 턴에 공격 가능한가?
        public bool HasAttacked { get; set; } // 이번 턴에 이미 공격했는가?
        public List<CardKeywords>? Keywords { get; set; }
        public CardTribe? Tribe { get; set; }
        // 필드 개체의 버프 기록 (보통 SourceCard의 Enchantments와 동기화하거나 별도 관리)
        public List<EnchantmentInfo> Enchantments { get; set; } = new List<EnchantmentInfo>();
        public int Position { get; set; }
        public bool IsMember { get; set; }
        public bool IsLeader { get; set; }

         // 체력과 무관하게 강제 파괴(처치) 대상이 되었는지를 나타내는 플래그
        public bool IsDestroyed { get; set; } = false; 

        public GameEntity(int entityId, GameCard sourceCard, string ownerUid)
        {
            EntityId = entityId;
            SourceCard = sourceCard;
            OwnerUid = ownerUid;
            
            // 소환 시점의 카드 스탯을 가져옴
            Attack = sourceCard.CurrentAttack;
            Health = sourceCard.CurrentHealth;
            MaxHealth = sourceCard.CurrentHealth;
            Keywords = new List<CardKeywords>(sourceCard.CurrentKeywords);
            Tribe = sourceCard.Tribe; 
            
            
            // 속공(Rush)이나 돌진(Charge)이 있으면 바로 공격 가능
            bool hasCharge = Keywords.Contains(CardKeywords.Charge);
            bool hasRush = Keywords.Contains(CardKeywords.Rush);

            CanAttack = hasCharge || hasRush; 
            // CanAttack = true; // [테스트용]
            HasAttacked = false;
        }

        // 클라이언트에게 보낼 데이터(EntityData)로 변환
        public EntityData ToEntityData()
        {
            return new EntityData
            {
                entityId = this.EntityId,
                cardId = this.SourceCard.CardId,
                cardName = this.SourceCard.CardName,
                ownerUid = this.OwnerUid,
                attack = this.Attack,
                health = this.Health,
                maxHealth = this.MaxHealth,
                canAttack = this.CanAttack,
                hasAttacked = this.HasAttacked,
                keywords = this.Keywords,
                enchantments = new List<EnchantmentInfo>(this.Enchantments),
                position = this.Position, 
                isMember = this.IsMember,
                isLeader = this.IsLeader,
            };
        }
    }

    // 로그 데이터 구조 정의
    public class GameLogEvent
    {
         public DateTime Timestamp { get; set; }
        public string Actor { get; set; } = "";     // 행동한 주체 (예: "PlayerA", "PlayerB", "System")
        public string ActionType { get; set; } = "";// 액션 종류 (예: "PLAY_CARD", "ATTACK", "DAMAGE", "HEAL", "PHASE_CHANGE")
        public string Message { get; set; } = "";   // 사람이 읽기 쉬운 요약 메세지 (대시보드 출력용)
        public object? Details { get; set; }        // 구체적인 타겟 ID나 데미지 수치 등 (인게임 UI 처리용 JSON 객체)
    }
    // ==================================================================
    // 1. PlayerState 클래스
    // ==================================================================

    /// <summary>
    /// 플레이어 한 명의 게임 내 상태(덱, 손패, 필드, 자원 등)를 관리하는 클래스입니다.
    /// </summary>
    public class PlayerState
    {
        public string Uid { get; private set; }              // 플레이어 고유 식별자
        public GamePlayer PlayerRef { get; private set; }   // 네트워크 통신을 위한 플레이어 객체 참조
        public GameEntity Leader { get; private set; }      // 플레이어의 영웅(본체) 개체
        
        public List<GameCard> Deck { get; private set; }    // 현재 덱에 남은 카드 목록
        public List<GameCard> Hand { get; private set; }    // 현재 손에 들고 있는 카드 목록
        public List<GameCard> Graveyard { get; private set; } = new List<GameCard>();

        // 필드 슬롯: [0]~[4]까지 총 5칸의 하수인 배치 구역
        public GameEntity?[] Field { get; private set; } = new GameEntity?[5];
        
        // 멤버 존: 특정 '멤버' 타입의 개체가 배치되는 전용 구역 (1칸)
        public GameEntity?[] MemberZone { get; private set; } = new GameEntity?[1];
        
        public int CurrentMana { get; set; }                // 이번 턴에 사용 가능한 현재 마나
        public int MaxMana = 0;                             // 이번 턴의 최대 마나 한도
        
        private int _nextInstanceId = 1;                    // 카드 인스턴스 ID 발급을 위한 카운터

        // PlayerState 전용 이벤트
        public event Action<string, string>? OnCardDrawn;

        public PlayerState(GamePlayer player, GameEntity leader)
        {
            Uid = player.Uid;
            PlayerRef = player;
            Leader = leader;
            Deck = new List<GameCard>();
            Hand = new List<GameCard>();
            Graveyard = new List<GameCard>();

            // 게임 시작 시 플레이어의 덱 데이터를 기반으로 GameCard 객체들을 생성하여 덱에 채움
            if (player.Deck != null && player.Deck.cardIds != null)
            {
                foreach (string cardId in player.Deck.cardIds)
                {
                    // 덱 내의 카드들도 구분을 위해 임시 인스턴스 ID 부여
                    string instanceId = $"DeckCard_{Uid}_{_nextInstanceId++}";
                    Deck.Add(new GameCard(cardId, instanceId) { OwnerUid = Uid });
                }
            }
        }

        /// <summary>
        /// 덱에 있는 카드들의 순서를 무작위로 섞습니다.
        /// </summary>
        public void ShuffleDeck(Random rng)
        {
            int n = Deck.Count;
            while (n > 1) 
            { 
                n--; 
                int k = rng.Next(n + 1); 
                (Deck[k], Deck[n]) = (Deck[n], Deck[k]); 
            }
        }

        /// <summary>
        /// 덱에서 카드 한 장을 뽑아 손패(Hand)로 이동시킵니다.
        /// </summary>
        /// <returns>뽑은 카드 객체, 덱이 비어있으면 null</returns>
        public GameCard? DrawCard()
        {
            if (Deck.Count == 0) return null; // 탈진 상태 등 처리 가능 구역
            
            // 덱의 가장 마지막(위) 카드를 가져옴
            GameCard card = Deck[Deck.Count - 1];
            Deck.RemoveAt(Deck.Count - 1);
            
            // 손으로 들어올 때 클라이언트와 통신할 고유 인스턴스 ID 새로 부여
            card.InstanceId = $"HandCard_{Uid}_{_nextInstanceId++}";
            Hand.Add(card);
            // 2. 나(PlayerState) 카드 뽑았다고 소리침!
            OnCardDrawn?.Invoke(this.Uid, card.InstanceId);
            return card;
        }
        
    }
    


    // ==================================================================
    // 2. GameState 클래스 (핵심 로직 엔진)
    // ==================================================================

    /// <summary>
    /// 실제 게임의 흐름(턴, 페이즈, 규칙 검사, 전투 판정)을 총괄하는 핵심 엔진 클래스입니다.
    /// </summary>
    public class GameState
    {
        private readonly GameRoom _room;                     // 메시지 전송을 위한 방 참조
        private readonly PlayerState _playerA;               // 플레이어 A의 상태
        private readonly PlayerState _playerB;               // 플레이어 B의 상태
        
        // 필드 위의 모든 개체(영웅, 하수인, 멤버)를 ID로 빠르게 찾기 위한 저장소
        private readonly Dictionary<int, GameEntity> _allEntities = new Dictionary<int, GameEntity>();
        
        public Effects.EventSystem EventSystem { get; private set; }

        private string _currentTurnPlayerUid = "";           // 현재 턴을 진행 중인 플레이어 UID
        private string _firstPlayerUid = "";                // 이번 게임의 선공 플레이어 UID
        private string _secondPlayerUid = "";               // 이번 게임의 후공 플레이어 UID
        private string? _currentPhase;                       // 현재 게임 단계 (Mulligan, Main 등)
        
        public Random Rng { get; private set; } = new Random();
        private int _nextGlobalEntityId = 100;               // 하수인/멤버 생성을 위한 전역 개체 ID 카운터
        private bool _isGameOver = false;                   // 게임 종료 여부
        private readonly object _lock = new object();        // 멀티스레드 환경에서의 데이터 안전을 위한 락 객체
        private bool _isGameStarted = false;                // 게임 루프 시작 여부

        private long _turnEndTime = 0;                      // 턴 제한 시간

        // 멀리건 결정을 저장 (양쪽 모두 완료될 때까지 대기용)
        private readonly Dictionary<string, C_MulliganDecision?> _mulliganDecisions = new Dictionary<string, C_MulliganDecision?>();
        
        // 클라이언트에 한꺼번에 보낼 변경된 개체 데이터 목록
        private List<GameEvent> _eventBuffer = new List<GameEvent>();
        private List<EntityData> _pendingUpdates = new List<EntityData>();

        // ==========================================
        // 비동기 액션 큐 (Event Queue)
        // ==========================================
        private readonly Queue<Func<Task>> _actionQueue = new Queue<Func<Task>>();
        private bool _isProcessingQueue = false; // 큐 중복 실행 방지용 플래그

        // ==========================================
        // 클라이언트 응답 대기용 메모리
        // ==========================================
        public string CurrentPhase => _currentPhase ?? ""; // 외부에서 읽기 위한 Getter
        private string _pendingChoiceType = ""; 
        private string _pendingChoiceData = ""; 
        private int _pendingChoiceCount = 0;

        // 하수인 소환시 타겟변수들
        private string _pendingPlayHandInstanceId = ""; // 대기 중인 카드의 InstanceId
        private int _pendingPlayPosition = -1;          // 대기 중인 소환 슬롯 인덱스
        private string _pendingPlaySenderUid = "";      // 카드를 낸 유저의 UID
        private List<int> _pendingPlayTargets = new List<int>(); // 수집된 타겟 EntityId 목록

        /// <summary>
        /// (신규) 이벤트를 로그에 기록합니다.
        /// </summary>
        public void LogEvent(GameEventType type, int sourceId, int targetId = 0, int val = 0, string? strVal = null, EffectTriggerType effectTriggerType = EffectTriggerType.NONE, EntityData? entityData = null)
        {
            _eventBuffer.Add(new GameEvent
            {
                eventType = type,
                sourceEntityId = sourceId,
                targetEntityId = targetId,
                value = val,
                stringValue = strVal,
                triggerType = effectTriggerType,
                entityData = entityData,
            });
        }

        // 로그 보관 리스트
         private List<GameLogEvent> _actionLogs = new List<GameLogEvent>();
         // 방송국(이벤트) 설립
        public event Action<string, string>? OnCardPlayed;    // 누가, 무슨 카드를 냈는가?
        public event Action<string, string>? OnAttacked; // 누가, 누구를, 데미지 몇으로 공격하려 했는가?
        public event Action<string, string, int>? ApllyAttacked; // 누가, 누구를, 데미지 몇으로 공격했는가?
        public event Action<string, string, int>? OnMinionSummoned; // 플레이어 UID, 하수인 이름, 배치 슬롯 위치
        public event Action<string, string>? OnMinionDestroyed;     // 플레이어 UID, 하수인 이름
        public event Action<string, int, int>? OnDamageApplied;     // 피해 대상 이름, 피해량, 원인 엔티티 ID
        public event Action<string, int, int>? OnHealApplied;       // 회복 대상 이름, 회복량, 원인 엔티티 ID 
        public event Action<string, string, string>? EffectLog;     // 대상, 시전자, 효과

        

        public GameState(GameRoom room, GamePlayer playerA, GamePlayer playerB)
        {
            _room = room;
            
            // --- 1. 플레이어 A 영웅 개체 생성 ---
            // 문자열인 직업(deckClass)을 CardClass Enum으로 안전하게 변환
            CardClass classA = CardClass.Gangzi; // 파싱 실패 시 기본 직업
            if (Enum.TryParse<CardClass>(playerA.Deck?.deckClass, true, out var parsedClassA))
            {
                classA = parsedClassA;
            }
            // (InstanceId, 직업 Enum, 종족 Enum, 체력) 순으로 전달
            GameCard leaderCardA = new GameCard("Leader_A_Instance", classA, CardTribe.무소속, 30) { OwnerUid = playerA.Uid };
            GameEntity leaderA = new GameEntity(10000, leaderCardA, playerA.Uid);
            leaderA.IsLeader = true;


            // --- 2. 플레이어 B 영웅 개체 생성 ---
            CardClass classB = CardClass.Gangzi;
            if (Enum.TryParse<CardClass>(playerB.Deck?.deckClass, true, out var parsedClassB))
            {
                classB = parsedClassB;
            }
            GameCard leaderCardB = new GameCard("Leader_B_Instance", classB, CardTribe.무소속, 30) { OwnerUid = playerB.Uid };
            GameEntity leaderB = new GameEntity(20000, leaderCardB, playerB.Uid);
            leaderB.IsLeader = true;

            // 2. 전역 개체 목록에 등록 (ID 1, 2번은 영웅 고정)
            _allEntities.Add(leaderA.EntityId, leaderA);
            _allEntities.Add(leaderB.EntityId, leaderB);
            
            // 3. 플레이어 상태 초기화
            _playerA = new PlayerState(playerA, leaderA);
            _playerB = new PlayerState(playerB, leaderB);
            
            // 4. 멀리건 상태 초기화
            _mulliganDecisions[_playerA.Uid] = null;
            _mulliganDecisions[_playerB.Uid] = null;
            
            EventSystem = new Effects.EventSystem(this);

            // Player A와 B가 "카드 뽑았다"고 소리치면, GameState가 그걸 듣고 AddLog를 실행함
            _playerA.OnCardDrawn += (playerName, id) => {
                AddLog(playerName, "DRAW", $"{playerName}이(가) {id}카드를 뽑았습니다.");
            };

            _playerB.OnCardDrawn += (playerName, id) => {
                AddLog(playerName, "DRAW", $"{playerName}이(가) {id}카드를 뽑았습니다.");
            };

            // 게임이 생성될 때 로그 시스템을 이벤트에 연결(구독)시킵니다.
            InitializeLogger(); 

            foreach (var card in _playerA.Deck) card.UpdateZone(GameServer.Effects.Zone.Deck, EventSystem);
            foreach (var card in _playerB.Deck) card.UpdateZone(GameServer.Effects.Zone.Deck, EventSystem);
        }


        /// <summary>
        /// 대시보드에 로그 남기기
        /// </summary>
        /// <param name="targetName"></param>
        /// <param name="sourceName"></param>
        /// <param name="effectName"></param>
        public void RaiseEffectLog(string targetName, string? sourceName, string effectName)
        {
            // 시전자(sourceName)가 null일 경우를 대비해 기본값을 지정해 줍니다.
            string caster = sourceName ?? "System";
            
            // 이벤트를 GameState 내부에서 안전하게 실행합니다.
            EffectLog?.Invoke(targetName, caster, effectName);
        }

        private void InitializeLogger()
        {
            // 카드를 냈을 때 알아서 로그 작성
            this.OnCardPlayed += (playerName, cardName) => {
                AddLog(playerName, "PLAY_CARD", $"{playerName}이(가) [{cardName}]을(를) 사용했습니다.");
            };

            // 공격시도 했을때 알아서 로그 작성
            /*
            this.OnAttacked += (attackerName, targetName) => {
                AddLog(attackerName, "ATTACK", $"{attackerName}이(가) {targetName}에게 공격을 시도합니다!");
            };
            */

            // 공격했을 때 알아서 로그 작성
            this.ApllyAttacked += (attackerName, targetName, damage) => {
                AddLog(attackerName, "ATTACK", $"{attackerName}이(가) {targetName}에게 {damage}의 피해를 입혔습니다!");
            };

            // =================================================================
            // 하수인이 필드에 소환되었을 때 로그 작성
            // =================================================================
            this.OnMinionSummoned += (playerName, minionName, position) => {
                AddLog(playerName, "SUMMON", $"{playerName}의 필드 {position}번 슬롯에 [{minionName}] 하수인이 소환되었습니다.");
            };

            // =================================================================
            // 하수인이 필드에서 파괴(사망)되었을 때 로그 작성
            // =================================================================
            this.OnMinionDestroyed += (playerName, minionName) => {
                AddLog(playerName, "DEATH", $"{playerName}의 하수인 [{minionName}]이(가) 전장에서 파괴되었습니다.");
            };

            // =================================================================
            // 하수인이나 영웅이 피해(데미지)를 입었을 때 로그 작성
            // =================================================================
            this.OnDamageApplied += (targetName, damage, sourceId) => {
                AddLog("System", "DAMAGE", $"[{targetName}]이(가) {damage}의 피해를 입었습니다. (피해 원인 ID: {sourceId})");
            };

            // =================================================================
            // 하수인이나 영웅이 회복(치유)되었을 때 로그 작성
            // =================================================================
            this.OnHealApplied += (targetName, healAmount, sourceId) => {
                AddLog("System", "HEAL", $"[{targetName}]이(가) 체력을 {healAmount}만큼 회복했습니다. (치유 원인 ID: {sourceId})");
            };

            // =================================================================
            // 카드 효과가 발동되었을 때 로그 작성
            // =================================================================
            this.EffectLog += (targetName, sourceId, effectname) =>
            {
                AddLog("System","EFFECT",  $"[{sourceId}]이(가) {targetName}에게 {effectname}을 사용했습니다.");
            };
        }

        /// <summary>
        /// 클라이언트(WebSocket)로부터 받은 JSON 메시지를 해석하고 권한을 확인하여 실행합니다.
        /// </summary>
        public async Task HandlePlayerActionAsync(string senderUid, string messageJson)
        {
            // ==========================================
            // [신규] 1. 디버그 액션 우선 처리
            // JSON 문자열에 "debugAction" 키가 있다면 디버그 파이프라인으로 보냅니다.
            // ==========================================
            if (messageJson.Contains("\"debugAction\""))
            {
                var debugActionBase = JsonConvert.DeserializeObject<BaseDebugAction>(messageJson);
                if (debugActionBase != null && debugActionBase.debugAction != DebugAction.NONE)
                {
                    await DispatchDebugActionAsync(senderUid, debugActionBase.debugAction, messageJson);
                    return; // 디버그 처리를 했으므로 일반 로직은 타지 않고 종료
                }
            }

            // ==========================================
            // 2. 기존 상용 게임 액션 처리
            // ==========================================
            BaseGameAction? baseAction = null;
            try { baseAction = JsonConvert.DeserializeObject<BaseGameAction>(messageJson); } catch { return; }
            if (baseAction == null) return;

            // ... (기존 Mulligan / Main Phase 검증 코드 동일하게 유지) ...
            if (_currentPhase == "Mulligan")
            {
                PlayerState currentPlayer = GetPlayerState(_currentTurnPlayerUid);
                await DispatchActionAsync(senderUid, GameActionType.MULLIGAN_DECISION, messageJson);
            }
            else
            {
                PlayerState turnOwner = GetPlayerState(_currentTurnPlayerUid);
                await DispatchActionAsync(senderUid, baseAction.action, messageJson);
            }
        }

        /// <summary>
        /// [신규] 디버그 전용 라우팅(Dispatch) 함수
        /// </summary>
        private async Task DispatchDebugActionAsync(string uid, DebugAction debugAction, string json)
        {
            switch (debugAction)
            {
                // ==========================================
                // 특정 카드 드로우 요청 처리
                // ==========================================
                case DebugAction.SpecificCardDraw:
                    var drawReq = JsonConvert.DeserializeObject<C_DebugSpecificCardDraw>(json);
                    if (drawReq != null && !string.IsNullOrEmpty(drawReq.targetCardId))
                    {
                        // 이전에 만들어두신 카드를 뽑는 함수 호출
                        await DrawSpecificCardFromDeckAsync(uid, drawReq.targetCardId);
                    }
                    break;
                // ==========================================
                // 덱 정보 요청 처리
                // ==========================================
                case DebugAction.RequestDeckInfo:
                    PlayerState player = GetPlayerState(uid);
                    
                    // 1. 현재 덱에 있는 카드들을 CardInfo 리스트로 변환
                    List<CardInfo> currentDeckInfo = player.Deck.Select(c => c.ToCardInfo()).ToList();

                    // 2. 응답 패킷 생성
                    var responseMsg = new S_DebugResponseDeckInfo
                    {
                        debugAction = DebugAction.ResponseDeckInfo,
                        deckCards = currentDeckInfo
                    };

                    // 3. 요청한 클라이언트에게만 전송
                    await _room.SendMessageToPlayerAsync(player.PlayerRef, JsonConvert.SerializeObject(responseMsg));
                    break;
            }
        }

        /// <summary>
        /// 검증된 UID와 액션 종류에 따라 실제 로직 함수를 호출합니다.
        /// </summary>
        private async Task DispatchActionAsync(string uid, GameActionType action, string json)
        {
            // 🏳️ 항복은 본인 턴 여부나 현재 페이즈와 관계없이 언제든지 처리 가능
            if (action == GameActionType.CONCEDE)
            {
                await ProcessConcedeAsync(uid);
                return;
            }

            // 턴 주인이 아닌데 멀리건 페이즈도 아니라면 무시
            if (_currentPhase != "Mulligan" && uid != _currentTurnPlayerUid)
            {
                Console.WriteLine($"[DispatchActionAsync] ❌ 실패: {uid}님의 턴이 아닙니다. (현재 턴: {_currentTurnPlayerUid}, 페이즈: {_currentPhase})");
                return;
            }

            switch (action)
            {
                case GameActionType.MULLIGAN_DECISION: 
                    var mul = JsonConvert.DeserializeObject<C_MulliganDecision>(json);
                    if (mul != null) await ProcessMulliganDecisionAsync(uid, mul);
                    break;
                case GameActionType.END_TURN:          
                    await ProcessEndTurnAsync(uid);
                    break;
                case GameActionType.PLAY_CARD:        
                    var play = JsonConvert.DeserializeObject<C_PlayCard>(json);
                    if (play != null) await ProcessPlayCardAsync(uid, play);
                    else Console.WriteLine($"[DispatchActionAsync] ❌ 실패: C_PlayCard JSON 역직렬화에 실패했습니다.");
                    break;
                case GameActionType.VALID_TARGETS_REQUEST:
                    var target = JsonConvert.DeserializeObject<C_ValidTargetRequest>(json);
                    if (target != null) await ProcessTargetChoiceAsync(uid, target);
                    break;
                case GameActionType.SELECT_TARGET_FOR_PLAY:
                var selectTarget = JsonConvert.DeserializeObject<C_SelectTargetForPlay>(json);
                if (selectTarget != null) await ProcessSelectTargetForPlayAsync(uid, selectTarget);
                else Console.WriteLine($"[DispatchActionAsync] ❌ 실패: C_SelectTargetForPlay JSON 역직렬화에 실패했습니다.");
                break;
                case GameActionType.VALID_ATTACK_TARGETS_REQUEST:
                var atkTargetReq = JsonConvert.DeserializeObject<C_ValidAttackTargetsRequest>(json);
                if (atkTargetReq != null) await ProcessValidAttackTargetsRequestAsync(uid, atkTargetReq);
                break;
                case GameActionType.MAKE_CHOICE:
                    if (_currentPhase == "AWAITING_CHOICE")
                    {
                        var choice = JsonConvert.DeserializeObject<C_MakeChoice>(json);
                        if (choice != null) await ProcessMakeChoiceAsync(uid, choice);
                    }
                    break;
                case GameActionType.ATTACK:           
                    var atk = JsonConvert.DeserializeObject<C_Attack>(json);
                    if (atk != null) await ProcessAttackAsync(uid, atk);
                    break;
            }
        }

        /// <summary>
        /// 플레이어가 항복(CONCEDE)했을 때 처리합니다.
        /// </summary>
        private async Task ProcessConcedeAsync(string concedingUid)
        {
            Console.WriteLine($"[GameState] 🏳️ 플레이어 항복 수신: {concedingUid}");
            if (_isGameOver) return;

            // 항복한 플레이어의 상대방을 승자로 결정
            string winnerUid = (concedingUid == _playerA.Uid) ? _playerB.Uid : _playerA.Uid;
            await EndGameAsync(winnerUid, "항복");
        }

        /// <summary>
        /// [수정됨] 효과 발동 중 플레이어의 개입이 필요할 때 호출합니다.
        /// </summary>
        public async Task RequestPlayerChoiceAsync(
            string uid, 
            string choiceType, 
            string choiceData, 
            string uiMessage,       // [신규] UI 안내 텍스트
            int sourceEntityId = 0, // [신규] 원인 개체 ID
            int count = 1)
        {
            // 1. 상태 기억
            _pendingChoiceType = choiceType;
            _pendingChoiceData = choiceData;
            _pendingChoiceCount = count;

            // 2. 화면 최신화
            await FlushUpdatesAsync();
            _currentPhase = "AWAITING_CHOICE";

            // 3. 친절한 안내 메세지와 함께 클라이언트에게 패킷 전송
            var reqMsg = new S_RequestChoice
            {
                action = GameActionType.REQUEST_CHOICE,
                choiceType = choiceType,
                count = count,
                message = uiMessage,               // 유저 화면 중앙에 띄울 텍스트
                sourceEntityId = sourceEntityId,   // 빛나게 할 하수인 ID
                targetDataId = choiceData          // 토큰 ID 등
            };
            
            PlayerState p = GetPlayerState(uid);
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(reqMsg));
        }

        /// <summary>
        /// 액션 큐에 쌓인 작업들을 하나씩 순차적으로 실행합니다.
        /// 중간에 플레이어 입력 대기 상태(AWAITING_CHOICE)가 되면 실행을 즉시 일시 정지합니다.
        /// </summary>
        public async Task ProcessActionQueueAsync()
        {
            // 이미 큐를 처리 중이라면 중복 실행 방지
            if (_isProcessingQueue) return;
            _isProcessingQueue = true;

            try
            {
                while (_actionQueue.Count > 0)
                {
                    // [핵심] 만약 효과 발동 중 선택이 필요해서 상태가 바뀌었다면, 큐 진행을 멈춥니다!
                    // 멈춘 큐는 플레이어가 선택을 마치고 C_MakeChoice 패킷을 보내면 그때 이어서 실행됩니다.
                    if (_currentPhase == "AWAITING_CHOICE")
                    {
                        break;
                    }

                    // 큐에서 작업(Task)을 하나 꺼내서 실행
                    var nextAction = _actionQueue.Dequeue();
                    await nextAction();
                }
            }
            finally
            {
                _isProcessingQueue = false;
            }
        }

        // ------------------------------------------------------------------
        // [Phase 1] 멀리건 (Mulligan) 단계
        // ------------------------------------------------------------------

        /// <summary>
        /// 게임의 첫 시작인 멀리건 페이즈를 시작합니다.
        /// </summary>
        public async Task StartMulliganAsync()
        {
            Console.WriteLine($"[GameState] 멀리건 페이즈 시작.");
            _currentPhase = "Mulligan";
            
            // 1. 선공 결정
            if (Rng.Next(2) == 0) { _firstPlayerUid = _playerA.Uid; _secondPlayerUid = _playerB.Uid; }
            else { _firstPlayerUid = _playerB.Uid; _secondPlayerUid = _playerA.Uid; }
            _currentTurnPlayerUid = _firstPlayerUid; 

            // 2. 덱 섞기
            _playerA.ShuffleDeck(Rng);
            _playerB.ShuffleDeck(Rng);

            // 3. 시작 손패 구성 (중복 방지 체크 포함)
            List<CardInfo> handA = _playerA.Hand.Count == 0 ? DrawInitialHand(_playerA) : _playerA.Hand.Select(c => c.ToCardInfo()).ToList();
            List<CardInfo> handB = _playerB.Hand.Count == 0 ? DrawInitialHand(_playerB) : _playerB.Hand.Select(c => c.ToCardInfo()).ToList();

            // 4. 정보 전송 (로컬 함수 사용으로 가독성 개선)
            async Task SendInfo(PlayerState p, List<CardInfo> hand) {
                long endTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30;
                var msg = new S_MulliganInfo { action = GameActionType.MULLIGAN_INFO, cardsToMulligan = hand, mulliganEndTime = endTime };
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(msg));
            }

            await SendInfo(_playerA, handA);
            await SendInfo(_playerB, handB);

            // 🤖 봇이 있다면 BotAI에게 멀리건 의사결정을 위임
            if (_playerA.PlayerRef.IsBot) _ = Task.Run(async () => await new BotAI(this, _playerA.Uid).ExecuteMulliganAsync());
            if (_playerB.PlayerRef.IsBot) _ = Task.Run(async () => await new BotAI(this, _playerB.Uid).ExecuteMulliganAsync());
        }

        private List<CardInfo> DrawInitialHand(PlayerState p) {
            for (int i = 0; i < 5; i++)
            {
                GameCard? drawnCard = p.DrawCard();
                if (drawnCard != null)
                {
                    // 덱에서 패로 들어왔으므로 Zone.Hand로 업데이트! (손패 버프 효과가 여기서부터 켜짐)
                    drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
                }
            }
            return p.Hand.Select(c => c.ToCardInfo()).ToList();
        }
        
        /// <summary>
        /// 플레이어가 멀리건에서 교체할 카드를 선택했을 때 이를 처리합니다.
        /// </summary>
        public async Task ProcessMulliganDecisionAsync(string senderUid, C_MulliganDecision action)
        {
            PlayerState player = GetPlayerState(senderUid);
            PlayerState opponent = GetPlayerState(senderUid, true);

            // 이미 결정을 내렸는지 확인 (중복 요청 방지)
            lock (_lock) 
            {
                if (_mulliganDecisions.ContainsKey(senderUid) && _mulliganDecisions[senderUid] != null) return;
                _mulliganDecisions[senderUid] = action;
            }

            List<int> replacedIndices = new List<int>();
            List<GameCard> cardsToReturn = new List<GameCard>();

            // 1. 교체할 카드를 손패에서 제거하고 보관
            if (action.cardInstanceIdsToReplace != null)
            {
                for (int i = 0; i < player.Hand.Count; i++)
                {
                    if (action.cardInstanceIdsToReplace.Contains(player.Hand[i].InstanceId))
                        replacedIndices.Add(i);
                }

                foreach (string id in action.cardInstanceIdsToReplace)
                {
                    GameCard? c = player.Hand.FirstOrDefault(x => x.InstanceId == id);
                    if (c != null) { player.Hand.Remove(c); cardsToReturn.Add(c); }
                }
            }

            // 2. 제거한 수만큼 새로운 카드를 덱에서 뽑음
            for (int i = 0; i < cardsToReturn.Count; i++) 
            {
                  GameCard? drawnCard = player.DrawCard();
                if (drawnCard != null)
                {
                    // 덱에서 패로 들어왔으므로 Zone.Hand로 업데이트! (손패 버프 효과가 여기서부터 켜짐)
                    drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
                }
            }
            
            // 3. 뺐던 카드를 다시 덱에 넣고 섞음
            if (cardsToReturn.Count > 0) { 
                foreach (var card in cardsToReturn)
                {
                    // 손패(Hand)에서 다시 덱(Deck)으로 돌아가므로 Zone 업데이트!
                    card.UpdateZone(GameServer.Effects.Zone.Deck, this.EventSystem);
                }
                player.Deck.AddRange(cardsToReturn); 
                player.ShuffleDeck(Rng); 
            }

            // 4. 상대방에게 나의 멀리건 완료 상태(몇 장 바꿨는지)를 알림
            var statusMsg = new S_OpponentMulliganStatus
            {
                action = GameActionType.OPPONENT_MULLIGAN_STATUS,
                opponentUid = senderUid,
                replacedIndices = replacedIndices,
                replacedCount = replacedIndices.Count,
                isReady = true
            };
            await _room.SendMessageToPlayerAsync(opponent.PlayerRef, JsonConvert.SerializeObject(statusMsg));

            // 5. 두 플레이어 모두 준비되었는지 확인 후 게임 시작
            bool allReady = false;
            lock (_lock) { allReady = _mulliganDecisions.Values.All(x => x != null); }

            if (allReady)
            {
                lock(_lock) 
                { 
                    if(!_isGameStarted) { _isGameStarted = true; } 
                    else { return; } 
                }
                await StartGameAsync();
            }
        }

        // ------------------------------------------------------------------
        // [Phase 2] 게임 시작 및 턴 진행
        // ------------------------------------------------------------------

        /// <summary>
        /// 멀리건이 끝나고 실제 대전 페이즈로 진입합니다.
        /// </summary>
        private async Task StartGameAsync()
        {
            _currentPhase = "StartGame";
            var handA = _playerA.Hand.Select(c => c.ToCardInfo()).ToList();
            var handB = _playerB.Hand.Select(c => c.ToCardInfo()).ToList();

            // 각 플레이어의 리더 객체를 EntityData로 변환
            var leaderA = _playerA.Leader.ToEntityData();
            var leaderB = _playerB.Leader.ToEntityData();

            // 양쪽 플레이어에게 최종 손패와 리더 정보와 함께 게임 시작을 알림
                await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { 
                    action = GameActionType.GAME_READY, 
                    firstPlayerUid = _firstPlayerUid, 
                    finalHand = handA, 
                    enermyfinalHand = handB,
                    myLeader = leaderA,       // A에게는 자신의 리더가 A
                    enemyLeader = leaderB     // A에게는 적의 리더가 B
                }));
                
                await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { 
                    action = GameActionType.GAME_READY, 
                    firstPlayerUid = _firstPlayerUid, 
                    finalHand = handB, 
                    enermyfinalHand = handA,
                    myLeader = leaderB,       // B에게는 자신의 리더가 B
                    enemyLeader = leaderA     // B에게는 적의 리더가 A
                }));
                
            await Task.Delay(1500); // 연출을 위한 잠시 대기
            await StartTurnAsync(_firstPlayerUid); // 선공 플레이어 턴 시작
        }

        /// <summary>
        /// 특정 플레이어의 새로운 턴을 시작합니다 (마나 증가, 드로우 등).
        /// </summary>
        private async Task StartTurnAsync(string uid)
        {
            _currentTurnPlayerUid = uid;
            PlayerState p = GetPlayerState(uid);
            PlayerState op = GetPlayerState(uid, true);

            Console.WriteLine($"[GameState] 📢 {(p.PlayerRef.IsBot ? "🤖 봇" : "🧑 플레이어")} ({p.Uid})의 턴 시작");

            // 1. Standby 페이즈 알림
            _currentPhase = "Standby";
            var phaseMsg = JsonConvert.SerializeObject(new S_PhaseStart { action=GameActionType.PHASE_START, phase = GamePhase.STANDBY, TurnPlayerUid=_currentTurnPlayerUid });
            await _room.SendMessageToPlayerAsync(p.PlayerRef, phaseMsg);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, phaseMsg);

            var startContext = new GameServer.Effects.EffectContext(_currentTurnPlayerUid, null, EffectTriggerType.ON_TURN_START);
            await EventSystem.PublishAsync(EffectTriggerType.ON_TURN_START, startContext);

            // 2. 마나 충전 (최대 10까지 1씩 증가)
            p.MaxMana = Math.Min(p.MaxMana + 1, 10);
            p.CurrentMana = p.MaxMana;
            
            await BroadcastManaUpdate(p, op);
            await Task.Delay(1000);

            // 3. Draw 페이즈 (카드 한 장 뽑기)
            _currentPhase = "Draw";
            GameCard? drawnCard = p.DrawCard();
            if (drawnCard != null)
            {
                // 덱에서 패로 들어왔으므로 Zone.Hand로 업데이트! (손패 버프 효과가 여기서부터 켜짐)
                drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
            }
            // 드로우 카드 전송
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart {TurnPlayerUid = _currentTurnPlayerUid, action=GameActionType.PHASE_START, phase=GamePhase.DRAW, drawnCard=drawnCard?.ToCardInfo() }));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart {TurnPlayerUid = _currentTurnPlayerUid, action=GameActionType.PHASE_START, phase=GamePhase.DRAW, drawnCard=null }));


            await Task.Delay(1000);

            // 4. Main 페이즈 시작 (실제 플레이 타임, 60초 제한)
            _currentPhase = "Main";
            long _turnEndTime  = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60; 
            var mainMsg = new S_PhaseStart { action = GameActionType.PHASE_START, phase = GamePhase.MAIN, turnEndTime = _turnEndTime  };
            
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            
            // 5. 필드 위 개체들의 공격 기회 초기화
            foreach(var e in p.Field) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            foreach(var e in p.MemberZone) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            p.Leader.CanAttack = true; p.Leader.HasAttacked = false;

            //  봇의 턴이라면 BotAI 실행
            if (p.PlayerRef.IsBot)
            {
                _ = Task.Run(async () => {
                    // BotAI 클래스가 별도로 구현되어 있다고 가정
                    BotAI ai = new BotAI(this, uid);
                    await ai.ExecuteTurnAsync();
                });
            }
        }

        public async Task<GameCard?> DrawCardWithSyncAsync(string playerUid)
        {
            PlayerState p = GetPlayerState(playerUid);
            PlayerState op = GetPlayerState(playerUid, true);

            GameCard? drawnCard = p.DrawCard();
            if (drawnCard != null)
            {
                // 1. 카드의 현재 구역을 손패(Zone.Hand)로 업데이트
                drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);

                // 2. 서버 이벤트 로그 기록
                LogEvent(GameEventType.DRAW, 0, 0, 1, drawnCard.CardId);

                // 3. 전용 드로우 패킷(S_DrawCard)을 만들어서 양측에 전송
                // 나에게는 카드 앞면 정보를 전송하고, 상대방에게는 null을 보내 카드 장수 늘어나는 연출만 유도합니다.
                var msgToSelf = new S_DrawCard
                {
                    action = GameActionType.DRAW_CARD, // 혹은 Enum이 없다면 문자열/매핑 규격에 맞게 설정
                    playerUid = playerUid,
                    drawnCard = drawnCard.ToCardInfo()
                };

                var msgToOpponent = new S_DrawCard
                {
                    action = GameActionType.DRAW_CARD,
                    playerUid = playerUid,
                    drawnCard = null
                };

                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(msgToSelf));
                await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(msgToOpponent));
            }

            return drawnCard;
        }

        /// <summary>
        /// 양쪽 플레이어에게 현재 마나 상태를 최신화하여 보냅니다.
        /// </summary>
        private async Task BroadcastManaUpdate(PlayerState p, PlayerState op)
        {
            var manaMsg = new S_UpdateMana { action=GameActionType.UPDATE_MANA, ownerUid=p.Uid, currentMana=p.CurrentMana, maxMana=p.MaxMana };
            string json = JsonConvert.SerializeObject(manaMsg);
            await _room.SendMessageToPlayerAsync(p.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, json);
        }

        /// <summary>
        /// 플레이어가 턴 종료 버튼을 눌렀을 때의 처리입니다.
        /// </summary>
        public async Task ProcessEndTurnAsync(string senderUid)
        {
            if (senderUid != _currentTurnPlayerUid) return; // 내 턴이 아니면 무시
            
            _currentPhase = "End";
            var msg = JsonConvert.SerializeObject(new S_PhaseStart { action=GameActionType.PHASE_START, phase=GamePhase.END });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, msg);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, msg);

            var endContext = new GameServer.Effects.EffectContext(_currentTurnPlayerUid, null, EffectTriggerType.ON_TURN_END);
            await EventSystem.PublishAsync(EffectTriggerType.ON_TURN_END, endContext);
            
            await Task.Delay(500);
            
            // 상대방의 턴으로 교체
            string nextUid = (senderUid == _playerA.Uid) ? _playerB.Uid : _playerA.Uid;
            await StartTurnAsync(nextUid);
        }

        // ------------------------------------------------------------------
        // [Phase 3] 전투 및 카드 플레이 로직
        // ------------------------------------------------------------------

        /// <summary>
        /// [테스트 전용] 덱에서 특정 카드를 찾아 맨 위(마지막)로 올린 후 즉시 드로우합니다.
        /// </summary>
        /// <param name="playerUid">카드를 뽑을 플레이어의 Uid</param>
        /// <param name="targetCardId">뽑고 싶은 카드의 원본 ID (예: "Fireball_001")</param>
        public async Task DrawSpecificCardFromDeckAsync(string playerUid, string targetCardId)
        {
            // 1. 대상 플레이어의 상태 객체를 가져옵니다 [2].
            PlayerState currentPlayer = GetPlayerState(_currentTurnPlayerUid);
            PlayerState player = GetPlayerState(playerUid);

            // 2. 덱에서 요청한 카드가 있는지 찾습니다.
            GameCard? targetCard = player.Deck.FirstOrDefault(c => c.CardId == targetCardId);

            if (targetCard != null)
            {
                // 3. 덱에서 해당 카드를 제거한 뒤, 맨 뒤(맨 위)로 다시 추가합니다 [1].
                player.Deck.Remove(targetCard);
                player.Deck.Add(targetCard);

                await DrawCardWithSyncAsync(playerUid);
            }
            else
            {
                Console.WriteLine($"[GameState - Cheat] ❌ {playerUid}의 덱에 '{targetCardId}' 카드가 존재하지 않습니다.");
            }
        }


        /// <summary>
        /// [1단계] 플레이어가 손에서 카드를 내려고 시도할 때 검증하고, 타겟팅 필요 시 대기 상태로 전환합니다.
        /// </summary>
        public async Task ProcessPlayCardAsync(string senderUid, C_PlayCard action)
        {
            PlayerState p = GetPlayerState(senderUid);
            PlayerState op = GetPlayerState(senderUid, true);
            _eventBuffer.Clear();
            _pendingUpdates.Clear();

            // 1. 카드 존재 및 마나 자원 확인 [5, 6]
            GameCard? card = p.Hand.FirstOrDefault(c => c.InstanceId == action.handCardInstanceId);
            if (card == null)
            {
                Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: 서버 손패에서 카드를 찾을 수 없습니다!");
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                            action = GameActionType.PLAY_CARD_FAIL,
                            failedCardInstanceId = card.InstanceId,
                            reason = "서버 손패에서 카드를 찾을 수 없습니다!"
                        }));
                return;
            }

            if (p.CurrentMana < card.CurrentCost)
            {
                Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: 마나가 부족합니다. (현재 마나: {p.CurrentMana}, 필요 마나: {card.CurrentCost})");
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                            action = GameActionType.PLAY_CARD_FAIL,
                            failedCardInstanceId = card.InstanceId,
                            reason = "마나가 부족합니다. (현재 마나: {p.CurrentMana}, 필요 마나: {card.CurrentCost})"
                        }));
                return;
            }

            // 2. 하수인 또는 멤버 소환인 경우 미리 위치 유효성 검사 [6]
            bool isUnit = card.Type == CardType.하수인 || card.Type == CardType.멤버;
            bool isMember = card.Type == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            if (isUnit)
            {
                if (action.position < 0 || action.position >= targetZone.Length)
                {
                    Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: 잘못된 소환 위치입니다. (요청한 Position: {action.position})");
                    await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                            action = GameActionType.PLAY_CARD_FAIL,
                            failedCardInstanceId = card.InstanceId,
                            reason = "잘못된 소환 위치입니다. (요청한 Position: {action.position})"
                        }));
                    return;
                }
                if (targetZone[action.position] != null)
                {
                    Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: {action.position}번 위치에 이미 다른 개체가 존재합니다.");
                    await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                            action = GameActionType.PLAY_CARD_FAIL,
                            failedCardInstanceId = card.InstanceId,
                            reason = "{action.position}번 위치에 이미 다른 개체가 존재합니다."
                        }));
                    return;
                }
            }

            // 3. 타겟팅 필요 여부 분기 처리 (TargetRule 검증) [7]
            if (card.TargetRule == true)
            {
                // 🤖 [디버그 봇 전용 예외 방어]: 봇은 항상 원테이크로 처리 [7]
                if (p.PlayerRef.IsBot)
                {
                    var botValidIds = TargetValidator.GetValidTargetIds(this, card, senderUid);
                    int chosenBotTarget = 0;
                    if (botValidIds != null && botValidIds.Count > 0)
                    {
                        chosenBotTarget = botValidIds[Rng.Next(botValidIds.Count)];
                    }
                    await ExecuteCardPlayWithTargetAsync(senderUid, card, action.position, chosenBotTarget);
                    return;
                }

                // =================================================================
                // 🪄 [유저의 주문 카드 핵심 최적화]: 사용자님 말씀대로 2단계 대기 없이 즉시 시전!
                // =================================================================
                if (card.Type == CardType.주문)
                {
                    // 클라이언트가 처음부터 보내준 targetEntityId가 진짜 유효한 대상인지 룰 검사 수행 [8]
                    var validIds = TargetValidator.GetValidTargetIds(this, card, senderUid);
                    
                    if (validIds != null && validIds.Contains(action.targetEntityId))
                    {
                        // 타겟이 유효하므로 즉시 1단계만에 마나를 소모하고 주문 효과를 실행합니다! [9]
                        Console.WriteLine($"[ProcessPlayCardAsync] 🪄 주문 카드 '{card.CardId}' 즉시 발동 성공! (지정 타겟: {action.targetEntityId})");
                        await ExecuteCardPlayWithTargetAsync(senderUid, card, action.position, action.targetEntityId);
                    }
                    else
                    {
                        // 잘못된 대상을 찍었거나 대상을 누락한 경우 사용 실패 패킷 전송 [10]
                        Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: 주문 카드의 대상(ID: {action.targetEntityId})이 유효하지 않습니다.");
                        await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                            action = GameActionType.PLAY_CARD_FAIL,
                            failedCardInstanceId = card.InstanceId,
                            reason = "지정한 대상이 유효하지 않은 대상입니다."
                        }));
                    }
                    return;
                }

                // =================================================================
                // 🃏 [유저의 하수인 카드]: 필드 슬롯 배치 후 2차 타겟을 수집하는 기존 2단계 적용
                // =================================================================
                var validIdsForMinion = TargetValidator.GetValidTargetIds(this, card, senderUid);

                if (validIdsForMinion != null && validIdsForMinion.Count > 0)
                {
                    _pendingPlayHandInstanceId = card.InstanceId;
                    _pendingPlayPosition = action.position;
                    _pendingPlaySenderUid = senderUid;
                    _pendingPlayTargets.Clear();

                    _currentPhase = "AWAITING_TARGET_FOR_PLAY"; // 대기 상태 돌입 [2]

                    var reqMsg = new S_RequestTargetForPlay
                    {
                        action = GameActionType.REQUEST_TARGET_FOR_PLAY,
                        CardEntityId = card.InstanceId,
                        position = action.position,
                        targetIndex = 0,
                        ValidTargetIds = validIdsForMinion
                    };
                    await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(reqMsg));
                    Console.WriteLine($"[ProcessPlayCardAsync] 🎯 하수인 '{card.CardId}' 타겟 대기 페이즈 진입. 클라이언트에 2차 타겟팅을 요청했습니다.");
                    return;
                }
                else
                {
                    // 하수인의 전투의 함성은 타겟 대상이 없어도 필드 소환은 가능합니다 [11]
                    Console.WriteLine($"[ProcessPlayCardAsync] ⚠️ '{card.CardId}' 대상이 없으므로 타겟팅 효과 없이 소환을 진행합니다.");
                    await ExecuteCardPlayWithTargetAsync(senderUid, card, action.position, 0);
                }
            }
            else
            {
                // 타겟팅이 아예 필요 없는 일반 카드(바닐라 하수인, 광역 주문 등)는 즉시 처리 [11]
                await ExecuteCardPlayWithTargetAsync(senderUid, card, action.position, 0);
            }
        }

        /// <summary>
        /// [도우미] 타겟팅 검증을 모두 마쳤거나 필요 없는 카드를 실제로 실행하고 마나를 차감합니다. (하수인/주문 공용)
        /// </summary>
        private async Task ExecuteCardPlayWithTargetAsync(string senderUid, GameCard card, int position, int targetEntityId)
        {
            PlayerState p = GetPlayerState(senderUid);
            PlayerState op = GetPlayerState(senderUid, true);

            bool isUnit = card.Type == CardType.하수인 || card.Type == CardType.멤버;
            bool isMember = card.Type == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            // 1. 실제 마나 자원 소모 및 손패에서 제거 확정 [9]
            p.CurrentMana -= card.CurrentCost;
            p.Hand.Remove(card);

            // 2. [분기 A] 하수인 및 멤버(유닛) 소환 로직 확정 [9]
            if (isUnit)
            {
                card.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

                int eid = _nextGlobalEntityId++;
                GameEntity sourceEntity = new GameEntity(eid, card, senderUid);
                sourceEntity.Position = position;
                sourceEntity.IsMember = isMember;

                LogEvent(GameEventType.SUMMON, sourceEntity.EntityId, 0, position, card.CardId, EffectTriggerType.ON_PLAY, sourceEntity.ToEntityData());
                _allEntities.Add(eid, sourceEntity);
                targetZone[position] = sourceEntity;
                AddPendingUpdate(sourceEntity);

                GameEntity? battlecryTarget = null;
                if (targetEntityId > 0)
                {
                    _allEntities.TryGetValue(targetEntityId, out battlecryTarget);
                }

                // 성공 브로드캐스팅 전송 [14]
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardSuccess { action = GameActionType.PLAY_CARD_SUCCESS, serverInstanceId = card.InstanceId }));
                await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_OpponentPlayCard { 
                    action = GameActionType.OPPONENT_PLAY_CARD, 
                    cardPlayed = card.ToCardInfo(), 
                    targetEntityId = targetEntityId,
                    position = position, // [추가 대입]
                    entityId = eid       // [추가 대입]
                }));
                OnCardPlayed!.Invoke(p.Uid, card.CardId);

                // 액션 대기열 대기 [8]
                _actionQueue.Enqueue(async () =>
                {
                    Console.WriteLine($"[ProcessPlayCardAsync] 🃏 하수인 '{card.CardId}' (ID:{eid}) 소환완료 및 전투의 함성 실행.");
                    LogEvent(GameEventType.EFFECT_TRIGGER, sourceEntity.EntityId, targetEntityId, 0, null, EffectTriggerType.ON_PLAY);

                    var context = new GameServer.Effects.EffectContext(senderUid, card, EffectTriggerType.ON_PLAY)
                    {
                        SourceEntity = sourceEntity,
                        TargetEntity = battlecryTarget
                    };
                    await EventSystem.PublishAsync(EffectTriggerType.ON_PLAY, context);
                });

                OnMinionSummoned?.Invoke(p.Uid, card.CardId, position);
            }
            // 3. [분기 B] 주문(Spell) 카드 발동 로직 확정
            else if (card.Type == CardType.주문)
            {
                GameEntity? spellTarget = null;
                if (targetEntityId > 0)
                {
                    _allEntities.TryGetValue(targetEntityId, out spellTarget);
                }

                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardSuccess { action = GameActionType.PLAY_CARD_SUCCESS, serverInstanceId = card.InstanceId }));
                await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_OpponentPlayCard { 
                    action = GameActionType.OPPONENT_PLAY_CARD, 
                    cardPlayed = card.ToCardInfo(), 
                    targetEntityId = targetEntityId,
                }));
                OnCardPlayed!.Invoke(p.Uid, card.CardId);

                _actionQueue.Enqueue(async () =>
                {
                    Console.WriteLine($"[ProcessPlayCardAsync] 🪄 주문 카드 '{card.CardId}' 발동 시작.");
                    LogEvent(GameEventType.EFFECT_TRIGGER, 0, targetEntityId, 0, null, EffectTriggerType.ON_PLAY);

                    var context = new GameServer.Effects.EffectContext(senderUid, card, EffectTriggerType.ON_PLAY)
                    {
                        TargetEntity = spellTarget
                    };
                    await EventSystem.PublishAsync(EffectTriggerType.ON_PLAY, context);

                    // 주문 완료 후 묘지 소모 처리
                    card.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                    p.Graveyard.Add(card);
                });
            }

            // 공통 사후 처리 등록
            _actionQueue.Enqueue(async () =>
            {
                await ProcessDeathsAsync();
                await BroadcastUpdatesAsync(senderUid);
            });

            await ProcessActionQueueAsync();
        }

        /// <summary>
        /// 대기 상태의 타겟 데이터를 초기화합니다.
        /// </summary>
        private void ClearPendingPlayState()
        {
            _pendingPlayHandInstanceId = "";
            _pendingPlayPosition = -1;
            _pendingPlayTargets.Clear();
            _pendingPlaySenderUid = "";
        }

        /// <summary>
        /// [2단계] 클라이언트가 지정한 최종 타겟 패킷을 받아 검증하고 최종 사용 처리를 명령합니다.
        /// </summary>
        public async Task ProcessSelectTargetForPlayAsync(string senderUid, C_SelectTargetForPlay action)
        {
            if (_currentPhase != "AWAITING_TARGET_FOR_PLAY" || senderUid != _pendingPlaySenderUid)
            {
                Console.WriteLine($"[ProcessSelectTargetForPlayAsync] ❌ 실패: 잘못된 타겟 응답입니다.");
                return;
            }

            PlayerState p = GetPlayerState(senderUid);

            // Case 1: 유저가 마우스 우클릭 등으로 "카드 사용을 취소"하여 -1이나 0을 보낸 경우
            if (action.selectedEntityId <= 0)
            {
                Console.WriteLine($"[ProcessSelectTargetForPlayAsync] 🛑 유저가 카드 사용을 취소했습니다: {action.CardEntityId}");
                
                // 유니티 클라이언트가 들어 올렸던 카드를 손패로 고스란히 복구시키도록 실패 패킷 전송 [15]
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail
                {
                    action = GameActionType.PLAY_CARD_FAIL,
                    failedCardInstanceId = _pendingPlayHandInstanceId,
                    reason = "카드 사용을 취소하셨습니다."
                }));

                ClearPendingPlayState();
                _currentPhase = "Main"; // 메인 페이즈로 다시 돌려줌
                return;
            }

            // 원본 카드 수색
            GameCard? card = p.Hand.FirstOrDefault(c => c.InstanceId == _pendingPlayHandInstanceId);
            if (card == null)
            {
                Console.WriteLine($"[ProcessSelectTargetForPlayAsync] ❌ 실패: 사용 대기 중인 카드가 더이상 손패에 존재하지 않습니다.");
                ClearPendingPlayState();
                _currentPhase = "Main";
                return;
            }

            // TargetValidator로 대상 유효성 2차 철통 검사 [5]
            var validIds = TargetValidator.GetValidTargetIds(this, card, senderUid);
            if (validIds == null || !validIds.Contains(action.selectedEntityId))
            {
                Console.WriteLine($"[ProcessSelectTargetForPlayAsync] ❌ 실패: 규칙에 위반되는 타겟 ID({action.selectedEntityId})입니다.");
                
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail
                {
                    action = GameActionType.PLAY_CARD_FAIL,
                    failedCardInstanceId = _pendingPlayHandInstanceId,
                    reason = "타겟팅 대상이 규칙에 맞지 않습니다."
                }));

                ClearPendingPlayState();
                _currentPhase = "Main";
                return;
            }

            // =================================================================
            // [다중 타겟 응용 설계 가이드]
            // 만약 미래에 타겟을 2번 찍어야 하는 특수 하수인이 추가된다면 다음과 같이 유연하게 확장됩니다.
            // 
            // int requiredTargetCount = 1; // 기본값 1개
            // if (card.CardId == "cards-epic-multiTarget") requiredTargetCount = 2;
            //
            // _pendingPlayTargets.Add(action.selectedEntityId);
            //
            // if (_pendingPlayTargets.Count < requiredTargetCount)
            // {
            //     // 다음 두 번째 타겟팅을 위해 S_RequestTargetForPlay 패킷을 targetIndex만 1로 변경해서 유니티에 한 번 더 보내고 대기합니다!
            //     await _room.SendMessageToPlayerAsync(...);
            //     return;
            // }
            // =================================================================

            // 단일 타겟 지정 완료 처리 시작!
            _pendingPlayTargets.Add(action.selectedEntityId);
            int finalTargetId = _pendingPlayTargets[0];

            // 최종 소환 및 자원 차감 돌입
            await ExecuteCardPlayWithTargetAsync(senderUid, card, _pendingPlayPosition, finalTargetId);

            // 초기화 및 복구
            ClearPendingPlayState();
            _currentPhase = "Main";
        }

        public async Task ProcessTargetChoiceAsync(string uid, C_ValidTargetRequest targetReq)
        {
            PlayerState currentPlayer = GetPlayerState(uid);
            GameCard? targetCard = currentPlayer.Hand.FirstOrDefault(c => c.InstanceId == targetReq.CardEntityId);

            if (targetCard != null)
            {
                // 3. 완성된 TargetValidator를 사용해 조건에 맞는 대상 ID 목록 추출
                var validIds = TargetValidator.GetValidTargetIds(this, targetCard, uid);

                // 4. 결과를 클라이언트로 전송
                var responseMsg = new S_ValidTargetResponse
                {
                    action = GameActionType.VALID_TARGETS_RESPONSE,
                    CardEntityId = targetReq.CardEntityId,
                    ValidTargetIds = validIds ?? new List<int>()
                };

                string targetListStr = (responseMsg.ValidTargetIds != null && responseMsg.ValidTargetIds.Count > 0)
                    ? string.Join(", ", responseMsg.ValidTargetIds)
                    : "없음(0개)";
                Console.WriteLine($"[TargetValidator/S_ValidTargetResponse] 🎯 플레이어 {uid} 카드 '{targetCard.CardId}'({targetCard.InstanceId}) 타겟 목록 계산 완료 -> 유효 타겟 IDs: [{targetListStr}]");
                
                await _room.SendMessageToPlayerAsync(currentPlayer.PlayerRef, JsonConvert.SerializeObject(responseMsg));
            }
            else
            {
                Console.WriteLine($"[ProcessTargetChoiceAsync] ⚠️ 손패에서 카드({targetReq.CardEntityId})를 찾을 수 없습니다.");
            }
        }

        /// <summary>
        /// [신규] 클라이언트가 요청받은 선택(위치, 타겟 등)을 완료하고 보낸 패킷을 처리합니다.
        /// </summary>
        public async Task ProcessMakeChoiceAsync(string senderUid, C_MakeChoice action)
        {
            Console.WriteLine($"[GameState] 플레이어 {senderUid}가 선택 완료. (Position: {action.selectedPosition})");

            // 1. 내 필드에 지정 소환일 경우
            if (_pendingChoiceType == "POSITION" && action.selectedPosition >= 0)
            {
                // 기억해둔 카드 ID를 이용해 해당 위치에 소환!
                SummonEntityAtPosition(senderUid, _pendingChoiceData, action.selectedPosition);
            }
            // 2. 적 필드에 지정 소환일 경우
            else if (_pendingChoiceType == "POSITION_ENEMY" && action.selectedPosition >= 0)
            {
                // 상대방(Opponent)의 UID를 가져와서 소환 함수의 소유자로 넘겨줍니다!
                PlayerState opp = GetPlayerState(senderUid, true);
                SummonEntityAtPosition(opp.Uid, _pendingChoiceData, action.selectedPosition);
            }

            _pendingChoiceCount--;

            if (_pendingChoiceCount > 0)
            {
                await FlushUpdatesAsync(); 
                
                await RequestPlayerChoiceAsync(
                    senderUid, 
                    _pendingChoiceType, 
                    _pendingChoiceData, 
                    $"남은 소환: {_pendingChoiceCount}회. 다음 위치를 선택해주세요.", 
                    0, 
                    _pendingChoiceCount);
                    
                return; // 여기서 멈추고 다시 클라이언트의 응답을 기다림
            }

            // 2. 메모리 초기화 및 상태 복구
            _pendingChoiceType = "";
            _pendingChoiceData = "";
            _currentPhase = "Main"; 

            // 3. (핵심) 멈춰있던 이벤트 큐를 재개하여 남은 효과 및 사망 처리를 마저 진행합니다!
            await ProcessActionQueueAsync();
        }

        /// <summary>
        /// 카드 효과나 죽음의 메아리 등 '특수 효과'로 개체를 소환할 때 호출하는 메서드입니다.
        /// 마나를 소모하지 않고, 전투의 함성(ON_PLAY)이 발동하지 않으며, 
        /// 클라이언트 연출용 트리거가 NONE으로 전달됩니다.
        /// </summary>
        public void SummonEntityByEffect(string ownerUid, string cardId)
        {
            PlayerState p = GetPlayerState(ownerUid);

            // 1. 소환할 카드의 기본 데이터 확인 (서버 DB에서 원본 카드 데이터 조회)
            ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(cardId);
            if (cardData == null)
            {
                Console.WriteLine($"[GameState] ⚠️ 효과 소환 실패: '{cardId}' 데이터를 찾을 수 없습니다.");
                return;
            }

            // 2. 하수인인지 멤버인지 판별하여 타겟 존 설정
            bool isMember = cardData.CardType == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            // 3. 타겟 존에서 가장 왼쪽의 빈자리(인덱스) 찾기
            int emptySlot = -1;
            for (int i = 0; i < targetZone.Length; i++)
            {
                if (targetZone[i] == null)
                {
                    emptySlot = i;
                    break;
                }
            }

            // 빈자리가 없으면 소환 실패 (필드가 꽉 참)
            if (emptySlot == -1) return;

            // 4. 새로운 고유 인스턴스 ID 발급 및 GameCard 객체 생성
            string newInstanceId = $"EffectToken_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            GameCard newCard = new GameCard(cardId, newInstanceId) { OwnerUid = ownerUid };
            newCard.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

            // 5. 전역 엔티티 ID 발급 및 GameEntity 객체 생성
            int eid = _nextGlobalEntityId++;
            GameEntity newEntity = new GameEntity(eid, newCard, ownerUid)
            {
                Position = emptySlot,
                IsMember = isMember
            };

            // 6. 서버의 전역 개체 딕셔너리 및 플레이어 필드에 등록
            _allEntities.Add(eid, newEntity);
            targetZone[emptySlot] = newEntity;

            // 7. 클라이언트 연출을 위한 SUMMON 이벤트 로그 기록
            // 핵심: 손에서 직접 낸 것이 아니므로 EffectTriggerType.NONE을 사용하여 조용히 등장하는 연출을 유도합니다.
            LogEvent(GameEventType.SUMMON, newEntity.EntityId, 0, emptySlot, newCard.CardId, EffectTriggerType.NONE, newEntity.ToEntityData());
            OnMinionSummoned?.Invoke(p.Uid, newCard.CardId, emptySlot);

            // 8. 클라이언트 데이터 동기화를 위해 변경 대기열에 추가
            AddPendingUpdate(newEntity);

            // ※ 주의: 효과 소환이므로 마나 소모 코드가 없으며, ExecuteEffectsAsync(ON_PLAY) 역시 호출하지 않습니다.
        }

        /// <summary>
        /// 이미 생성되어 있는 카드 객체(예: 덱이나 손패에 있던 카드)를 그대로 필드에 소환합니다.
        /// 덱 버프나 핸드 버프 등 기존 스탯 변화가 그대로 유지됩니다.
        /// </summary>
        public void SummonExistingCard(string ownerUid, GameCard card)
        {
            PlayerState p = GetPlayerState(ownerUid);

            // 1. 하수인인지 멤버인지 판별하여 타겟 존 설정
            bool isMember = card.Type == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            // 2. 타겟 존에서 가장 왼쪽의 빈자리(인덱스) 찾기
            int emptySlot = -1;
            for (int i = 0; i < targetZone.Length; i++)
            {
                if (targetZone[i] == null) { emptySlot = i; break; }
            }

            // 빈자리가 없으면 소환 실패 (카드가 증발하지 않도록 다시 덱/패에 넣는 기획이 필요할 수도 있습니다)
            if (emptySlot == -1) return; 

            // 3. 카드의 위치(Zone)를 필드로 업데이트! (구독 중인 효과 갱신)
            card.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

            // 4. 전역 엔티티 ID 발급 및 개체(Entity) 생성
            int eid = _nextGlobalEntityId++;
            GameEntity newEntity = new GameEntity(eid, card, ownerUid)
            {
                Position = emptySlot,
                IsMember = isMember
            };

            // 5. 서버에 등록
            _allEntities.Add(eid, newEntity);
            targetZone[emptySlot] = newEntity;

            // 6. [핵심] 클라이언트 연출 로그 (이미 만들어두신 SUMMON_FROM_DECK 활용!)
            LogEvent(GameEventType.SUMMON_FROM_DECK, newEntity.EntityId, 0, emptySlot, card.CardId, EffectTriggerType.NONE, newEntity.ToEntityData());
            OnMinionSummoned?.Invoke(p.Uid, card.CardId, emptySlot);

            // 7. 클라이언트 데이터 동기화
            AddPendingUpdate(newEntity);
        }

        /// <summary>
        /// 효과 발동 중 플레이어가 직접 지정한 특정 위치(targetPos)에 개체를 소환합니다.
        /// 마나 소모 없음, 전투의 함성 미발동, 트리거 NONE(토큰 소환 연출) 규칙이 적용됩니다.
        /// </summary>
        public void SummonEntityAtPosition(string ownerUid, string cardId, int targetPos)
        {
            PlayerState p = GetPlayerState(ownerUid);

            // 1. 소환할 카드의 기본 데이터 확인 (서버 DB에서 원본 카드 데이터 조회)
            ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(cardId);
            if (cardData == null)
            {
                Console.WriteLine($"[GameState] ⚠️ 효과 소환 실패: '{cardId}' 데이터를 찾을 수 없습니다.");
                return;
            }

            // 2. 하수인인지 멤버인지 판별하여 타겟 존 설정
            bool isMember = cardData.CardType == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            // 3. 플레이어가 지정한 위치가 유효한지, 그리고 빈자리인지 검사
            if (targetPos < 0 || targetPos >= targetZone.Length || targetZone[targetPos] != null)
            {
                Console.WriteLine($"[GameState] ⚠️ 소환 실패: 지정된 위치({targetPos})가 유효하지 않거나 이미 개체가 존재합니다.");
                return;
            }

            // 4. 새로운 고유 인스턴스 ID 발급 및 GameCard 객체 생성
            string newInstanceId = $"EffectToken_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            GameCard newCard = new GameCard(cardId, newInstanceId) { OwnerUid = ownerUid };
            newCard.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

            // 5. 전역 엔티티 ID 발급 및 GameEntity 객체 생성
            int eid = _nextGlobalEntityId++;
            GameEntity newEntity = new GameEntity(eid, newCard, ownerUid)
            {
                Position = targetPos,
                IsMember = isMember
            };

            // 6. 서버의 전역 개체 딕셔너리 및 플레이어 필드 지정 위치에 등록
            _allEntities.Add(eid, newEntity);
            targetZone[targetPos] = newEntity;

            // 7. 클라이언트 연출을 위한 SUMMON 이벤트 로그 기록
            // (손에서 직접 낸 것이 아니므로 EffectTriggerType.NONE을 사용하여 조용히 등장하는 마법 연출 유도)
            LogEvent(GameEventType.SUMMON, newEntity.EntityId, 0, targetPos, newCard.CardId, EffectTriggerType.NONE, newEntity.ToEntityData());
            OnMinionSummoned?.Invoke(p.Uid, newCard.CardId, targetPos);

            // 8. 클라이언트 데이터 동기화를 위해 변경 대기열에 추가
            AddPendingUpdate(newEntity);

            // ※ 주의: 위치 지정 소환 역시 '효과에 의한 소환'이므로 전투의 함성(ON_PLAY)을 발동시키지 않습니다.
        }

        /// <summary>
        /// 플레이어가 소환된 하수인을 드래그할 때 공격할 수 있는 대상을 검증하여 응답합니다.
        /// </summary>
        public async Task ProcessValidAttackTargetsRequestAsync(string senderUid, C_ValidAttackTargetsRequest action)
        {
            PlayerState me = GetPlayerState(senderUid);
            PlayerState opp = GetPlayerState(senderUid, true);

            var response = new S_ValidAttackTargetsResponse
            {
                attackerEntityId = action.attackerEntityId
            };

            // 1. 공격자 개체 유효성 확인 (존재 여부, 소유권, 공격 가능 상태, 공격 여부 검증)
            if (!_allEntities.TryGetValue(action.attackerEntityId, out var attacker))
            {
                Console.WriteLine($"[ProcessValidAttackTargetsRequest] 실패: 공격자 ID({action.attackerEntityId})를 찾을 수 없습니다.");
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            if (attacker.OwnerUid != senderUid || !attacker.CanAttack || attacker.HasAttacked)
            {
                // 공격이 불가능한 상태라면 유효 공격 대상 목록을 빈 배열로 반환하여 드래그 비활성화 유도
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            // 2. 적의 살아있는 캐릭터 수집 (영웅, 필드 하수인, 멤버존 하수인)
            var enemyCandidates = new List<GameEntity>();
            
            if (opp.Leader != null && opp.Leader.Health > 0)
            {
                enemyCandidates.Add(opp.Leader);
            }
            
            foreach (var e in opp.Field)
            {
                if (e != null && e.Health > 0)
                {
                    enemyCandidates.Add(e);
                }
            }
            
            foreach (var e in opp.MemberZone)
            {
                if (e != null && e.Health > 0)
                {
                    enemyCandidates.Add(e);
                }
            }

            // 3. 적 필드에 도발(Taunt) 하수인이 있는지 판별
            // 영웅이나 멤버존을 제외하고, 'Field'에 수집된 일반 하수인 중 Taunt 키워드를 가진 자를 색출합니다.
            var tauntEnemies = enemyCandidates
                .Where(e => !e.IsLeader && !e.IsMember && e.Keywords != null && e.Keywords.Contains(CardKeywords.Taunt))
                .ToList();

            // 4. 타겟 최종 결정
            if (tauntEnemies.Count > 0)
            {
                // 적 진영에 도발 하수인이 하나라도 있으면, 오직 도발 대상들만 공격 가능 리스트에 들어갑니다.
                response.validDefenderEntityIds = tauntEnemies.Select(e => e.EntityId).ToList();
                Console.WriteLine($"[ProcessValidAttackTargetsRequest] 도발 유닛 활성화로 타겟 제한됨. (공격자: {attacker.SourceCard.CardId})");
            }
            else
            {
                // 도발이 없다면 생존한 모든 적 캐릭터가 유효 타겟입니다.
                response.validDefenderEntityIds = enemyCandidates.Select(e => e.EntityId).ToList();
            }

            // 5. 요청한 클라이언트에게 최종 타겟 목록 반환
            await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
        }

        /// <summary>
        /// 한 유닛이 다른 유닛을 공격하는 로직을 검증하고 실행합니다.
        /// </summary>
        public async Task ProcessAttackAsync(string senderUid, C_Attack action)
        {
            _eventBuffer.Clear();

            // 공격자와 방어자가 유효한지 확인
            if(!_allEntities.TryGetValue(action.attackerEntityId, out var att) || !_allEntities.TryGetValue(action.defenderEntityId, out var def)) return;
            
            // 공격 권한(내 것인지) 및 공격 가능 상태 확인
            if(att.OwnerUid != senderUid || !att.CanAttack || att.HasAttacked) return;

            // 이벤트 로그 저장
            LogEvent(GameEventType.ATTACK, att.EntityId, def.EntityId);

            // 공격 기회 소모 및 실제 전투 계산
            att.HasAttacked = true;
            await ResolveCombatAsync(att, def);

            ApllyAttacked?.Invoke(att.OwnerUid, def.OwnerUid, att.Attack);
            
            // 결과 브로드캐스트 및 사망 처리
            await BroadcastUpdatesAsync(senderUid);
            await ProcessDeathsAsync();
        }

        /// <summary>
        /// 실제 전투 데미지를 상호 교환합니다.
        /// </summary>
        public async Task ResolveCombatAsync(GameEntity att, GameEntity def)
        {
            // _pendingUpdates.Clear();
            // 서로의 공격력만큼 체력 차감
            await ApplyDamageAsync(def, att.Attack, att.EntityId);
            
            // 방어자가 공격력이 0이 아니라면 이벤트로그에 반격추가
            if (def.Attack > 0)
            {
                LogEvent(GameEventType.ATTACK, def.EntityId, att.EntityId);
            }

            await ApplyDamageAsync(att, def.Attack, def.EntityId);
        }

        // 카드의 효과를 발동한 대상을 찾는 기능
        public GameEntity? FindEntityByCard(GameCard card)
        {
            var p = GetPlayerState(card.OwnerUid);
            if (p == null) return null;
            
            if (p.Leader?.SourceCard?.InstanceId == card.InstanceId) return p.Leader;
            
            foreach (var e in p.Field)
            {
                if (e != null && e.SourceCard.InstanceId == card.InstanceId) return e;
            }
            foreach (var e in p.MemberZone)
            {
                if (e != null && e.SourceCard.InstanceId == card.InstanceId) return e;
            }
            return null;
        }


        // ==================================================================
        // 카드의 효과 적용
        // ==================================================================

        /// <summary>
        /// 특정 개체가 강제공격을 하게 만들고 업데이트 목록에 추가합니다.
        /// </summary>
        public async Task ApplyForceAttackAsync(GameEntity attacker, GameEntity? defender, EffectTriggerType trigger)
        {
            PlayerState attOwner = GetPlayerState(attacker.OwnerUid);
            PlayerState attOpp = GetPlayerState(attacker.OwnerUid, true);

            // 방어자가 딱히 지정되지 않았다면(null), 살아있는 무작위 적을 탐색 (기존 로직 유지)
            if (defender == null)
            {
                var enemies = new List<GameEntity> { attOpp.Leader };
                enemies.AddRange(attOpp.Field.Where(e => e != null && e.Health > 0)!);
                enemies.AddRange(attOpp.MemberZone.Where(e => e != null && e.Health > 0)!);

                if (enemies.Count > 0)
                {
                    defender = enemies[Rng.Next(enemies.Count)];
                }
            }

            // 공격자와 방어자가 유효하고, 자기 자신을 때리는 게 아닐 때만 전투 실행
            if (defender != null && defender.Health > 0 && attacker.EntityId != defender.EntityId)
            {
                Console.WriteLine($"[EffectProcessor] ⚔️ 강제 공격 실행: {attacker.EntityId} -> {defender.EntityId}");
                
                // 클라이언트 연출용 로그
                LogEvent(GameEventType.ATTACK, attacker.EntityId, defender.EntityId, 0, null, trigger);
                
                // 실제 데미지 교환 및 사망자 처리
                await ResolveCombatAsync(attacker, defender);
                await ProcessDeathsAsync();
            }
            else
            {
                Console.WriteLine($"[EffectProcessor] ⚠️ 강제 공격 실패: 유효한 방어자가 없습니다.");
            }
        }

        /// <summary>
        /// 특정 개체에 데미지를 입히고 업데이트 목록에 추가합니다. (비동기 버전)
        /// </summary>
        public async Task ApplyDamageAsync(GameEntity target, int amount, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE)
        {
            if (amount <= 0) return;
            target.Health -= amount;

            // 데미지 발생 이벤트 클라이언트에 전송 대기
            LogEvent(GameEventType.DAMAGE, sourceId, target.EntityId, amount, null, triggerType);
            AddPendingUpdate(target);
            OnDamageApplied?.Invoke(target.SourceCard.CardName, amount, sourceId);

            // =================================================================
            // 🚀 [신규 추가] 피해를 입었을 때 ON_DAMAGE 이벤트 발행!
            // =================================================================
            // 피해를 입은 당사자 카드와 개체를 context에 실어서 이벤트를 전파합니다.
            var damageContext = new GameServer.Effects.EffectContext(target.OwnerUid, target.SourceCard, EffectTriggerType.ON_DAMAGE)
            {
                SourceEntity = target,
                TargetEntity = target
            };

            // 비동기로 하수인의 데미지 반응 효과(예: 드로우)들을 즉시 실행합니다.
            await EventSystem.PublishAsync(EffectTriggerType.ON_DAMAGE, damageContext);
        }
        /// <summary>
        /// 특정 개체에 힐을 하고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyHeal(GameEntity target, int amount, int sourceId = 0)
        {
            int oldHealth = target.Health;
            target.Health = Math.Min(target.Health + amount, target.MaxHealth);
            int actualHeal = target.Health - oldHealth;
            Console.WriteLine($"[GameState] {target.EntityId} 회복 {actualHeal}");
            
            if (actualHeal > 0) 
            {
                LogEvent(GameEventType.HEAL, sourceId, target.EntityId, actualHeal);
                OnHealApplied?.Invoke(target.SourceCard.CardName, actualHeal, sourceId);
            }
            AddPendingUpdate(target);
        }

        /// <summary>
        /// 특정 개체에 버프를 주고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyBuff(GameEntity target, int attackBuff, int healthBuff, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE)
        {
            target.Attack += attackBuff;
            target.Health += healthBuff;
            target.MaxHealth += healthBuff;

            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                effectType = GameEventType.BUFF,
                attackMod = attackBuff,
                healthMod = healthBuff
            });

            // triggerType을 LogEvent의 마지막 파라미터 위치에 맞게 전달
            LogEvent(GameEventType.BUFF, sourceId, target.EntityId, attackBuff, healthBuff.ToString(), triggerType);
            AddPendingUpdate(target);
        }

        /// <summary>
        /// 손패 중 지정된 특정 카드 리스트에만 버프를 적용하고 클라이언트에 일괄 알립니다.
        /// </summary>
        public async Task ApplyHandBuffAsync(string ownerUid, int attackBuff, int healthBuff, int costBuff, List<GameCard> targetCards)
        {
            PlayerState p = GetPlayerState(ownerUid);
            List<GameCard> buffedCards = new List<GameCard>();

            // 던져진 카드 리스트만 순회하므로 내부 코드가 매우 깔끔해집니다.
            foreach (var card in targetCards)
            {
                // 안전장치: 손패에 실제로 존재하는 하수인 카드인 경우에만 버프를 적용합니다.
                if (p.Hand.Contains(card) && card.Type == CardType.하수인)
                {
                    card.CurrentAttack += attackBuff;
                    card.CurrentHealth += healthBuff;
                    card.CurrentCost += costBuff;
                    card.CurrentCost = Math.Max(0, card.CurrentCost); // 코스트 안전가드

                    buffedCards.Add(card);
                }
            }

            // 버프 성공 시 단 한 번만 패킷을 전송하여 네트워크를 최적화합니다.
            if (buffedCards.Count > 0)
            {
                LogEvent(GameEventType.BUFF_HAND, 0, 0, attackBuff, healthBuff.ToString());
                AddLog("System", "BUFF_HAND", $"{ownerUid}의 손패 하수인 {buffedCards.Count}장이 공격력 {attackBuff}/체력 {healthBuff} 버프를 받았습니다.");

                var updateMsg = new S_UpdateHandCards
                {
                    action = GameActionType.UPDATE_HAND_CARDS,
                    updatedCards = buffedCards.Select(c => c.ToCardInfo()).ToList()
                };
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(updateMsg));
            }
        }

        /// <summary>
        /// 덱에 있는 카드 중 특정 조건에 맞는 카드에만 버프를 누적시킵니다.
        /// </summary>
        public Task ApplyDeckBuffAsync(string ownerUid, int attackBuff, int healthBuff, int costBuff, List<GameCard> targetCards)
        {
            PlayerState p = GetPlayerState(ownerUid);
            List<GameCard> buffedCards = new List<GameCard>();

            // 던져진 카드 리스트만 순회하므로 내부 코드가 매우 깔끔해집니다.
            foreach (var card in targetCards)
            {
                // 안전장치: 덱에 실제로 존재하는 하수인 카드인 경우에만 버프를 적용합니다.
                if (p.Deck.Contains(card) && card.Type == CardType.하수인)
                {
                    card.CurrentAttack += attackBuff;
                    card.CurrentHealth += healthBuff;
                    card.CurrentCost += costBuff;
                    card.CurrentCost = Math.Max(0, card.CurrentCost); // 코스트 안전가드

                    buffedCards.Add(card);
                }
            }

            // 버프 성공 시 단 한 번만 패킷을 전송하여 네트워크를 최적화합니다.
            if (buffedCards.Count > 0)
            {
                LogEvent(GameEventType.BUFF_DECK, 0, 0, attackBuff, healthBuff.ToString());
                AddLog("System", "BUFF_DECK", $"{ownerUid}의 덱 하수인 {buffedCards.Count}장이 공격력 {attackBuff}/체력 {healthBuff} 버프를 받았습니다.");
            }

            return Task.CompletedTask;
        }

        
        /// <summary> 특정 개체에 '속박(Bind)'을 부여합니다. </summary>
        public void ApplyBind(GameEntity target, int sourceId = 0)
        {
            if (target.Keywords != null && !target.Keywords.Contains(CardKeywords.Bind))
            {
                target.Keywords.Add(CardKeywords.Bind);
            }

            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                effectType = GameEventType.BIND,
            });
            target.CanAttack = false; // 즉시 공격 불가 상태로 만듦
            LogEvent(GameEventType.BIND, sourceId, target.EntityId);
            AddPendingUpdate(target);
        }

        /// <summary> 특정 개체에 '침묵(Silence)'을 적용하여 능력치와 키워드를 원본으로 되돌립니다. </summary>
        public void ApplySilence(GameEntity target, int sourceId = 0)
        {
            target.Keywords?.Clear(); // 모든 특수 키워드 제거
            
            // 스탯을 카드 원본 스탯으로 강제 롤백
            target.Attack = target.SourceCard.OriginalAttack;
            target.MaxHealth = target.SourceCard.OriginalHealth;
            if (target.Health > target.MaxHealth) target.Health = target.MaxHealth;

            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                effectType = GameEventType.SILENCE,
            });
            LogEvent(GameEventType.SILENCE, sourceId, target.EntityId);
            AddPendingUpdate(target);
        }

        /// <summary> 특정 개체에 새로운 '키워드(Keyword)'를 부여합니다. </summary>
        public void GrantKeyword(GameEntity target, List<string> keywordStr, int sourceId = 0)
        {
            for(int i = 0; i < keywordStr.Count; i++)
            {
                if (Enum.TryParse<CardKeywords>(keywordStr[i], true, out var keyword))
            {
                if (target.Keywords != null && !target.Keywords.Contains(keyword))
                {
                    target.Keywords.Add(keyword);
                    target.Enchantments.Add(new EnchantmentInfo
                    {
                        sourceEntityId = sourceId,
                        effectType = GameEventType.GRANT_KEYWORD,
                        grantedKeyword = keywordStr[i]
                    });
                    LogEvent(GameEventType.GRANT_KEYWORD, sourceId, target.EntityId);
                    AddPendingUpdate(target);
                }
            }
            }
        }

        /// <summary> 특정 플레이어의 마나를 조작합니다 (음수 입력 시 마나 파괴/감소). </summary>
        public void ApplyManaMod(string uid, int amount)
        {
            PlayerState p = GetPlayerState(uid);
            // 현재 마나를 amount만큼 조정 (0 미만, MaxMana 초과 방지)
            p.CurrentMana = Math.Clamp(p.CurrentMana + amount, 0, p.MaxMana);
            
            // 마나 변화는 즉시 브로드캐스트가 필요하므로 별도 로그 전송 구조 추가 필요
        }


        /// <summary>
        /// 필드 위 모든 개체의 체력을 확인하여 0 이하인 개체를 제거하고 '죽음의 메아리'를 처리합니다.
        /// </summary>
        /// <returns>사망자가 발생했으면 true</returns>
        public async Task<bool> ProcessDeathsAsync()
        {
            if(_isGameOver) return true;

            // 1. 죽은 개체들 필터링
            var deadEntities = _allEntities.Values.Where(e => e.Health <= 0 || e.IsDestroyed).ToList();
            if (deadEntities.Count == 0) return false;

            foreach (var dead in deadEntities)
            {
                LogEvent(GameEventType.DEATH, dead.EntityId);
                if (!dead.IsLeader)
                {
                    OnMinionDestroyed?.Invoke(dead.OwnerUid, dead.SourceCard.CardName);
                }

                // =================================================================
                // [개선 1] 사망한 하수인의 필드 슬롯을 먼저 null로 비워 공간을 확보합니다.
                // 이 처리를 먼저 해야 죽음의 메아리로 소환되는 토큰 하수인이 이 빈 자리에 들어갈 수 있습니다.
                // =================================================================
                PlayerState owner = GetPlayerState(dead.OwnerUid);
                for(int i=0; i<owner.Field.Length; i++) 
                {
                    if (owner.Field[i] == dead) owner.Field[i] = null;
                }
                for(int i=0; i<owner.MemberZone.Length; i++) 
                {
                    if (owner.MemberZone[i] == dead) owner.MemberZone[i] = null;
                }

                // =================================================================
                // [개선 2] 슬롯은 비었지만 아직 UpdateZone(Graveyard)을 호출하지 않았으므로 
                // 효과 구독은 유지된 상태입니다. 이 상태에서 죽음의 메아리를 안전하게 실행합니다.
                // =================================================================
                if (dead.SourceCard.NewEffects.Any(e => e.Trigger == EffectTriggerType.ON_DEATH))
                {
                    LogEvent(GameEventType.EFFECT_TRIGGER, dead.EntityId, 0, 0, null, EffectTriggerType.ON_DEATH);

                    var context = new GameServer.Effects.EffectContext(dead.OwnerUid, dead.SourceCard, EffectTriggerType.ON_DEATH)
                    {
                        SourceEntity = dead,
                        TargetEntity = dead
                    };

                    // 비동기로 죽음의 메아리 효과를 즉시 실시간 전파 및 실행합니다.
                    await EventSystem.PublishAsync(EffectTriggerType.ON_DEATH, context);
                }

                // =================================================================
                // [개선 3] 효과 해결이 모두 끝난 후에 카드를 무덤 구역으로 보내고 구독을 해제합니다.
                // =================================================================
                dead.SourceCard.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                owner.Graveyard.Add(dead.SourceCard);

                // 3. 서버 전역 관리 딕셔너리에서 최종 제거
                _allEntities.Remove(dead.EntityId);

                // 5. 클라이언트에 알릴 데이터 구성 (체력 0 상태)
                var dData = dead.ToEntityData();
                dData.health = 0;
                _pendingUpdates.Add(dData);
            }

            // 6. 사망 정보 즉시 전송
            await BroadcastUpdatesAsync(_playerA.Uid);

            // 7. 게임 종료 조건 확인 (영웅 사망 시)
            if (_playerA.Leader.Health <= 0 && _playerB.Leader.Health <= 0)
            {
                await EndGameAsync("DRAW", "BOTH_LEADERS_KILLED");
                return true;
            }
            else if (_playerA.Leader.Health <= 0 || _playerB.Leader.Health <= 0)
            {
                string winner = _playerA.Leader.Health > 0 ? _playerA.Uid : _playerB.Uid;
                await EndGameAsync(winner, "LEADER_KILLED");
                return true;
            }

            // 8. 죽음의 메아리로 인해 연쇄적으로 죽은 개체가 있을 수 있으므로 재귀 호출
            return await ProcessDeathsAsync();
        }

        /// <summary>
        /// 상태가 변경된 개체를 전송 대기 목록에 추가합니다. (중복 방지)
        /// </summary>
        private void AddPendingUpdate(GameEntity entity)
        {
            var existing = _pendingUpdates.FirstOrDefault(e => e.entityId == entity.EntityId);
            if (existing != null) 
            { 
                existing.health = entity.Health; 
                existing.attack = entity.Attack; 
            }
            else 
            {
                _pendingUpdates.Add(entity.ToEntityData());
            }
        }
        
        /// <summary>
        /// UID를 통해 플레이어 상태 객체를 가져옵니다.
        /// </summary>
        /// <param name="opp">true일 경우 상대방의 상태를 반환</param>
        public PlayerState GetPlayerState(string uid, bool opp=false) => 
            (opp ? (uid==_playerA.Uid?_playerB:_playerA) : (uid==_playerA.Uid?_playerA:_playerB));

        /// <summary>
        /// 현재까지 쌓인 이벤트 로그와 변경된 개체 상태를 클라이언트에게 즉시 전송하고 버퍼를 비웁니다.
        /// 효과 발동 중 플레이어의 입력을 기다려야 할 때(일시 정지) 화면을 최신화하기 위해 호출됩니다.
        /// </summary>
        public async Task FlushUpdatesAsync()
        {
            // 이벤트가 없거나 업데이트할 내용이 없다면 스킵
            if (_eventBuffer.Count == 0 && _pendingUpdates.Count == 0) return;

            var resolutionMsg = new S_ActionResolution
            {
                action = GameActionType.ACTION_RESOLUTION,
                eventLog = [.. _eventBuffer], // 복사본 전달
                finalStateUpdates = [.. _pendingUpdates]
            };

            string json = JsonConvert.SerializeObject(resolutionMsg);

            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
            
            // 버퍼 비우기 (매우 중요)
            _eventBuffer.Clear();
            _pendingUpdates.Clear();
        }
        
        /// <summary>
        /// 이번 액션으로 변경된 모든 게임 상태 정보를 양쪽 클라이언트에 동기화합니다.
        /// </summary>
        private async Task BroadcastUpdatesAsync(string triggerPlayerUid)
        {
            // 1. 방금 만든 FlushUpdatesAsync를 재사용하여 상태 변경 사항을 전송합니다.
            await FlushUpdatesAsync();

            // 2. 현재 행동한 플레이어의 마나 정보 갱신 전송
            PlayerState p = GetPlayerState(triggerPlayerUid);
            var manaMsg = new S_UpdateMana { action = GameActionType.UPDATE_MANA, ownerUid = p.Uid, currentMana = p.CurrentMana, maxMana = p.MaxMana };
            string manaJson = JsonConvert.SerializeObject(manaMsg);
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, manaJson);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, manaJson);
        }

        /// <summary>
        /// 게임 승패가 결정되었을 때 호출되어 결과를 전송하고, 승자와 패자에게 전적/경험치/레벨/골드/점수 보상을 적용합니다.
        /// </summary>
        private async Task EndGameAsync(string winner, string reason)
        {
            if(_isGameOver) return;
            _isGameOver = true;
            _currentPhase = "GameOver";

            string loser = (winner == _playerA.Uid) ? _playerB.Uid : _playerA.Uid;

            string json = JsonConvert.SerializeObject(new S_GameOver { action=GameActionType.GAME_OVER, winnerUid=winner, reason=reason });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);

            // 🏆 승자 및 패자 보상 / 전적 / 레벨업 일괄 처리
            _ = ProcessMatchRewardsAsync(winner, loser);
        }

        /// <summary>
        /// 대전 종료 후 승자와 패자의 데이터(골드, 경험치, 레벨, 점수, 전적)를 계산하여 Firestore에 저장합니다.
        /// </summary>
        private async Task ProcessMatchRewardsAsync(string winnerUid, string loserUid)
        {
            try
            {
                if (_room.Db == null) return;

                // 1. 승자 보상: 골드 +100, 경험치 +100, 점수 +30, 승리 전적 +1
                if (!string.IsNullOrEmpty(winnerUid) && !winnerUid.StartsWith("BOT_"))
                {
                    await ApplyPlayerMatchOutcomeAsync(winnerUid, isWinner: true, goldChange: 100, expChange: 100, scoreChange: 30);
                }

                // 2. 패자 보상: 골드 +20, 경험치 +30, 점수 -15, 패배 전적 +1
                if (!string.IsNullOrEmpty(loserUid) && !loserUid.StartsWith("BOT_"))
                {
                    await ApplyPlayerMatchOutcomeAsync(loserUid, isWinner: false, goldChange: 20, expChange: 30, scoreChange: -15);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchReward] ❌ 대전 결과 보상 처리 중 오류 발생: {ex.Message}");
            }
        }

        /// <summary>
        /// 개별 플레이어의 Firestore 문서를 트랜잭션으로 안전하게 업데이트하고 레벨업 여부를 판정합니다.
        /// </summary>
        private async Task ApplyPlayerMatchOutcomeAsync(string uid, bool isWinner, int goldChange, int expChange, int scoreChange)
        {
            try
            {
                DocumentReference userRef = _room.Db.Collection("Users").Document(uid);
                await _room.Db.RunTransactionAsync(async transaction =>
                {
                    DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(userRef);
                    if (!snapshot.Exists) return;

                    UserData user = snapshot.ConvertTo<UserData>();

                    // 1. 골드 갱신
                    int newGold = Math.Max(0, user.Gold + goldChange);

                    // 2. 점수(랭크 포인트) 갱신 (최소 0점)
                    int newScore = Math.Max(0, user.Score + scoreChange);

                    // 3. 전적 갱신
                    int newWinCount = user.WinCount + (isWinner ? 1 : 0);
                    int newLossCount = user.LossCount + (isWinner ? 0 : 1);

                    // 4. 경험치 및 레벨업 계산 (필요 경험치 = 현재레벨 * 100)
                    int currentLevel = user.Level > 0 ? user.Level : 1;
                    int currentExp = user.Exp + expChange;

                    while (true)
                    {
                        int requiredExp = currentLevel * 100;
                        if (currentExp >= requiredExp)
                        {
                            currentExp -= requiredExp;
                            currentLevel++;
                            Console.WriteLine($"[LevelUp] 🎉 플레이어 {uid} 레벨업! (Lv.{currentLevel})");
                        }
                        else
                        {
                            break;
                        }
                    }

                    Dictionary<string, object> updates = new Dictionary<string, object>
                    {
                        { "Gold", newGold },
                        { "Score", newScore },
                        { "Level", currentLevel },
                        { "Exp", currentExp },
                        { "WinCount", newWinCount },
                        { "LossCount", newLossCount }
                    };

                    transaction.Update(userRef, updates);
                    Console.WriteLine($"[MatchReward] 🎮 플레이어 {uid} ({(isWinner ? "승리" : "패배")}): Gold={newGold}(+{(goldChange >= 0 ? "+" : "")}{goldChange}), Lv={currentLevel}, Exp={currentExp}(+{expChange}), Score={newScore}({(scoreChange >= 0 ? "+" : "")}{scoreChange}), 전적={newWinCount}승 {newLossCount}패");
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchReward] ❌ 플레이어 {uid} 보상 적용 실패: {ex.Message}");
            }
        }

        // 대시보드 전용
        public class GameSnapshot
        {
            public string PlayerAUid { get; set; } = "";
            public string PlayerBUid { get; set; } = "";
            public string CurrentTurnPlayerUid { get; set; } = "";
            public string CurrentPhase { get; set; } = "";
            public int PlayerAMana { get; set; }
            public int PlayerBMana { get; set; }
            public int PlayerAHealth { get; set; }
            public int PlayerBHealth { get; set; }
            
            // [신규] 턴 남은 시간
            public long TurnEndTime { get; set; }

            // [신규] 필드 상황 (5칸) 및 멤버존 (1칸)
            public List<EntityData?> PlayerAField { get; set; } = new List<EntityData?>();
            public List<EntityData?> PlayerBField { get; set; } = new List<EntityData?>();
            public List<EntityData?> PlayerAMember { get; set; } = new List<EntityData?>();
            public List<EntityData?> PlayerBMember { get; set; } = new List<EntityData?>();

            // [신규] 손패 상황
            public List<CardInfo> PlayerAHand { get; set; } = new List<CardInfo>();
            public List<CardInfo> PlayerBHand { get; set; } = new List<CardInfo>();

            public List<GameLogEvent>? Logs { get; set; }
        }

        // GameState 클래스 내부에 스냅샷 반환 메서드 추가
        public GameSnapshot GetSnapshot()
        {
            return new GameSnapshot
            {
                // 1. 플레이어 기본 정보 및 마나, 체력 [1, 2]
                PlayerAUid = _playerA.Uid,
                PlayerBUid = _playerB.Uid,
                CurrentTurnPlayerUid = _currentTurnPlayerUid,
                CurrentPhase = _currentPhase,
                PlayerAMana = _playerA.CurrentMana,
                PlayerBMana = _playerB.CurrentMana,
                PlayerAHealth = _playerA.Leader.Health,
                PlayerBHealth = _playerB.Leader.Health,
                TurnEndTime = _turnEndTime,

                // 2. 필드 상황 (5칸의 하수인 정보) [1, 3]
                // 필드 위 GameEntity 객체들을 클라이언트가 읽을 수 있는 EntityData 형태로 변환(ToEntityData)하여 전달합니다.
                PlayerAField = _playerA.Field.Select(e => e?.ToEntityData()).ToList(),
                PlayerBField = _playerB.Field.Select(e => e?.ToEntityData()).ToList(),

                // 3. 특수 멤버 존 상황 (1칸) [1, 3]
                PlayerAMember = _playerA.MemberZone.Select(e => e?.ToEntityData()).ToList(),
                PlayerBMember = _playerB.MemberZone.Select(e => e?.ToEntityData()).ToList(),

                // 4. 각 플레이어의 손패 정보 (실시간 카드 목록) [1, 3]
                // 손에 든 GameCard 객체들을 CardInfo 형태로 변환(ToCardInfo)하여 대시보드로 보냅니다.
                PlayerAHand = _playerA.Hand.Select(c => c.ToCardInfo()).ToList(),
                PlayerBHand = _playerB.Hand.Select(c => c.ToCardInfo()).ToList(),

                // 5. 최근 게임 로그 [3]
                // 게임 중에 기록된 전체 액션 로그 중 최근 50개만 잘라서 보냅니다.
                Logs = _actionLogs.TakeLast(100).ToList()
            };
        }

        public void AddLog(string actor, string actionType, string message, object? details = null)
        {
        var newLog = new GameLogEvent
        {
            Timestamp = DateTime.UtcNow,
            Actor = actor,
            ActionType = actionType,
            Message = message,
            Details = details
        };
    
        lock (_lock) {
            _actionLogs.Add(newLog);
         }
    
        // 이 시점에서 인게임 클라이언트(WebSocket 등)에게도 
        // "S_NewLogEvent" 패킷을 브로드캐스팅하면 인게임 UI 좌측 로그에 즉시 표시됩니다!
    }
    }
}