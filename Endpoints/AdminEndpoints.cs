using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace GameServer.Endpoints
{
    public static class AdminEndpoints
    {
        public static void MapAdminEndpoints(this WebApplication app)
        {
            // 1. 활성화된 방 목록 API
            app.MapGet("/api/admin/rooms", () =>
            {
                var activeRooms = GameRoomManager.GetActiveRoomIds();
                return Results.Ok(new { status = "success", count = activeRooms.Count, rooms = activeRooms });
            });

            // 2. 특정 방의 상세 상태 API
            app.MapGet("/api/admin/rooms/{gameId}", (string gameId) =>
            {
                var room = GameRoomManager.GetRoom(gameId);
                if (room == null) return Results.NotFound(new { status = "error", message = "방을 찾을 수 없습니다." });

                var gameState = room.GetCurrentGameState();
                if (gameState == null) return Results.Ok(new { status = "success", state = "대기 중 (게임 미시작)" });

                var snapshot = gameState.GetSnapshot();
                return Results.Ok(new { status = "success", data = snapshot });
            });

            // 3. 서버 설정 조회 API
            app.MapGet("/api/admin/settings", () =>
            {
                return Results.Ok(new
                {
                    status = "success",
                    enableSinglePlayerBot = GameServerSettings.EnableSinglePlayerBot
                });
            });

            // 4. 싱글 플레이 봇 모드 실시간 토글 API (서버 무중단 전환)
            app.MapPost("/api/admin/settings/toggle-bot", () =>
            {
                GameServerSettings.EnableSinglePlayerBot = !GameServerSettings.EnableSinglePlayerBot;
                Console.WriteLine($"[Admin] ⚙️ 봇 모드 실시간 전환: {GameServerSettings.EnableSinglePlayerBot}");
                return Results.Ok(new
                {
                    status = "success",
                    message = $"봇 모드가 {(GameServerSettings.EnableSinglePlayerBot ? "활성화" : "비활성화")}되었습니다.",
                    enableSinglePlayerBot = GameServerSettings.EnableSinglePlayerBot
                });
            });

            // 5. 싱글 플레이 봇 모드 명시적 설정 API
            app.MapPost("/api/admin/settings/bot", (bool enable) =>
            {
                GameServerSettings.EnableSinglePlayerBot = enable;
                Console.WriteLine($"[Admin] ⚙️ 봇 모드 설정 변경: {enable}");
                return Results.Ok(new
                {
                    status = "success",
                    enableSinglePlayerBot = GameServerSettings.EnableSinglePlayerBot
                });
            });
        }
    }
}
