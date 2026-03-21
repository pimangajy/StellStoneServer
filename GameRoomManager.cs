using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http; // HttpContext
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.Collections.Concurrent; // (중요) 스레드 안전 딕셔너리
using System;

namespace GameServer
{
    /// <summary>
    /// 모든 'GameRoom' 인스턴스를 관리하는 정적(Static) 클래스입니다.
    /// (스레드 안전하게 구현)
    /// </summary>
    public static class GameRoomManager
    {
        // (중요) Key: gameId, Value: GameRoom 인스턴스
        // 여러 플레이어가 동시에 접속(방 생성)하는 것을 처리하기 위해 스레드에 안전한 ConcurrentDictionary를 사용합니다.
        private static readonly ConcurrentDictionary<string, GameRoom> _rooms = new ConcurrentDictionary<string, GameRoom>();

        /// <summary>
        /// GameSocketHandler로부터 인증된 플레이어를 받아
        /// 적절한 GameRoom에 입장시키거나, 새 GameRoom을 생성합니다.
        /// </summary>
        public static async Task JoinRoomAsync(string gameId, string uid, WebSocket webSocket, HttpContext context, FirestoreDb db)
        {
            // (핵심 로직)
            // 1. gameId로 방을 찾습니다.
            // 2. 만약 방이 없으면, new GameRoom()을 실행하여 새 방을 '추가'합니다.
            // 3. 만약 방이 있으면, 기존 방을 '반환'합니다.
            // (GetOrAdd는 이 모든 과정을 스레드 안전하게 한 번에 처리해줍니다.)
            GameRoom room = _rooms.GetOrAdd(gameId, (id) => 
            {
                Console.WriteLine($"[GameRoomManager] ℹ️ 새 GameRoom 생성 (GameID: {id})");
                return new GameRoom(id, db); // FirestoreDb 인스턴스 전달
            });

            // 플레이어 객체 생성
            var player = new GamePlayer(uid, webSocket, context.Connection.Id);

            // (핵심 위임)
            // 생성(또는 검색)된 GameRoom에 플레이어 추가를 위임합니다.
            // 'await'를 사용함으로써, 이 플레이어의 연결이 완전히 끝날 때까지(ListenForMessagesAsync 루프가 끝날 때까지)
            // GameSocketHandler의 HandleConnectionAsync 메서드는 여기서 대기하게 됩니다.
            await room.AddPlayerAsync(player);
            
            // --- 이 라인은 플레이어의 연결이 끊긴 *후*에 실행됩니다 ---
        }

        /// <summary>
        /// 플레이어의 연결이 끊겼을 때 GameSocketHandler의 'finally' 블록에서 호출됩니다.
        /// </summary>
        public static async Task OnPlayerDisconnectedAsync(string gameId, string uid)
        {
            Console.WriteLine($"[GameRoomManager] 🔌 플레이어 {uid} 연결 종료 처리 (GameID: {gameId})");

            if (_rooms.TryGetValue(gameId, out GameRoom? room))
            {
                // 1. 방에 남아있는 상대방에게 "상대 나감"을 알립니다.
                await room.RemovePlayerAsync(uid);

                // 2. (정리) 방이 이제 비어있다면, 매니저의 딕셔너리에서 방을 제거합니다.
                if (room.IsEmpty())
                {
                    if (_rooms.TryRemove(gameId, out _))
                    {
                        Console.WriteLine($"[GameRoomManager] 🗑️ 방이 비어있으므로 GameRoom({gameId})을 제거합니다.");
                    }
                }
            }
            else
            {
                Console.WriteLine($"[GameRoomManager] ⚠️ {gameId} 방을 찾을 수 없어 연결 종료를 처리할 수 없습니다.");
            }
        }

        // 대시보드 전용 스크립트
        public static List<string> GetActiveRoomIds()
        {
            // ConcurrentDictionary의 Key(GameId)들만 추출하여 반환
         return _rooms.Keys.ToList();
        }

        public static GameRoom? GetRoom(string gameId)
        {
         _rooms.TryGetValue(gameId, out GameRoom? room);
         return room;
        }
    }
}