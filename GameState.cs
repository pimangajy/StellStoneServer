using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text; // StringBuilder 사용 (문자열 조합)
using System.Threading.Tasks; // 비동기 작업(Task) 사용
using Newtonsoft.Json; // JSON 직렬화/역직렬화

namespace GameServer
{
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
        
        // --- 원본 스탯 (절대 변하지 않는 기준값) ---
        public int OriginalCost { get; private set; }
        public int OriginalAttack { get; private set; }
        public int OriginalHealth { get; private set; }
        public List<string> OriginalKeywords { get; private set; }

        public string Type { get; private set; }   // "Minion", "Spell", "Member" 등
        public string Class { get; private set; }  // 직업
        public string Tribe { get; private set; }  // 종족 (강도단 등)
        public string TargetRule { get; private set; } // 타겟팅 규칙
        public List<ServerEffectData> Effects { get; private set; } // 특수 효과 목록

        // --- 현재 스탯 (게임 중 버프/너프에 의해 변하는 값) ---
        public int CurrentCost { get; set; }
        public int CurrentAttack { get; set; }
        public int CurrentHealth { get; set; }
        public List<string> CurrentKeywords { get; set; }

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
                OriginalCost = data.Cost;
                OriginalAttack = data.AttackValue;
                OriginalHealth = data.HealthValue;
                OriginalKeywords = data.Keywords != null ? new List<string>(data.Keywords) : new List<string>();
                
                Type = data.CardType ?? "Unknown";
                Class = data.Class ?? "Neutral";
                Tribe = data.Tribe ?? "None";
                TargetRule = data.TargetRule ?? "None";
                Effects = data.GetParsedEffects();

                // 타입 자체를 키워드로 추가 (로직 처리 편의성)
                if (!string.IsNullOrEmpty(Type))
                {
                    string typeUpper = Type.ToUpper();
                    if (!OriginalKeywords.Contains(typeUpper)) OriginalKeywords.Add(typeUpper);
                }
            }
            else
            {
                // DB에 없는 카드일 경우 (에러 방지용 기본값)
                Console.WriteLine($"[GameCard] ⚠️ DB에서 카드 데이터를 찾을 수 없습니다: {cardId}");
                OriginalCost = 1; OriginalAttack = 1; OriginalHealth = 1;
                Type = "Minion"; Class = "Neutral"; Tribe = "None"; TargetRule = "None";
                OriginalKeywords = new List<string> { "MINION" };
                Effects = new List<ServerEffectData>();
            }

            // 초기에는 현재 스탯 = 원본 스탯
            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<string>(OriginalKeywords);
        }

        // [생성자 2] 영웅(Leader) 카드 생성 (코드에서 직접 생성)
        public GameCard(string instanceId, string playerClass, int health = 30)
        {
            CardId = $"LEADER_{playerClass}";
            InstanceId = instanceId;

            OriginalCost = 0;
            OriginalAttack = 0;
            OriginalHealth = health;
            OriginalKeywords = new List<string> { "HERO" };

            Type = "Hero";
            Class = playerClass;
            Tribe = "None";
            TargetRule = "None";
            Effects = new List<ServerEffectData>();

            CurrentCost = OriginalCost;
            CurrentAttack = OriginalAttack;
            CurrentHealth = OriginalHealth;
            CurrentKeywords = new List<string>(OriginalKeywords);
        }

        // 클라이언트에게 보낼 데이터(CardInfo)로 변환
        public CardInfo ToCardInfo()
        {
            return new CardInfo
            {
                cardId = this.CardId,
                instanceId = this.InstanceId,
                currentCost = this.CurrentCost,
                currentAttack = this.CurrentAttack,
                currentHealth = this.CurrentHealth
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
        public List<string> Keywords { get; set; }
        public string Tribe { get; set; }
        public int Position { get; set; }
        public bool IsMember { get; set; }

        public GameEntity(int entityId, GameCard sourceCard, string ownerUid)
        {
            EntityId = entityId;
            SourceCard = sourceCard;
            OwnerUid = ownerUid;
            
            // 소환 시점의 카드 스탯을 가져옴
            Attack = sourceCard.CurrentAttack;
            Health = sourceCard.CurrentHealth;
            MaxHealth = sourceCard.CurrentHealth;
            Keywords = new List<string>(sourceCard.CurrentKeywords);
            Tribe = sourceCard.Tribe; 
            
            // 속공(Rush)이나 돌진(Charge)이 있으면 바로 공격 가능
            bool hasCharge = Keywords.Contains("CHARGE");
            bool hasRush = Keywords.Contains("RUSH");

            //CanAttack = hasCharge || hasRush; 
            CanAttack = true; // [테스트용]
            HasAttacked = false;
        }

        // 클라이언트에게 보낼 데이터(EntityData)로 변환
        public EntityData ToEntityData()
        {
            return new EntityData
            {
                entityId = this.EntityId,
                cardId = this.SourceCard.CardId,
                ownerUid = this.OwnerUid,
                attack = this.Attack,
                health = this.Health,
                maxHealth = this.MaxHealth,
                canAttack = this.CanAttack,
                hasAttacked = this.HasAttacked,
                keywords = this.Keywords,
                position = this.Position, 
                isMember = this.IsMember 
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

            // 게임 시작 시 플레이어의 덱 데이터를 기반으로 GameCard 객체들을 생성하여 덱에 채움
            if (player.Deck != null && player.Deck.cardIds != null)
            {
                foreach (string cardId in player.Deck.cardIds)
                {
                    // 덱 내의 카드들도 구분을 위해 임시 인스턴스 ID 부여
                    string instanceId = $"DeckCard_{Uid}_{_nextInstanceId++}";
                    Deck.Add(new GameCard(cardId, instanceId));
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
        
        // 카드 효과(데미지, 버프 등) 처리를 전담하는 프로세서
        private readonly GameEffectProcessor _effectProcessor;

        private string _currentTurnPlayerUid = "";           // 현재 턴을 진행 중인 플레이어 UID
        private string _firstPlayerUid = "";                // 이번 게임의 선공 플레이어 UID
        private string _secondPlayerUid = "";               // 이번 게임의 후공 플레이어 UID
        private string? _currentPhase;                       // 현재 게임 단계 (Mulligan, Main 등)
        
        public Random Rng { get; private set; } = new Random();
        private int _nextGlobalEntityId = 100;               // 하수인/멤버 생성을 위한 전역 개체 ID 카운터
        private bool _isGameOver = false;                   // 게임 종료 여부
        private readonly object _lock = new object();        // 멀티스레드 환경에서의 데이터 안전을 위한 락 객체
        private bool _isGameStarted = false;                // 게임 루프 시작 여부

        // 멀리건 결정을 저장 (양쪽 모두 완료될 때까지 대기용)
        private readonly Dictionary<string, C_MulliganDecision?> _mulliganDecisions = new Dictionary<string, C_MulliganDecision?>();
        
        // 클라이언트에 한꺼번에 보낼 변경된 개체 데이터 목록
        private List<GameEvent> _eventBuffer = new List<GameEvent>();
        private List<EntityData> _pendingUpdates = new List<EntityData>();

        /// <summary>
        /// (신규) 이벤트를 로그에 기록합니다.
        /// </summary>
        public void LogEvent(string type, int sourceId, int targetId = 0, int val = 0, string? strVal = null, EntityData? entityData = null)
        {
            _eventBuffer.Add(new GameEvent
            {
                eventType = type,
                sourceEntityId = sourceId,
                targetEntityId = targetId,
                value = val,
                stringValue = strVal,
                entityData = entityData,
            });
        }

        // 로그 보관 리스트
         private List<GameLogEvent> _actionLogs = new List<GameLogEvent>();
         // 방송국(이벤트) 설립
        public event Action<string, string>? OnCardPlayed;    // 누가, 무슨 카드를 냈는가?
        public event Action<string, string, int>? OnAttacked; // 누가, 누구를, 데미지 몇으로 공격했는가?
        

        public GameState(GameRoom room, GamePlayer playerA, GamePlayer playerB)
        {
            _room = room;
            
            // 1. 각 플레이어의 영웅 개체 생성 (기본 체력 30)
            GameCard leaderCardA = new GameCard("Leader_A_Instance", playerA.Deck?.deckClass ?? "Neutral", 30);
            GameEntity leaderA = new GameEntity(1, leaderCardA, playerA.Uid);
            
            GameCard leaderCardB = new GameCard("Leader_B_Instance", playerB.Deck?.deckClass ?? "Neutral", 30);
            GameEntity leaderB = new GameEntity(2, leaderCardB, playerB.Uid);

            // 2. 전역 개체 목록에 등록 (ID 1, 2번은 영웅 고정)
            _allEntities.Add(leaderA.EntityId, leaderA);
            _allEntities.Add(leaderB.EntityId, leaderB);
            
            // 3. 플레이어 상태 초기화
            _playerA = new PlayerState(playerA, leaderA);
            _playerB = new PlayerState(playerB, leaderB);
            
            // 4. 멀리건 상태 초기화
            _mulliganDecisions[_playerA.Uid] = null;
            _mulliganDecisions[_playerB.Uid] = null;
            
            _effectProcessor = new GameEffectProcessor(this);

            // Player A와 B가 "카드 뽑았다"고 소리치면, GameState가 그걸 듣고 AddLog를 실행함
            _playerA.OnCardDrawn += (playerName, id) => {
                AddLog(playerName, "DRAW", $"{playerName}이(가) {id}카드를 뽑았습니다.");
            };

            _playerB.OnCardDrawn += (playerName, id) => {
                AddLog(playerName, "DRAW", $"{playerName}이(가) {id}카드를 뽑았습니다.");
            };

            // 게임이 생성될 때 로그 시스템을 이벤트에 연결(구독)시킵니다.
            InitializeLogger(); 
        }

        private void InitializeLogger()
        {
            // 카드를 냈을 때 알아서 로그 작성
            this.OnCardPlayed += (playerName, cardName) => {
                AddLog(playerName, "PLAY_CARD", $"{playerName}이(가) [{cardName}]을(를) 사용했습니다.");
            };

            // 공격했을 때 알아서 로그 작성
            this.OnAttacked += (attackerName, targetName, damage) => {
                AddLog(attackerName, "ATTACK", $"{attackerName}이(가) {targetName}에게 {damage}의 피해를 입혔습니다!");
            };
        }

        /// <summary>
        /// 클라이언트(WebSocket)로부터 받은 JSON 메시지를 해석하고 권한을 확인하여 실행합니다.
        /// </summary>
        public async Task HandlePlayerActionAsync(string senderUid, string messageJson)
        {
            BaseGameAction? baseAction = null;
            try { baseAction = JsonConvert.DeserializeObject<BaseGameAction>(messageJson); } catch { return; }
            if (baseAction == null) return;
            
            if (_currentPhase == "Mulligan")
            {
                // 멀리건 페이즈: 턴에 관계없이 각자 전송 가능
                PlayerState currentPlayer = GetPlayerState(_currentTurnPlayerUid);
                
                await DispatchActionAsync(senderUid, baseAction.action!, messageJson);
                
            }
            else
            {
                // 메인 게임 중: 턴 주인인지 확인
                PlayerState turnOwner = GetPlayerState(_currentTurnPlayerUid);

                // 유저의 턴이면 보낸 사람 본인의 UID로 실행
                await DispatchActionAsync(senderUid, baseAction.action!, messageJson);
                
            }
        }

        /// <summary>
        /// 검증된 UID와 액션 종류에 따라 실제 로직 함수를 호출합니다.
        /// </summary>
        private async Task DispatchActionAsync(string uid, string action, string json)
        {
            // 턴 주인이 아닌데 멀리건 페이즈도 아니라면 무시
            if (_currentPhase != "Mulligan" && uid != _currentTurnPlayerUid) 
            {
                return;
            }

            switch (action)
            {
                case "MULLIGAN_DECISION": // 멀리건 결정
                    var mul = JsonConvert.DeserializeObject<C_MulliganDecision>(json);
                    if (mul != null) await ProcessMulliganDecisionAsync(uid, mul);
                    break;
                case "END_TURN":          // 턴 종료
                    await ProcessEndTurnAsync(uid);
                    break;
                case "PLAY_CARD":        // 카드 내기
                    var play = JsonConvert.DeserializeObject<C_PlayCard>(json);
                    if (play != null) await ProcessPlayCardAsync(uid, play);
                    break;
                case "ATTACK":           // 공격 명령
                    var atk = JsonConvert.DeserializeObject<C_Attack>(json);
                    if (atk != null) await ProcessAttackAsync(uid, atk);
                    break;
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
                var msg = new S_MulliganInfo { action = "MULLIGAN_INFO", cardsToMulligan = hand, mulliganEndTime = endTime };
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(msg));
            }

            await SendInfo(_playerA, handA);
            await SendInfo(_playerB, handB);

            // 🤖 봇이 있다면 BotAI에게 멀리건 의사결정을 위임
            if (_playerA.PlayerRef.IsBot) _ = Task.Run(async () => await new BotAI(this, _playerA.Uid).ExecuteMulliganAsync());
            if (_playerB.PlayerRef.IsBot) _ = Task.Run(async () => await new BotAI(this, _playerB.Uid).ExecuteMulliganAsync());
        }

        private List<CardInfo> DrawInitialHand(PlayerState p) {
            for (int i = 0; i < 5; i++) p.DrawCard();
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
            for (int i = 0; i < cardsToReturn.Count; i++) player.DrawCard();
            
            // 3. 뺐던 카드를 다시 덱에 넣고 섞음
            if (cardsToReturn.Count > 0) { player.Deck.AddRange(cardsToReturn); player.ShuffleDeck(Rng); }

            // 4. 상대방에게 나의 멀리건 완료 상태(몇 장 바꿨는지)를 알림
            var statusMsg = new S_OpponentMulliganStatus
            {
                action = "OPPONENT_MULLIGAN_STATUS",
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

            // 양쪽 플레이어에게 최종 손패와 함께 게임 시작을 알림
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { action = "GAME_READY", firstPlayerUid = _firstPlayerUid, finalHand = handA, enermyfinalHand = handB }));
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { action = "GAME_READY", firstPlayerUid = _firstPlayerUid, finalHand = handB, enermyfinalHand = handA }));

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

            // 1. Standby 페이즈 알림
            _currentPhase = "Standby";
            var phaseMsg = JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="Standby", TurnPlayerUid=_currentTurnPlayerUid });
            await _room.SendMessageToPlayerAsync(p.PlayerRef, phaseMsg);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, phaseMsg);

            // 2. 마나 충전 (최대 10까지 1씩 증가)
            p.MaxMana = Math.Min(p.MaxMana + 1, 10);
            p.CurrentMana = p.MaxMana;
            
            await BroadcastManaUpdate(p, op);
            await Task.Delay(1000);

            // 3. Draw 페이즈 (카드 한 장 뽑기)
            _currentPhase = "Draw";
            GameCard? drawnCard = p.DrawCard();
            // 드로우 카드 전송
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart {TurnPlayerUid = _currentTurnPlayerUid, action="PHASE_START", phase="Draw", drawnCard=drawnCard?.ToCardInfo() }));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart {TurnPlayerUid = _currentTurnPlayerUid, action="PHASE_START", phase="Draw", drawnCard=drawnCard?.ToCardInfo() }));

            await Task.Delay(1000);

            // 4. Main 페이즈 시작 (실제 플레이 타임, 60초 제한)
            _currentPhase = "Main";
            long turnEndTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60; 
            var mainMsg = new S_PhaseStart { action = "PHASE_START", phase = "Main", turnEndTime = turnEndTime };
            
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            
            // 5. 필드 위 개체들의 공격 기회 초기화
            foreach(var e in p.Field) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            foreach(var e in p.MemberZone) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            p.Leader.CanAttack = true; p.Leader.HasAttacked = false;

            // 🤖 봇의 턴이라면 BotAI 실행
            if (p.PlayerRef.IsBot)
            {
                _ = Task.Run(async () => {
                    // BotAI 클래스가 별도로 구현되어 있다고 가정
                    BotAI ai = new BotAI(this, uid);
                    await ai.ExecuteTurnAsync();
                });
            }
        }

        /// <summary>
        /// 양쪽 플레이어에게 현재 마나 상태를 최신화하여 보냅니다.
        /// </summary>
        private async Task BroadcastManaUpdate(PlayerState p, PlayerState op)
        {
            var manaMsg = new S_UpdateMana { action="UPDATE_MANA", ownerUid=p.Uid, currentMana=p.CurrentMana, maxMana=p.MaxMana };
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
            var msg = JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="End" });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, msg);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, msg);
            
            await Task.Delay(500);
            
            // 상대방의 턴으로 교체
            string nextUid = (senderUid == _playerA.Uid) ? _playerB.Uid : _playerA.Uid;
            await StartTurnAsync(nextUid);
        }

        // ------------------------------------------------------------------
        // [Phase 3] 전투 및 카드 플레이 로직
        // ------------------------------------------------------------------

        /// <summary>
        /// 손에 있는 카드를 필드에 내거나 사용하는 로직입니다.
        /// </summary>
        public async Task ProcessPlayCardAsync(string senderUid, C_PlayCard action)
        {
            PlayerState p = GetPlayerState(senderUid);
            PlayerState op = GetPlayerState(senderUid, true);
            _eventBuffer.Clear();
            _pendingUpdates.Clear();

            // 1. 카드 존재 및 마나 자원 확인
            GameCard? card = p.Hand.FirstOrDefault(c => c.InstanceId == action.handCardInstanceId);
            if (card == null || p.CurrentMana < card.CurrentCost) 
            {
                return;
            }

            // 2. 하수인 또는 멤버인지 확인
            bool isUnit = card.Type == "Minion" || card.Type == "하수인" || card.Type == "Member" || card.Type == "멤버";
            bool isMember = (card.Type == "멤버" || card.Type == "Member");
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            if (isUnit)
            {
                // 3. 소환 위치(슬롯) 유효성 및 빈 자리인지 확인
                if (action.position < 0 || action.position >= targetZone.Length || targetZone[action.position] != null) return;
                
                // 4. 자원 소모 및 손패 제거
                p.CurrentMana -= card.CurrentCost;
                p.Hand.Remove(card);

                // 5. 실제 개체(Entity) 생성 및 필드 배치
                int eid = _nextGlobalEntityId++;
                GameEntity sourceEntity = new GameEntity(eid, card, senderUid);
                sourceEntity.Position = action.position;
                sourceEntity.IsMember = isMember;
                
                LogEvent("SUMMON", sourceEntity.EntityId, 0, action.position, card.CardId, sourceEntity.ToEntityData());
                _allEntities.Add(eid, sourceEntity);
                targetZone[action.position] = sourceEntity;
                AddPendingUpdate(sourceEntity); // 클라이언트에게 알리기 위해 추가

                // 6. '전투의 함성(ON_PLAY)' 효과 처리
                GameEntity? battlecryTarget = null;
                if(action.targetEntityId > 0) 
                {
                    _allEntities.TryGetValue(action.targetEntityId, out battlecryTarget);
                }
                // 타겟 지정 여부와 무관하게 효과가 발동한다는 연출을 띄우기 위해 이벤트 기록
                LogEvent("EFFECT_TRIGGER", sourceEntity.EntityId, action.targetEntityId, 0, "ON_PLAY");

                await _effectProcessor.ExecuteEffectsAsync(card, sourceEntity, battlecryTarget, "ON_PLAY", senderUid);
            }

            // 7. 결과 성공 통보 및 필드 상태 브로드캐스트
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardSuccess { action = "PLAY_CARD_SUCCESS", serverInstanceId = card.InstanceId }));
            await BroadcastUpdatesAsync(senderUid);
            
            // 상대방에게는 '누가 어떤 카드를 냈다'는 연출용 메시지 전송
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_OpponentPlayCard { action="OPPONENT_PLAY_CARD", cardPlayed=card.ToCardInfo(), targetEntityId=action.targetEntityId }));
            
            // 8. 카드 효과 등으로 인해 죽은 개체가 있는지 확인
            await ProcessDeathsAsync();

            OnCardPlayed!(p.Uid, card.CardId);
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
            LogEvent("ATTACK", att.EntityId, def.EntityId);

            // 공격 기회 소모 및 실제 전투 계산
            att.HasAttacked = true;
            await ResolveCombatAsync(att, def);

            OnAttacked!(att.OwnerUid, def.OwnerUid, att.Attack);
            
            // 결과 브로드캐스트 및 사망 처리
            await BroadcastUpdatesAsync(senderUid);
            await ProcessDeathsAsync();
        }

        /// <summary>
        /// 실제 전투 데미지를 상호 교환합니다.
        /// </summary>
        public async Task ResolveCombatAsync(GameEntity att, GameEntity def)
        {
            _pendingUpdates.Clear();
            // 서로의 공격력만큼 체력 차감
            ApplyDamage(def, att.Attack, att.EntityId);
            ApplyDamage(att, def.Attack, def.Attack);
        }

        /// <summary>
        /// 특정 개체에 데미지를 입히고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyDamage(GameEntity target, int amount, int sourceId = 0)
        {
            if (amount <= 0) return;
            target.Health -= amount;
            LogEvent("DAMAGE", sourceId, target.EntityId, amount);
            OnAttacked!(sourceId.ToString(), target.EntityId.ToString(), amount);
            AddPendingUpdate(target);
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
            
            if (actualHeal > 0) LogEvent("HEAL", sourceId, target.EntityId, actualHeal);
            AddPendingUpdate(target);
        }
        /// <summary>
        /// 특정 개체에 버프를 주고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyBuff(GameEntity target, int attackBuff, int healthBuff, int sourceId = 0)
        {
            target.Attack += attackBuff;
            target.Health += healthBuff;
            target.MaxHealth += healthBuff; 
            
            LogEvent("BUFF", sourceId, target.EntityId, attackBuff, healthBuff.ToString());
            AddPendingUpdate(target);
        }

        /// <summary>
        /// 필드 위 모든 개체의 체력을 확인하여 0 이하인 개체를 제거하고 '죽음의 메아리'를 처리합니다.
        /// </summary>
        /// <returns>사망자가 발생했으면 true</returns>
        private async Task<bool> ProcessDeathsAsync()
        {
            if(_isGameOver) return true;
            _eventBuffer.Clear();
            _pendingUpdates.Clear();

            // 1. 죽은 개체들 필터링
            var deadEntities = _allEntities.Values.Where(e => e.Health <= 0).ToList();
            if (deadEntities.Count == 0) return false;

            foreach (var dead in deadEntities)
            {
                LogEvent("DEATH", dead.EntityId);
                
                // 2. '죽음의 메아리(ON_DEATH)' 효과 실행
                LogEvent("EFFECT_TRIGGER", dead.EntityId, 0, 0, "ON_DEATH");
                await _effectProcessor.ExecuteEffectsAsync(dead.SourceCard, dead, null, "ON_DEATH", dead.OwnerUid);

                // 3. 서버 데이터 저장소에서 제거
                _allEntities.Remove(dead.EntityId);
                
                // 4. 소유 플레이어의 필드/멤버존 슬롯 비우기
                PlayerState owner = GetPlayerState(dead.OwnerUid);
                for(int i=0; i<owner.Field.Length; i++) if (owner.Field[i] == dead) owner.Field[i] = null;
                for(int i=0; i<owner.MemberZone.Length; i++) if (owner.MemberZone[i] == dead) owner.MemberZone[i] = null;

                // 5. 클라이언트에 알릴 데이터 구성 (체력 0 상태)
                var dData = dead.ToEntityData();
                dData.health = 0; 
                _pendingUpdates.Add(dData);
            }

            // 6. 사망 정보 즉시 전송
            await BroadcastUpdatesAsync(_playerA.Uid); 
            
            // 7. 게임 종료 조건 확인 (영웅 사망 시)
            if (_playerA.Leader.Health <= 0 || _playerB.Leader.Health <= 0)
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
        /// 이번 액션으로 변경된 모든 게임 상태 정보를 양쪽 클라이언트에 동기화합니다.
        /// </summary>
        private async Task BroadcastUpdatesAsync(string triggerPlayerUid) 
        { 
             PlayerState p = GetPlayerState(triggerPlayerUid);

             // 이벤트가 없거나 업데이트할 내용이 없다면 스킵
            if (_eventBuffer.Count == 0 && _pendingUpdates.Count == 0) return;
             
             // 1. 변경된 엔티티(하수인/영웅) 정보 전송
            var resolutionMsg = new S_ActionResolution
            {
                action = "ACTION_RESOLUTION",
                eventLog = [.. _eventBuffer], // 복사본 전달
                finalStateUpdates = [.. _pendingUpdates]
            };

            string json = JsonConvert.SerializeObject(resolutionMsg);

            // string json = JsonConvert.SerializeObject(new S_UpdateEntities { action = "UPDATE_ENTITIES", updatedEntities = _pendingUpdates });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
            _eventBuffer.Clear();
            _pendingUpdates.Clear();
            

             // 2. 현재 행동한 플레이어의 마나 정보 갱신 전송
             var manaMsg = new S_UpdateMana { action = "UPDATE_MANA", ownerUid = p.Uid, currentMana = p.CurrentMana, maxMana = p.MaxMana };
             string manaJson = JsonConvert.SerializeObject(manaMsg);
             await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, manaJson);
             await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, manaJson);
        }

        /// <summary>
        /// 게임 승패가 결정되었을 때 호출되어 결과를 전송하고 세션을 정리합니다.
        /// </summary>
        private async Task EndGameAsync(string winner, string reason)
        {
            if(_isGameOver) return;
            _isGameOver = true;
            _currentPhase = "GameOver";
            string json = JsonConvert.SerializeObject(new S_GameOver { action="GAME_OVER", winnerUid=winner, reason=reason });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
        }

        // 대시보드 전용
        public class GameSnapshot
        {
         public string CurrentTurnPlayerUid { get; set; } = "";
          public string CurrentPhase { get; set; } = "";
          public int PlayerAMana { get; set; }
           public int PlayerBMana { get; set; }
           public int PlayerAHealth { get; set; }
          public int PlayerBHealth { get; set; }
          public List<GameLogEvent>? Logs { get; set; }
          // 필요하다면 필드의 하수인 개수나 액션 로그를 추가할 수 있습니다.
        }

        // GameState 클래스 내부에 스냅샷 반환 메서드 추가
        public GameSnapshot GetSnapshot()
        {
            // 스레드 안전성을 위해 lock 블록 안에서 중요 상태를 복사합니다.
            lock (_lock)
            {
                return new GameSnapshot
                {
                    CurrentTurnPlayerUid = _currentTurnPlayerUid,
                    CurrentPhase = _currentPhase ?? "Waiting",
                    PlayerAMana = _playerA.CurrentMana,
                    PlayerBMana = _playerB.CurrentMana,
                    PlayerAHealth = _playerA.Leader?.Health ?? 0,
                    PlayerBHealth = _playerB.Leader?.Health ?? 0,

                    Logs = _actionLogs.ToList()
                    // Logs = _actionLogs.TakeLast(50).ToList() // 끝에서부터 최근 50개만 잘라서 전송
                };
            }
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