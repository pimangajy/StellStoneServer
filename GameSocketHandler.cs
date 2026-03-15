using System.Net.WebSockets;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Http; // HttpContext, IQueryCollection
using System.Text;
using System.Threading;
using System;
using System.Threading.Tasks;
using System.Linq; // FirstOrDefault() 사용

namespace GameServer
{
    public static class GameSocketHandler
    {
        public static async Task HandleConnectionAsync(HttpContext context, WebSocket webSocket, FirestoreDb db)
        {
            var connectionId = context.Connection.Id;
            string? uid = null; // 인증된 유저의 UID
            string? gameId = null; // 참가할 게임 ID

            try
            {
                // ==================================================================
                // 1. (신규) WebSocket 접속 인증
                // ==================================================================
                // 유니티 클라이언트는 'ws://.../ws/game?token=...&gameId=...'로 접속해야 합니다.
                IQueryCollection query = context.Request.Query;
                string? token = query["token"].FirstOrDefault();
                gameId = query["gameId"].FirstOrDefault();

                // 1-1. 필수 파라미터 검사
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(gameId))
                {
                    Console.WriteLine($"[WS {connectionId}] ❌ 인증 실패: 'token' 또는 'gameId'가 URL 쿼리에 없습니다.");
                    // (중요) 인증 실패 시, 클라이언트에게 사유를 알리고 연결을 즉시 종료합니다.
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation, // 1008: 정책 위반
                        "Missing token or gameId query parameter",
                        CancellationToken.None);
                    return; // 핸들러 종료
                }

                // 1-2. 토큰 검증
                // Program.cs에 새로 추가한 public 헬퍼 함수를 호출합니다.
                uid = await Program.VerifyTokenStringAsync(token);

                // 1-3. 토큰 유효성 검사
                if (string.IsNullOrEmpty(uid))
                {
                    Console.WriteLine($"[WS {connectionId}] ❌ 인증 실패: 전송된 토큰이 유효하지 않습니다.");
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation,
                        "Invalid authentication token",
                        CancellationToken.None);
                    return; // 핸들러 종료
                }

                // ==================================================================
                // 2. 인증 성공 -> GameRoomManager에 위임 (수정됨)
                // ==================================================================
                Console.WriteLine($"✅ [WS {connectionId}] WebSocket 인증 성공! UID: {uid}, GameID: {gameId}");
                Console.WriteLine($"[WS {connectionId}] GameRoomManager에게 연결을 위임합니다...");

                // (핵심 수정)
                // GameRoomManager에게 연결 처리를 위임합니다.
                // 'await'로 인해, 이 플레이어의 연결이 완전히 끊길 때까지(GameRoom의 루프가 끝날 때까지)
                // 이 HandleConnectionAsync 메서드는 여기서 '대기' 상태가 됩니다.
                await GameRoomManager.JoinRoomAsync(gameId, uid, webSocket, context, db);
                
                // --- 플레이어 연결이 끊기면 await가 풀리고 이 아래로 코드가 진행됩니다 ---

                Console.WriteLine($"[WS {connectionId}] 🔌 플레이어 {uid}의 GameRoom 세션이 종료되었습니다.");


                // ==================================================================
                // 3. (제거됨) 메시지 수신 루프
                // ==================================================================
                // 이 핸들러는 더 이상 메시지 루프를 직접 처리하지 않습니다.
                // 모든 메시지 루프는 GameRoom.cs의 ListenForMessagesAsync에서 개별 처리됩니다.
            }
            catch (WebSocketException ex)
            {
                // 클라이언트가 비정상적으로 연결을 끊었을 때
                Console.WriteLine($"[WS {connectionId}] WebSocket 연결 비정상 종료 (UID: {uid}): {ex.Message}");
            }
            catch (Exception ex)
            {
                // 핸들러 내부의 기타 모든 오류 (예: 인증 중단 등)
                Console.WriteLine($"[WS {connectionId}] ❌ WebSocket 핸들러 오류 (UID: {uid}): {ex.Message}");
                
                if (webSocket.State == WebSocketState.Open)
                {
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.InternalServerError,
                        "Server error processing message",
                        CancellationToken.None);
                }
            }
            finally
            {
                // ==================================================================
                // 5. 연결 종료 시 최종 정리 (수정)
                // ==================================================================
                
                // (신규)
                // (중요) 이 핸들러가 종료되면(즉, 연결이 끊기면),
                // GameRoomManager에게 알려 방을 정리하도록 합니다.
                if (!string.IsNullOrEmpty(gameId) && !string.IsNullOrEmpty(uid))
                {
                    // (주의) 이 함수는 async이지만, finally 블록에서는 await를 권장하지 않습니다.
                    // 연결이 이미 닫히는 중이므로, 정리 작업만 '시작'시킵니다.
                    _ = GameRoomManager.OnPlayerDisconnectedAsync(gameId, uid);
                }
                
                Console.WriteLine($"[WS {connectionId}] 연결 리소스 정리 완료 (UID: {uid}).");
            }
        }
    }
}