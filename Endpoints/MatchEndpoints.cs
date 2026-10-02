using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using GameServer.Models;
using GameServer.Services;

namespace GameServer.Endpoints
{
    public static class MatchEndpoints
    {
        public static void MapMatchEndpoints(this WebApplication app)
        {
            // 1. 매칭 요청 (POST /api/match/request)
            app.MapPost("/api/match/request", async (
                HttpContext context,
                MatchRequestDto requestDto,
                ServerMatchmakingService matchmakingService) =>
            {
                string? authHeader = context.Request.Headers["Authorization"];
                string? uid = await Program.VerifyTokenAsync(authHeader);

                if (string.IsNullOrEmpty(uid))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    MatchResponseDto response = await matchmakingService.RequestMatchAsync(uid, requestDto?.DeckId);
                    if (response.Status == "error")
                    {
                        return Results.BadRequest(response);
                    }
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatchEndpoint] ❌ 매칭 요청 처리 실패 ({uid}): {ex.Message}");
                    return Results.Problem("매칭 처리 중 서버 오류가 발생했습니다.");
                }
            });

            // 2. 매칭 상태 조회 (GET /api/match/status)
            app.MapGet("/api/match/status", async (
                HttpContext context,
                ServerMatchmakingService matchmakingService) =>
            {
                string? authHeader = context.Request.Headers["Authorization"];
                string? uid = await Program.VerifyTokenAsync(authHeader);

                if (string.IsNullOrEmpty(uid))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    MatchResponseDto response = await matchmakingService.GetMatchStatusAsync(uid);
                    return Results.Ok(response);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatchEndpoint] ❌ 매칭 상태 조회 실패 ({uid}): {ex.Message}");
                    return Results.Problem("매칭 상태 조회 중 오류가 발생했습니다.");
                }
            });

            // 3. 매칭 취소 (POST /api/match/cancel)
            app.MapPost("/api/match/cancel", async (
                HttpContext context,
                ServerMatchmakingService matchmakingService) =>
            {
                string? authHeader = context.Request.Headers["Authorization"];
                string? uid = await Program.VerifyTokenAsync(authHeader);

                if (string.IsNullOrEmpty(uid))
                {
                    return Results.Unauthorized();
                }

                try
                {
                    bool success = await matchmakingService.CancelMatchAsync(uid);
                    return Results.Ok(new { success, message = "매칭이 취소되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MatchEndpoint] ❌ 매칭 취소 실패 ({uid}): {ex.Message}");
                    return Results.Problem("매칭 취소 중 오류가 발생했습니다.");
                }
            });
        }
    }
}
