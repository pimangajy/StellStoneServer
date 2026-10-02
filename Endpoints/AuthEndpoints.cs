using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using GameServer.Services;
using GameServer.Models;

namespace GameServer.Endpoints
{
    public static class AuthEndpoints
    {
        public static void MapAuthEndpoints(this WebApplication app)
        {
            // 1. 회원가입 API: POST /api/auth/signup
            app.MapPost("/api/auth/signup", async (SignupRequest req, FirestoreDb db) =>
            {
                Console.WriteLine($"📧 회원가입 요청 수신: {req.email}");
                if (string.IsNullOrWhiteSpace(req.email))
                {
                    return Results.BadRequest(new { status = "error", message = "아이디(이메일)를 입력해주세요." });
                }
                if (!req.email.Contains("@") || !req.email.Contains("."))
                {
                    return Results.BadRequest(new { status = "error", message = "아이디(이메일)가 잘못되었습니다." });
                }
                if (string.IsNullOrWhiteSpace(req.password))
                {
                    return Results.BadRequest(new { status = "error", message = "비밀번호를 입력해주세요." });
                }
                if (req.password.Length < 6)
                {
                    return Results.BadRequest(new { status = "error", message = "비밀번호는 6자리 이상이어야 합니다." });
                }
                if (string.IsNullOrWhiteSpace(req.username))
                {
                    return Results.BadRequest(new { status = "error", message = "닉네임을 입력해주세요." });
                }

                try
                {
                    // 1. Firebase Authentication에 사용자 생성
                    UserRecordArgs args = new UserRecordArgs()
                    {
                        Email = req.email,
                        Password = req.password,
                        DisplayName = req.username,
                        Disabled = false,
                    };
                    UserRecord userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
                    Console.WriteLine($"✅ Firebase Auth에 사용자 생성 성공: {userRecord.Uid} ({userRecord.Email})");

                    // 2. Firestore에 추가 사용자 정보 저장 (UID를 문서 ID로 사용)
                    DocumentReference docRef = db.Collection("Users").Document(userRecord.Uid);
                    UserData userData = new UserData
                    {
                        Username = req.username,
                        Level = 1,
                        CreateTime = Timestamp.GetCurrentTimestamp(),
                        Gold = StarterAccountConfig.InitialGold,
                        Stardust = StarterAccountConfig.InitialStardust,
                        Stellastone = StarterAccountConfig.InitialStellastone,
                        SelectDeck = StarterAccountConfig.InitialDeckId,
                        OwnedSkins = new List<string>(),
                        OwnedEmotes = new List<string>(),
                        OwnedCards = StarterAccountConfig.GetStarterCards(),
                        OwnedPrismCards = new Dictionary<string, int>(),
                        OwnedPacks = new Dictionary<string, int>(),
                        PurchaseCounts = new Dictionary<string, int>()
                    };
                    await docRef.SetAsync(userData);
                    Console.WriteLine($"✅ Firestore에 사용자 정보 저장 성공: {userRecord.Uid} (Gold: {userData.Gold}, Stardust: {userData.Stardust}, Stellastone: {userData.Stellastone})");

                    // 'Decks' 서브컬렉션에 기본 덱을 생성합니다.
                    CollectionReference decksRef = docRef.Collection("Decks");
                    DeckData initialDeck = StarterAccountConfig.CreateStarterDeck();

                    await decksRef.Document(initialDeck.deckId).SetAsync(initialDeck);
                    Console.WriteLine($"✅ 'Decks' 서브컬렉션에 기본 덱 생성 완료 (ID: {initialDeck.deckId}): {userRecord.Uid}");

                    return Results.Ok(new { status = "success", message = "회원가입이 완료되었습니다!", user_id = userRecord.Uid });
                }
                catch (FirebaseAuthException ex)
                {
                    Console.WriteLine($"❌ 회원가입 실패: {ex.Message}");
                    string friendlyMsg = "회원가입에 실패했습니다.";
                    if (ex.Message.Contains("already in use") || ex.Message.Contains("EMAIL_EXISTS"))
                    {
                        friendlyMsg = "이미 존재하는 아이디(이메일)입니다.";
                    }
                    else if (ex.Message.Contains("badly formatted") || ex.Message.Contains("INVALID_EMAIL"))
                    {
                        friendlyMsg = "아이디(이메일)가 잘못되었습니다.";
                    }
                    else if (ex.Message.Contains("WEAK_PASSWORD"))
                    {
                        friendlyMsg = "비밀번호는 6자리 이상이어야 합니다.";
                    }
                    return Results.Conflict(new { status = "error", message = friendlyMsg });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 2. 로그인 API: POST /api/auth/login (서버 경유 로그인)
            app.MapPost("/api/auth/login", async (
                LoginRequest req,
                FirestoreDb db,
                IConfiguration config,
                IHttpClientFactory httpClientFactory) =>
            {
                Console.WriteLine($"🔑 로그인 요청 수신: {req.email}");

                if (string.IsNullOrWhiteSpace(req.email))
                {
                    return Results.BadRequest(new { status = "error", message = "아이디(이메일)를 입력해주세요." });
                }
                if (!req.email.Contains("@") || !req.email.Contains("."))
                {
                    return Results.BadRequest(new { status = "error", message = "아이디(이메일)가 잘못되었습니다." });
                }
                if (string.IsNullOrWhiteSpace(req.password))
                {
                    return Results.BadRequest(new { status = "error", message = "비밀번호를 입력해주세요." });
                }
                if (req.password.Length < 6)
                {
                    return Results.BadRequest(new { status = "error", message = "비밀번호는 6자리 이상이어야 합니다." });
                }

                string? apiKey = config["Firebase:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    Console.WriteLine("❌ Firebase:ApiKey가 appsettings.json에 설정되지 않았습니다.");
                    return Results.Problem("서버 인증 설정 오류입니다.");
                }

                try
                {
                    // 1. Google Identity Toolkit REST API를 호출하여 비밀번호 검증
                    var httpClient = httpClientFactory.CreateClient();
                    var verifyUrl = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={apiKey}";

                    var payload = new
                    {
                        email = req.email,
                        password = req.password,
                        returnSecureToken = true
                    };

                    var response = await httpClient.PostAsJsonAsync(verifyUrl, payload);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"❌ 로그인 검증 실패: {response.StatusCode} - {responseBody}");

                        string friendlyMessage = "아이디 또는 비밀번호가 올바르지 않습니다.";
                        try
                        {
                            using var doc = JsonDocument.Parse(responseBody);
                            if (doc.RootElement.TryGetProperty("error", out var errObj) &&
                                errObj.TryGetProperty("message", out var msgProp))
                            {
                                string firebaseErr = msgProp.GetString() ?? "";
                                if (firebaseErr.Contains("INVALID_LOGIN_CREDENTIALS") ||
                                    firebaseErr.Contains("INVALID_PASSWORD") ||
                                    firebaseErr.Contains("EMAIL_NOT_FOUND"))
                                {
                                    friendlyMessage = "아이디 또는 비밀번호가 올바르지 않습니다.";
                                }
                                else if (firebaseErr.Contains("TOO_MANY_ATTEMPTS_TRY_LATER"))
                                {
                                    friendlyMessage = "로그인 시도가 너무 많아 일시적으로 차단되었습니다. 잠시 후 다시 시도해주세요.";
                                }
                                else if (firebaseErr.Contains("USER_DISABLED"))
                                {
                                    friendlyMessage = "비활성화된 계정입니다. 관리자에게 문의하세요.";
                                }
                                else if (firebaseErr.Contains("INVALID_EMAIL"))
                                {
                                    friendlyMessage = "아이디(이메일)가 잘못되었습니다.";
                                }
                                else
                                {
                                    friendlyMessage = "아이디 또는 비밀번호가 올바르지 않습니다.";
                                }
                            }
                        }
                        catch { }

                        return Results.Json(new { status = "error", message = friendlyMessage }, statusCode: 401);
                    }

                    // 2. 로그인 성공 -> UID 추출 및 CustomToken 생성
                    using var successDoc = JsonDocument.Parse(responseBody);
                    string uid = successDoc.RootElement.GetProperty("localId").GetString()!;
                    string idToken = successDoc.RootElement.GetProperty("idToken").GetString()!;

                    // Firebase Custom Token 생성 (클라이언트 Firebase SDK 세션 동기화용)
                    string customToken = await FirebaseAuth.DefaultInstance.CreateCustomTokenAsync(uid);

                    // 유저 데이터 무결성 보정 (누락 필드 자동 생성)
                    await AuthHelper.EnsureUserDataIntegrityAsync(uid, db);

                    // Firestore 유저 데이터 로드
                    DocumentReference userRef = db.Collection("Users").Document(uid);
                    DocumentSnapshot userSnap = await userRef.GetSnapshotAsync();
                    UserData? userData = userSnap.Exists ? userSnap.ConvertTo<UserData>() : null;

                    Console.WriteLine($"✅ 서버 로그인 성공: {uid} ({req.email})");

                    return Results.Ok(new
                    {
                        status = "success",
                        message = "로그인 성공!",
                        user_id = uid,
                        customToken = customToken,
                        idToken = idToken,
                        userData = userData
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 로그인 처리 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 3. 토큰 검증 API: POST /api/auth/verify-token (자동 로그인 및 세션 유지용)
            app.MapPost("/api/auth/verify-token", async (
                IDictionary<string, string> req,
                FirestoreDb db) =>
            {
                string idToken = req["token"];
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
                string uid = decodedToken.Uid;
                Console.WriteLine($"✅ 토큰 검증 성공, 로그인 유저: {uid}");

                await AuthHelper.EnsureUserDataIntegrityAsync(uid, db);

                DocumentReference userRef = db.Collection("Users").Document(uid);
                DocumentSnapshot userSnap = await userRef.GetSnapshotAsync();
                UserData? userData = userSnap.Exists ? userSnap.ConvertTo<UserData>() : null;

                return Results.Ok(new
                {
                    status = "success",
                    message = "로그인 성공!",
                    user_id = uid,
                    userData = userData
                });
            });

            // 내 프로필/계정 데이터 조회 API: GET /api/users/me
            app.MapGet("/api/users/me", async (
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                DocumentReference userRef = db.Collection("Users").Document(uid);
                DocumentSnapshot userSnap = await userRef.GetSnapshotAsync();
                if (!userSnap.Exists)
                {
                    return Results.NotFound(new { status = "error", message = "유저 정보를 찾을 수 없습니다." });
                }

                UserData userData = userSnap.ConvertTo<UserData>();
                return Results.Ok(new
                {
                    status = "success",
                    user_id = uid,
                    userData = userData
                });
            });

            // 대표 덱 선택 API: PUT /api/user/select-deck
            app.MapPut("/api/user/select-deck", async (
                SelectDeckRequest req,
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                if (string.IsNullOrEmpty(req.DeckId))
                {
                    return Results.BadRequest(new { status = "error", message = "DeckId가 비어있습니다." });
                }

                try
                {
                    DocumentReference userDocRef = db.Collection("Users").Document(uid);
                    await userDocRef.UpdateAsync("SelectDeck", req.DeckId);

                    Console.WriteLine($"대표 덱 선택 성공 (DeckID: {req.DeckId}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", message = "대표 덱이 성공적으로 업데이트되었습니다." });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($" 대표 덱 선택 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });

            // 대표 덱 조회 API: GET /api/user/select-deck
            app.MapGet("/api/user/select-deck", async (
                FirestoreDb db,
                [FromHeader(Name = "Authorization")] string authorization) =>
            {
                string? uid = await AuthHelper.VerifyTokenAsync(authorization);
                if (uid == null)
                {
                    return Results.Json(new { status = "error", message = "인증에 실패했습니다." }, statusCode: 401);
                }

                try
                {
                    DocumentReference userDocRef = db.Collection("Users").Document(uid);
                    DocumentSnapshot userSnapshot = await userDocRef.GetSnapshotAsync();

                    if (!userSnapshot.Exists)
                    {
                        return Results.NotFound(new { status = "error", message = "유저 정보를 찾을 수 없습니다." });
                    }

                    UserData? userData = userSnapshot.ConvertTo<UserData>();
                    string? selectedDeckId = userData?.SelectDeck;

                    if (string.IsNullOrEmpty(selectedDeckId))
                    {
                        return Results.Ok(new { status = "success", deck = (DeckData?)null });
                    }

                    DocumentReference deckDocRef = userDocRef.Collection("Decks").Document(selectedDeckId);
                    DocumentSnapshot deckSnapshot = await deckDocRef.GetSnapshotAsync();

                    if (!deckSnapshot.Exists)
                    {
                        return Results.Ok(new { status = "success", deck = (DeckData?)null, message = "선택된 덱을 찾을 수 없습니다." });
                    }

                    DeckData selectedDeck = deckSnapshot.ConvertTo<DeckData>();
                    selectedDeck.deckId = deckSnapshot.Id;

                    Console.WriteLine($"대표 덱 데이터 조회 성공 (Deck: {selectedDeck.deckName}) - 유저: {uid}");
                    return Results.Ok(new { status = "success", deck = selectedDeck });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 대표 덱 조회 중 서버 오류: {ex.Message}");
                    return Results.Problem("서버 내부 오류가 발생했습니다.");
                }
            });
        }
    }
}
