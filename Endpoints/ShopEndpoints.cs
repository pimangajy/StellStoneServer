using System;
using System.Linq;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using GameServer.Shop;
using GameServer.Services;

namespace GameServer.Endpoints
{
    public static class ShopEndpoints
    {
        public static void MapShopEndpoints(this WebApplication app)
        {
            // 상점 상품 목록 조회 API: GET /api/shop/products
            app.MapGet("/api/shop/products", async (
                int? category_id,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string? authorization) =>
            {
                UserData? userData = null;
                if (!string.IsNullOrEmpty(authorization))
                {
                    string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                    if (uid != null)
                    {
                        DocumentReference userRef = db.Collection("Users").Document(uid);
                        DocumentSnapshot snapshot = await userRef.GetSnapshotAsync();
                        if (snapshot.Exists)
                        {
                            userData = snapshot.ConvertTo<UserData>();
                        }
                    }
                }

                var products = ServerProductDatabase.Instance.GetProducts(category_id);
                var resultList = products.Select(p => new ShopProductDto(p, userData)).ToList();

                Console.WriteLine($"[Shop] 🛒 상품 목록 조회 요청: category_id={category_id} (반환: {resultList.Count}개, 유저: {userData?.Username ?? "비로그인"})");

                return Results.Ok(new
                {
                    status = "success",
                    message = "상품 목록 조회 성공",
                    data = resultList
                });
            });

            // 상점 상품 구매 API: POST /api/shop/purchase
            app.MapPost("/api/shop/purchase", async (
                PurchaseRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                var result = await ShopService.Instance.PurchaseProductAsync(uid, req, db);
                if (!result.Success)
                {
                    return Results.Json(new { status = "error", message = result.ErrorMessage }, statusCode: result.StatusCode);
                }

                return Results.Ok(result.Response);
            });

            // 카드팩 개봉 API: POST /api/inventory/open-pack
            app.MapPost("/api/inventory/open-pack", async (
                OpenPackRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                var result = await ShopService.Instance.OpenPackAsync(uid, req, db);
                if (!result.Success)
                {
                    return Results.Json(new { status = "error", message = result.ErrorMessage }, statusCode: result.StatusCode);
                }

                return Results.Ok(result.Response);
            });

            // 카드 분해 API: POST /api/cards/disenchant
            app.MapPost("/api/cards/disenchant", async (
                DisenchantCardRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                var result = await ShopService.Instance.DisenchantCardAsync(uid, req, db);
                if (!result.Success)
                {
                    return Results.Json(new { status = "error", message = result.ErrorMessage }, statusCode: result.StatusCode);
                }

                return Results.Ok(result.Response);
            });

            // 카드 제작/분해 가루 레이트 조회 API: GET /api/cards/dust-rates
            app.MapGet("/api/cards/dust-rates", () =>
            {
                var rates = new
                {
                    status = "success",
                    disenchant = ShopService.DisenchantDustRates.ToDictionary(k => k.Key.ToString(), v => v.Value),
                    craft = ShopService.CraftDustRates.ToDictionary(k => k.Key.ToString(), v => v.Value),
                    dropRates = ShopService.CardRarityDropRates.ToDictionary(k => k.Key.ToString(), v => v.Value)
                };
                return Results.Ok(rates);
            });

            // 카드 제작 API: POST /api/cards/craft
            app.MapPost("/api/cards/craft", async (
                CraftCardRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                var result = await ShopService.Instance.CraftCardAsync(uid, req, db);
                if (!result.Success)
                {
                    return Results.Json(new { status = "error", message = result.ErrorMessage }, statusCode: result.StatusCode);
                }

                return Results.Ok(result.Response);
            });
        }
    }
}
