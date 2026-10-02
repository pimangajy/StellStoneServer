using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text; // StringBuilder 사용 (문자열 조합)
using System.Threading.Tasks; // 비동기 작업(Task) 사용
using Google.Cloud.Firestore;
using Newtonsoft.Json; // JSON 직렬화/역직렬화
using System.Text.Json; // 고성능 JSON 파싱

namespace GameServer
{
    /// <summary>
    /// 실제 게임의 흐름(턴, 페이즈, 규칙 검사, 전투 판정)을 총괄하는 핵심 엔진 클래스입니다.
    /// </summary>
    public partial class GameState
    {
        private readonly GameRoom _room;                     // 메시지 전송을 위한 방 참조
        private readonly PlayerState _playerA;               // 플레이어 A의 상태
        private readonly PlayerState _playerB;               // 플레이어 B의 상태
        
        // 필드 위의 모든 개체(영웅, 하수인, 멤버)를 ID로 빠르게 찾기 위한 저장소
        private readonly Dictionary<int, GameEntity> _allEntities = new Dictionary<int, GameEntity>();
        
        public Effects.EventSystem EventSystem { get; private set; }
        public List<GameServer.Effects.GameActionPacket> ActionHistory { get; } = new List<GameServer.Effects.GameActionPacket>();

        /// <summary>
        /// 발생한 행동 패킷을 게임 이력에 기록합니다.
        /// </summary>
        public void RecordAction(GameServer.Effects.GameActionPacket packet)
        {
            lock (_lock)
            {
                packet.TurnNumber = _turnCount;
                packet.TurnPlayerUid = _currentTurnPlayerUid;
                packet.Phase = _currentPhase ?? "";
                if (!ActionHistory.Contains(packet))
                {
                    ActionHistory.Add(packet);
                }
            }
        }

        /// <summary>
        /// 표준 사건 패킷(GameActionPacket)을 기록하고 EventSystem에 전송합니다.
        /// </summary>
        public async Task PublishActionAsync(GameServer.Effects.GameActionPacket packet)
        {
            RecordAction(packet);
            await EventSystem.PublishAsync(packet);
        }

        private string _currentTurnPlayerUid = "";           // 현재 턴을 진행 중인 플레이어 UID
        private string _firstPlayerUid = "";                // 이번 게임의 선공 플레이어 UID
        private string _secondPlayerUid = "";               // 이번 게임의 후공 플레이어 UID
        private string? _currentPhase;                       // 현재 게임 단계 (Mulligan, Main 등)
        public int _turnCount = 0;                          // 현재 게임의 총 턴 수 카운터
        
        public Random Rng { get; private set; } = new Random();
        private int _nextGlobalEntityId = 100;               // 하수인/멤버 생성을 위한 전역 개체 ID 카운터
        private bool _isGameOver = false;                   // 게임 종료 여부
        private readonly object _lock = new object();        // 멀티스레드 환경에서의 데이터 안전을 위한 락 객체
        private readonly SemaphoreSlim _stateLock = new SemaphoreSlim(1, 1); // [C-01 동시성 최적화] 방 게임 상태 비동기 순차 실행 게이트
        private readonly object _bufferLock = new object();   // [C-03 동시성 최적화] 이벤트 및 엔티티 버퍼 스레드 안전 락
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
        private ActionLogScope? _currentActionScope = null; // 🚀 통합 행동 로그 트랜잭션 스코프

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
        /// [C-01 동시성 최적화] 방 안의 게임 상태를 스레드 안전하게 순차 실행합니다.
        /// </summary>
        public async Task ExecuteStateActionAsync(Func<Task> action)
        {
            await _stateLock.WaitAsync();
            try
            {
                await action();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        /// <summary>
        /// 이벤트를 로그에 기록합니다. (스레드 안전)
        /// </summary>
        public void LogEvent(GameEventType type, int sourceId, int targetId = 0, int val = 0, string? strVal = null, EffectTriggerType effectTriggerType = EffectTriggerType.NONE, EntityData? entityData = null, int val2 = 0, string? cardId = null)
        {
            lock (_bufferLock)
            {
                _eventBuffer.Add(new GameEvent
                {
                    eventType = type,
                    sourceEntityId = sourceId,
                    targetEntityId = targetId,
                    value = val,
                    value2 = val2,
                    cardId = cardId,
                    stringValue = strVal,
                    triggerType = effectTriggerType,
                    entityData = entityData,
                });
            }
        }

        private void AddPendingUpdateData(EntityData dData)
        {
            lock (_bufferLock)
            {
                _pendingUpdates.Add(dData);
            }
        }

        private void ClearBuffers()
        {
            lock (_bufferLock)
            {
                _eventBuffer.Clear();
                _pendingUpdates.Clear();
            }
        }

        // 로그 보관 리스트
        private List<GameLogEvent> _actionLogs = new List<GameLogEvent>();
        private List<string> _debugLogs = new List<string>();
        private const int MaxDebugLogs = 1000;
        public static bool EnableConsoleLog = true;
        private DateTime _gameStartTime = DateTime.UtcNow;
        private bool _isResolvingCombat = false; // 전투 공격 진행 중 하위 피해/사망 로그 중복 생성 억제 플래그

        /// <summary>
        /// 상세한 디버그 추적 로그를 기록합니다. (타임스탬프 및 카테고리 포함)
        /// </summary>
        public void LogDebug(string category, string message)
        {
            string time = DateTime.UtcNow.AddHours(9).ToString("HH:mm:ss.fff");
            string formatted = $"[{time}] [{category,-16}] {message}";
            lock (_lock)
            {
                if (_debugLogs.Count >= MaxDebugLogs)
                {
                    _debugLogs.RemoveAt(0);
                }
                _debugLogs.Add(formatted);
            }
            if (EnableConsoleLog)
            {
                Console.WriteLine(formatted);
            }
        }

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
            _gameStartTime = DateTime.UtcNow;
            
            // --- 1. 플레이어 A 영웅 개체 생성 ---
            // 문자열인 직업(deckClass)을 CardClass Enum으로 안전하게 변환
            CardClass classA = CardClass.Gangzi; // 파싱 실패 시 기본 직업
            if (Enum.TryParse<CardClass>(playerA.Deck?.deckClass, true, out var parsedClassA))
            {
                classA = parsedClassA;
            }
            // (InstanceId, 직업 Enum, 종족 Enum, 체력) 순으로 전달
            GameCard leaderCardA = new GameCard("Leader_A_Instance", classA, CardTribe.무소속, 30) { OwnerUid = playerA.Uid, CardName = playerA.Username ?? classA.ToString() };
            GameEntity leaderA = new GameEntity(10000, leaderCardA, playerA.Uid);
            leaderA.IsLeader = true;
            leaderA.SkinId = playerA.Deck?.GetEquippedSkinId() ?? $"Skin_{classA}_Default";

            // --- 2. 플레이어 B 영웅 개체 생성 ---
            CardClass classB = CardClass.Gangzi;
            if (Enum.TryParse<CardClass>(playerB.Deck?.deckClass, true, out var parsedClassB))
            {
                classB = parsedClassB;
            }
            GameCard leaderCardB = new GameCard("Leader_B_Instance", classB, CardTribe.무소속, 30) { OwnerUid = playerB.Uid, CardName = playerB.Username ?? classB.ToString() };
            GameEntity leaderB = new GameEntity(20000, leaderCardB, playerB.Uid);
            leaderB.IsLeader = true;
            leaderB.SkinId = playerB.Deck?.GetEquippedSkinId() ?? $"Skin_{classB}_Default";

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

            // Player A와 B가 "카드 뽑았다"고 소리치면, GameState가 그걸 듣고 AddLog를 실행함 (멀리건 종료 이후부터 기록)
            _playerA.OnCardDrawn += (playerUid, cardId) => {
                if (_currentPhase == "Mulligan" || _currentPhase == "StartGame" || _currentPhase == "Draw" || _currentPhase == "Standby") return;
                if (_currentActionScope != null)
                {
                    _currentActionScope.RecordDraw(playerUid);
                    return;
                }
                BroadcastDrawLog(playerUid, cardId);
            };

            _playerB.OnCardDrawn += (playerUid, cardId) => {
                if (_currentPhase == "Mulligan" || _currentPhase == "StartGame" || _currentPhase == "Draw" || _currentPhase == "Standby") return;
                if (_currentActionScope != null)
                {
                    _currentActionScope.RecordDraw(playerUid);
                    return;
                }
                BroadcastDrawLog(playerUid, cardId);
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

        /// <summary>
        /// 플레이어 UID에 해당하는 닉네임을 반환합니다 (없으면 UID 반환).
        /// </summary>
        public string GetPlayerDisplayName(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return "";
            if (_playerA != null && _playerA.Uid == uid)
            {
                return !string.IsNullOrEmpty(_playerA.PlayerRef?.Username) ? _playerA.PlayerRef.Username : _playerA.Uid;
            }
            if (_playerB != null && _playerB.Uid == uid)
            {
                return !string.IsNullOrEmpty(_playerB.PlayerRef?.Username) ? _playerB.PlayerRef.Username : _playerB.Uid;
            }
            return uid;
        }

        /// <summary>
        /// 필드 개체(하수인 또는 리더)의 표시 이름을 반환합니다.
        /// </summary>
        public string GetEntityDisplayName(GameEntity? entity)
        {
            if (entity == null) return "";
            if (entity.IsLeader)
            {
                return GetPlayerDisplayName(entity.OwnerUid);
            }
            return entity.SourceCard?.CardName ?? $"개체 #{entity.EntityId}";
        }

        private void InitializeLogger()
        {
            // 카드를 냈을 때 알아서 로그 작성 (하수인 카드는 OnMinionSummoned에서 [소환]으로 단독 처리되므로, 주문(Spell) 카드만 [사용] 로그 발행)
            this.OnCardPlayed += (playerUid, cardId) => {
                if (_currentActionScope != null) return; // 통합 액션 스코프 내에서는 최종 종합 로그로 발행
                var cardData = ServerCardDatabase.Instance.GetCardData(cardId);
                if (cardData != null && cardData.CardType != CardType.주문) return;
                string cardName = cardData?.Name ?? cardId;
                string displayName = GetPlayerDisplayName(playerUid);
                AddLog(displayName, "PLAY_CARD", $"{displayName}이(가) [{cardName}]을(를) 사용했습니다.", null, cardId, cardName, playerUid: playerUid);
            };

            // 공격했을 때 알아서 로그 작성 (ProcessAttackAsync에서 전투 전반을 묶어 종합 ATTACK 로그 1회만 발행)
            this.ApllyAttacked += (attackerName, targetName, damage) => {
            };

            // =================================================================
            // 하수인이 필드에 소환되었을 때 로그 작성
            // =================================================================
            this.OnMinionSummoned += (playerUid, cardIdOrName, position) => {
                var cardData = ServerCardDatabase.Instance.GetCardData(cardIdOrName);
                string minionName = cardData?.Name ?? cardIdOrName;
                if (_currentActionScope != null)
                {
                    // 스코프의 주 소환 하수인이면 무시 (통합 로그 기본 정보에 포함)
                    if (cardIdOrName == _currentActionScope.SourceCardId || minionName == _currentActionScope.SourceCardName)
                    {
                        return;
                    }
                    // 전투의 함성 등으로 추가 소환된 토큰 하수인
                    _currentActionScope.RecordSummon(minionName, position);
                    return;
                }
                string displayName = GetPlayerDisplayName(playerUid);
                AddLog(displayName, "SUMMON", $"{displayName}의 필드 {position}번 슬롯에 [{minionName}] 하수인이 소환되었습니다.", null, cardIdOrName, minionName, 0, 0, null, position, playerUid: playerUid);
            };

            // =================================================================
            // 하수인이 필드에서 파괴(사망)되었을 때 로그 작성
            // =================================================================
            this.OnMinionDestroyed += (playerUid, minionName) => {
                if (_isResolvingCombat) return; // 전투 공격 진행 도중 발생하는 사망은 종합 ATTACK 로그에 포함되므로 개별 생성 억제
                if (_currentActionScope != null)
                {
                    _currentActionScope.RecordDeath(minionName);
                    return;
                }
                string displayName = GetPlayerDisplayName(playerUid);
                AddLog(displayName, "DEATH", $"{displayName}의 하수인 [{minionName}]이(가) 전장에서 파괴되었습니다.", null, null, minionName, playerUid: playerUid);
            };

            // =================================================================
            // 하수인이나 영웅이 피해(데미지)를 입었을 때 로그 작성
            // =================================================================
            this.OnDamageApplied += (targetName, damage, sourceId) => {
                if (_isResolvingCombat) return; // 전투 공격 진행 도중 발생하는 피해는 종합 ATTACK 로그에 포함되므로 개별 생성 억제
                if (_currentActionScope != null)
                {
                    _currentActionScope.RecordDamage(targetName, damage);
                    return;
                }
                AddLog("System", "DAMAGE", $"[{targetName}]이(가) {damage}의 피해를 입었습니다.", null, null, null, sourceId, 0, targetName, damage);
            };

            // =================================================================
            // 하수인이나 영웅이 회복(치유)되었을 때 로그 작성
            // =================================================================
            this.OnHealApplied += (targetName, healAmount, sourceId) => {
                if (_currentActionScope != null)
                {
                    _currentActionScope.RecordHeal(targetName, healAmount);
                    return;
                }
                AddLog("System", "HEAL", $"[{targetName}]이(가) 체력을 {healAmount}만큼 회복했습니다.", null, null, null, sourceId, 0, targetName, healAmount);
            };

            // =================================================================
            // 카드 효과가 발동되었을 때 로그 작성
            // =================================================================
            this.EffectLog += (targetName, sourceId, effectname) =>
            {
                // [개선] 내부 액션(예: DrawAction, DamageAction)의 단순 발동 로그는 UI 오염을 방지하기 위해 생성하지 않음
            };
        }

        /// <summary>
        /// 클라이언트(WebSocket)로부터 받은 JSON 메시지를 해석하고 권한을 확인하여 실행합니다.
        /// </summary>
        public async Task HandlePlayerActionAsync(string senderUid, string messageJson)
        {
            // [C-01 동시성 최적화] 동일 룸에 대한 모든 클라이언트 요청을 순차적으로 원자적 실행
            await _stateLock.WaitAsync();
            try
            {
                // [P-01 성능 최적화] System.Text.Json.JsonDocument를 사용하여 이중 역직렬화 제거
                using var doc = JsonDocument.Parse(messageJson);
                var root = doc.RootElement;

                // ==========================================
                // 1. 디버그 액션 우선 처리
                // ==========================================
                if (root.TryGetProperty("debugAction", out var debugProp))
                {
                    DebugAction debugAction = DebugAction.NONE;
                    if (debugProp.ValueKind == JsonValueKind.String)
                    {
                        Enum.TryParse<DebugAction>(debugProp.GetString(), true, out debugAction);
                    }
                    else if (debugProp.ValueKind == JsonValueKind.Number)
                    {
                        debugAction = (DebugAction)debugProp.GetInt32();
                    }

                    if (debugAction != DebugAction.NONE)
                    {
                        await DispatchDebugActionAsync(senderUid, debugAction, messageJson);
                        return; // 디버그 처리를 했으므로 일반 로직은 타지 않고 종료
                    }
                }

                // ==========================================
                // 2. 상용 게임 액션 처리
                // ==========================================
                if (root.TryGetProperty("action", out var actionProp))
                {
                    GameActionType actionType = GameActionType.NONE;
                    if (actionProp.ValueKind == JsonValueKind.String)
                    {
                        Enum.TryParse<GameActionType>(actionProp.GetString(), true, out actionType);
                    }
                    else if (actionProp.ValueKind == JsonValueKind.Number)
                    {
                        actionType = (GameActionType)actionProp.GetInt32();
                    }

                    if (actionType == GameActionType.NONE) return;

                    // 감정표현이나 항복 등 페이즈와 무관한 공통 액션은 원래 액션 타입 그대로 전달
                    if (_currentPhase == "Mulligan" && actionType != GameActionType.SEND_EMOTE && actionType != GameActionType.CONCEDE)
                    {
                        await DispatchActionAsync(senderUid, GameActionType.MULLIGAN_DECISION, messageJson);
                    }
                    else
                    {
                        await DispatchActionAsync(senderUid, actionType, messageJson);
                    }
                }
            }
            catch
            {
                return;
            }
            finally
            {
                _stateLock.Release();
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
                        // isOpponent가 true면 상대방 UID, false면 본인 UID를 대상으로 드로우
                        string targetUid = drawReq.isOpponent ? GetPlayerState(uid, opp: true).Uid : uid;
                        await DrawSpecificCardFromDeckAsync(targetUid, drawReq.targetCardId);
                    }
                    break;
                // ==========================================
                // 덱 정보 요청 처리
                // ==========================================
                case DebugAction.RequestDeckInfo:
                    var deckReq = JsonConvert.DeserializeObject<C_DebugRequestDeckInfo>(json);
                    bool isOpp = deckReq?.isOpponent ?? false;
                    PlayerState targetPlayer = GetPlayerState(uid, opp: isOpp);
                    
                    // 1. 대상 덱에 있는 카드들을 CardInfo 리스트로 변환
                    List<CardInfo> currentDeckInfo = targetPlayer.Deck.Select(c => c.ToCardInfo()).ToList();

                    // 2. 응답 패킷 생성 (isOpponent 플래그 포함)
                    var responseMsg = new S_DebugResponseDeckInfo
                    {
                        debugAction = DebugAction.ResponseDeckInfo,
                        isOpponent = isOpp,
                        deckCards = currentDeckInfo
                    };

                    // 3. 요청한 클라이언트에게만 전송
                    PlayerState requester = GetPlayerState(uid);
                    await _room.SendMessageToPlayerAsync(requester.PlayerRef, JsonConvert.SerializeObject(responseMsg));
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

            // 💬 감정표현은 본인 턴 여부나 현재 페이즈와 관계없이 언제든지 처리 가능
            if (action == GameActionType.SEND_EMOTE)
            {
                var emoteReq = JsonConvert.DeserializeObject<C_SendEmote>(json);
                if (emoteReq != null)
                {
                    await ProcessSendEmoteAsync(uid, emoteReq);
                }
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
                case GameActionType.VALID_MEMBER_SKILL_TARGETS_REQUEST:
                    var memberTargetReq = JsonConvert.DeserializeObject<C_ValidMemberSkillTargetsRequest>(json);
                    if (memberTargetReq != null) await ProcessValidMemberSkillTargetsRequestAsync(uid, memberTargetReq);
                    break;
                case GameActionType.USE_MEMBER_SKILL:
                    var useSkill = JsonConvert.DeserializeObject<C_UseMemberSkill>(json);
                    if (useSkill != null) await ProcessUseMemberSkillAsync(uid, useSkill);
                    break;
                case GameActionType.GET_CARD_FROM_SIDE_DECK:
                    var sideDeckReq = JsonConvert.DeserializeObject<C_GetCardFromSideDeck>(json);
                    if (sideDeckReq != null) await ProcessGetCardFromSideDeckAsync(uid, sideDeckReq);
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
        /// 플레이어가 감정표현(SEND_EMOTE)을 보냈을 때 같은 룸의 상대방 플레이어에게 중계(S_RECEIVE_EMOTE)합니다.
        /// </summary>
        private async Task ProcessSendEmoteAsync(string senderUid, C_SendEmote emoteReq)
        {
            if (emoteReq == null || string.IsNullOrEmpty(emoteReq.emoteId)) return;

            Console.WriteLine($"[GameState] 💬 감정표현 수신: sender={senderUid}, emoteId={emoteReq.emoteId}, message='{emoteReq.message}'");

            var receivePacket = new S_ReceiveEmote
            {
                action = GameActionType.RECEIVE_EMOTE,
                senderUid = senderUid,
                emoteId = emoteReq.emoteId,
                message = emoteReq.message ?? ""
            };

            string json = JsonConvert.SerializeObject(receivePacket);

            // 상대방 플레이어 세션에 전달
            PlayerState opponent = GetPlayerState(senderUid, opp: true);
            if (opponent?.PlayerRef != null)
            {
                await _room.SendMessageToPlayerAsync(opponent.PlayerRef, json);
            }
        }

        /// <summary>
        /// 카드 사용이나 효과 발동이 실패/종료되었을 때 클라이언트에 알립니다.
        /// </summary>
        public async Task SendActionFailAsync(string uid, string reason)
        {
            PlayerState p = GetPlayerState(uid);
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail
            {
                action = GameActionType.PLAY_CARD_FAIL,
                reason = reason
            }));
        }

        /// <summary>
        /// 특정 선택 유형(위치 지정, 대상 지정, 카드 발견 등)에 대해
        /// 현재 게임 상태에서 유효하게 선택할 수 있는 후보가 존재하는지 검사합니다.
        /// </summary>
        public bool HasValidChoices(string uid, string choiceType, string choiceData, List<CardInfo>? availableOptions = null)
        {
            PlayerState me = GetPlayerState(uid);
            PlayerState opp = GetPlayerState(uid, true);

            switch (choiceType)
            {
                case "POSITION":
                {
                    ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(choiceData);
                    bool isMember = cardData?.CardType == CardType.멤버;
                    GameEntity?[] targetZone = isMember ? me.MemberZone : me.Field;
                    return isMember ? true : targetZone.Any(slot => slot == null);
                }
                case "POSITION_ENEMY":
                {
                    ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(choiceData);
                    bool isMember = cardData?.CardType == CardType.멤버;
                    GameEntity?[] targetZone = isMember ? opp.MemberZone : opp.Field;
                    return targetZone.Any(slot => slot == null);
                }
                case "TARGET_ENEMY_MINION":
                {
                    return opp.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed && (e.Keywords == null || !e.Keywords.Contains(CardKeywords.Stealth)));
                }
                case "TARGET_FRIENDLY_MINION":
                {
                    return me.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed);
                }
                case "TARGET_MINION":
                {
                    bool oppMinion = opp.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed && (e.Keywords == null || !e.Keywords.Contains(CardKeywords.Stealth)));
                    bool myMinion = me.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed);
                    return oppMinion || myMinion;
                }
                case "TARGET_ENEMY":
                {
                    bool oppLeader = opp.Leader != null && opp.Leader.Health > 0;
                    bool oppMinion = opp.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed && (e.Keywords == null || !e.Keywords.Contains(CardKeywords.Stealth)));
                    return oppLeader || oppMinion;
                }
                case "DISCOVER":
                case "DISCOVER_CARD":
                {
                    return availableOptions != null && availableOptions.Count > 0;
                }
                default:
                    if (choiceType.StartsWith("POSITION"))
                    {
                        return me.Field.Any(slot => slot == null);
                    }
                    return true;
            }
        }

        /// <summary>
        /// [수정됨] 효과 발동 중 플레이어의 개입이 필요할 때 호출합니다.
        /// 유효한 선택지가 없다면 요청하지 않고 효과를 즉시 종료합니다.
        /// </summary>
        public async Task RequestPlayerChoiceAsync(
            string uid, 
            string choiceType, 
            string choiceData, 
            string uiMessage,       // [신규] UI 안내 텍스트
            int sourceEntityId = 0, // [신규] 원인 개체 ID
            int count = 1,
            List<CardInfo>? availableOptions = null)
        {
            // 1. 선택 가능한 대상이나 위치가 남아있는지 사전 검증
            if (!HasValidChoices(uid, choiceType, choiceData, availableOptions))
            {
                Console.WriteLine($"[RequestPlayerChoiceAsync] ⚠️ {uid}의 선택({choiceType}) 후보가 없어 요청이 취소됩니다.");
                AddLog("System", "EFFECT_TERMINATED", $"{uid}의 선택 가능한 대상이나 위치가 없어 효과가 종료되었습니다.");

                // 클라이언트에게 실패 패킷 전송 (선택 UI 해제 및 종료)
                await SendActionFailAsync(uid, "선택할 수 있는 대상이나 위치가 없어 효과가 종료되었습니다.");

                _pendingChoiceType = "";
                _pendingChoiceData = "";
                _pendingChoiceCount = 0;
                _currentPhase = "Main";
                await ProcessActionQueueAsync();
                return;
            }

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
                availableOptions = availableOptions,
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

                // 🌟 [통합 로그 스코프 완료 검사]: 큐가 모두 비워졌고 입력 대기(AWAITING_CHOICE) 상태가 아니면 단일 통합 로그 전송!
                if (_actionQueue.Count == 0 && _currentPhase != "AWAITING_CHOICE" && _currentActionScope != null)
                {
                    var scopeToEmit = _currentActionScope;
                    _currentActionScope = null;
                    EmitScopeLog(scopeToEmit);
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

            // 4. 리더 정보 생성 (선택된 덱의 장착 스킨 정보 포함)
            var leaderA = _playerA.Leader.ToEntityData();
            var leaderB = _playerB.Leader.ToEntityData();

            // 5. 정보 전송 (로컬 함수 사용으로 가독성 개선)
            async Task SendInfo(PlayerState p, List<CardInfo> hand, EntityData myLeader, EntityData enemyLeader) {
                long endTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30;
                var msg = new S_MulliganInfo { 
                    action = GameActionType.MULLIGAN_INFO, 
                    cardsToMulligan = hand, 
                    mulliganEndTime = endTime,
                    myLeader = myLeader,
                    enemyLeader = enemyLeader
                };
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(msg));
            }

            await SendInfo(_playerA, handA, leaderA, leaderB);
            await SendInfo(_playerB, handB, leaderB, leaderA);

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
            var sideDeckA = _playerA.SideDeck.Select(c => c.ToCardInfo()).ToList();
            var sideDeckB = _playerB.SideDeck.Select(c => c.ToCardInfo()).ToList();

            string nameA = !string.IsNullOrEmpty(_playerA.PlayerRef.Username) ? _playerA.PlayerRef.Username : _playerA.Uid;
            string nameB = !string.IsNullOrEmpty(_playerB.PlayerRef.Username) ? _playerB.PlayerRef.Username : _playerB.Uid;

            // 양쪽 플레이어에게 최종 손패와 선공 정보, 본인의 사이드덱 카드 목록과 함께 게임 시작을 알림
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { 
                action = GameActionType.GAME_READY, 
                firstPlayerUid = _firstPlayerUid, 
                finalHand = handA, 
                enermyfinalHand = handB,
                mySideDeck = sideDeckA,
                opponentName = nameB
            }));
            
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(new S_GameReady { 
                action = GameActionType.GAME_READY, 
                firstPlayerUid = _firstPlayerUid, 
                finalHand = handB, 
                enermyfinalHand = handA,
                mySideDeck = sideDeckB,
                opponentName = nameA
            }));
                
            await Task.Delay(1500); // 연출을 위한 잠시 대기
            await StartTurnAsync(_firstPlayerUid); // 선공 플레이어 턴 시작
        }

        /// <summary>
        /// 특정 플레이어의 새로운 턴을 시작합니다 (마나 증가, 드로우 등).
        /// </summary>
        private async Task StartTurnAsync(string uid)
        {
            _turnCount++;
            _currentTurnPlayerUid = uid;
            PlayerState p = GetPlayerState(uid);
            PlayerState op = GetPlayerState(uid, true);

            Console.WriteLine($"[GameState] 📢 {(p.PlayerRef.IsBot ? "🤖 봇" : "🧑 플레이어")} ({p.Uid})의 턴 시작");
            p.CardsPlayedThisTurn.Clear();
            p.HasUsedSideDeckThisTurn = false; // 이번 턴 사이드덱 사용 여부 리셋 (매 턴 1회 가능)

            // 1. Standby 페이즈 알림
            _currentPhase = "Standby";
            var phaseMsg = JsonConvert.SerializeObject(new S_PhaseStart { action=GameActionType.PHASE_START, phase = GamePhase.STANDBY, TurnPlayerUid=_currentTurnPlayerUid });
            await _room.SendMessageToPlayerAsync(p.PlayerRef, phaseMsg);
            await _room.SendMessageToPlayerAsync(op.PlayerRef, phaseMsg);

            var startContext = new GameServer.Effects.EffectContext(_currentTurnPlayerUid, null, EffectTriggerType.ON_TURN_START)
            {
                SourceEntity = p.Leader,
                TargetEntity = p.Leader
            };
            await EventSystem.PublishAsync(EffectTriggerType.ON_TURN_START, startContext);
            await FlushUpdatesAsync();
            if (await ProcessDeathsAsync()) return;

            // 2. 마나 충전 (최대 10까지 1씩 증가)
            p.MaxMana = Math.Min(p.MaxMana + 1, 10);
            p.CurrentMana = p.MaxMana;
            
            await BroadcastManaUpdate(p, op);
            await Task.Delay(1000);

            // 3. Draw 페이즈 (카드 한 장 뽑기)
            _currentPhase = "Draw";
            GameCard? drawnCard = null;
            bool isBurned = false;

            if (IsDrawSealed(p.Uid))
            {
                LogDebug("DrawSeal", $"🚫 [드로우봉인] 상대 필드에 '드로우봉인' 하수인이 존재하여 {p.Uid}의 턴 시작 드로우가 차단되었습니다.");
            }
            else
            {
                drawnCard = p.DrawCard(out isBurned);
                if (drawnCard != null)
                {
                    if (isBurned)
                    {
                        drawnCard.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                        LogDebug("Hand", $"🔥 [카드 소각] {p.Uid}의 손패가 가득 차 '{drawnCard.CardName}' 카드가 소각되었습니다.");
                        AddLog(p.Uid, "BURN_CARD", $"{p.Uid}의 손패가 가득 차 [{drawnCard.CardName}] 카드가 소각되었습니다.");
                    }
                    else
                    {
                        // 덱에서 패로 들어왔으므로 Zone.Hand로 업데이트! (손패 버프 효과가 여기서부터 켜짐)
                        drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
                    }
                }
            }
            // 드로우 카드 전송 (봉인 시 drawnCard=null, hasDrawn=false)
            bool hasDrawn = (drawnCard != null);
            var drawnCardInfo = drawnCard?.ToCardInfo();
            if (drawnCardInfo != null)
            {
                drawnCardInfo.isBurned = isBurned;
            }
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart { TurnPlayerUid = _currentTurnPlayerUid, action = GameActionType.PHASE_START, phase = GamePhase.DRAW, drawnCard = drawnCardInfo, hasDrawn = hasDrawn }));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(new S_PhaseStart { TurnPlayerUid = _currentTurnPlayerUid, action = GameActionType.PHASE_START, phase = GamePhase.DRAW, drawnCard = null, hasDrawn = hasDrawn }));

            // 손패의 동적 스탯/코스트 실시간 갱신
            await RefreshAllDynamicHandStatsAsync();

            await Task.Delay(1000);

            // 4. Main 페이즈 시작 (실제 플레이 타임, 60초 제한)
            _currentPhase = "Main";
            long _turnEndTime  = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60; 
            var mainMsg = new S_PhaseStart { action = GameActionType.PHASE_START, phase = GamePhase.MAIN, turnEndTime = _turnEndTime  };
            
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(mainMsg));
            
            // 5. 필드 위 개체들의 공격 기회 초기화 (속박 및 질풍 횟수 초기화 포함)
            void RefreshEntityAttackState(GameEntity? e)
            {
                if (e == null) return;
                e.AttacksThisTurn = 0;
                e.HasAttacked = false;
                e.HasUsedSkillThisTurn = false; // 매 턴 시작 시 멤버 스킬 사용 기회 초기화

                if (e.IsMember)
                {
                    e.CanAttack = false; // 멤버는 직접 공격 불가
                    AddPendingUpdate(e);
                    return;
                }

                if (e.Keywords != null && e.Keywords.Contains(CardKeywords.Bind))
                {
                    e.Keywords.Remove(CardKeywords.Bind);
                    e.Enchantments.RemoveAll(enc => enc.effectType == GameEventType.BIND);
                    e.CanAttack = false; // 이번 턴 공격 불가
                    AddPendingUpdate(e);
                }
                else
                {
                    e.CanAttack = true;
                }
            }

            foreach (var e in p.Field) RefreshEntityAttackState(e);
            foreach (var e in p.MemberZone) RefreshEntityAttackState(e);
            RefreshEntityAttackState(p.Leader);

            await FlushUpdatesAsync();

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

        public async Task<GameCard?> DrawCardWithSyncAsync(string playerUid, bool logEvent = true)
        {
            if (IsDrawSealed(playerUid))
            {
                LogDebug("DrawSeal", $"🚫 [드로우봉인] 상대 필드에 '드로우봉인' 하수인이 존재하여 {playerUid}의 드로우가 전면 차단되었습니다.");
                return null;
            }

            PlayerState p = GetPlayerState(playerUid);
            PlayerState op = GetPlayerState(playerUid, true);

            GameCard? drawnCard = p.DrawCard(out bool isBurned);
            if (drawnCard != null)
            {
                if (isBurned)
                {
                    drawnCard.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                    LogDebug("Hand", $"🔥 [카드 소각] {playerUid}의 손패가 가득 차 '{drawnCard.CardName}' 카드가 소각되었습니다.");
                    AddLog(playerUid, "BURN_CARD", $"{playerUid}의 손패가 가득 차 [{drawnCard.CardName}] 카드가 소각되었습니다.");

                    var burnedCardInfo = drawnCard.ToCardInfo();
                    burnedCardInfo.isBurned = true;

                    var burnMsgToSelf = new S_DrawCard
                    {
                        action = GameActionType.DRAW_CARD,
                        playerUid = playerUid,
                        drawnCard = burnedCardInfo
                    };

                    var burnMsgToOpponent = new S_DrawCard
                    {
                        action = GameActionType.DRAW_CARD,
                        playerUid = playerUid,
                        drawnCard = null
                    };

                    await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(burnMsgToSelf));
                    await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(burnMsgToOpponent));

                    return drawnCard;
                }

                // 1. 카드의 현재 구역을 손패(Zone.Hand)로 업데이트
                drawnCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);

                // 2. 서버 이벤트 로그 기록 (효과로 인한 드로우일 때만 액션 큐에 기록)
                if (logEvent)
                {
                    LogEvent(GameEventType.DRAW, 0, 0, 1, drawnCard.CardId);
                }

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

                // 드로우 후 동적 스탯/코스트 실시간 갱신
                await RefreshAllDynamicHandStatsAsync();

                // 🚀 [신규] 전역 드로우 이벤트(ON_DRAW) 발행!
                var drawContext = new GameServer.Effects.EffectContext(playerUid, drawnCard, EffectTriggerType.ON_DRAW)
                {
                    SourceCard = drawnCard,
                    TriggerCard = drawnCard,
                    TriggerOwnerUid = playerUid,
                    TargetEntity = p.Leader
                };
                await EventSystem.PublishAsync(EffectTriggerType.ON_DRAW, drawContext);
            }

            return drawnCard;
        }

        /// <summary>
        /// 특정 플레이어의 손패에 새 카드를 직접 생성하여 추가합니다. (토큰 창조, 효과 획득 등)
        /// </summary>
        public async Task<GameCard?> AddCardToHandAsync(string playerUid, string cardId)
        {
            PlayerState p = GetPlayerState(playerUid);
            PlayerState op = GetPlayerState(playerUid, true);
            if (p == null || string.IsNullOrEmpty(cardId)) return null;

            // 1. 새 카드 인스턴스 생성
            string instanceId = $"{cardId}_{Guid.NewGuid().ToString().Substring(0, 8)}";
            GameCard newCard = new GameCard(cardId, instanceId)
            {
                OwnerUid = playerUid,
                Origin = CardOrigin.Created
            };

            // 손패 최대치 초과 검사 (최대 10장)
            if (p.Hand.Count >= 10)
            {
                newCard.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                p.Graveyard.Add(newCard);
                LogDebug("Hand", $"🔥 [카드 소각] {playerUid}의 손패가 가득 차서 '{newCard.CardName}'({cardId}) 카드가 소각되었습니다.");
                AddLog(playerUid, "BURN_CARD", $"{playerUid}의 손패가 가득 차 [{newCard.CardName}] 카드가 소각되었습니다.");

                var burnedCardInfo = newCard.ToCardInfo();
                burnedCardInfo.isBurned = true;

                var burnMsgToSelf = new S_DrawCard
                {
                    action = GameActionType.DRAW_CARD,
                    playerUid = playerUid,
                    drawnCard = burnedCardInfo
                };

                var burnMsgToOpponent = new S_CardCreated
                {
                    action = GameActionType.CARD_CREATED,
                    playerUid = playerUid,
                    card = null,
                    createdCard = null,
                    handCount = p.Hand.Count
                };

                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(burnMsgToSelf));
                await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(burnMsgToOpponent));

                return newCard;
            }

            // 2. Zone.Hand 활성화 및 손패 등록
            newCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
            p.Hand.Add(newCard);

            // 3. 이벤트 로그 기록 (DRAW 이벤트로 처리하여 유니티 클라이언트에서 카드 획득 연출)
            LogEvent(GameEventType.DRAW, 0, 0, 1, newCard.CardId);
            LogDebug("Hand", $"🎁 [카드 획득] {playerUid}의 손패에 '{newCard.CardName}'({cardId}) 카드가 생성되었습니다.");
            AddLog(playerUid, "ADD_TO_HAND", $"{playerUid}가 손패에 '{newCard.CardName}' 카드를 획득했습니다.");

            // 4. 클라이언트에 드로우/획득 패킷 전송
            // 본인에게는 카드 정보가 포함된 패킷 전송
            var msgToSelf = new S_DrawCard
            {
                action = GameActionType.DRAW_CARD,
                playerUid = playerUid,
                drawnCard = newCard.ToCardInfo()
            };

            // 상대방에게는 일반 드로우가 아닌 '카드 생성 전용 패킷(S_CardCreated)' 전송
            // (상대방 클라이언트에서 카드 생성 애니메이션을 실행할 수 있도록 함)
            var msgToOpponent = new S_CardCreated
            {
                action = GameActionType.CARD_CREATED,
                playerUid = playerUid,
                card = null,
                createdCard = null,
                handCount = p.Hand.Count
            };

            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(msgToSelf));
            await _room.SendMessageToPlayerAsync(op.PlayerRef, JsonConvert.SerializeObject(msgToOpponent));

            // 5. 손패 장수 변동에 따른 동적 스탯/코스트 실시간 갱신
            await RefreshAllDynamicHandStatsAsync();

            return newCard;
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
            if (_currentPhase == "End") return; // 이미 턴 종료 처리 진행 중이면 중복 요청 무시
            
            _currentPhase = "End";
            var msg = JsonConvert.SerializeObject(new S_PhaseStart { action=GameActionType.PHASE_START, phase=GamePhase.END });
            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, msg);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, msg);

            PlayerState currentTurnPlayer = GetPlayerState(_currentTurnPlayerUid);
            var endContext = new GameServer.Effects.EffectContext(_currentTurnPlayerUid, null, EffectTriggerType.ON_TURN_END)
            {
                SourceEntity = currentTurnPlayer?.Leader,
                TargetEntity = currentTurnPlayer?.Leader
            };
            await EventSystem.PublishAsync(EffectTriggerType.ON_TURN_END, endContext);

            // ⏳ 지속 턴 수(Duration)가 지정된 임시 버프/키워드 만료 처리
            foreach (var entity in _allEntities.Values.Where(e => e != null && e.Health > 0 && !e.IsDestroyed))
            {
                var expiredEnchants = new List<EnchantmentInfo>();
                foreach (var ench in entity.Enchantments.Where(e => e.duration > 0))
                {
                    ench.duration--;
                    if (ench.duration <= 0)
                    {
                        expiredEnchants.Add(ench);

                        if (ench.effectType == GameEventType.GRANT_KEYWORD && !string.IsNullOrEmpty(ench.grantedKeyword))
                        {
                            if (Enum.TryParse<CardKeywords>(ench.grantedKeyword, true, out var kw))
                            {
                                bool hasOtherSource = (entity.SourceCard?.CurrentKeywords != null && entity.SourceCard.CurrentKeywords.Contains(kw)) ||
                                    entity.Enchantments.Any(other => other != ench && other.effectType == GameEventType.GRANT_KEYWORD && other.grantedKeyword == ench.grantedKeyword);
                                if (!hasOtherSource)
                                {
                                    entity.Keywords?.Remove(kw);
                                }
                            }
                        }
                        else if (ench.effectType == GameEventType.BUFF)
                        {
                            entity.Attack -= ench.attackMod;
                            entity.Health -= ench.healthMod;
                            entity.MaxHealth -= ench.healthMod;
                            entity.SpellAmp -= ench.spellAmpMod;
                            entity.SpellWeakness -= ench.spellWeaknessMod;
                            entity.BuffAmpAttack -= ench.buffAmpAttackMod;
                            entity.BuffAmpHealth -= ench.buffAmpHealthMod;
                        }
                    }
                }

                if (expiredEnchants.Count > 0)
                {
                    foreach (var exp in expiredEnchants) entity.Enchantments.Remove(exp);
                    AddPendingUpdate(entity);
                }
            }

            await FlushUpdatesAsync();
            if (await ProcessDeathsAsync()) return;
            
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
        public async Task<GameCard?> DrawSpecificCardFromDeckAsync(string playerUid, string targetCardId)
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

                return await DrawCardWithSyncAsync(playerUid, logEvent: false);
            }
            else
            {
                Console.WriteLine($"[GameState - Cheat] ❌ {playerUid}의 덱에 '{targetCardId}' 카드가 존재하지 않습니다.");
                return null;
            }
        }


        /// <summary>
        /// [1단계] 플레이어가 손에서 카드를 내려고 시도할 때 검증하고, 타겟팅 필요 시 대기 상태로 전환합니다.
        /// </summary>
        public async Task ProcessPlayCardAsync(string senderUid, C_PlayCard action)
        {
            PlayerState p = GetPlayerState(senderUid);
            PlayerState op = GetPlayerState(senderUid, true);
            ClearBuffers();

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
                if (isMember)
                {
                    // 멤버는 전용 1칸 슬롯(index: 0)이므로 위치를 0으로 자동 고정
                    action.position = 0;
                    // 기존 멤버가 있어도 교체(Replacement) 소환 로직으로 처리하므로 슬롯 중복 에러를 발생시키지 않음
                }
                else
                {
                    if (action.position < 0 || action.position >= targetZone.Length)
                    {
                        Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: 잘못된 소환 위치입니다. (요청한 Position: {action.position})");
                        await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                                action = GameActionType.PLAY_CARD_FAIL,
                                failedCardInstanceId = card.InstanceId,
                                reason = $"잘못된 소환 위치입니다. (요청한 Position: {action.position})"
                            }));
                        return;
                    }
                    if (targetZone[action.position] != null)
                    {
                        Console.WriteLine($"[ProcessPlayCardAsync] ❌ 실패: {action.position}번 위치에 이미 다른 개체가 존재합니다.");
                        await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(new S_PlayCardFail {
                                action = GameActionType.PLAY_CARD_FAIL,
                                failedCardInstanceId = card.InstanceId,
                                reason = $"{action.position}번 위치에 이미 다른 개체가 존재합니다."
                            }));
                        return;
                    }
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

            ClearBuffers();

            bool isUnit = card.Type == CardType.하수인 || card.Type == CardType.멤버;
            bool isMember = card.Type == CardType.멤버;
            GameEntity?[] targetZone = isMember ? p.MemberZone : p.Field;

            // 🌟 통합 액션 로그 스코프 시작
            string actionType = isUnit ? "SUMMON" : "PLAY_CARD";
            var scope = new ActionLogScope(senderUid, GetPlayerDisplayName(senderUid), actionType, card.CardId, card.CardName, position, targetEntityId);
            _currentActionScope = scope;

            try
            {
                // 1. 실제 마나 자원 소모 및 손패에서 제거 확정 [9]
                p.CurrentMana -= card.CurrentCost;
                p.Hand.Remove(card);
                p.CardsPlayedThisTurn.Add(card);

                // 2. [분기 A] 하수인 및 멤버(유닛) 소환 로직 확정 [9]
                if (isUnit)
                {
                    if (isMember)
                    {
                        position = 0; // 멤버는 전용 1칸 슬롯(index: 0) 고정
                        if (p.MemberZone[0] != null)
                        {
                            // 🌟 [멤버 교체 소환] 기존 멤버를 묘지로 퇴장 처리
                            await RetireMemberToGraveyardAsync(p, p.MemberZone[0]!);
                        }
                    }

                    card.UpdateZone(isMember ? GameServer.Effects.Zone.MemberZone : GameServer.Effects.Zone.Field, this.EventSystem);

                    int eid = _nextGlobalEntityId++;
                    GameEntity sourceEntity = new GameEntity(eid, card, senderUid)
                    {
                        Position = position,
                        IsMember = isMember,
                        SummonedTurn = _turnCount,
                        HasUsedSkillThisTurn = false // 새로 소환된 멤버는 소환 턴부터 스킬 사용 가능
                    };
                    scope.SourceEntityId = eid;

                    LogEvent(GameEventType.SUMMON, sourceEntity.EntityId, 0, position, card.CardId, EffectTriggerType.ON_PLAY, sourceEntity.ToEntityData());
                    _allEntities.Add(eid, sourceEntity);
                    targetZone[position] = sourceEntity;
                    AddPendingUpdate(sourceEntity);

                    GameEntity? battlecryTarget = null;
                    if (targetEntityId > 0)
                    {
                        _allEntities.TryGetValue(targetEntityId, out battlecryTarget);
                        if (battlecryTarget != null) scope.PrimaryTargetName = GetEntityDisplayName(battlecryTarget);
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

                        // 1. 주변 감시자들에게 PlayCard 전파
                        var playPacket = GameServer.Effects.GameActionPacket.CreatePlayCard(senderUid, card, battlecryTarget, position);
                        playPacket.SourceEntity = sourceEntity;
                        await PublishActionAsync(playPacket);

                        // 2. 하수인 소환 글로벌 감지 이벤트(Summon) 발행 (필드 착지 우선 확정)
                        var summonPacket = GameServer.Effects.GameActionPacket.CreateSummon(senderUid, card, sourceEntity, position);
                        await PublishActionAsync(summonPacket);

                        LogEvent(GameEventType.EFFECT_TRIGGER, sourceEntity.EntityId, targetEntityId, 0, null, EffectTriggerType.ON_PLAY, null, 0, card.CardId);

                        var context = new GameServer.Effects.EffectContext(senderUid, card, EffectTriggerType.ON_PLAY)
                        {
                            SourceEntity = sourceEntity,
                            TargetEntity = battlecryTarget
                        };
                        
                        // 3. 필드 착지 확정 후 본인의 사용 효과(Play / 전투의 함성) 실행
                        await ExecutePlayEffectsAsync(card, context);
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
                        if (spellTarget != null) scope.PrimaryTargetName = GetEntityDisplayName(spellTarget);
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
                        LogEvent(GameEventType.EFFECT_TRIGGER, 0, targetEntityId, 0, card.CardId, EffectTriggerType.ON_PLAY);

                        var context = new GameServer.Effects.EffectContext(senderUid, card, EffectTriggerType.ON_PLAY)
                        {
                            TargetEntity = spellTarget
                        };

                        // 1. 본인의 사용 효과(Play / 주문 본체) 직접 실행
                        await ExecutePlayEffectsAsync(card, context);

                        // 2. 주변 감시자들에게 PlayCard 전파 (5W1H 표준 패킷)
                        var playPacket = GameServer.Effects.GameActionPacket.CreatePlayCard(senderUid, card, spellTarget);
                        await PublishActionAsync(playPacket);

                        // 주문 완료 후 묘지 소모 처리
                        card.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                        p.Graveyard.Add(card);
                    });
                }

                // 공통 사후 처리 등록
                _actionQueue.Enqueue(async () =>
                {
                    await ProcessDeathsAsync();
                    await RefreshAllDynamicHandStatsAsync();
                    await BroadcastUpdatesAsync(senderUid);
                });

                await ProcessActionQueueAsync();
            }
            finally
            {
                if (_currentPhase != "AWAITING_CHOICE" && _currentActionScope == scope)
                {
                    _currentActionScope = null;
                    EmitScopeLog(scope);
                }
            }
        }

        /// <summary>
        /// 카드가 시전될 때 카드 본체의 사용 효과(Play)들을 직접 순차 실행합니다.
        /// </summary>
        public async Task ExecutePlayEffectsAsync(GameCard card, GameServer.Effects.EffectContext context)
        {
            var playEffects = card.NewEffects.Where(e => e.Kind == GameServer.Effects.EffectKind.Play).ToList();
            if (playEffects.Count == 0) return;

            var playPacket = GameServer.Effects.GameActionPacket.CreatePlayCard(
                context.OwnerUid,
                card,
                context.TargetEntity,
                context.TargetPosition);
            playPacket.SourceEntity = context.SourceEntity;

            foreach (var effect in playEffects)
            {
                // 트리거 조건(연계, 대상 검사 등) 검사
                var triggerConds = effect.TriggerConditions ?? new List<GameServer.Effects.ICondition>();
                bool allConditionsMet = true;
                foreach (var cond in triggerConds)
                {
                    if (!cond.Check(this, playPacket, card, context.SourceEntity))
                    {
                        allConditionsMet = false;
                        break;
                    }
                }

                if (allConditionsMet)
                {
                    int repeatCount = Math.Max(1, effect.RepeatCount);

                    for (int i = 0; i < repeatCount; i++)
                    {
                        if (CurrentPhase == "AWAITING_CHOICE") break;

                        playPacket.SequenceIndex = i;
                        playPacket.TotalSequenceCount = repeatCount;

                        foreach (var action in effect.Actions)
                        {
                            await action.ExecuteAsync(this, context);
                        }
                    }
                }
            }
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
                // 다음 반복 선택 전, 여전히 유효한 대상/위치가 남아있는지 검사
                if (HasValidChoices(senderUid, _pendingChoiceType, _pendingChoiceData))
                {
                    await FlushUpdatesAsync(); 
                    
                    await RequestPlayerChoiceAsync(
                        senderUid, 
                        _pendingChoiceType, 
                        _pendingChoiceData, 
                        $"남은 선택: {_pendingChoiceCount}회. 다음 대상을 선택해주세요.", 
                        0, 
                        _pendingChoiceCount);
                        
                    return; // 여기서 멈추고 다시 클라이언트의 응답을 기다림
                }
                else
                {
                    // 더 이상 선택할 대상이나 위치가 없으므로 남은 반복 취소 및 실패 패킷 전송
                    Console.WriteLine($"[GameState] ⚠️ 더 이상 선택 가능한 대상/위치가 없어 남은 반복({_pendingChoiceCount}회)이 종료됩니다.");
                    AddLog("System", "EFFECT_TERMINATED", $"{senderUid}의 선택 가능한 대상이나 위치가 없어 효과가 조기 종료되었습니다.");

                    await SendActionFailAsync(senderUid, "더 이상 선택할 수 있는 대상이나 위치가 없어 효과가 종료되었습니다.");
                }
            }

            // 2. 메모리 초기화 및 상태 복구
            _pendingChoiceType = "";
            _pendingChoiceData = "";
            _pendingChoiceCount = 0;
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
            GameCard newCard = new GameCard(cardId, newInstanceId) 
            { 
                OwnerUid = ownerUid,
                Origin = CardOrigin.Created 
            };
            newCard.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

            // 5. 전역 엔티티 ID 발급 및 GameEntity 객체 생성
            int eid = _nextGlobalEntityId++;
            GameEntity newEntity = new GameEntity(eid, newCard, ownerUid)
            {
                Position = emptySlot,
                IsMember = isMember,
                SummonedTurn = _turnCount
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

            // 9. 하수인 소환 글로벌 감지 이벤트(ON_SUMMON) 발행
            _actionQueue.Enqueue(async () =>
            {
                var summonContext = new GameServer.Effects.EffectContext(ownerUid, newCard, EffectTriggerType.ON_SUMMON)
                {
                    SourceEntity = newEntity,
                    TargetEntity = newEntity
                };
                await EventSystem.PublishAsync(EffectTriggerType.ON_SUMMON, summonContext);
                await RefreshAllDynamicHandStatsAsync();
            });
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
                IsMember = isMember,
                SummonedTurn = _turnCount
            };

            // 5. 서버에 등록
            _allEntities.Add(eid, newEntity);
            targetZone[emptySlot] = newEntity;

            // 6. [핵심] 클라이언트 연출 로그 (이미 만들어두신 SUMMON_FROM_DECK 활용!)
            LogEvent(GameEventType.SUMMON_FROM_DECK, newEntity.EntityId, 0, emptySlot, card.CardId, EffectTriggerType.NONE, newEntity.ToEntityData());
            OnMinionSummoned?.Invoke(p.Uid, card.CardId, emptySlot);

            // 7. 클라이언트 데이터 동기화
            AddPendingUpdate(newEntity);

            // 8. 하수인 소환 글로벌 감지 이벤트(ON_SUMMON) 발행
            _actionQueue.Enqueue(async () =>
            {
                var summonContext = new GameServer.Effects.EffectContext(ownerUid, card, EffectTriggerType.ON_SUMMON)
                {
                    SourceEntity = newEntity,
                    TargetEntity = newEntity
                };
                await EventSystem.PublishAsync(EffectTriggerType.ON_SUMMON, summonContext);
                await RefreshAllDynamicHandStatsAsync();
            });
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
            GameCard newCard = new GameCard(cardId, newInstanceId) 
            { 
                OwnerUid = ownerUid,
                Origin = CardOrigin.Created 
            };
            newCard.UpdateZone(GameServer.Effects.Zone.Field, this.EventSystem);

            // 5. 전역 엔티티 ID 발급 및 GameEntity 객체 생성
            int eid = _nextGlobalEntityId++;
            GameEntity newEntity = new GameEntity(eid, newCard, ownerUid)
            {
                Position = targetPos,
                IsMember = isMember,
                SummonedTurn = _turnCount
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

            // 9. 하수인 소환 글로벌 감지 이벤트(ON_SUMMON) 발행
            _actionQueue.Enqueue(async () =>
            {
                var summonContext = new GameServer.Effects.EffectContext(ownerUid, newCard, EffectTriggerType.ON_SUMMON)
                {
                    SourceEntity = newEntity,
                    TargetEntity = newEntity
                };
                await EventSystem.PublishAsync(EffectTriggerType.ON_SUMMON, summonContext);
                await RefreshAllDynamicHandStatsAsync();
            });
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

            int maxAttacks = (attacker.Keywords != null && attacker.Keywords.Contains(CardKeywords.Windfury)) ? 2 : 1;
            if (attacker.OwnerUid != senderUid || !attacker.CanAttack || attacker.AttacksThisTurn >= maxAttacks)
            {
                // 공격이 불가능한 상태라면 유효 공격 대상 목록을 빈 배열로 반환하여 드래그 비활성화 유도
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            // 2. 적의 살아있는 캐릭터 수집 (영웅, 필드 하수인, 멤버존 하수인)
            // ※ 은신(Stealth) 상태인 개체는 공격 대상 후보에서 제외됩니다.
            var enemyCandidates = new List<GameEntity>();
            
            if (opp.Leader != null && opp.Leader.Health > 0)
            {
                enemyCandidates.Add(opp.Leader);
            }
            
            foreach (var e in opp.Field)
            {
                if (e != null && e.Health > 0)
                {
                    // 은신 상태가 아닌 하수인만 공격 대상 후보로 등록
                    if (e.Keywords == null || !e.Keywords.Contains(CardKeywords.Stealth))
                    {
                        enemyCandidates.Add(e);
                    }
                }
            }
            
            foreach (var e in opp.MemberZone)
            {
                if (e != null && e.Health > 0)
                {
                    // 은신 상태가 아닌 멤버만 공격 대상 후보로 등록
                    if (e.Keywords == null || !e.Keywords.Contains(CardKeywords.Stealth))
                    {
                        enemyCandidates.Add(e);
                    }
                }
            }

            // ⚡ [Rush / 속공] 소환된 턴에는 적 영웅(리더)을 공격할 수 없습니다. (돌진이 없는 경우)
            bool isRushOnly = attacker.Keywords != null 
                           && attacker.Keywords.Contains(CardKeywords.Rush) 
                           && !attacker.Keywords.Contains(CardKeywords.Charge) 
                           && attacker.SummonedTurn == _turnCount;
            if (isRushOnly)
            {
                enemyCandidates.RemoveAll(e => e.IsLeader);
            }

            // 3. 적 필드에 도발(Taunt) 하수인이 있는지 판별
            // 영웅이나 멤버존을 제외하고, 'Field'에 수집된 일반 하수인 중 Taunt 키워드를 가진 자를 색출합니다.
            // (은신 상태인 하수인은 enemyCandidates에서 이미 제외되었으므로 도발이 작동하지 않습니다)
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
                // 도발이 없다면 생존한 모든 적 캐릭터(은신 제외)가 유효 타겟입니다.
                response.validDefenderEntityIds = enemyCandidates.Select(e => e.EntityId).ToList();
            }

            // 5. 요청한 클라이언트에게 최종 타겟 목록 반환
            await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
        }

        /// <summary>
        /// (C->S) 플레이어가 멤버 카드의 스킬을 조준하려 할 때, 해당 스킬로 조준 가능한 타겟 ID 목록을 계산하여 응답합니다.
        /// </summary>
        public async Task ProcessValidMemberSkillTargetsRequestAsync(string senderUid, C_ValidMemberSkillTargetsRequest action)
        {
            PlayerState me = GetPlayerState(senderUid);
            var response = new S_ValidMemberSkillTargetsResponse
            {
                entityId = action.entityId,
                skillId = action.skillId
            };

            // 1. 엔티티 존재 확인 및 소유권, 멤버 여부 확인
            if (!_allEntities.TryGetValue(action.entityId, out var member) || member.OwnerUid != senderUid || !member.IsMember)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            // 2. 현재 턴 플레이어 확인 및 이번 턴 사용 여부 확인
            if (_currentTurnPlayerUid != senderUid || member.HasUsedSkillThisTurn)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            // 3. 스킬 존재 및 코스트 지불 가능 여부 확인
            var skill = member.MemberSkills.FirstOrDefault(s => s.SkillId == action.skillId);
            if (skill == null || !skill.CanPayCost(member.Health))
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
                return;
            }

            // 4. TargetValidator를 통해 유효 타겟 목록 계산
            response.validTargetIds = TargetValidator.GetValidMemberSkillTargetIds(this, member, skill, senderUid);
            await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(response));
        }

        /// <summary>
        /// (C->S) 플레이어가 멤버 카드의 액티브 스킬을 시전했을 때 처리합니다.
        /// 체력 코스트를 지불하고 효과를 비동기 실행한 뒤, 효과 종료 시점에 체력이 0 이하이면 묘지로 이동시킵니다.
        /// </summary>
        public async Task ProcessUseMemberSkillAsync(string senderUid, C_UseMemberSkill action)
        {
            PlayerState me = GetPlayerState(senderUid);
            PlayerState opp = GetPlayerState(senderUid, true);

            // 1. 현재 턴 플레이어 확인
            if (_currentTurnPlayerUid != senderUid)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                {
                    memberEntityId = action.entityId,
                    skillId = action.skillId,
                    reason = "자신의 턴에만 멤버 스킬을 사용할 수 있습니다."
                }));
                return;
            }

            // 2. 멤버 엔티티 존재 및 상태 확인
            if (!_allEntities.TryGetValue(action.entityId, out var member) || member.OwnerUid != senderUid || !member.IsMember || member.IsDestroyed || member.Health <= 0)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                {
                    memberEntityId = action.entityId,
                    skillId = action.skillId,
                    reason = "유효하지 않은 멤버 카드입니다."
                }));
                return;
            }

            // 3. 이번 턴 스킬 사용 여부 확인
            if (member.HasUsedSkillThisTurn)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                {
                    memberEntityId = action.entityId,
                    skillId = action.skillId,
                    reason = "멤버 스킬은 한 턴에 한 번만 사용할 수 있습니다."
                }));
                return;
            }

            // 4. 스킬 정보 확인
            var skill = member.MemberSkills.FirstOrDefault(s => s.SkillId == action.skillId);
            if (skill == null)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                {
                    memberEntityId = action.entityId,
                    skillId = action.skillId,
                    reason = "존재하지 않는 스킬입니다."
                }));
                return;
            }

            // 5. 체력 코스트 지불 가능 여부 확인 (소모치 이상의 체력 보유 필수)
            if (!skill.CanPayCost(member.Health))
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                {
                    memberEntityId = action.entityId,
                    skillId = action.skillId,
                    reason = $"체력이 부족하여 스킬을 사용할 수 없습니다. (현재 체력: {member.Health}, 필요: {Math.Abs(skill.HealthCost)})"
                }));
                return;
            }

            // 6. 타겟 유효성 검사 (타겟팅 스킬인 경우)
            GameEntity? targetEntity = null;
            if (skill.Targeting)
            {
                var validTargetIds = TargetValidator.GetValidMemberSkillTargetIds(this, member, skill, senderUid);
                if (!validTargetIds.Contains(action.targetEntityId))
                {
                    await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_UseMemberSkillFail
                    {
                        memberEntityId = action.entityId,
                        skillId = action.skillId,
                        reason = "선택한 대상이 유효하지 않습니다."
                    }));
                    return;
                }
                _allEntities.TryGetValue(action.targetEntityId, out targetEntity);
            }

            // --- 유효성 검증 완료: 스킬 실행 시작 ---
            ClearBuffers();

            var scope = new ActionLogScope(senderUid, GetPlayerDisplayName(senderUid), "EFFECT", member.SourceCard?.CardId ?? "", skill.Name, 0, action.targetEntityId);
            scope.SourceEntityId = member.EntityId;
            if (targetEntity != null) scope.PrimaryTargetName = GetEntityDisplayName(targetEntity);
            _currentActionScope = scope;

            try
            {
                // 7. 체력 코스트 적용 (HealthCost: 양수면 힐/충전, 음수면 소모)
                int oldHp = member.Health;
                member.Health += skill.HealthCost;
                if (skill.HealthCost > 0 && member.Health > member.MaxHealth)
                {
                    member.MaxHealth = member.Health; // 충전 시 최대 체력 확장
                }

                // 스킬 사용권 소비
                member.HasUsedSkillThisTurn = true;
                AddPendingUpdate(member);

                // 이벤트 로깅
                if (skill.HealthCost < 0)
                {
                    LogEvent(GameEventType.DAMAGE, member.EntityId, member.EntityId, Math.Abs(skill.HealthCost));
                }
                else if (skill.HealthCost > 0)
                {
                    LogEvent(GameEventType.HEAL, member.EntityId, member.EntityId, skill.HealthCost);
                }
                LogEvent(GameEventType.EFFECT_TRIGGER, member.EntityId, action.targetEntityId, skill.SkillId, null, EffectTriggerType.ON_PLAY);

                // 8. 성공 패킷 브로드캐스트
                var successMsg = new S_UseMemberSkillSuccess
                {
                    memberEntityId = member.EntityId,
                    skillId = skill.SkillId,
                    targetEntityId = action.targetEntityId,
                    currentHp = member.Health
                };
                string successJson = JsonConvert.SerializeObject(successMsg);
                await _room.SendMessageToPlayerAsync(me.PlayerRef, successJson);
                await _room.SendMessageToPlayerAsync(opp.PlayerRef, successJson);

                // 9. 스킬 효과(Actions) 비동기 실행 및 사후 사망 처리 큐 등록
                _actionQueue.Enqueue(async () =>
                {
                    Console.WriteLine($"[GameState] 🌟 멤버 '{member.SourceCard?.CardName}' (ID:{member.EntityId}) 스킬 '{skill.Name}' 실행 (체력: {oldHp} -> {member.Health})");

                    var context = new GameServer.Effects.EffectContext(senderUid, member.SourceCard, EffectTriggerType.ON_PLAY)
                    {
                        SourceEntity = member,
                        TargetEntity = targetEntity
                    };

                    if (skill.Effects != null)
                    {
                        foreach (var effect in skill.Effects)
                        {
                            if (effect.Actions != null)
                            {
                                foreach (var act in effect.Actions)
                                {
                                    for (int r = 0; r < effect.RepeatCount; r++)
                                    {
                                        await act.ExecuteAsync(this, context);
                                    }
                                }
                            }
                        }
                    }

                    // 🌟 [핵심 기획 룰]: 스킬 효과가 모두 발동/해결된 후, 체력이 0 이하인 경우 묘지 이동
                    await ProcessDeathsAsync();

                    // 모든 필드/손패 상태 최신화 동기화
                    await BroadcastUpdatesAsync(senderUid);
                    await RefreshAllDynamicHandStatsAsync();
                });

                // 🚀 스킬 효과 및 상태 동기화 액션 큐 즉시 실행
                await ProcessActionQueueAsync();
            }
            finally
            {
                if (_currentPhase != "AWAITING_CHOICE" && _currentActionScope == scope)
                {
                    _currentActionScope = null;
                    EmitScopeLog(scope);
                }
            }
        }

        /// <summary>
        /// (C->S) 플레이어가 사이드덱에서 원하는 카드를 손패로 가져오도록 처리합니다.
        /// 카드의 코스트만큼 마나를 소모하며, 턴당 1회만 사용 가능합니다.
        /// </summary>
        public async Task ProcessGetCardFromSideDeckAsync(string senderUid, C_GetCardFromSideDeck action)
        {
            PlayerState me = GetPlayerState(senderUid);
            PlayerState opp = GetPlayerState(senderUid, true);

            // 1. 현재 턴 플레이어 확인
            if (_currentTurnPlayerUid != senderUid)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_GetCardFromSideDeckFail
                {
                    action = GameActionType.GET_CARD_FROM_SIDE_DECK_FAIL,
                    reason = "자신의 턴에만 사이드덱에서 카드를 가져올 수 있습니다."
                }));
                return;
            }

            // 2. 턴당 1회 사용 제한 확인
            if (me.HasUsedSideDeckThisTurn)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_GetCardFromSideDeckFail
                {
                    action = GameActionType.GET_CARD_FROM_SIDE_DECK_FAIL,
                    reason = "사이드덱 카드는 턴마다 한 번만 가져올 수 있습니다."
                }));
                return;
            }

            // 3. 손패 최대치(10장) 확인
            if (me.Hand.Count >= 10)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_GetCardFromSideDeckFail
                {
                    action = GameActionType.GET_CARD_FROM_SIDE_DECK_FAIL,
                    reason = "손패가 가득 차서(최대 10장) 카드를 가져올 수 없습니다."
                }));
                return;
            }

            // 4. 사이드덱에서 카드 검색 (instanceId 우선, cardId fallback)
            GameCard? targetCard = null;
            if (!string.IsNullOrEmpty(action.cardInstanceId))
            {
                targetCard = me.SideDeck.FirstOrDefault(c => c.InstanceId == action.cardInstanceId)
                          ?? me.SideDeck.FirstOrDefault(c => c.CardId == action.cardInstanceId);
            }

            if (targetCard == null)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_GetCardFromSideDeckFail
                {
                    action = GameActionType.GET_CARD_FROM_SIDE_DECK_FAIL,
                    reason = "사이드덱에서 해당 카드를 찾을 수 없습니다."
                }));
                return;
            }

            // 5. 마나(코스트) 부족 검사
            if (me.CurrentMana < targetCard.CurrentCost)
            {
                await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(new S_GetCardFromSideDeckFail
                {
                    action = GameActionType.GET_CARD_FROM_SIDE_DECK_FAIL,
                    reason = $"마나가 부족합니다. (현재 마나: {me.CurrentMana}, 필요 마나: {targetCard.CurrentCost})"
                }));
                return;
            }

            // ==========================================
            // 6. 성공 처리: 마나 차감, 턴 플래그 설정, 손패 이동
            // ==========================================
            me.CurrentMana -= targetCard.CurrentCost;
            me.HasUsedSideDeckThisTurn = true;
            me.SideDeck.Remove(targetCard);

            // 카드 출처 설정 및 손패 Zone 등록
            targetCard.Origin = CardOrigin.SideDeck;
            targetCard.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
            me.Hand.Add(targetCard);

            Console.WriteLine($"[GameState] 📦 {senderUid}가 사이드덱에서 '{targetCard.CardName}'({targetCard.InstanceId}) 카드를 획득했습니다. (소모 마나: {targetCard.CurrentCost}, 남은 마나: {me.CurrentMana})");
            AddLog(senderUid, "GET_FROM_SIDE_DECK", $"{senderUid}가 사이드덱에서 '{targetCard.CardName}' 카드를 가져왔습니다. ({targetCard.CurrentCost} 마나 소모)");

            // 7. 성공 결과 패킷 전송
            var msgSelf = new S_GetCardFromSideDeckSuccess
            {
                action = GameActionType.GET_CARD_FROM_SIDE_DECK_SUCCESS,
                playerUid = senderUid,
                card = targetCard.ToCardInfo(),
                consumedCost = targetCard.CurrentCost,
                remainingMana = me.CurrentMana,
                remainingSideDeckCount = me.SideDeck.Count
            };

            var msgOpp = new S_GetCardFromSideDeckSuccess
            {
                action = GameActionType.GET_CARD_FROM_SIDE_DECK_SUCCESS,
                playerUid = senderUid,
                card = null, // 상대에게는 어떤 카드인지 비공개
                consumedCost = targetCard.CurrentCost,
                remainingMana = me.CurrentMana,
                remainingSideDeckCount = me.SideDeck.Count
            };

            await _room.SendMessageToPlayerAsync(me.PlayerRef, JsonConvert.SerializeObject(msgSelf));
            await _room.SendMessageToPlayerAsync(opp.PlayerRef, JsonConvert.SerializeObject(msgOpp));

            // 8. 마나 업데이트 브로드캐스트
            await BroadcastManaUpdate(me, opp);

            // 9. 손패 장수 변동에 따른 동적 스탯/코스트 실시간 갱신
            await RefreshAllDynamicHandStatsAsync();
        }

        /// <summary>
        /// 한 유닛이 다른 유닛을 공격하는 로직을 검증하고 실행합니다.
        /// </summary>
        public async Task ProcessAttackAsync(string senderUid, C_Attack action)
        {
            ClearBuffers();

            // 공격자와 방어자가 유효한지 확인
            if(!_allEntities.TryGetValue(action.attackerEntityId, out var att) || !_allEntities.TryGetValue(action.defenderEntityId, out var def)) return;
            
            // 공격 권한(내 것인지) 및 공격 가능 상태 확인 (질풍인 경우 2회까지)
            int maxAttacks = (att.Keywords != null && att.Keywords.Contains(CardKeywords.Windfury)) ? 2 : 1;
            if(att.OwnerUid != senderUid || !att.CanAttack || att.AttacksThisTurn >= maxAttacks) return;

            // 방어자가 상대방 개체이고 은신(Stealth) 상태라면 공격 불가 (비정상 패킷 방어)
            if(def.OwnerUid != senderUid && def.Keywords != null && def.Keywords.Contains(CardKeywords.Stealth)) return;

            // ⚡ [Rush / 속공] 소환된 턴에 상대 영웅 공격 방지
            bool isRushOnly = att.Keywords != null 
                           && att.Keywords.Contains(CardKeywords.Rush) 
                           && !att.Keywords.Contains(CardKeywords.Charge) 
                           && att.SummonedTurn == _turnCount;
            if (isRushOnly && def.IsLeader)
            {
                LogDebug("Rush", $"🚫 [속공] '{att.SourceCard.CardName}'(ID:{att.EntityId})은 소환된 턴에 상대 영웅을 공격할 수 없습니다.");
                return;
            }

            // 이벤트 로그 저장 (공격 선언 및 공격력 전달, 트리거 타입: ON_ATTACK)
            LogEvent(GameEventType.ATTACK, att.EntityId, def.EntityId, val: att.Attack, effectTriggerType: EffectTriggerType.ON_ATTACK, cardId: att.SourceCard?.CardId);

            // 공격 횟수 증가 및 상태 업데이트 (질풍 처리)
            att.AttacksThisTurn++;
            if (att.AttacksThisTurn >= maxAttacks)
            {
                att.HasAttacked = true;
                att.CanAttack = false;
            }
            else
            {
                att.HasAttacked = false;
                att.CanAttack = true; // 아직 1회 더 공격 가능!
            }
            AddPendingUpdate(att);

            // 공격자가 은신(Stealth) 상태였다면 공격 즉시 은신 해제!
            if (att.Keywords != null && att.Keywords.Contains(CardKeywords.Stealth))
            {
                att.Keywords.Remove(CardKeywords.Stealth);
                AddPendingUpdate(att);
            }

            int attDmg = att.Attack;
            int defDmg = def.IsLeader ? 0 : def.Attack;
            bool defWasAlive = def.Health > 0;
            bool attWasAlive = att.Health > 0;
            string attName = att.SourceCard?.CardName ?? "공격자";
            string defName = def.SourceCard?.CardName ?? "대상";
            string attOwnerName = GetPlayerDisplayName(att.OwnerUid);
            string defOwnerName = GetPlayerDisplayName(def.OwnerUid);

            bool anyDied = false;
            _isResolvingCombat = true;
            try
            {
                // 실제 전투 계산 (ATTACK, DAMAGE, ATTACK, DAMAGE 기록)
                await ResolveCombatAsync(att, def);

                // 사망 처리 (사망자가 발생하면 DEATH 로그와 함께 단일 패킷으로 브로드캐스트)
                anyDied = await ProcessDeathsAsync();
            }
            finally
            {
                _isResolvingCombat = false;
            }

            bool defDied = defWasAlive && (def.IsDestroyed || def.Health <= 0);
            bool attDied = attWasAlive && (att.IsDestroyed || att.Health <= 0);

            // 단일 종합 공격 로그 발행 (공격, 반격, 피해, 사망을 하나의 완성된 로그로 통합)
            string combatMsg = $"{attOwnerName}의 [{attName}]이(가) {defOwnerName}의 [{defName}]을(를) 공격하여 {attDmg}의 피해를 입혔습니다.";
            if (defDmg > 0)
            {
                combatMsg += $" (반격 피해: {defDmg})";
            }
            if (defDied && attDied)
            {
                combatMsg += $" [{defName}], [{attName}] 파괴됨.";
            }
            else if (defDied)
            {
                combatMsg += $" [{defName}] 파괴됨.";
            }
            else if (attDied)
            {
                combatMsg += $" [{attName}] 파괴됨.";
            }

            AddLog(
                actor: attOwnerName,
                actionType: "ATTACK",
                message: combatMsg,
                details: null,
                sourceCardId: att.SourceCard?.CardId,
                sourceCardName: attName,
                sourceEntityId: att.EntityId,
                targetEntityId: def.EntityId,
                targetCardName: defName,
                value: attDmg,
                value2: defDmg,
                playerUid: att.OwnerUid
            );

            if (!anyDied)
            {
                // 아무도 죽지 않았을 때 전투 결과 브로드캐스트
                await BroadcastUpdatesAsync(senderUid);
            }
        }

        /// <summary>
        /// 실제 전투 데미지를 상호 교환합니다.
        /// </summary>
        public async Task ResolveCombatAsync(GameEntity att, GameEntity def)
        {
            // _pendingUpdates.Clear();
            // 서로의 공격력만큼 체력 차감
            await ApplyDamageAsync(def, att.Attack, att.EntityId);
            
            // 방어자가 공격력이 0이 아니라면 이벤트로그에 반격추가 (반격 선언 및 반격 공격력 전달, 트리거 타입: ON_ATTACK)
            if (def.Attack > 0)
            {
                LogEvent(GameEventType.ATTACK, def.EntityId, att.EntityId, val: def.Attack, effectTriggerType: EffectTriggerType.ON_ATTACK, cardId: def.SourceCard?.CardId);
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
            // 슬롯에서는 이미 비워졌으나 아직 완전히 파괴/삭제되지 않은 사망/퇴장 개체 검색
            foreach (var e in _allEntities.Values)
            {
                if (e != null && e.SourceCard?.InstanceId == card.InstanceId) return e;
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
        public async Task ApplyDamageAsync(GameEntity target, int amount, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE, string? cardId = null)
        {
            if (amount <= 0) return;

            // 💂 [Bodyguard] 리더가 피해를 입을 때 필드에 Bodyguard 하수인이 존재하면 대신 피해를 받음!
            if (target.IsLeader)
            {
                PlayerState? targetPlayer = GetPlayerState(target.OwnerUid);
                if (targetPlayer != null)
                {
                    var bodyguard = targetPlayer.Field.Concat(targetPlayer.MemberZone)
                        .FirstOrDefault(e => e != null && e.Health > 0 && !e.IsDestroyed && e.Keywords != null && e.Keywords.Contains(CardKeywords.Bodyguard));

                    if (bodyguard != null)
                    {
                        LogDebug("Bodyguard", $"💂 [보디가드] {target.OwnerUid}의 리더 대신 '{bodyguard.SourceCard.CardName}'(ID:{bodyguard.EntityId})이 {amount}의 피해를 대신 받습니다!");
                        await ApplyDamageAsync(bodyguard, amount, sourceId, triggerType, cardId);
                        return;
                    }
                }
            }

            // 🛡️ [DivineShield] 천상의 보호막 검사: 피해를 0으로 흡수하고 보호막 해제
            if (target.Keywords != null && target.Keywords.Contains(CardKeywords.DivineShield))
            {
                LogDebug("DivineShield", $"🛡️ [천상의 보호막] '{target.SourceCard?.CardName}'(ID:{target.EntityId})이 {amount}의 피해를 천상의 보호막으로 완전히 막아냅니다!");
                target.Keywords.Remove(CardKeywords.DivineShield);
                AddPendingUpdate(target);
                // 피해를 0으로 흡수했음을 클라이언트에 전송 (보호막 깨짐 연출 유도)
                LogEvent(GameEventType.DAMAGE, sourceId, target.EntityId, 0, null, triggerType, null, 0, cardId);
                return;
            }

            target.Health -= amount;

            // 데미지 발생 이벤트 클라이언트에 전송 대기 (value: amount, cardId: cardId)
            _allEntities.TryGetValue(sourceId, out var dmgSourceEntity);
            LogEvent(GameEventType.DAMAGE, sourceId, target.EntityId, amount, null, triggerType, dmgSourceEntity?.ToEntityData(), 0, cardId);
            AddPendingUpdate(target);
            OnDamageApplied?.Invoke(target.SourceCard.CardName, amount, sourceId);

            // ⚡ 피해를 입힌 공격자(sourceEntity)의 특수 키워드 효과 처리
            if (sourceId > 0 && amount > 0)
            {
                var sourceEntity = dmgSourceEntity;
                if (sourceEntity != null && sourceEntity.Keywords != null)
                {
                    // 🩸 [Lifesteal / 흡혈] 입힌 피해만큼 내 리더(영웅)의 체력 회복
                    if (sourceEntity.Keywords.Contains(CardKeywords.Lifesteal))
                    {
                        PlayerState? srcPlayer = GetPlayerState(sourceEntity.OwnerUid);
                        if (srcPlayer?.Leader != null)
                        {
                            LogDebug("Lifesteal", $"🩸 [흡혈] '{sourceEntity.SourceCard?.CardName}'(ID:{sourceEntity.EntityId})이 {amount}의 피해를 입혀 {srcPlayer.Uid} 영웅의 체력을 {amount} 회복합니다.");
                            ApplyHeal(srcPlayer.Leader, amount, sourceEntity.EntityId, sourceEntity.SourceCard?.CardId, triggerType);
                        }
                    }

                    // ☠️ [Poisonous / 독성] 하수인에게 유효한 피해를 입혔다면 대상 즉시 처치
                    if (sourceEntity.Keywords.Contains(CardKeywords.Poisonous) && !target.IsLeader && !target.IsDestroyed)
                    {
                        LogDebug("Poisonous", $"☠️ [독성] '{sourceEntity.SourceCard?.CardName}'(ID:{sourceEntity.EntityId})의 독성 피해로 '{target.SourceCard?.CardName}'(ID:{target.EntityId})이 즉시 처치됩니다.");
                        target.IsDestroyed = true;
                        target.Health = 0;
                        AddPendingUpdate(target);
                    }
                }
            }

            // =================================================================
            // 🚀 [신규 표준] 5W1H 표준 피해 패킷(GameActionPacket) 발행!
            // 가해자, 피격자, 피해량, 생사 여부(TargetDied), 오버킬(OverkillAmount) 완벽 캡슐화
            // =================================================================
            _allEntities.TryGetValue(sourceId, out var attackerEntity);
            GameCard? attackerCard = attackerEntity?.SourceCard;
            string sourcePlayerUid = attackerEntity?.OwnerUid ?? (target.OwnerUid == _playerA.Uid ? _playerB.Uid : _playerA.Uid);

            var damagePacket = GameServer.Effects.GameActionPacket.CreateDamage(
                attackerEntity,
                attackerCard,
                sourcePlayerUid,
                target,
                amount);

            // 비동기로 하수인의 데미지 반응 효과(예: 고통의 수행사제 드로우 등)를 즉시 실행
            await PublishActionAsync(damagePacket);

            // 리더가 피해를 입어 체력이 0 이하가 되었을 경우 즉시 게임 종료 검사
            if (target.IsLeader && target.Health <= 0)
            {
                await CheckGameOverAsync();
            }
        }
        /// <summary>
        /// 특정 개체에 힐을 하고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyHeal(GameEntity target, int amount, int sourceId = 0, string? cardId = null, EffectTriggerType triggerType = EffectTriggerType.NONE)
        {
            int oldHealth = target.Health;
            target.Health = Math.Min(target.Health + amount, target.MaxHealth);
            int actualHeal = target.Health - oldHealth;
            Console.WriteLine($"[GameState] {target.EntityId} 회복 {actualHeal}");
            
            if (actualHeal > 0) 
            {
                _allEntities.TryGetValue(sourceId, out var healSourceEntity);
                // value: actualHeal, cardId: cardId
                LogEvent(GameEventType.HEAL, sourceId, target.EntityId, actualHeal, null, triggerType, healSourceEntity?.ToEntityData(), 0, cardId);
                OnHealApplied?.Invoke(target.SourceCard.CardName, actualHeal, sourceId);
            }
            AddPendingUpdate(target);
        }

        /// <summary>
        /// 특정 개체에 버프를 주고 업데이트 목록에 추가합니다.
        /// </summary>
        public void ApplyBuff(GameEntity target, int attackBuff, int healthBuff, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE, string? cardId = null, int spellAmp = 0, int spellWeakness = 0, int buffAmpAttack = 0, int buffAmpHealth = 0, int duration = 0)
        {
            if (target.IsMember)
            {
                attackBuff = 0; // 멤버는 공격력이 없으므로 공격력 버프 무시 (항상 0 유지)
            }

            target.Attack += attackBuff;
            if (target.IsMember)
            {
                target.Attack = 0; // 이중 안전가드
            }
            target.Health += healthBuff;
            target.MaxHealth += healthBuff;
            target.SpellAmp += spellAmp;
            target.SpellWeakness += spellWeakness;
            target.BuffAmpAttack += buffAmpAttack;
            target.BuffAmpHealth += buffAmpHealth;

            _allEntities.TryGetValue(sourceId, out var buffSourceEntity);
            string? srcCardId = buffSourceEntity?.SourceCard?.CardId ?? cardId;
            string? srcCardName = buffSourceEntity?.SourceCard?.CardName;
            string desc = $"+{attackBuff}/+{healthBuff}" + (duration > 0 ? $" ({duration}턴)" : "");

            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                sourceCardId = srcCardId,
                sourceCardName = srcCardName,
                description = desc,
                effectType = GameEventType.BUFF,
                attackMod = attackBuff,
                healthMod = healthBuff,
                duration = duration,
                spellAmpMod = spellAmp,
                spellWeaknessMod = spellWeakness,
                buffAmpAttackMod = buffAmpAttack,
                buffAmpHealthMod = buffAmpHealth
            });

            // value: attackBuff, val2: healthBuff, cardId: cardId
            LogEvent(GameEventType.BUFF, sourceId, target.EntityId, attackBuff, null, triggerType, buffSourceEntity?.ToEntityData(), healthBuff, cardId);
            AddPendingUpdate(target);
        }

        /// <summary>
        /// 손패 중 지정된 특정 카드 리스트에만 버프(스탯 및 키워드)를 적용하고 클라이언트에 일괄 알립니다.
        /// </summary>
        public async Task ApplyHandBuffAsync(string ownerUid, int attackBuff, int healthBuff, int costBuff, List<GameCard> targetCards, List<string>? keywords = null, int spellAmp = 0, int spellWeakness = 0, int buffAmpAttack = 0, int buffAmpHealth = 0)
        {
            PlayerState p = GetPlayerState(ownerUid);
            List<GameCard> buffedCards = new List<GameCard>();

            // 던져진 카드 리스트만 순회하므로 내부 코드가 매우 깔끔해집니다.
            foreach (var card in targetCards)
            {
                // 안전장치: 손패에 실제로 존재하는 카드인 경우에만 버프를 적용합니다.
                if (p.Hand.Contains(card) && (card.Type == CardType.하수인 || costBuff != 0))
                {
                    card.CurrentAttack += attackBuff;
                    card.CurrentHealth += healthBuff;
                    card.CurrentCost += costBuff;
                    card.CurrentCost = Math.Max(0, card.CurrentCost); // 코스트 안전가드
                    card.SpellAmp += spellAmp;
                    card.SpellWeakness += spellWeakness;
                    card.BuffAmpAttack += buffAmpAttack;
                    card.BuffAmpHealth += buffAmpHealth;

                    if (keywords != null)
                    {
                        foreach (var kwStr in keywords)
                        {
                            if (Enum.TryParse<CardKeywords>(kwStr, true, out var kw))
                            {
                                if (card.CurrentKeywords != null && !card.CurrentKeywords.Contains(kw))
                                {
                                    card.CurrentKeywords.Add(kw);
                                }
                            }
                        }
                    }

                    string handBuffDesc = $"공격력 {attackBuff:+0;-#;0}, 체력 {healthBuff:+0;-#;0}" + (costBuff != 0 ? $", 비용 {costBuff:+0;-#;0}" : "");
                    card.Enchantments.Add(new EnchantmentInfo
                    {
                        sourceEntityId = 0,
                        sourceCardName = "손패 강화",
                        description = handBuffDesc,
                        effectType = GameEventType.BUFF_HAND,
                        attackMod = attackBuff,
                        healthMod = healthBuff,
                        costMod = costBuff
                    });

                    LogDebug("HandBuff", $"🃏 [손패 버프] {ownerUid}의 '{card.CardName}'({card.CardId}) 스탯({attackBuff:+0;-#;0}/{healthBuff:+0;-#;0}), 비용({costBuff:+0;-#;0} -> 현재: {card.CurrentCost})");
                    buffedCards.Add(card);
                }
            }

            // 버프 성공 시 단 한 번만 패킷을 전송하여 네트워크를 최적화합니다.
            if (buffedCards.Count > 0)
            {
                LogEvent(GameEventType.BUFF_HAND, 0, 0, attackBuff, healthBuff.ToString());
                string costSummary = costBuff != 0 ? $", 비용 변동 {costBuff:+0;-#}" : "";
                AddLog("System", "BUFF_HAND", $"{ownerUid}의 손패 카드 {buffedCards.Count}장이 공격력 {attackBuff}/체력 {healthBuff}{costSummary} 버프를 받았습니다.");

                var updateMsg = new S_UpdateHandCards
                {
                    action = GameActionType.UPDATE_HAND_CARDS,
                    updatedCards = buffedCards.Select(c => c.ToCardInfo()).ToList()
                };
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(updateMsg));
            }
        }

        /// <summary>
        /// 특정 손패 카드의 스택 또는 스탯 변경 사항을 클라이언트에 동기화합니다.
        /// </summary>
        public async Task SyncHandCardAsync(string playerUid, GameCard card)
        {
            PlayerState p = GetPlayerState(playerUid);
            if (p == null) return;

            var updateMsg = new S_UpdateHandCards
            {
                action = GameActionType.UPDATE_HAND_CARDS,
                updatedCards = new List<CardInfo> { card.ToCardInfo() }
            };
            await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(updateMsg));
        }

        /// <summary>
        /// 특정 플레이어의 손패에서 지정된 카드 목록을 버리고 묘지로 보냅니다.
        /// </summary>
        public async Task DiscardCardsAsync(string ownerUid, List<GameCard> cardsToDiscard)
        {
            PlayerState p = GetPlayerState(ownerUid);
            if (p == null || cardsToDiscard == null || cardsToDiscard.Count == 0) return;

            foreach (var card in cardsToDiscard)
            {
                if (p.Hand.Contains(card))
                {
                    p.Hand.Remove(card);
                    card.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                    p.Graveyard.Add(card);

                    int targetLeaderId = p.Leader?.EntityId ?? 0;
                    LogEvent(GameEventType.DISCARD, targetLeaderId, targetLeaderId, 1, card.CardId);
                    LogDebug("Discard", $"🗑️ [손패 버리기] {p.Uid}가 '{card.CardName}'({card.CardId})을(를) 버렸습니다.");
                }
            }

            AddLog(p.Uid, "DISCARD", $"{p.Uid}가 손패에서 {cardsToDiscard.Count}장의 카드를 버렸습니다.");

            // 손패 장수 변동에 따른 동적 스탯/코스트 실시간 동기화
            await RefreshAllDynamicHandStatsAsync();
        }

        /// <summary>
        /// 덱에 있는 카드 중 특정 조건에 맞는 카드에만 버프(스탯 및 키워드)를 누적시킵니다.
        /// </summary>
        public Task ApplyDeckBuffAsync(string ownerUid, int attackBuff, int healthBuff, int costBuff, List<GameCard> targetCards, List<string>? keywords = null, int spellAmp = 0, int spellWeakness = 0, int buffAmpAttack = 0, int buffAmpHealth = 0)
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
                    card.SpellAmp += spellAmp;
                    card.SpellWeakness += spellWeakness;
                    card.BuffAmpAttack += buffAmpAttack;
                    card.BuffAmpHealth += buffAmpHealth;

                    if (keywords != null)
                    {
                        foreach (var kwStr in keywords)
                        {
                            if (Enum.TryParse<CardKeywords>(kwStr, true, out var kw))
                            {
                                if (card.CurrentKeywords != null && !card.CurrentKeywords.Contains(kw))
                                {
                                    card.CurrentKeywords.Add(kw);
                                }
                            }
                        }
                    }

                    string deckBuffDesc = $"공격력 {attackBuff:+0;-#;0}, 체력 {healthBuff:+0;-#;0}" + (costBuff != 0 ? $", 비용 {costBuff:+0;-#;0}" : "");
                    card.Enchantments.Add(new EnchantmentInfo
                    {
                        sourceEntityId = 0,
                        sourceCardName = "덱 강화",
                        description = deckBuffDesc,
                        effectType = GameEventType.BUFF_DECK,
                        attackMod = attackBuff,
                        healthMod = healthBuff,
                        costMod = costBuff
                    });

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

        
        /// <summary>
        /// 필드의 하수인을 주인의 손패로 되돌립니다 (바운스).
        /// </summary>
        public async Task ReturnEntityToHandAsync(GameEntity target, int costModifier = 0)
        {
            if (target == null || target.IsLeader) return;

            PlayerState owner = GetPlayerState(target.OwnerUid);
            PlayerState opp = GetPlayerState(target.OwnerUid, true);
            if (owner == null) return;

            // 1. 필드 또는 멤버존 슬롯에서 제거
            for (int i = 0; i < owner.Field.Length; i++)
            {
                if (owner.Field[i] == target) owner.Field[i] = null;
            }
            for (int i = 0; i < owner.MemberZone.Length; i++)
            {
                if (owner.MemberZone[i] == target) owner.MemberZone[i] = null;
            }

            // 2. 서버 전역 관리 엔티티에서 제거
            _allEntities.Remove(target.EntityId);

            // 3. 원본 카드 스탯/버프 초기화 및 코스트 증감 적용
            GameCard card = target.SourceCard;
            card.CurrentAttack = card.OriginalAttack;
            card.CurrentHealth = card.OriginalHealth;
            card.CurrentCost = Math.Max(0, card.OriginalCost + costModifier);
            card.CurrentKeywords = new List<CardKeywords>(card.OriginalKeywords);
            card.Enchantments.Clear();

            // 4. 손패 수 한도 검사 (최대 10장)
            if (owner.Hand.Count < 10)
            {
                // 손패로 정상 복귀
                card.UpdateZone(GameServer.Effects.Zone.Hand, this.EventSystem);
                owner.Hand.Add(card);

                LogEvent(GameEventType.RETURN_TO_HAND, 0, target.EntityId, 0, card.CardId);
                LogDebug("Return", $"↩️ [손패 복귀] {owner.Uid}의 하수인 '{card.CardName}'(ID:{target.EntityId})이(가) 손패로 되돌아갔습니다. (비용: {card.CurrentCost})");
                AddLog(owner.Uid, "RETURN_TO_HAND", $"{owner.Uid}의 '{card.CardName}' 하수인이 손패로 되돌아갔습니다.");

                // 클라이언트에 손패 획득 패킷 전송
                var msgToSelf = new S_DrawCard
                {
                    action = GameActionType.DRAW_CARD,
                    playerUid = owner.Uid,
                    drawnCard = card.ToCardInfo()
                };
                var msgToOpponent = new S_DrawCard
                {
                    action = GameActionType.DRAW_CARD,
                    playerUid = owner.Uid,
                    drawnCard = null
                };
                await _room.SendMessageToPlayerAsync(owner.PlayerRef, JsonConvert.SerializeObject(msgToSelf));
                await _room.SendMessageToPlayerAsync(opp.PlayerRef, JsonConvert.SerializeObject(msgToOpponent));
            }
            else
            {
                // 손패가 10장으로 가득 차서 소멸(사망)
                card.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
                owner.Graveyard.Add(card);

                LogEvent(GameEventType.DESTROY, 0, target.EntityId, 0, card.CardId);
                LogDebug("Return", $"⚠️ [손패 초과 소멸] {owner.Uid}의 손패가 가득 차서 '{card.CardName}'이(가) 소멸했습니다.");
                AddLog(owner.Uid, "BURN_CARD", $"{owner.Uid}의 손패가 가득 차서 '{card.CardName}' 하수인이 소멸했습니다.");
            }

            // 5. 필드 오브젝트 제거를 위해 클라이언트에 전달 (체력 0 상태)
            var dData = target.ToEntityData();
            dData.health = 0;
            AddPendingUpdateData(dData);

            // 6. 손패/필드 변화에 따른 동적 스탯 실시간 동기화
            await RefreshAllDynamicHandStatsAsync();
        }

        /// <summary>
        /// 필드의 하수인을 주인의 덱 속으로 무작위 섞어 넣습니다.
        /// </summary>
        public async Task ShuffleEntityToDeckAsync(GameEntity target)
        {
            if (target == null || target.IsLeader) return;

            PlayerState owner = GetPlayerState(target.OwnerUid);
            if (owner == null) return;

            // 1. 필드 또는 멤버존 슬롯에서 제거
            for (int i = 0; i < owner.Field.Length; i++)
            {
                if (owner.Field[i] == target) owner.Field[i] = null;
            }
            for (int i = 0; i < owner.MemberZone.Length; i++)
            {
                if (owner.MemberZone[i] == target) owner.MemberZone[i] = null;
            }

            // 2. 서버 전역 관리 엔티티에서 제거
            _allEntities.Remove(target.EntityId);

            // 3. 카드 스탯/버프 원본 복구
            GameCard card = target.SourceCard;
            card.CurrentAttack = card.OriginalAttack;
            card.CurrentHealth = card.OriginalHealth;
            card.CurrentCost = card.OriginalCost;
            card.CurrentKeywords = new List<CardKeywords>(card.OriginalKeywords);
            card.Enchantments.Clear();

            // 4. 덱 구역으로 이동 및 덱 무작위 위치에 삽입
            card.UpdateZone(GameServer.Effects.Zone.Deck, this.EventSystem);
            int insertIndex = Rng.Next(owner.Deck.Count + 1);
            owner.Deck.Insert(insertIndex, card);

            LogEvent(GameEventType.SHUFFLE_TO_DECK, 0, target.EntityId, 0, card.CardId);
            LogDebug("Shuffle", $"🔀 [덱 섞어넣기] {owner.Uid}의 '{card.CardName}'(ID:{target.EntityId})이(가) 덱으로 섞여 들어갔습니다.");
            AddLog(owner.Uid, "SHUFFLE_TO_DECK", $"{owner.Uid}의 '{card.CardName}' 하수인이 덱으로 섞여 들어갔습니다.");

            // 5. 필드 오브젝트 제거 전송 (체력 0 상태)
            var sData = target.ToEntityData();
            sData.health = 0;
            AddPendingUpdateData(sData);

            // 6. 손패/필드 변화에 따른 동적 스탯 실시간 동기화
            await RefreshAllDynamicHandStatsAsync();
        }

        /// <summary> 특정 개체에 '속박(Bind)'을 부여합니다. </summary>
        public void ApplyBind(GameEntity target, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE)
        {
            if (target.Keywords != null && !target.Keywords.Contains(CardKeywords.Bind))
            {
                target.Keywords.Add(CardKeywords.Bind);
            }

            _allEntities.TryGetValue(sourceId, out var bindSource);
            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                sourceCardId = bindSource?.SourceCard?.CardId,
                sourceCardName = bindSource?.SourceCard?.CardName ?? "속박 효과",
                description = "속박 (공격 불가)",
                effectType = GameEventType.BIND,
            });
            target.CanAttack = false; // 즉시 공격 불가 상태로 만듦
            LogEvent(GameEventType.BIND, sourceId, target.EntityId, 0, null, triggerType);
            AddPendingUpdate(target);
        }

        /// <summary> 특정 개체에 '침묵(Silence)'을 적용하여 능력치와 키워드를 원본으로 되돌립니다. </summary>
        public void ApplySilence(GameEntity target, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE)
        {
            target.Keywords?.Clear(); // 모든 특수 키워드 제거
            target.Keywords?.Add(CardKeywords.Silence); // 침묵 키워드 부여
            
            // 스탯 및 특수 오라 능력을 0으로 초기화
            target.Attack = target.SourceCard.OriginalAttack;
            target.MaxHealth = target.SourceCard.OriginalHealth;
            if (target.Health > target.MaxHealth) target.Health = target.MaxHealth;

            target.SpellAmp = 0;
            target.SpellWeakness = 0;
            target.HasDrawSeal = false;
            target.BuffAmpAttack = 0;
            target.BuffAmpHealth = 0;

            _allEntities.TryGetValue(sourceId, out var silSource);
            target.Enchantments.Add(new EnchantmentInfo
            {
                sourceEntityId = sourceId,
                sourceCardId = silSource?.SourceCard?.CardId,
                sourceCardName = silSource?.SourceCard?.CardName ?? "침묵 효과",
                description = "침묵 (모든 능력치 및 효과 무효화)",
                effectType = GameEventType.SILENCE,
            });
            LogEvent(GameEventType.SILENCE, sourceId, target.EntityId, 0, null, triggerType);
            AddPendingUpdate(target);
        }

        /// <summary> 특정 개체에 새로운 '키워드(Keyword)'를 부여합니다. </summary>
        public void GrantKeyword(GameEntity target, List<string> keywordStr, int sourceId = 0, EffectTriggerType triggerType = EffectTriggerType.NONE, int duration = 0)
        {
            _allEntities.TryGetValue(sourceId, out var kwSource);
            for(int i = 0; i < keywordStr.Count; i++)
            {
                if (Enum.TryParse<CardKeywords>(keywordStr[i], true, out var keyword))
                {
                    if (target.Keywords != null && !target.Keywords.Contains(keyword))
                    {
                        target.Keywords.Add(keyword);

                        // 돌진이나 속공을 부여받은 경우, 속박 상태가 아니고 공격한 적이 없다면 즉시 공격 가능하게 활성화
                        if ((keyword == CardKeywords.Charge || keyword == CardKeywords.Rush) && !target.HasAttacked)
                        {
                            bool isBound = target.Keywords.Contains(CardKeywords.Bind);
                            if (!isBound)
                            {
                                target.CanAttack = true;
                            }
                        }

                        string kwDesc = $"'{keywordStr[i]}' 키워드 부여" + (duration > 0 ? $" ({duration}턴)" : "");
                        target.Enchantments.Add(new EnchantmentInfo
                        {
                            sourceEntityId = sourceId,
                            sourceCardId = kwSource?.SourceCard?.CardId,
                            sourceCardName = kwSource?.SourceCard?.CardName ?? "키워드 부여",
                            description = kwDesc,
                            effectType = GameEventType.GRANT_KEYWORD,
                            grantedKeyword = keywordStr[i],
                            duration = duration
                        });
                        LogEvent(GameEventType.GRANT_KEYWORD, sourceId, target.EntityId, 0, keywordStr[i], triggerType);
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
        /// 영웅 체력을 확인하여 0 이하이면 즉시 게임 종료(S_GameOver)를 처리합니다.
        /// </summary>
        public async Task<bool> CheckGameOverAsync()
        {
            if (_isGameOver) return true;

            if (_playerA.Leader.Health <= 0 && _playerB.Leader.Health <= 0)
            {
                await EndGameAsync("DRAW", "BOTH_LEADERS_KILLED");
                return true;
            }
            if (_playerA.Leader.Health <= 0)
            {
                await EndGameAsync(_playerB.Uid, "LEADER_KILLED");
                return true;
            }
            if (_playerB.Leader.Health <= 0)
            {
                await EndGameAsync(_playerA.Uid, "LEADER_KILLED");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 필드 위 모든 개체의 체력을 확인하여 0 이하인 개체를 제거하고 '죽음의 메아리'를 처리합니다.
        /// </summary>
        /// <returns>사망자가 발생했거나 게임이 끝났으면 true</returns>
        public async Task<bool> ProcessDeathsAsync()
        {
            if (_isGameOver) return true;
            if (await CheckGameOverAsync()) return true;

            // 1. 죽은 하수인/멤버 개체들 필터링 (리더 제외)
            var deadEntities = _allEntities.Values.Where(e => !e.IsLeader && (e.Health <= 0 || e.IsDestroyed)).ToList();
            if (deadEntities.Count == 0) return false;

            foreach (var dead in deadEntities)
            {
                OnMinionDestroyed?.Invoke(dead.OwnerUid, dead.SourceCard.CardName);

                // =================================================================
                // [개선 1] 사망한 하수인의 필드 슬롯을 먼저 null로 비워 공간을 확보합니다.
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
                var deathContext = new GameServer.Effects.EffectContext(dead.OwnerUid, dead.SourceCard, EffectTriggerType.ON_DEATH)
                {
                    SourceEntity = dead,
                    TargetEntity = dead
                };

                if (dead.SourceCard.NewEffects.Any(e => e.Trigger == EffectTriggerType.ON_DEATH))
                {
                    LogEvent(GameEventType.EFFECT_TRIGGER, dead.EntityId, 0, 0, null, EffectTriggerType.ON_DEATH, dead.ToEntityData());
                }

                // 비동기로 사망 감지 및 죽음의 메아리 효과를 표준 패킷으로 실시간 전파 및 실행합니다.
                var deathPacket = GameServer.Effects.GameActionPacket.CreateDeath(dead, cause: GameServer.Effects.GameActionKind.Damage);
                await PublishActionAsync(deathPacket);

                // 효과 발동 및 투사체 이벤트가 모두 등록된 후 사망 이벤트 기록 (연출 순서: 효과 발동/투사체 -> 사망)
                LogEvent(GameEventType.DEATH, dead.EntityId, 0, 0, null, EffectTriggerType.NONE, dead.ToEntityData());

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
                AddPendingUpdateData(dData);
            }

            // 6. 사망 정보 즉시 전송
            await BroadcastUpdatesAsync(_playerA.Uid);

            // 사망으로 인한 필드 개체 수 변화 -> 손패 동적 스탯/코스트 갱신
            await RefreshAllDynamicHandStatsAsync();

            // 7. 게임 종료 조건 확인 (영웅 사망 시)
            if (await CheckGameOverAsync()) return true;

            // 8. 죽음의 메아리로 인해 연쇄적으로 죽은 개체가 있을 수 있으므로 재귀 호출
            return await ProcessDeathsAsync();
        }

        /// <summary>
        /// 멤버 카드가 필드에 이미 존재하는 상태에서 새로운 멤버가 소환될 때,
        /// 기존 멤버를 필드에서 퇴장시키고 묘지로 이동시키는 교체 소환 처리기입니다.
        /// </summary>
        private async Task RetireMemberToGraveyardAsync(PlayerState owner, GameEntity oldMember)
        {
            Console.WriteLine($"[GameState] 🔄 멤버 교체 소환: 기존 멤버 '{oldMember.SourceCard?.CardName ?? oldMember.SourceCard?.CardId}' (ID:{oldMember.EntityId}) -> 묘지로 퇴장");
            
            oldMember.IsDestroyed = true;

            // 1. 소유자의 멤버 존 슬롯 비우기
            for (int i = 0; i < owner.MemberZone.Length; i++)
            {
                if (owner.MemberZone[i] == oldMember) owner.MemberZone[i] = null;
            }

            // 2. 퇴장/사망 반응 트리거 통보 (ON_DEATH)
            var deathContext = new GameServer.Effects.EffectContext(owner.Uid, oldMember.SourceCard, EffectTriggerType.ON_DEATH)
            {
                SourceEntity = oldMember,
                TargetEntity = oldMember
            };

            if (oldMember.SourceCard?.NewEffects != null && oldMember.SourceCard.NewEffects.Any(e => e.Trigger == EffectTriggerType.ON_DEATH))
            {
                LogEvent(GameEventType.EFFECT_TRIGGER, oldMember.EntityId, 0, 0, null, EffectTriggerType.ON_DEATH, oldMember.ToEntityData());
            }

            var deathPacket = GameServer.Effects.GameActionPacket.CreateDeath(oldMember, cause: GameServer.Effects.GameActionKind.Damage);
            await PublishActionAsync(deathPacket);

            // 효과 연출 등록 후 사망 이벤트 기록
            LogEvent(GameEventType.DEATH, oldMember.EntityId, 0, 0, null, EffectTriggerType.NONE, oldMember.ToEntityData());

            // 3. 카드 위치를 무덤으로 갱신하고 구독 해제 후 묘지에 추가
            oldMember.SourceCard?.UpdateZone(GameServer.Effects.Zone.Graveyard, this.EventSystem);
            if (oldMember.SourceCard != null)
            {
                owner.Graveyard.Add(oldMember.SourceCard);
            }

            // 4. 전역 엔티티 관리에서 제거
            _allEntities.Remove(oldMember.EntityId);

            // 5. 클라이언트에게 기존 멤버 제거 상태 알림 (체력 0으로 처리)
            var dData = oldMember.ToEntityData();
            dData.health = 0;
            AddPendingUpdateData(dData);
        }

        /// <summary>
        /// 상태가 변경된 개체를 전송 대기 목록에 추가합니다. (중복 방지, 스레드 안전)
        /// </summary>
        private void AddPendingUpdate(GameEntity entity)
        {
            lock (_bufferLock)
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
            List<GameEvent> eventsToSend;
            List<EntityData> updatesToSend;

            lock (_bufferLock)
            {
                // 이벤트가 없거나 업데이트할 내용이 없다면 스킵
                if (_eventBuffer.Count == 0 && _pendingUpdates.Count == 0) return;

                eventsToSend = [.. _eventBuffer];
                updatesToSend = [.. _pendingUpdates];

                // 버퍼 비우기 (스레드 안전)
                _eventBuffer.Clear();
                _pendingUpdates.Clear();
            }

            var resolutionMsg = new S_ActionResolution
            {
                action = GameActionType.ACTION_RESOLUTION,
                eventLog = eventsToSend, // 복사본 전달
                finalStateUpdates = updatesToSend
            };

            string json = JsonConvert.SerializeObject(resolutionMsg);

            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
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
        /// 각 플레이어에게 맞춤화된 획득 재화 및 성장 정보가 포함된 S_GameOver 패킷을 전송합니다.
        /// </summary>
        public async Task EndGameAsync(string winner, string reason)
        {
            if(_isGameOver) return;
            _isGameOver = true;
            _currentPhase = "GameOver";

            // 🏆 승자 및 패자 보상 / 전적 / 레벨업 일괄 처리 (결과 맵 수신)
            var rewardMap = await ProcessMatchRewardsAsync(winner);

            // 각 플레이어에게 맞춤화된 패킷 생성 및 전송
            var msgA = CreateGameOverPacket(_playerA.Uid, winner, reason, rewardMap.GetValueOrDefault(_playerA.Uid));
            var msgB = CreateGameOverPacket(_playerB.Uid, winner, reason, rewardMap.GetValueOrDefault(_playerB.Uid));

            await _room.SendMessageToPlayerAsync(_playerA.PlayerRef, JsonConvert.SerializeObject(msgA));
            await _room.SendMessageToPlayerAsync(_playerB.PlayerRef, JsonConvert.SerializeObject(msgB));

            // 📁 매치 로그 파일 저장
            _ = SaveMatchLogsToFileAsync(winner, reason);
        }

        /// <summary>
        /// 특정 플레이어에게 전송할 S_GameOver 패킷을 생성합니다.
        /// </summary>
        private S_GameOver CreateGameOverPacket(string playerUid, string winnerUid, string reason, PlayerMatchRewardResult? reward)
        {
            var packet = new S_GameOver
            {
                action = GameActionType.GAME_OVER,
                winnerUid = winnerUid,
                reason = reason
            };

            if (reward != null)
            {
                packet.earnedGold = reward.EarnedGold;
                packet.earnedExp = reward.EarnedExp;
                packet.currentGold = reward.CurrentGold;
                packet.currentLevel = reward.CurrentLevel;
                packet.currentExp = reward.CurrentExp;
                packet.maxExp = reward.MaxExp;
                packet.isLevelUp = reward.IsLevelUp;
                packet.scoreChange = reward.ScoreChange;
                packet.currentScore = reward.CurrentScore;
            }

            return packet;
        }

        /// <summary>
        /// 게임이 종료되었을 때, 이 판에서 발생한 모든 액션(Match.log)과 디버그(Debug.log) 로그를 지정된 폴더에 저장합니다.
        /// 경로: E:\Server\MyGameServer\MyGameServer\MachingLog\Match_YYYYMMDD_HHmmss_[플레이어A]_vs_[플레이어B]\
        /// </summary>
        private Task SaveMatchLogsToFileAsync(string winnerUid, string reason)
        {
            try
            {
                DateTime endTime = DateTime.UtcNow;
                TimeSpan duration = endTime - _gameStartTime;

                // 1. 기본 폴더 설정
                string baseLogDir = @"E:\Server\MyGameServer\MyGameServer\MachingLog";
                if (!Directory.Exists(baseLogDir))
                {
                    try { Directory.CreateDirectory(baseLogDir); }
                    catch { baseLogDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MachingLog"); Directory.CreateDirectory(baseLogDir); }
                }

                string nameA = _playerA.PlayerRef?.IsBot == true ? $"BOT_{_playerA.Uid}" : _playerA.Uid;
                string nameB = _playerB.PlayerRef?.IsBot == true ? $"BOT_{_playerB.Uid}" : _playerB.Uid;

                // 파일/폴더명에 들어갈 수 없는 특수문자 제거
                string safeNameA = string.Concat(nameA.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                string safeNameB = string.Concat(nameB.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string matchFolderName = $"Match_{timestamp}_{safeNameA}_vs_{safeNameB}";
                string matchFolderPath = Path.Combine(baseLogDir, matchFolderName);

                // 2. 하위 폴더 생성: MatchLogs & DebugLogs
                string matchLogsDir = Path.Combine(matchFolderPath, "MatchLogs");
                string debugLogsDir = Path.Combine(matchFolderPath, "DebugLogs");
                Directory.CreateDirectory(matchLogsDir);
                Directory.CreateDirectory(debugLogsDir);

                string matchFilePath = Path.Combine(matchLogsDir, "Match.log");
                string debugFilePath = Path.Combine(debugLogsDir, "Debug.log");

                string winnerName = (winnerUid == _playerA.Uid) ? nameA : (winnerUid == _playerB.Uid) ? nameB : winnerUid;

                // ==========================================
                // 3. MatchLogs/Match.log 작성 (게임 요약 & 플레이 타임라인)
                // ==========================================
                var matchSb = new StringBuilder();
                matchSb.AppendLine("================================================================================");
                matchSb.AppendLine("🎮 [STELLAR DUEL - 대전 결과 및 플레이 로그]");
                matchSb.AppendLine("================================================================================");
                matchSb.AppendLine($"- 대전 시작 (KST): {_gameStartTime.AddHours(9):yyyy-MM-dd HH:mm:ss}");
                matchSb.AppendLine($"- 대전 종료 (KST): {endTime.AddHours(9):yyyy-MM-dd HH:mm:ss}");
                matchSb.AppendLine($"- 총 플레이 시간 : {(int)duration.TotalMinutes}분 {duration.Seconds}초");
                matchSb.AppendLine($"- 룸 ID          : {_room.GameId}");
                matchSb.AppendLine($"- 플레이어 A     : {nameA} (UID: {_playerA.Uid}) | 직업: {_playerA.Leader?.SourceCard.Class} | 최종 체력: {_playerA.Leader?.Health ?? 0}");
                matchSb.AppendLine($"- 플레이어 B     : {nameB} (UID: {_playerB.Uid}) | 직업: {_playerB.Leader?.SourceCard.Class} | 최종 체력: {_playerB.Leader?.Health ?? 0}");
                matchSb.AppendLine($"- 🏆 승리자      : {winnerName} (UID: {winnerUid})");
                matchSb.AppendLine($"- 종료 사유      : {reason}");
                matchSb.AppendLine("================================================================================");
                matchSb.AppendLine($"📜 [타임라인 액션 로그 (총 {_actionLogs.Count}건)]");
                matchSb.AppendLine("================================================================================");

                lock (_lock)
                {
                    foreach (var log in _actionLogs)
                    {
                        string logTime = log.Timestamp.AddHours(9).ToString("HH:mm:ss.fff");
                        string actorFormatted = string.IsNullOrEmpty(log.Actor) ? "System" : log.Actor;
                        matchSb.AppendLine($"[{logTime}] [{actorFormatted,-10}] [{log.ActionType,-15}] {log.Message}");
                        if (log.Details != null)
                        {
                            matchSb.AppendLine($"               └─ Details: {JsonConvert.SerializeObject(log.Details)}");
                        }
                    }
                }

                matchSb.AppendLine("================================================================================");
                matchSb.AppendLine("🏁 [로그 기록 종료]");
                matchSb.AppendLine("================================================================================");

                File.WriteAllText(matchFilePath, matchSb.ToString(), Encoding.UTF8);

                // ==========================================
                // 4. DebugLogs/Debug.log 작성 (정밀 디버그 로그)
                // ==========================================
                var debugSb = new StringBuilder();
                debugSb.AppendLine("================================================================================");
                debugSb.AppendLine("🔍 [STELLAR DUEL - 서버 정밀 디버그 로그]");
                debugSb.AppendLine("================================================================================");
                debugSb.AppendLine($"- 대전: {nameA} vs {nameB} | 룸 ID: {_room.GameId} | 시작: {_gameStartTime.AddHours(9):yyyy-MM-dd HH:mm:ss}");
                debugSb.AppendLine("================================================================================");

                lock (_lock)
                {
                    foreach (var dlog in _debugLogs)
                    {
                        debugSb.AppendLine(dlog);
                    }
                }

                debugSb.AppendLine("================================================================================");
                debugSb.AppendLine("🏁 [디버그 로그 기록 종료]");
                debugSb.AppendLine("================================================================================");

                File.WriteAllText(debugFilePath, debugSb.ToString(), Encoding.UTF8);

                Console.WriteLine($"[GameState] 📁 [매치 로그 저장 완료]");
                Console.WriteLine($"  ├─ 📂 폴더: {matchFolderPath}");
                Console.WriteLine($"  ├─ 📄 매치 로그: {matchFilePath}");
                Console.WriteLine($"  └─ 🔍 디버그 로그: {debugFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameState] ⚠️ 매치 로그 파일 저장 실패: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 대전 종료 후 승자와 패자의 데이터(골드, 경험치, 레벨, 점수, 전적)를 계산하여 Firestore에 저장하고, 각 플레이어별 보상 결과를 반환합니다.
        /// </summary>
        private async Task<Dictionary<string, PlayerMatchRewardResult>> ProcessMatchRewardsAsync(string winnerUid)
        {
            var results = new Dictionary<string, PlayerMatchRewardResult>();
            try
            {
                bool isDraw = (winnerUid == "DRAW");

                // 플레이어 A 보상 판정
                bool isWinnerA = !isDraw && (winnerUid == _playerA.Uid);
                int goldA = isDraw ? 20 : (isWinnerA ? 100 : 20);
                int expA = isDraw ? 30 : (isWinnerA ? 100 : 30);
                int scoreA = isDraw ? 0 : (isWinnerA ? 30 : -15);

                // 플레이어 B 보상 판정
                bool isWinnerB = !isDraw && (winnerUid == _playerB.Uid);
                int goldB = isDraw ? 20 : (isWinnerB ? 100 : 20);
                int expB = isDraw ? 30 : (isWinnerB ? 100 : 30);
                int scoreB = isDraw ? 0 : (isWinnerB ? 30 : -15);

                var taskA = ApplyPlayerMatchOutcomeAsync(_playerA.Uid, isWinnerA, isDraw, goldA, expA, scoreA);
                var taskB = ApplyPlayerMatchOutcomeAsync(_playerB.Uid, isWinnerB, isDraw, goldB, expB, scoreB);

                await Task.WhenAll(taskA, taskB);

                if (taskA.Result != null) results[_playerA.Uid] = taskA.Result;
                if (taskB.Result != null) results[_playerB.Uid] = taskB.Result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchReward] ❌ 대전 결과 보상 처리 중 오류 발생: {ex.Message}");
            }

            return results;
        }

        /// <summary>
        /// 개별 플레이어의 Firestore 문서를 트랜잭션으로 안전하게 업데이트하고 레벨업 여부를 판정하여 결과를 반환합니다.
        /// </summary>
        private async Task<PlayerMatchRewardResult?> ApplyPlayerMatchOutcomeAsync(string uid, bool isWinner, bool isDraw, int goldChange, int expChange, int scoreChange)
        {
            // BOT 플레이어인 경우 DB 연동 없이 모의 보상 데이터 반환
            if (string.IsNullOrEmpty(uid) || uid.StartsWith("BOT_"))
            {
                return new PlayerMatchRewardResult
                {
                    EarnedGold = goldChange,
                    EarnedExp = expChange,
                    CurrentGold = 0,
                    CurrentLevel = 1,
                    CurrentExp = 0,
                    MaxExp = 100,
                    IsLevelUp = false,
                    ScoreChange = scoreChange,
                    CurrentScore = 1000
                };
            }

            if (_room.Db == null)
            {
                // DB가 연결되지 않은 테스트 환경
                return new PlayerMatchRewardResult
                {
                    EarnedGold = goldChange,
                    EarnedExp = expChange,
                    CurrentGold = goldChange,
                    CurrentLevel = 1,
                    CurrentExp = expChange,
                    MaxExp = 100,
                    IsLevelUp = false,
                    ScoreChange = scoreChange,
                    CurrentScore = 1000 + scoreChange
                };
            }

            try
            {
                PlayerMatchRewardResult? result = null;
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
                    int newLossCount = user.LossCount + ((!isWinner && !isDraw) ? 1 : 0);

                    // 4. 경험치 및 레벨업 계산 (필요 경험치 = 현재레벨 * 100)
                    int currentLevel = user.Level > 0 ? user.Level : 1;
                    int initialLevel = currentLevel;
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

                    bool isLevelUp = currentLevel > initialLevel;
                    int maxExp = currentLevel * 100;

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
                    Console.WriteLine($"[MatchReward] 🎮 플레이어 {uid} ({(isWinner ? "승리" : (isDraw ? "무승부" : "패배"))}): Gold={newGold}(+{(goldChange >= 0 ? "+" : "")}{goldChange}), Lv={currentLevel}, Exp={currentExp}(+{expChange}), Score={newScore}({(scoreChange >= 0 ? "+" : "")}{scoreChange}), 전적={newWinCount}승 {newLossCount}패");

                    result = new PlayerMatchRewardResult
                    {
                        EarnedGold = goldChange,
                        EarnedExp = expChange,
                        CurrentGold = newGold,
                        CurrentLevel = currentLevel,
                        CurrentExp = currentExp,
                        MaxExp = maxExp,
                        IsLevelUp = isLevelUp,
                        ScoreChange = scoreChange,
                        CurrentScore = newScore
                    };
                });

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MatchReward] ❌ 플레이어 {uid} 보상 적용 실패: {ex.Message}");
                return new PlayerMatchRewardResult
                {
                    EarnedGold = goldChange,
                    EarnedExp = expChange,
                    ScoreChange = scoreChange
                };
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

        #region 특수 오라 능력 (주문증폭 / 주문약화 / 드로우봉인 / 능력강화)

        /// <summary>
        /// 아군 필드(및 멤버존)에 살아있는 하수인들의 주문증폭(SpellAmp) 총합을 반환합니다.
        /// </summary>
        public int GetSpellAmp(string playerUid)
        {
            PlayerState? p = GetPlayerState(playerUid);
            if (p == null) return 0;
            return p.Field.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.SpellAmp)
                 + p.MemberZone.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.SpellAmp);
        }

        /// <summary>
        /// 시전자(casterUid)의 주문을 맞는 적군 필드(및 멤버존)에 살아있는 하수인들의 주문약화(SpellWeakness) 총합을 반환합니다.
        /// </summary>
        public int GetSpellWeakness(string casterUid)
        {
            PlayerState? enemy = GetPlayerState(casterUid, true);
            if (enemy == null) return 0;
            return enemy.Field.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.SpellWeakness)
                 + enemy.MemberZone.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.SpellWeakness);
        }

        /// <summary>
        /// 상대방의 필드(및 멤버존)에 살아있는 하수인 중 드로우봉인(HasDrawSeal) 능력을 가진 하수인이 존재하는지 확인합니다.
        /// (내 필드에 있으면 상대가 드로우 불가, 상대 필드에 있으면 내가 드로우 불가)
        /// </summary>
        public bool IsDrawSealed(string playerUid)
        {
            PlayerState? op = GetPlayerState(playerUid, true);
            if (op == null) return false;

            bool opHasSeal = (op.Field != null && op.Field.Any(e => e != null && e.Health > 0 && !e.IsDestroyed && e.HasDrawSeal))
                          || (op.MemberZone != null && op.MemberZone.Any(e => e != null && e.Health > 0 && !e.IsDestroyed && e.HasDrawSeal));
            return opHasSeal;
        }

        /// <summary>
        /// 아군 필드(및 멤버존)에 살아있는 하수인들의 능력강화(BuffAmpAttack, BuffAmpHealth) 총합을 반환합니다.
        /// </summary>
        public (int attackAmp, int healthAmp) GetBuffAmp(string playerUid)
        {
            PlayerState? p = GetPlayerState(playerUid);
            if (p == null) return (0, 0);
            int atk = p.Field.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.BuffAmpAttack)
                    + p.MemberZone.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.BuffAmpAttack);
            int hp = p.Field.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.BuffAmpHealth)
                   + p.MemberZone.Where(e => e != null && e.Health > 0 && !e.IsDestroyed).Sum(e => e.BuffAmpHealth);
            return (atk, hp);
        }

        /// <summary>
        /// 특정 조건에 따른 현재 필드/손패 개수를 집계합니다.
        /// </summary>
        public int GetDynamicConditionCount(string playerUid, GameServer.Effects.Actions.DynamicConditionType conditionType, CardTribe? requiredTribe = null)
        {
            PlayerState? p = GetPlayerState(playerUid);
            PlayerState? op = GetPlayerState(playerUid, true);
            if (p == null) return 0;

            switch (conditionType)
            {
                case GameServer.Effects.Actions.DynamicConditionType.MyMinionCount:
                    return p.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed);

                case GameServer.Effects.Actions.DynamicConditionType.EnemyMinionCount:
                    return op?.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed) ?? 0;

                case GameServer.Effects.Actions.DynamicConditionType.AllMinionCount:
                    int myCount = p.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed);
                    int opCount = op?.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed) ?? 0;
                    return myCount + opCount;

                case GameServer.Effects.Actions.DynamicConditionType.MyHandCount:
                    return p.Hand.Count;

                case GameServer.Effects.Actions.DynamicConditionType.DamagedMinionCount:
                    return p.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed && e.Health < e.MaxHealth);

                case GameServer.Effects.Actions.DynamicConditionType.TribeMinionCount:
                    return p.Field.Count(e => e != null && e.Health > 0 && !e.IsDestroyed && e.Tribe == requiredTribe);

                default:
                    return 0;
            }
        }

        /// <summary>
        /// 양측 플레이어의 손패에 있는 동적 스탯/코스트(DynamicStatAction) 카드들을 실시간 재계산하여 동기화합니다.
        /// </summary>
        public async Task RefreshAllDynamicHandStatsAsync()
        {
            await RefreshDynamicHandStatsAsync(_playerA.Uid);
            await RefreshDynamicHandStatsAsync(_playerB.Uid);
        }

        /// <summary>
        /// 특정 플레이어의 손패에 있는 동적 스탯/코스트(DynamicStatAction) 및 주문/버프 증폭(SpellAmp/BuffAmp) 카드들을 실시간 재계산하여 동기화합니다.
        /// </summary>
        public async Task RefreshDynamicHandStatsAsync(string playerUid)
        {
            PlayerState? p = GetPlayerState(playerUid);
            if (p == null || p.Hand == null || p.Hand.Count == 0) return;

            List<GameCard> updatedCards = new List<GameCard>();

            // 1. 현재 플레이어의 주문 증폭 및 버프 증폭 수치 집계
            int spellAmp = GetSpellAmp(playerUid);
            int spellWeakness = GetSpellWeakness(playerUid);
            int netSpellAmp = spellAmp - spellWeakness;
            var (atkAmp, hpAmp) = GetBuffAmp(playerUid);

            foreach (var card in p.Hand)
            {
                if (card == null) continue;
                bool cardChanged = false;

                // [A] 손패 구역(Zone.Hand)에서 활성화되는 DynamicStatAction 효과 탐색 (기존 로직)
                if (card.NewEffects != null)
                {
                    foreach (var effect in card.NewEffects)
                    {
                        if (effect.ActiveZone != GameServer.Effects.Zone.Hand) continue;

                        foreach (var action in effect.Actions)
                        {
                            if (action is GameServer.Effects.Actions.DynamicStatAction dynamicAction)
                            {
                                int count = GetDynamicConditionCount(playerUid, dynamicAction.ConditionType, dynamicAction.RequiredTribe);

                                int prevCost = card.CurrentCost;
                                int prevAtk = card.CurrentAttack;
                                int prevHp = card.CurrentHealth;

                                int newCost = Math.Max(0, card.OriginalCost + (count * dynamicAction.CostPerUnit));
                                int newAtk = Math.Max(0, card.OriginalAttack + (count * dynamicAction.AttackPerUnit));
                                int newHp = Math.Max(1, card.OriginalHealth + (count * dynamicAction.HealthPerUnit));

                                if (prevCost != newCost || prevAtk != newAtk || prevHp != newHp)
                                {
                                    card.CurrentCost = newCost;
                                    card.CurrentAttack = newAtk;
                                    card.CurrentHealth = newHp;
                                    cardChanged = true;
                                }
                            }
                        }
                    }
                }

                // [B] 주문 카드의 실시간 주문 증폭(SpellAmp) 및 버프 증폭(BuffAmp) 재계산
                if (card.Type == CardType.주문 && card.NewEffects != null)
                {
                    // 1) DamageAction 검사 및 데미지 계수 갱신
                    var damageAction = card.NewEffects
                        .SelectMany(e => e.Actions)
                        .OfType<GameServer.Effects.Actions.DamageAction>()
                        .FirstOrDefault();

                    if (damageAction != null)
                    {
                        int baseDmg = damageAction.Amount + card.CustomValue;
                        int newDmg = Math.Max(0, baseDmg + netSpellAmp);
                        bool newSpellAmped = (netSpellAmp > 0);

                        if (card.BaseDamage != baseDmg || card.DynamicDamage != newDmg || card.IsSpellAmplified != newSpellAmped || card.SpellAmpBonus != netSpellAmp)
                        {
                            card.BaseDamage = baseDmg;
                            card.DynamicDamage = newDmg;
                            card.IsSpellAmplified = newSpellAmped;
                            card.SpellAmpBonus = netSpellAmp;
                            cardChanged = true;
                        }
                    }

                    // 2) BuffAction 검사 및 버프 수치 계수 갱신
                    var buffAction = card.NewEffects
                        .SelectMany(e => e.Actions)
                        .OfType<GameServer.Effects.Actions.BuffAction>()
                        .FirstOrDefault();

                    if (buffAction != null)
                    {
                        int baseBuffAtk = buffAction.AttackBuff;
                        int baseBuffHp = buffAction.HealthBuff;
                        int newBuffAtk = baseBuffAtk + (baseBuffAtk > 0 ? atkAmp : 0);
                        int newBuffHp = baseBuffHp + (baseBuffHp > 0 ? hpAmp : 0);
                        bool newBuffAmped = (atkAmp > 0 || hpAmp > 0);

                        if (card.BaseBuffAttack != baseBuffAtk || card.BaseBuffHealth != baseBuffHp ||
                            card.DynamicBuffAttack != newBuffAtk || card.DynamicBuffHealth != newBuffHp ||
                            card.IsBuffAmplified != newBuffAmped || card.BuffAmpAtkBonus != atkAmp || card.BuffAmpHpBonus != hpAmp)
                        {
                            card.BaseBuffAttack = baseBuffAtk;
                            card.BaseBuffHealth = baseBuffHp;
                            card.DynamicBuffAttack = newBuffAtk;
                            card.DynamicBuffHealth = newBuffHp;
                            card.IsBuffAmplified = newBuffAmped;
                            card.BuffAmpAtkBonus = atkAmp;
                            card.BuffAmpHpBonus = hpAmp;
                            cardChanged = true;
                        }
                    }

                    // 3) 전체 증폭 상태 결정 (오라 이펙트용)
                    bool overallAmped = card.IsSpellAmplified || card.IsBuffAmplified;
                    if (card.IsAmplified != overallAmped)
                    {
                        card.IsAmplified = overallAmped;
                        cardChanged = true;
                    }
                }

                if (cardChanged && !updatedCards.Contains(card))
                {
                    updatedCards.Add(card);
                }
            }

            if (updatedCards.Count > 0)
            {
                var updateMsg = new S_UpdateHandCards
                {
                    action = GameActionType.UPDATE_HAND_CARDS,
                    updatedCards = updatedCards.Select(c => c.ToCardInfo()).ToList()
                };

                LogDebug("DynamicStat", $"⚡ [동적 스탯/증폭 동기화] {playerUid}의 손패 {updatedCards.Count}장 갱신 (SpellAmp:{netSpellAmp}, BuffAmp:{atkAmp}/{hpAmp})");
                await _room.SendMessageToPlayerAsync(p.PlayerRef, JsonConvert.SerializeObject(updateMsg));
            }
        }

        #endregion

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

        public void AddLog(
            string actor, 
            string actionType, 
            string message, 
            object? details = null,
            string? sourceCardId = null,
            string? sourceCardName = null,
            int sourceEntityId = 0,
            int targetEntityId = 0,
            string? targetCardName = null,
            int value = 0,
            int value2 = 0,
            string? playerUid = null)
        {
            // 플레이어 UID를 닉네임으로 자동 치환
            if (_playerA != null && !string.IsNullOrEmpty(_playerA.Uid))
            {
                string nameA = GetPlayerDisplayName(_playerA.Uid);
                if (actor == _playerA.Uid) actor = nameA;
                if (!string.IsNullOrEmpty(nameA)) message = message.Replace(_playerA.Uid, nameA);
            }
            if (_playerB != null && !string.IsNullOrEmpty(_playerB.Uid))
            {
                string nameB = GetPlayerDisplayName(_playerB.Uid);
                if (actor == _playerB.Uid) actor = nameB;
                if (!string.IsNullOrEmpty(nameB)) message = message.Replace(_playerB.Uid, nameB);
            }

            // 행동 유발 플레이어 UID 자동 보정 (아군/적군 피아식별용)
            if (string.IsNullOrEmpty(playerUid))
            {
                if (_playerA != null && (actor == _playerA.Uid || actor == _playerA.PlayerRef?.Username))
                {
                    playerUid = _playerA.Uid;
                }
                else if (_playerB != null && (actor == _playerB.Uid || actor == _playerB.PlayerRef?.Username))
                {
                    playerUid = _playerB.Uid;
                }
                else if (sourceEntityId > 0 && _allEntities.TryGetValue(sourceEntityId, out var srcEntity) && !string.IsNullOrEmpty(srcEntity?.OwnerUid))
                {
                    playerUid = srcEntity.OwnerUid;
                }
                else
                {
                    playerUid = _currentTurnPlayerUid;
                }
            }

            var newLog = new GameLogEvent
            {
                Timestamp = DateTime.UtcNow,
                Actor = actor,
                ActionType = actionType,
                Message = message,
                Details = details
            };
        
            lock (_lock) 
            {
                if (_actionLogs.Count >= 1000)
                {
                    _actionLogs.RemoveAt(0);
                }
                _actionLogs.Add(newLog);
            }
        
            // 🚀 인게임 클라이언트에게 실시간 행동 로그(S_NewLogEvent) 패킷 브로드캐스팅 (하스스톤 좌측 히스토리 타일용)
            var logMsg = new S_NewLogEvent
            {
                actor = actor,
                playerUid = playerUid,
                actionType = actionType,
                message = message,
                sourceCardId = sourceCardId,
                sourceCardName = sourceCardName,
                sourceEntityId = sourceEntityId,
                targetEntityId = targetEntityId,
                targetCardName = targetCardName,
                value = value,
                value2 = value2,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            BroadcastLogEvent(logMsg);
        }

        public void BroadcastLogEvent(S_NewLogEvent logMsg)
        {
            try
            {
                if (_room != null && _playerA != null && _playerB != null)
                {
                    string json = JsonConvert.SerializeObject(logMsg);
                    _ = _room.SendMessageToPlayerAsync(_playerA.PlayerRef, json);
                    _ = _room.SendMessageToPlayerAsync(_playerB.PlayerRef, json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameState] ⚠️ BroadcastLogEvent 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 독립적인 효과 드로우(패시브 등) 발생 시 상대방에게 뽑힌 카드의 정보가 유출되지 않도록 마스킹하여 전송합니다.
        /// </summary>
        private void BroadcastDrawLog(string playerUid, string cardId)
        {
            if (_room == null || _playerA == null || _playerB == null) return;
            string displayName = GetPlayerDisplayName(playerUid);
            var cardData = ServerCardDatabase.Instance.GetCardData(cardId);
            string cardName = cardData?.Name ?? cardId;

            // 1. 본인에게는 자신이 뽑은 카드 정보 전송
            var selfLog = new S_NewLogEvent
            {
                actor = displayName,
                playerUid = playerUid,
                actionType = "DRAW",
                message = $"{displayName}이(가) [{cardName}] 카드를 뽑았습니다.",
                sourceCardId = cardId,
                sourceCardName = cardName,
                value = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // 2. 상대방에게는 덱에서 뽑힌 비공개 카드의 ID/이름/썸네일을 숨겨서 전송 (보안 보호)
            var oppLog = new S_NewLogEvent
            {
                actor = displayName,
                playerUid = playerUid,
                actionType = "DRAW",
                message = $"{displayName}이(가) 카드를 1장 뽑았습니다.",
                sourceCardId = null, // 상대에게 카드 썸네일 노출 차단
                sourceCardName = "카드",
                value = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            PlayerState selfPlayer = (_playerA.Uid == playerUid) ? _playerA : _playerB;
            PlayerState oppPlayer = (_playerA.Uid == playerUid) ? _playerB : _playerA;

            _ = _room.SendMessageToPlayerAsync(selfPlayer.PlayerRef, JsonConvert.SerializeObject(selfLog));
            _ = _room.SendMessageToPlayerAsync(oppPlayer.PlayerRef, JsonConvert.SerializeObject(oppLog));
        }

        /// <summary>
        /// ActionLogScope에 누적된 행동 및 하위 결과들을 단 1개의 종합 로그 이벤트로 취합하여 브로드캐스트합니다.
        /// </summary>
        private void EmitScopeLog(ActionLogScope scope)
        {
            if (scope == null) return;

            string baseText = "";
            if (scope.ActionType == "SUMMON")
            {
                baseText = $"{scope.ActorName}이(가) [{scope.SourceCardName}]을(를) {scope.PrimaryPosition + 1}번 슬롯에 소환";
            }
            else if (scope.ActionType == "EFFECT")
            {
                baseText = $"{scope.ActorName}이(가) [{scope.SourceCardName}] 효과를 사용";
            }
            else
            {
                baseText = $"{scope.ActorName}이(가) [{scope.SourceCardName}]을(를) 사용";
            }

            List<string> subPhrases = new List<string>();
            List<LogSubEvent> subEventsList = new List<LogSubEvent>();

            // 1. 피해 내역 (Damage)
            if (scope.DamageResults.Count > 0)
            {
                var dmgStrings = scope.DamageResults.Select(d => $"[{d.target}]에게 {d.damage}의 피해");
                subPhrases.Add(string.Join(", ", dmgStrings));
                foreach (var d in scope.DamageResults)
                {
                    subEventsList.Add(new LogSubEvent { type = "DAMAGE", targetName = d.target, value = d.damage });
                }
            }

            // 2. 치유 내역 (Heal)
            if (scope.HealResults.Count > 0)
            {
                var healStrings = scope.HealResults.Select(h => $"[{h.target}] 체력 {h.heal} 회복");
                subPhrases.Add(string.Join(", ", healStrings));
                foreach (var h in scope.HealResults)
                {
                    subEventsList.Add(new LogSubEvent { type = "HEAL", targetName = h.target, value = h.heal });
                }
            }

            // 3. 드로우 내역 (Draw) - 덱에서 뽑힌 카드는 N장으로만 요약
            if (scope.DrawnCardCount > 0)
            {
                subPhrases.Add($"카드 {scope.DrawnCardCount}장 드로우");
                subEventsList.Add(new LogSubEvent { type = "DRAW", targetName = scope.ActorName, value = scope.DrawnCardCount });
            }

            // 4. 추가 소환 (Token Minions)
            if (scope.SummonResults.Count > 0)
            {
                var summonStrings = scope.SummonResults.Select(s => $"[{s.minionName}]({s.slot + 1}번 슬롯) 소환");
                subPhrases.Add(string.Join(", ", summonStrings));
                foreach (var s in scope.SummonResults)
                {
                    subEventsList.Add(new LogSubEvent { type = "SUMMON", targetName = s.minionName, value = s.slot + 1 });
                }
            }

            // 5. 처치/사망 (Death)
            if (scope.DeathResults.Count > 0)
            {
                var deathStrings = scope.DeathResults.Select(d => $"[{d}] 파괴");
                subPhrases.Add(string.Join(", ", deathStrings));
                foreach (var d in scope.DeathResults)
                {
                    subEventsList.Add(new LogSubEvent { type = "DEATH", targetName = d, value = 0 });
                }
            }

            // 6. 기타 기록
            if (scope.OtherNotes.Count > 0)
            {
                subPhrases.AddRange(scope.OtherNotes);
            }

            string finalMessage = "";
            if (subPhrases.Count == 0)
            {
                finalMessage = baseText + "했습니다.";
            }
            else
            {
                finalMessage = baseText + "하여 " + string.Join(", ", subPhrases) + "했습니다.";
            }

            int primaryVal = 0;
            if (scope.DamageResults.Count > 0) primaryVal = scope.DamageResults.Sum(d => d.damage);
            else if (scope.HealResults.Count > 0) primaryVal = scope.HealResults.Sum(h => h.heal);
            else if (scope.DrawnCardCount > 0) primaryVal = scope.DrawnCardCount;
            else if (scope.ActionType == "SUMMON") primaryVal = scope.PrimaryPosition;

            var logMsg = new S_NewLogEvent
            {
                actor = scope.ActorName,
                playerUid = scope.PlayerUid,
                actionType = scope.ActionType,
                message = finalMessage,
                sourceCardId = scope.SourceCardId,
                sourceCardName = scope.SourceCardName,
                sourceEntityId = scope.SourceEntityId,
                targetEntityId = scope.PrimaryTargetEntityId,
                targetCardName = scope.PrimaryTargetName,
                value = primaryVal,
                value2 = 0,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                subEvents = subEventsList.Count > 0 ? subEventsList : null
            };

            // 내부 서버 저장소에도 기록
            var newLog = new GameLogEvent
            {
                Timestamp = DateTime.UtcNow,
                Actor = scope.ActorName,
                ActionType = scope.ActionType,
                Message = finalMessage,
                Details = finalMessage
            };
            lock (_lock)
            {
                if (_actionLogs.Count >= 1000) _actionLogs.RemoveAt(0);
                _actionLogs.Add(newLog);
            }

            BroadcastLogEvent(logMsg);
        }
    }
}