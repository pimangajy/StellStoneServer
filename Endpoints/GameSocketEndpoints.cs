using System;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GameServer.Endpoints
{
    public static class GameSocketEndpoints
    {
        public static void MapGameSocketEndpoints(this WebApplication app)
        {
            // 실시간 대전 (WebSocket) 엔드포인트: GET /ws/game
            app.MapGet("/ws/game", async (
                HttpContext context, 
                FirestoreDb db) =>
            {
                if (context.WebSockets.IsWebSocketRequest)
                {
                    Console.WriteLine($"WebSocket 연결 요청 수신: {context.Connection.Id}");
                    try
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        Console.WriteLine($"✅ WebSocket 연결 성공: {context.Connection.Id}");
                        await GameSocketHandler.HandleConnectionAsync(context, webSocket, db);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ WebSocket 연결 수락/처리 중 최상위 오류: {ex.Message}");
                        if (!context.Response.HasStarted)
                        {
                            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        }
                    }
                }
                else
                {
                    Console.WriteLine("❌ 비-WebSocket 요청이 /ws/game으로 수신됨");
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                }
            });
        }
    }
}
