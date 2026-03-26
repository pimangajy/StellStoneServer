using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using Newtonsoft.Json; // GameState에 메시지를 넘기기 위해 필요

namespace GameServer
{
    /// <summary>
    /// 실제 게임(2인 대전)이 일어나는 '방' 클래스입니다.
    /// 통신(WebSocket) 관리와 게임 상태(GameState) 중계를 담당합니다.
    /// </summary>
    public class GameRoom
    {
        // (신규) 디버그 모드 스위치 (이걸 true로 하면 혼자서 테스트 가능)
        private const bool ENABLE_SINGLE_PLAYER_DEBUG = true;
        
        // (수정) private _gameId -> public GameId 속성
        public string GameId { get; private set; }
        private readonly FirestoreDb _db;
        
        // (중요) 이 방의 '게임 두뇌'
        private GameState? _gameState; 

        // Key: Uid, Value: GamePlayer (플레이어 정보)
        private readonly ConcurrentDictionary<string, GamePlayer> _players = new ConcurrentDictionary<string, GamePlayer>();

        public GameRoom(string gameId, FirestoreDb db)
        {
            GameId = gameId;
            _db = db;
            Console.WriteLine($"[GameRoom {GameId}] 생성됨.");
        }

        /// <summary>
        /// 인증된 플레이어를 방에 추가하고 게임 시작을 시도합니다.
        /// </summary>
        public async Task AddPlayerAsync(GamePlayer player)
        {
            if (_players.Count >= 2)
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 방이 꽉 찼습니다. 플레이어 {player.Uid}를 받을 수 없습니다.");
                await player.WebSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Room is full", CancellationToken.None);
                return;
            }

            if (!_players.TryAdd(player.Uid, player))
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 플레이어 {player.Uid}가 이미 방에 존재합니다.");
                await player.WebSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Player already in room", CancellationToken.None);
                return;
            }

            Console.WriteLine($"[GameRoom {GameId}] 플레이어 {player.Uid} 입장. (현재 인원: {_players.Count}/2)");

            bool deckLoadedSuccessfully = false;

            // (중요) 플레이어의 덱을 서버가 직접 DB에서 로드 (치팅 방지)
            try
            {
                player.Deck = await LoadPlayerDeckAsync(player.Uid);
                if (player.Deck == null) throw new Exception("선택된 덱을 불러오는 데 실패했습니다.");
                deckLoadedSuccessfully = true;
                Console.WriteLine($"[GameRoom {GameId}] 플레이어 {player.Uid}의 덱({player.Deck.deckId}) 로드 성공.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 플레이어 {player.Uid} 덱 로드 실패: {ex.Message}");
                await SendMessageToPlayerAsync(player, $"{{\"action\":\"ERROR\", \"message\":\"Failed to load deck: {ex.Message}\"}}");
                await player.WebSocket.CloseAsync(WebSocketCloseStatus.InternalServerError, "Failed to load deck", CancellationToken.None);
                
                // (중요) 실패 시 방에서 즉시 제거
                _players.TryRemove(player.Uid, out _);
                return;
            }

            // === (신규) 싱글 플레이어 디버그 모드 로직 === (싱글 테스트) 
            if (ENABLE_SINGLE_PLAYER_DEBUG && _players.Count == 1 && deckLoadedSuccessfully)
            {
                Console.WriteLine($"[GameRoom] 디버그 모드: Bot 플레이어를 생성합니다.");
                
                // 1. Bot 생성 (WebSocket 없음)
                var botPlayer = new GamePlayer("BOT_UID", null!, "BOT_CONNECTION");
                botPlayer.IsBot = true;
                
                // 2. Bot에게 플레이어와 똑같은 덱 복사해주기
                // (실제로는 Deep Copy가 좋지만 테스트니 얕은 복사도 OK)
                botPlayer.Deck = new DeckData 
                { 
                    deckId = "BOT_DECK", 
                    deckName = "Bot Deck", 
                    cardIds = new List<string>(player.Deck.cardIds!) // 카드 목록 복사
                };

                // 3. 방에 Bot 추가
                _players.TryAdd(botPlayer.Uid, botPlayer);
                Console.WriteLine($"[GameRoom] Bot 입장 완료. (현재 인원: 2/2)");
            }
 
            // --- (핵심 수정) 게임 시작 로직 ---
            // 덱 로딩에 성공한 플레이어만 게임 시작 로직을 체크합니다.
            if (deckLoadedSuccessfully)
            {
                // 이 방에 2명이 있고,
                if (_players.Count == 2)
                {
                    // (이중 안전장치) 그 2명의 덱이 *모두* 로드되었는지(null이 아닌지) 확인합니다.
                    var allPlayers = _players.Values.ToList();
                    if (allPlayers.All(p => p.Deck != null))
                    {
                        Console.WriteLine($"[GameRoom {GameId}] 2명의 덱 로딩이 모두 완료되었습니다. (Triggered by {player.Uid})");
                        // (Fire-and-forget)
                        // 두 스레드 중 이 로직에 *마지막*으로 도달한 스레드가
                        // StartGameAsync를 호출하고, 자신은 ListenForMessagesAsync로 넘어갑니다.
                        _ = StartGameAsync();
                    }
                    // (만약 한쪽 덱이 아직 로딩 중이라면, 
                    // 덱 로딩을 마친 다른 쪽 스레드가 이 로직을 실행할 것입니다.)
                }
            }
            // --- (핵심 수정 끝) ---

          // (중요) Bot은 메시지를 들을 수 없으므로, 진짜 사람일 때만 Listen을 호출
            if (!player.IsBot)
            {
                await ListenForMessagesAsync(player);
            }
        }

        /// <summary>
        /// (권위) Firestore DB에서 플레이어의 대표 덱 정보를 불러옵니다.
        /// </summary>
        private async Task<DeckData?> LoadPlayerDeckAsync(string uid)
        {
            DocumentReference userDocRef = _db.Collection("Users").Document(uid);
            DocumentSnapshot userSnapshot = await userDocRef.GetSnapshotAsync();
            if (!userSnapshot.Exists)
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 유저 문서를 찾을 수 없습니다: {uid}");
                return null;
            }

            UserData? userData = userSnapshot.ConvertTo<UserData>();
            
            if (string.IsNullOrEmpty(userData?.SelectDeck))
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 유저 {uid}에게 'SelectDeck'이 설정되지 않았습니다.");
                return null;
            }

            DocumentReference deckDocRef = userDocRef.Collection("Decks").Document(userData.SelectDeck);
            DocumentSnapshot deckSnapshot = await deckDocRef.GetSnapshotAsync();
            if (!deckSnapshot.Exists)
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 덱 문서를 찾을 수 없습니다: {userData.SelectDeck}");
                return null;
            }

            DeckData deckData = deckSnapshot.ConvertTo<DeckData>();
            deckData.deckId = deckSnapshot.Id; // ID 수동 할당
            return deckData;
        }

        /// <summary>
        /// 2명이 모였을 때 게임 시작 로직을 트리거합니다.
        /// </summary>
        private async Task StartGameAsync()
        {
            Console.WriteLine($"[GameRoom {GameId}] ⚔️ 2명 모두 입장. 게임을 시작합니다.");
            
            List<GamePlayer> playerList = _players.Values.ToList();
            GamePlayer playerA = playerList[0];
            GamePlayer playerB = playerList[1];

            // (핵심) 게임 엔진(두뇌) 생성
            _gameState = new GameState(this, playerA, playerB);

            // (핵심) 멀리건 시퀀스 시작
            // GameState가 덱 섞기, 5장 뽑기, 메시지 전송을 모두 처리
            await _gameState.StartMulliganAsync();
        }

        /// <summary>
        /// 플레이어 1명의 WebSocket 메시지를 지속적으로 수신 대기합니다.
        /// </summary>
        private async Task ListenForMessagesAsync(GamePlayer player)
        {
            var buffer = new byte[1024 * 4];
            
            try
            {
                WebSocketReceiveResult receiveResult = await player.WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), CancellationToken.None);

                // 클라이언트가 연결을 닫지 않는 한 계속 수신
                while (!receiveResult.CloseStatus.HasValue)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                    Console.WriteLine($"[GameRoom {GameId}] 📩 메시지 수신 (from {player.Uid}): {message}");
                    
                    // 수신된 메시지를 게임 로직(GameState)에 따라 처리
                    if (_gameState != null)
                    {
                        // GameRoom은 메시지를 GameState에 넘기기만 함
                        await _gameState.HandlePlayerActionAsync(player.Uid, message);
                    }
                    else
                    {
                        Console.WriteLine($"[GameRoom {GameId}] _gameStaterk 가 없음");
                    }
                    
                    // 다음 메시지를 기다림
                    receiveResult = await player.WebSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), CancellationToken.None);
                }
                // 클라이언트가 정상 종료 요청
                Console.WriteLine($"[GameRoom {GameId}] 🔌 플레이어 {player.Uid}가 연결을 정상 종료했습니다.");
            }
            catch (WebSocketException)
            {
                // 클라이언트가 비정상 종료 (앱 강제종료 등)
                Console.WriteLine($"[GameRoom {GameId}] 🔌 플레이어 {player.Uid}의 연결이 비정상 종료되었습니다. (WebSocketException)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GameRoom {GameId}] ❌ 플레이어 {player.Uid} 메시지 루프 오류: {ex.Message}");
            }
            finally
            {
                // (중요) 이 루프가 끝나면(즉, 연결이 끊기면)
                // GameSocketHandler의 'finally' 블록이 실행되어
                // OnPlayerDisconnectedAsync가 호출될 것입니다.
            }
        }

        /// <summary>
        /// 플레이어 연결 종료 시 GameRoomManager가 호출합니다.
        /// </summary>
        public async Task RemovePlayerAsync(string uid)
        {
            // 스레드 안전하게 플레이어 제거
            _players.TryRemove(uid, out GamePlayer? disconnectedPlayer);
            Console.WriteLine($"[GameRoom {GameId}] 플레이어 {uid}가 방에서 제거되었습니다. (남은 인원: {_players.Count}/2)");

            // (중요) 남아있는 상대방에게 "상대 연결 끊김"을 알림
            if (disconnectedPlayer != null && _players.Count == 1)
            {
                GamePlayer remainingPlayer = _players.Values.First();
                
                // (게임 규칙) 상대가 나가면 즉시 승리
                var gameOverMessage = new S_GameOver
                {
                    action = "GAME_OVER",
                    winnerUid = remainingPlayer.Uid,
                    reason = "OPPONENT_DISCONNECTED"
                };
                await SendMessageToPlayerAsync(remainingPlayer, JsonConvert.SerializeObject(gameOverMessage));
            }
            
            // TODO: 게임이 진행 중이었다면 GameState도 정리
            if (_players.Count < 2)
            {
                _gameState = null; // 게임 상태 파기
            }
        }

        /// <summary>
        /// 특정 플레이어에게 WebSocket 메시지를 전송하는 헬퍼 함수
        /// (수정) GameState가 호출할 수 있도록 public으로 변경
        /// </summary>
        public async Task SendMessageToPlayerAsync(GamePlayer player, string message)
        {
            // (신규) Bot이거나 WebSocket이 없으면 전송하지 않고 리턴 (싱글 테스트)
            if (player.IsBot || player.WebSocket == null)
            {
                // (로그가 너무 많으면 주석 처리)
                // Console.WriteLine($"[GameRoom] Bot({player.Uid})에게 메시지 전송 생략.");
                return; 
            }
            
            if (player.WebSocket.State == WebSocketState.Open)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                try
                {
                    await player.WebSocket.SendAsync(
                        new ArraySegment<byte>(bytes, 0, bytes.Length),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameRoom {GameId}] ❌ {player.Uid}에게 메시지 전송 실패: {ex.Message}");
                }
            }else
            {
                // (신규 디버그 로그) 전송이 '조용히' 실패하는 이유 확인
                Console.WriteLine($"[GameRoom {GameId}] ⚠️ {player.Uid}에게 전송 시도했으나, WebSocket 상태가 Open이 아님: {player.WebSocket.State}");
            }
        }

        /// <summary>
        /// GameRoomManager가 방을 정리할지 판단하기 위해 호출합니다.
        /// </summary>
        public bool IsEmpty()
        {
            return _players.IsEmpty;
        }

        // 대시보드 전용
        public GameState? GetCurrentGameState()
        {
            return _gameState;
        }
    }
}