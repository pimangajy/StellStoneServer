using System;
using System.Collections.Generic;
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

    // ==================================================================
    // 2. PlayerState (플레이어 상태 관리)
    // ==================================================================
    
    /// <summary>
    /// 플레이어 한 명의 모든 정보(덱, 손패, 필드, 마나)를 관리하는 클래스입니다.
    /// </summary>
    public class PlayerState
    {
        public string Uid { get; private set; }
        public GamePlayer PlayerRef { get; private set; } // 통신용 참조
        public GameEntity Leader { get; private set; }    // 영웅(본체)
        
        public List<GameCard> Deck { get; private set; } // 덱 (보이지 않음)
        public List<GameCard> Hand { get; private set; } // 손패

        // ★ [중요] 필드 슬롯 시스템 (배열 사용)
        // List 대신 배열을 사용하여 '비어있는 칸(null)'을 표현합니다.
        // Field[0] ~ Field[4] : 일반 하수인 (5칸)
        public GameEntity?[] Field { get; private set; } = new GameEntity?[5];
        
        // MemberZone[0] : 멤버 전용 (1칸)
        public GameEntity?[] MemberZone { get; private set; } = new GameEntity?[1];
        
        public int CurrentMana { get; set; }
        public int MaxMana = 9 ;
        
        private int _nextInstanceId = 1; // 손패 카드 ID 발급용 카운터
        
        public PlayerState(GamePlayer player, GameEntity leader)
        {
            Uid = player.Uid;
            PlayerRef = player;
            Leader = leader;
            
            Deck = new List<GameCard>();
            Hand = new List<GameCard>();
            
            // 덱 정보가 있으면 GameCard 객체로 변환하여 채워넣음
            if(player.Deck != null && player.Deck.cardIds != null)
            {
                foreach(string cardId in player.Deck.cardIds)
                {
                    string instanceId = $"DeckCard_{Uid}_{_nextInstanceId++}";
                    Deck.Add(new GameCard(cardId, instanceId));
                }
            }
        }
        
        // 덱 섞기 (Fisher-Yates 알고리즘)
        public void ShuffleDeck(Random rng)
        {
            int n = Deck.Count;
            while (n > 1) { n--; int k = rng.Next(n + 1); (Deck[k], Deck[n]) = (Deck[n], Deck[k]); }
        }

        // 카드 1장 뽑기
        public GameCard? DrawCard()
        {
            if (Deck.Count == 0) return null; // 탈진
            
            GameCard card = Deck[Deck.Count - 1]; // 맨 위 카드 가져오기
            Deck.RemoveAt(Deck.Count - 1);
            
            // 손패로 들어올 때 인스턴스 ID 갱신 (HandCard_...)
            card.InstanceId = $"HandCard_{Uid}_{_nextInstanceId++}";
            Hand.Add(card);
            return card;
        }
    }

    // ==================================================================
    // 3. 게임 상태 엔진 (GameState) - 게임의 핵심 두뇌
    // ==================================================================
    public class GameState
    {
        private readonly GameRoom _room; // 통신 담당
        private readonly PlayerState _playerA;
        private readonly PlayerState _playerB;
        
        // 전체 Entity 검색용 딕셔너리 (ID로 개체를 빠르게 찾기 위함)
        private readonly Dictionary<int, GameEntity> _allEntities = new Dictionary<int, GameEntity>();
        
        // 효과 처리기 (데미지, 힐, 버프 로직 분리)
        private readonly GameEffectProcessor _effectProcessor;

        private string _currentTurnPlayerUid;
        private string _firstPlayerUid = ""; 
        private string _secondPlayerUid = ""; 
        private string? _currentPhase; // 현재 게임 단계 (Mulligan, Main, End 등)
        
        public Random Rng { get; private set; } = new Random();
        private int _nextGlobalEntityId = 100; // 하수인 ID는 100번부터 시작
        private bool _isGameOver = false; 
        private readonly object _lock = new object(); // 멀티스레드 안전장치
        private bool _isGameStarted = false; 

        // 멀리건 결정 저장소
        private readonly Dictionary<string, C_MulliganDecision?> _mulliganDecisions = new Dictionary<string, C_MulliganDecision?>();
        
        // 클라이언트에게 한 번에 보낼 변경 사항 목록
        private List<EntityData> _pendingUpdates = new List<EntityData>();

        // [생성자] 게임방이 만들어질 때 호출됨
        public GameState(GameRoom room, GamePlayer playerA, GamePlayer playerB)
        {
            _room = room;
            
            // 각 플레이어의 직업에 맞는 영웅 생성
            string classA = playerA.Deck?.deckClass ?? "Neutral";
            string classB = playerB.Deck?.deckClass ?? "Neutral";

            GameCard leaderCardA = new GameCard("Leader_A_Instance", classA, 30);
            GameEntity leaderA = new GameEntity(1, leaderCardA, playerA.Uid); // ID: 1

            GameCard leaderCardB = new GameCard("Leader_B_Instance", classB, 30);
            GameEntity leaderB = new GameEntity(2, leaderCardB, playerB.Uid); // ID: 2

            _allEntities.Add(leaderA.EntityId, leaderA);
            _allEntities.Add(leaderB.EntityId, leaderB);
            
            _playerA = new PlayerState(playerA, leaderA);
            _playerB = new PlayerState(playerB, leaderB);
            
            _mulliganDecisions[_playerA.Uid] = null;
            _mulliganDecisions[_playerB.Uid] = null;
            
            _currentTurnPlayerUid = _playerA.Uid;

            _effectProcessor = new GameEffectProcessor(this);
        }

        // ------------------------------------------------------------------
        // [Phase 1] 멀리건 (카드 교체 단계)
        // ------------------------------------------------------------------
        public async Task StartMulliganAsync()
        {
            Console.WriteLine($"[GameState {_room.GameId}] 🎲 멀리건 페이즈 시작.");
            _currentPhase = "Mulligan";
            _isGameStarted = false; 
            
            // 선공/후공 결정 (50% 확률)
            if (Rng.Next(2) == 0) { _firstPlayerUid = _playerA.Uid; _secondPlayerUid = _playerB.Uid; }
            else { _firstPlayerUid = _playerB.Uid; _secondPlayerUid = _playerA.Uid; }

            // 테스트용
            _firstPlayerUid = _playerB.Uid;
            _currentTurnPlayerUid = _firstPlayerUid; 
            Console.WriteLine($"선공 {_currentTurnPlayerUid}");

            // 덱 섞기
            _playerA.ShuffleDeck(Rng);
            _playerB.ShuffleDeck(Rng);

            // 초기 손패 5장씩 드로우
            List<CardInfo> handA = new List<CardInfo>();
            List<CardInfo> handB = new List<CardInfo>();

            for (int i = 0; i < 5; i++)
            {
                var cardA = _playerA.DrawCard();
                if (cardA != null) {
                    Console.WriteLine($"[Mulligan] 🃏 {_playerA.Uid} 드로우: {cardA.CardId}");
                    handA.Add(cardA.ToCardInfo());
                }
                var cardB = _playerB.DrawCard();
                if (cardB != null) {
                    Console.WriteLine($"[Mulligan] 🃏 {_playerB.Uid} 드로우: {cardB.CardId}");
                    handB.Add(cardB.ToCardInfo());
                }
            }

            // 클라이언트에게 멀리건 정보 전송 (교체할 시간 30초)
            long endTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30;
            var msgA = new S_MulliganInfo { action = "MULLIGAN_INFO", cardsToMulligan = handA, mulliganEndTime = endTime };
            var msgB = new S_MulliganInfo { action = "MULLIGAN_INFO", cardsToMulligan = handB, mulliganEndTime = endTime };

            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(msgA));
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(msgB));

            // (봇 처리) 봇은 카드를 바꾸지 않음
            if (_playerA.PlayerRef.IsBot) await ProcessMulliganDecisionAsync(_playerA.Uid, new C_MulliganDecision { cardInstanceIdsToReplace = new List<string>() });
            if (_playerB.PlayerRef.IsBot) await ProcessMulliganDecisionAsync(_playerB.Uid, new C_MulliganDecision { cardInstanceIdsToReplace = new List<string>() });
        }

        // [중앙 관제탑] 클라이언트가 보낸 메시지를 종류별로 분배하는 함수
        public async Task HandlePlayerActionAsync(string senderUid, string messageJson)
        {
            Console.WriteLine($"[GameRoom {messageJson}] ");
            BaseGameAction? baseAction = null;
            try { baseAction = JsonConvert.DeserializeObject<BaseGameAction>(messageJson); } catch {}

            if (baseAction == null) return;
            // 멀리건 단계가 아닌데 내 턴이 아니면 명령 무시
            if (_currentPhase != "Mulligan" && senderUid != _currentTurnPlayerUid) 
            {
                var failMsg = new S_PlayCardFail { 
                 action = "PLAY_CARD_FAIL", 
                 reason = "Not your turn" 
                };
                await _room.SendMessageToPlayerAsync(GetPlayerState(senderUid).PlayerRef, JsonConvert.SerializeObject(failMsg));
                Console.WriteLine("후공이 카드를 사용함");
                return;
            }

            switch (baseAction.action)
            {
                case "MULLIGAN_DECISION": // 멀리건 결정
                    var mul = JsonConvert.DeserializeObject<C_MulliganDecision>(messageJson);
                    if (mul != null) await ProcessMulliganDecisionAsync(senderUid, mul);
                    break;
                case "END_TURN": // 턴 종료
                    if (_currentPhase == "Main") await ProcessEndTurnAsync(senderUid);
                    break;
                case "PLAY_CARD": // 카드 내기
                    if (_currentPhase == "Main") {
                        Console.WriteLine($"[GameRoom 카드 사용 ");
                        var play = JsonConvert.DeserializeObject<C_PlayCard>(messageJson);
                        if (play != null) await ProcessPlayCardAsync(senderUid, play);
                    }
                    break;
                case "ATTACK": // 공격
                    if (_currentPhase == "Main") {
                        var atk = JsonConvert.DeserializeObject<C_Attack>(messageJson);
                        if (atk != null) await ProcessAttackAsync(senderUid, atk);
                    }
                    break;
            }
        }

        // [멀리건 처리] 유저가 교체할 카드를 고르면 처리
        private async Task ProcessMulliganDecisionAsync(string senderUid, C_MulliganDecision action)
        {
            lock (_lock) {
                if (_mulliganDecisions.ContainsKey(senderUid) && _mulliganDecisions[senderUid] != null) return;
                _mulliganDecisions[senderUid] = action;
            }

            Console.WriteLine($"[GameState] {senderUid} 멀리건 완료.");
            PlayerState player = GetPlayerState(senderUid);
            List<GameCard> cardsToReturn = new List<GameCard>();

            // 1. 교체할 카드를 손에서 빼둠
            if (action.cardInstanceIdsToReplace != null)
            {
                foreach (string id in action.cardInstanceIdsToReplace)
                {
                    GameCard? c = player.Hand.FirstOrDefault(x => x.InstanceId == id);
                    if (c != null) { player.Hand.Remove(c); cardsToReturn.Add(c); }
                }
            }

            // 2. 뺀 만큼 새로 뽑음
            for (int i = 0; i < cardsToReturn.Count; i++) player.DrawCard();
            
            // 3. 빼둔 카드를 다시 덱에 넣고 섞음
            if (cardsToReturn.Count > 0) { player.Deck.AddRange(cardsToReturn); player.ShuffleDeck(Rng); }

            // 4. 양쪽 다 준비됐으면 게임 시작
            bool allReady = false;
            lock (_lock) { allReady = _mulliganDecisions.Values.All(x => x != null); }

            if (allReady)
            {
                bool start = false;
                lock(_lock) { if(!_isGameStarted) { _isGameStarted = true; start = true; } }
                if(start) await StartGameAsync();
            }
        }

        // ------------------------------------------------------------------
        // [Phase 2] 게임 시작 및 턴 진행
        // ------------------------------------------------------------------
        private async Task StartGameAsync()
        {
            Console.WriteLine($"[GameState] ⚔️ 게임 시작!");

            // =========================================================
            // [테스트용] 강제로 적 필드에 더미 하수인 생성 (ID: 999)
            // =========================================================
            if (true) // 테스트가 끝나면 false로 바꾸거나 지우세요
            {
                // 1. 더미 카드 데이터 생성
                GameCard dummyCard = new GameCard("Test_Dummy_Card", "Dummy_Instance_999");
                dummyCard.CurrentAttack = 2;
                dummyCard.CurrentHealth = 10;

                // 2. 더미 엔티티 생성 (서버쪽 적은 _secondPlayerUid 혹은 상황에 따라 다름)
                // 테스트 환경에서 내가 _firstPlayerUid라면, 적은 _secondPlayerUid입니다.
                string enemyUid = _secondPlayerUid; 
                
                GameEntity dummyEntity = new GameEntity(999, dummyCard, enemyUid);
                dummyEntity.Position = 0; // 0번 슬롯
                dummyEntity.IsMember = false;

                // 3. 서버 메모리에 등록 (이게 있어야 ProcessAttackAsync가 인식함)
                _allEntities.Add(999, dummyEntity);
                
                // 4. 적 플레이어 필드 배열에도 등록
                PlayerState enemyState = GetPlayerState(enemyUid);
                enemyState.Field[0] = dummyEntity;

                Console.WriteLine($"[TEST] 테스트용 더미 하수인(ID:999)이 {enemyUid} 필드에 생성됨.");
                
                // (선택) 클라이언트에게 "여기 하수인 있어"라고 알려주는 패킷을 보낼 수도 있지만,
                // 지금은 클라이언트가 'DummyEntitySetup.cs'로 스스로 그리고 있으니 생략해도 됨.
            }
            // =========================================================

            
            _currentPhase = "StartGame";
            var handA = _playerA.Hand.Select(c => c.ToCardInfo()).ToList();
            var handB = _playerB.Hand.Select(c => c.ToCardInfo()).ToList();

            // 양쪽에게 "게임 시작! 최종 손패는 이거야" 라고 알림
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { action = "GAME_READY", firstPlayerUid = _firstPlayerUid, finalHand = handA }));
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { action = "GAME_READY", firstPlayerUid = _firstPlayerUid, finalHand = handB }));

            await Task.Delay(2000); // 연출용 대기
            _ = StartTurnAsync(_firstPlayerUid); // 선공 플레이어 턴 시작
        }

        // [턴 시작] 마나 증가, 드로우, 페이즈 전환
        private async Task StartTurnAsync(string uid)
        {
            _currentTurnPlayerUid = uid;
            PlayerState p = GetPlayerState(uid);
            PlayerState op = GetPlayerState(uid, true);

            // 1. Standby 페이즈 (턴 시작 알림)
            _currentPhase = "Standby";
            var phaseMsg = JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="Standby", newTurnPlayerUid=uid });
            await _room.SendMessageToPlayerAsync(p.PlayerRef, phaseMsg);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, phaseMsg);

            // 2. 마나 증가 (최대 10) 및 회복
            p.MaxMana = Math.Min(p.MaxMana + 1, 10);
            p.CurrentMana = p.MaxMana;
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_UpdateMana { action="UPDATE_MANA", currentMana=p.CurrentMana, maxMana=p.MaxMana }));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_UpdateMana { action="UPDATE_MANA", currentMana=op.CurrentMana, maxMana=op.MaxMana }));

            await Task.Delay(1000);

            // 3. Draw 페이즈 (카드 1장 뽑기)
            _currentPhase = "Draw";
            GameCard? drawnCard = p.DrawCard();
            if (drawnCard != null) Console.WriteLine($"[Turn] 🃏 {uid} 드로우: {drawnCard.CardId}");
            
            // 드로우 정보 전송 (나는 내 카드를 보고, 상대는 뒷면만 봄)
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="Draw", drawnCard=drawnCard?.ToCardInfo() }));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="Draw", drawnCard=null }));

            await Task.Delay(1000);

            // 4. Main 페이즈 (플레이어가 행동할 수 있는 시간)
            _currentPhase = "Main";
            long endTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60; // 턴 제한시간 60초
            var mainMsg = JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="Main", turnEndTime=endTime });
            await _room.SendMessageToPlayerAsync(p.PlayerRef, mainMsg);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, mainMsg);
            
            // 이번 턴 공격권 초기화 (소환 후유증 해제 등은 여기서 처리 가능)
            foreach(var e in p.Field) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            foreach(var e in p.MemberZone) { if(e!=null) { e.HasAttacked = false; e.CanAttack = true; } }
            p.Leader.CanAttack = true; p.Leader.HasAttacked = false;
        }

        // [턴 종료]
        private async Task ProcessEndTurnAsync(string senderUid)
        {
            if (senderUid != _currentTurnPlayerUid) return;
            _currentPhase = "End";
            var msg = JsonConvert.SerializeObject(new S_PhaseStart { action="PHASE_START", phase="End" });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, msg);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, msg);
            
            await Task.Delay(500);
            
            // 상대방 턴 시작
            string nextUid = (senderUid == _playerA.Uid) ? _playerB.Uid : _playerA.Uid;
            await StartTurnAsync(nextUid);
        }

        // ==================================================================
        // ★ [핵심] 카드 내기 & 소환 로직 (고정 슬롯 시스템 적용)
        // ==================================================================
        private async Task ProcessPlayCardAsync(string senderUid, C_PlayCard action)
        {
            PlayerState p = GetPlayerState(senderUid);
            PlayerState op = GetPlayerState(senderUid, true);
            _pendingUpdates.Clear(); // 이번 액션으로 변경된 사항들을 담을 리스트 초기화

            // 1. 손패에서 카드 찾기
            GameCard? card = p.Hand.FirstOrDefault(c => c.InstanceId == action.handCardInstanceId);
            if (card == null) 
            {
                await SendPlayCardFail(p, action.handCardInstanceId!, "Card not found");
                return;
            }

            // 2. 마나 확인
            if (p.CurrentMana < card.CurrentCost)
            {
                await SendPlayCardFail(p, action.handCardInstanceId!, "Not enough mana");
                return;
            }

            string cardName = card.CardId;
            var cardData = ServerCardDatabase.Instance.GetCardData(card.CardId);
            if (cardData != null && !string.IsNullOrEmpty(cardData.Name)) cardName = cardData.Name;

            // ------------------------------------------------------------------
            // 3. 소환 유효성 검사 (자원 소모 전에 먼저 체크!)
            // ------------------------------------------------------------------
            bool isUnit = card.Type == "Minion" || card.Type == "하수인" || card.Type == "Member" || card.Type == "멤버";
            bool isMember = (card.Type == "멤버" || card.Type == "Member");
            GameEntity?[] targetZone = null!;
            int spawnPos = action.position; // 클라이언트가 요청한 소환 위치 (0~4)

            if (isUnit)
            {
                // 하수인이냐 멤버냐에 따라 들어갈 배열 결정
                targetZone = isMember ? p.MemberZone : p.Field;
                int maxCount = targetZone.Length;

                // 3-1. 인덱스 범위 확인 (0 이상, 배열 길이 미만)
                if (spawnPos < 0 || spawnPos >= maxCount)
                {
                    Console.WriteLine($"[GameRoom {_room.GameId}] ⚠️ 잘못된 슬롯 위치: {spawnPos} (Max: {maxCount-1})");
                    await SendPlayCardFail(p, action.handCardInstanceId!, "Invalid position");
                    return;
                }

                // 3-2. ★ 핵심: 해당 자리가 비어있는지(null) 확인
                if (targetZone[spawnPos] != null)
                {
                    Console.WriteLine($"[GameRoom {_room.GameId}] ⚠️ 소환 실패: 슬롯 {spawnPos}에 이미 하수인이 있음.");
                    await SendPlayCardFail(p, action.handCardInstanceId!, "Slot occupied");
                    return;
                }
            }
            // ------------------------------------------------------------------

            // 4. 검증 통과! 자원 소모 및 손패에서 제거
            p.CurrentMana -= card.CurrentCost;
            p.Hand.Remove(card);

            // 타겟팅 정보가 있다면 가져오기 (전투의 함성 등용)
            GameEntity? target = null;
            if(action.targetEntityId > 0) _allEntities.TryGetValue(action.targetEntityId, out target);

            GameEntity? sourceEntity = null;

            // 5. 실제 소환 (배열에 할당)
            if (isUnit && targetZone != null)
            {
                int eid = _nextGlobalEntityId++; // 고유 ID 발급 (100, 101...)
                sourceEntity = new GameEntity(eid, card, senderUid);

                // [핵심] 하수인 객체에 위치 정보 저장
                sourceEntity.Position = spawnPos;
                sourceEntity.IsMember = isMember;

                _allEntities.Add(eid, sourceEntity); // 전체 목록에 등록
                
                // 지정된 위치에 하수인 배치 (비어있음이 확인됨)
                targetZone[spawnPos] = sourceEntity;
                
                AddPendingUpdate(sourceEntity); // 변경 사항 목록에 추가

                // 로그 출력 (□■□▣□ 형태)
                PrintMinionSpawnLog(cardName, p.Field, p.MemberZone, isMember, spawnPos);
            }
            else
            {
                Console.WriteLine($"[GameRoom {_room.GameId}] {cardName} 사용실패");
            }

            // 6. 효과 처리 (전투의 함성, 주문 효과 등)
            // GameEffectProcessor에게 위임
            await _effectProcessor.ExecuteEffectsAsync(card, sourceEntity, target, "ON_PLAY", senderUid);

            // 7. 결과 전송 (변경된 상태를 양쪽 플레이어에게 알림)
            // 카드 플레이어에게 "성공" 확답을 먼저 보냅니다.
            var successMsg = new S_PlayCardSuccess { 
                action = "PLAY_CARD_SUCCESS", 
                serverInstanceId = card.InstanceId 
            };
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(successMsg));
            await BroadcastUpdatesAsync(senderUid);
            
            // 상대방에게는 "상대가 카드를 냈다"는 별도 메시지 전송 (애니메이션 등 용도)
            var oppMsg = new S_OpponentPlayCard 
            { 
                action="OPPONENT_PLAY_CARD", 
                cardPlayed=card.ToCardInfo(), 
                targetEntityId=action.targetEntityId 
            };
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(oppMsg));

            // 8. 사망 처리 (혹시 전투의 함성으로 누가 죽었으면 처리)
            await ProcessDeathsAsync();
        }

        // --- 유틸리티 및 헬퍼 함수들 ---

        // 카드 내기 실패 시 클라이언트에게 알리는 함수
        private async Task SendPlayCardFail(PlayerState p, string cardId, string reason)
        {
             var failMsg = new S_PlayCardFail { action="PLAY_CARD_FAIL", failedCardInstanceId=cardId, reason=reason };
             await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(failMsg));
        }

        // 하수인 소환 상태를 콘솔에 예쁘게 찍어주는 함수
        private void PrintMinionSpawnLog(string cardName, GameEntity?[] field, GameEntity?[] memberZone, bool isMember, int spawnIndex)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"[GameRoom {_room.GameId}] {cardName} 소환 ");

            // 일반 필드 (5칸)
            for (int i = 0; i < field.Length; i++)
            {
                if (!isMember && i == spawnIndex) sb.Append("▣"); // 방금 소환된 위치
                else if (field[i] != null) sb.Append("■"); // 기존 하수인
                else sb.Append("□"); // 빈칸
            }

            sb.Append(" "); // 구분 공백

            // 멤버 존 (1칸)
            if (memberZone.Length > 0)
            {
                if (isMember && spawnIndex == 0) sb.Append("▣");
                else if (memberZone[0] != null) sb.Append("■");
                else sb.Append("□");
            }

            Console.WriteLine(sb.ToString());
        }

        // 1. [핸들러] 클라이언트의 공격 요청 처리 (엄격한 검증 필요)
        private async Task ProcessAttackAsync(string senderUid, C_Attack action)
        {
            Console.WriteLine($"[GameRoom {_room.GameId}] 공격 요청 수신 (from {senderUid})");
            
            // 1. 공격자, 방어자 존재 확인
            if(!_allEntities.TryGetValue(action.attackerEntityId, out var att) || 
               !_allEntities.TryGetValue(action.defenderEntityId, out var def)) 
            {
                Console.WriteLine(">> 존재하지 않는 엔티티 ID입니다.");
                return;
            }

            // 2. ★ 권한 검증 (내 하수인이 맞는지, 공격 가능한지 등)
            if(att.OwnerUid != senderUid) {
                Console.WriteLine($">> 권한 없음: {senderUid}가 {att.OwnerUid}의 유닛으로 공격 시도.");
                return;
            }
            if(!att.CanAttack || att.HasAttacked) {
                Console.WriteLine(">> 공격 불가 상태입니다.");
                return;
            }

            // 검증 통과 시 공격 기회 소모
            att.HasAttacked = true;

            // 3. 실제 전투 로직 호출
            await ResolveCombatAsync(att, def);

            // 4. 상태 브로드캐스트 및 사망 처리
            await BroadcastUpdatesAsync(senderUid);
            await ProcessDeathsAsync();
        }

        // 2. [내부 로직] 실제 전투 계산 및 적용 (검증 없이 강제 실행 가능)
        // 카드 효과로 인한 "강제 공격"은 이 함수를 직접 호출하면 됨.
        public async Task ResolveCombatAsync(GameEntity att, GameEntity def)
        {
            _pendingUpdates.Clear();

            // 1. 데미지 계산
            int dmgToDef = att.Attack;
            int dmgToAtt = def.Attack;
            
            PlayerState p = GetPlayerState(att.OwnerUid);

            // '멤버' 특성: 공격력 상관없이 무조건 1 피해만 받음 (예시 규칙)
            if (att.Tribe == "멤버") dmgToDef = 1;
            if (def.Tribe == "멤버") dmgToAtt = 1;
            
            // 영웅이 공격하는 경우 (무기 공격력 등 로직 필요, 여기선 상대 공격력만큼 피해)
            if (att.EntityId == p.Leader.EntityId) dmgToAtt = def.Attack; 

            // [LOG] 전투 시작 정보 출력
            string attName = att.SourceCard.CardId; 
            string defName = def.SourceCard.CardId;
            Console.WriteLine($"[Battle] ⚔️ 전투 발생: {attName}(ID:{att.EntityId}) ➔ {defName}(ID:{def.EntityId})");
            Console.WriteLine($"   >> 예상 데미지: 방어자에게 {dmgToDef}, 공격자에게 {dmgToAtt}");

            // 2. 피해 적용
            ApplyDamage(def, dmgToDef);
            ApplyDamage(att, dmgToAtt);

            // [LOG] 전투 결과 정보 출력
            Console.WriteLine($"   >> 전투 결과: 공격자 HP {att.Health}, 방어자 HP {def.Health}");

            // (주의) Broadcast와 Death 처리는 호출자가 담당하거나, 
            // 여기서 하려면 'await'가 끝까지 이어져야 함.
            // 일단 구조상 계산 후 값만 변경하고, 호출자(ProcessAttackAsync)가 전송함.
        }

        // 데미지 적용 함수
        public void ApplyDamage(GameEntity target, int amount)
        {
            target.Health -= amount;
            AddPendingUpdate(target); // 변경된 놈 목록에 추가
        }

        // 힐 적용 함수
        public void ApplyHeal(GameEntity target, int amount)
        {
            target.Health = Math.Min(target.Health + amount, target.MaxHealth);
            AddPendingUpdate(target);
        }

        // 버프 적용 함수
        public void ApplyBuff(GameEntity target, int attackBuff, int healthBuff)
        {
            target.Attack += attackBuff;
            target.Health += healthBuff;
            target.MaxHealth += healthBuff; 
            AddPendingUpdate(target);
        }

        // [사망 처리] 체력이 0 이하인 개체를 찾아 제거
        private async Task<bool> ProcessDeathsAsync()
        {
            if(_isGameOver) return true;
            _pendingUpdates.Clear();

            // 죽은 녀석들 찾기
            var deadEntities = _allEntities.Values.Where(e => e.Health <= 0).ToList();
            if (deadEntities.Count == 0) return false; // 죽은 놈 없으면 종료

            foreach (var dead in deadEntities)
            {
                Console.WriteLine($"[Death] {dead.EntityId} 사망.");

                // '죽음의 메아리' 효과 처리
                await _effectProcessor.ExecuteEffectsAsync(dead.SourceCard, dead, null, "ON_DEATH", dead.OwnerUid);

                // 전체 목록에서 제거
                _allEntities.Remove(dead.EntityId);
                
                PlayerState owner = GetPlayerState(dead.OwnerUid);
                
                // (수정) 배열을 순회하여 해당 하수인을 찾아 null로 만듦 (칸 비우기)
                for(int i=0; i<owner.Field.Length; i++) {
                    if (owner.Field[i] == dead) { owner.Field[i] = null; break; }
                }
                for(int i=0; i<owner.MemberZone.Length; i++) {
                    if (owner.MemberZone[i] == dead) { owner.MemberZone[i] = null; break; }
                }

                // 클라이언트에게 "얘 죽었어"라고 알림 (체력 0)
                var dData = dead.ToEntityData();
                dData.health = 0; 
                _pendingUpdates.Add(dData);
            }

            // 사망 정보 전송
            await BroadcastUpdatesAsync(_playerA.Uid); 

            // 게임 종료 조건 확인 (영웅 사망)
            if (_playerA.Leader.Health <= 0 || _playerB.Leader.Health <= 0)
            {
                string winner = _playerA.Leader.Health > 0 ? _playerA.Uid : _playerB.Uid;
                await EndGameAsync(winner, "LEADER_KILLED");
                return true;
            }

            // 죽음의 메아리로 인해 또 누군가 죽었을 수 있으므로 재귀 호출
            if (_allEntities.Values.Any(e => e.Health <= 0))
            {
                return await ProcessDeathsAsync();
            }
            return false;
        }

        // 변경 목록에 추가하는 헬퍼 함수
        private void AddPendingUpdate(GameEntity entity)
        {
            var existing = _pendingUpdates.FirstOrDefault(e => e.entityId == entity.EntityId);
            if (existing != null)
            {
                // 이미 목록에 있으면 최신 값으로 덮어쓰기
                existing.health = entity.Health;
                existing.attack = entity.Attack;
                existing.maxHealth = entity.MaxHealth;
            }
            else
            {
                _pendingUpdates.Add(entity.ToEntityData());
            }
        }
        
        // UID로 PlayerState 가져오기 (opp=true면 상대방 가져오기)
        public PlayerState GetPlayerState(string uid, bool opp=false) => (opp ? (uid==_playerA.Uid?_playerB:_playerA) : (uid==_playerA.Uid?_playerA:_playerB));
        
        // [상태 전송] 변경된 모든 개체 정보를 클라이언트에게 보냄
        private async Task BroadcastUpdatesAsync(string triggerPlayerUid) 
        { 
             PlayerState p = GetPlayerState(triggerPlayerUid);

             // 1. 엔티티(하수인/영웅) 업데이트 전송
             if (_pendingUpdates.Count > 0)
            {
                string json = JsonConvert.SerializeObject(new S_UpdateEntities { action = "UPDATE_ENTITIES", updatedEntities = _pendingUpdates });
                await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
                await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
                _pendingUpdates.Clear();
            }
             // 2. 마나 정보 갱신
             var manaMsg = new S_UpdateMana 
             { 
                 action = "UPDATE_MANA", 
                 ownerUid = p.Uid, 
                 currentMana = p.CurrentMana, 
                 maxMana = p.MaxMana 
             };
             string manaJson = JsonConvert.SerializeObject(manaMsg);
             
             await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, manaJson);
             await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, manaJson);
                
        }

        // 게임 종료 처리
        private async Task EndGameAsync(string winner, string reason)
        {
            if(_isGameOver) return;
            _isGameOver = true;
            _currentPhase = "GameOver";
            string json = JsonConvert.SerializeObject(new S_GameOver { action="GAME_OVER", winnerUid=winner, reason=reason });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
        }
    }
}