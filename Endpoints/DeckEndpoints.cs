using System;
using System.Collections.Generic;
using System.Linq;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using GameServer.Services;

namespace GameServer.Endpoints
{
    public static class DeckEndpoints
    {
        public static void MapDeckEndpoints(this WebApplication app)
        {
            // 2. 덱 목록 불러오기 API: GET /api/decks 
            app.MapGet("/api/decks", async (
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                Console.WriteLine("✅ 덱 목록 요청 수신");
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                try
                {
                    CollectionReference decksRef = db.Collection("Users").Document(uid).Collection("Decks");
                    QuerySnapshot snapshot = await decksRef.GetSnapshotAsync();

                    List<DeckData> allDecks = new List<DeckData>();
                    foreach (var doc in snapshot.Documents)
                    {
                        DeckData deck = doc.ConvertTo<DeckData>();
                        deck.deckId = doc.Id;
                        allDecks.Add(deck);
                    }

                    Console.WriteLine($"✅ 덱 목록 {allDecks.Count}개 반환 - 유저: {uid}");
                    return Results.Ok(new { decks = allDecks });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 목록 불러오기 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 3. 덱 생성 API: POST /api/decks/create 
            app.MapPost("/api/decks/create", async (
                CreateDeckRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (req.className == null)
                {
                    return Results.BadRequest(new { status = "error", message = "직업(className) 정보가 없습니다." });
                }

                try
                {
                    CollectionReference decksRef = db.Collection("Users").Document(uid).Collection("Decks");

                    const string defaultDeckNamePrefix = "새로운 덱 ";
                    QuerySnapshot snapshot = await decksRef.GetSnapshotAsync();

                    var existingNumbers = snapshot.Documents
                        .Select(doc => doc.ConvertTo<DeckData>().deckName)
                        .Where(name => name != null && name.StartsWith(defaultDeckNamePrefix))
                        .Select(name =>
                        {
                            string numberPart = name!.Substring(defaultDeckNamePrefix.Length);
                            int.TryParse(numberPart, out int number);
                            return number;
                        })
                        .Where(number => number > 0)
                        .ToHashSet();

                    int newDeckNumber = 1;
                    while (existingNumbers.Contains(newDeckNumber))
                    {
                        newDeckNumber++;
                    }

                    string deckName = $"{defaultDeckNamePrefix}{newDeckNumber}";
                    DeckData newDeck = new DeckData(deckName, req.className);

                    DocumentReference addedDocRef = await decksRef.AddAsync(newDeck);
                    Console.WriteLine($"✅ 덱 생성 성공 (ID: {addedDocRef.Id}) - 유저: {uid}");

                    newDeck.deckId = addedDocRef.Id;
                    return Results.Ok(newDeck);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 생성 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 4. 덱 업데이트 API: PUT /api/decks/update/{deckId} 
            app.MapPut("/api/decks/update/{deckId}", async (
                string deckId,
                DeckData updatedDeck,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                // 서버 측 검증 (Anti-Cheat)
                if (string.IsNullOrWhiteSpace(updatedDeck.deckName))
                {
                    return Results.BadRequest(new { status = "error", message = "덱 이름은 비워둘 수 없습니다." });
                }
                if (updatedDeck.cardIds != null && updatedDeck.cardIds.Count > 30)
                {
                    Console.WriteLine($"덱 크기 초과 ({updatedDeck.cardIds.Count}장)");
                    return Results.BadRequest(new { status = "error", message = "덱은 30장을 초과할 수 없습니다." });
                }
                if (updatedDeck.sideDeckCardIds != null && updatedDeck.sideDeckCardIds.Count > 5) 
                {
                    return Results.BadRequest(new { status = "error", message = "사이드 덱은 5장을 초과할 수 없습니다." });
                }
                if (updatedDeck.sideDeckFirstTurnCardIds != null && updatedDeck.sideDeckFirstTurnCardIds.Count > 3) 
                {
                    return Results.BadRequest(new { status = "error", message = "선공 사이드 덱은 3장을 초과할 수 없습니다." });
                }

                // 동일 카드 최대 2장 제한 검증 (메인 덱 + 사이드 덱 통합)
                var combinedCards = new List<string>();
                if (updatedDeck.cardIds != null) combinedCards.AddRange(updatedDeck.cardIds);
                if (updatedDeck.sideDeckCardIds != null) combinedCards.AddRange(updatedDeck.sideDeckCardIds);

                var duplicateExceeded = combinedCards
                    .GroupBy(c => c)
                    .FirstOrDefault(g => g.Count() > 2);

                if (duplicateExceeded != null)
                {
                    return Results.BadRequest(new { status = "error", message = $"동일한 카드는 메인 덱과 사이드 덱을 합쳐 최대 2장까지만 넣을 수 있습니다." });
                }

                // 리더 스킨 유효성 및 직업/공용 호환성 검증
                if (!string.IsNullOrEmpty(updatedDeck.leaderSkinId))
                {
                    string equippedSkin = updatedDeck.leaderSkinId.Trim();
                    bool isDefaultSkin = equippedSkin.EndsWith("_Default", StringComparison.OrdinalIgnoreCase) ||
                                         equippedSkin.EndsWith("0001", StringComparison.OrdinalIgnoreCase) ||
                                         equippedSkin.Equals($"Skin_{updatedDeck.deckClass}_Default", StringComparison.OrdinalIgnoreCase);

                    if (!isDefaultSkin)
                    {
                        DocumentReference userDocRef = db.Collection("Users").Document(uid);
                        DocumentSnapshot userSnap = await userDocRef.GetSnapshotAsync();
                        if (userSnap.Exists)
                        {
                            UserData userData = userSnap.ConvertTo<UserData>();
                            if (!userData.OwnedSkins.Contains(equippedSkin))
                            {
                                return Results.BadRequest(new { status = "error", message = "보유하지 않은 리더 스킨입니다." });
                            }

                            var skinProduct = ServerProductDatabase.Instance.GetProduct(equippedSkin);
                            if (skinProduct != null)
                            {
                                var allowedClasses = skinProduct.GetTargetClasses();
                                if (allowedClasses.Count > 0 && !string.IsNullOrEmpty(updatedDeck.deckClass) &&
                                    !allowedClasses.Any(c => c.ToString().Equals(updatedDeck.deckClass, StringComparison.OrdinalIgnoreCase)))
                                {
                                    return Results.BadRequest(new { status = "error", message = $"해당 스킨은 {updatedDeck.deckClass} 직업 덱에 장착할 수 없습니다." });
                                }
                            }
                        }
                    }
                }
                else
                {
                    updatedDeck.leaderSkinId = updatedDeck.GetEquippedSkinId();
                }

                try
                {
                    DocumentReference deckRef = db.Collection("Users").Document(uid).Collection("Decks").Document(deckId);
                    await deckRef.SetAsync(updatedDeck, SetOptions.Overwrite);

                    Console.WriteLine($"✅ 덱 업데이트 성공 (ID: {deckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "덱이 저장되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 업데이트 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 5. 덱 삭제 API: DELETE /api/decks/delete/{deckId} 
            app.MapDelete("/api/decks/delete/{deckId}", async (
                string deckId,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (string.IsNullOrEmpty(deckId))
                {
                    return Results.BadRequest(new { status = "error", message = "덱 ID가 필요합니다." });
                }

                try
                {
                    DocumentReference deckRef = db.Collection("Users").Document(uid).Collection("Decks").Document(deckId);
                    await deckRef.DeleteAsync();

                    Console.WriteLine($"✅ 덱 삭제 성공 (ID: {deckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "덱이 삭제되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 덱 삭제 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });
        }
    }
}
