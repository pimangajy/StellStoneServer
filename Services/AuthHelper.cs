using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FirebaseAdmin.Auth;
using Google.Cloud.Firestore;
using GameServer.Models;

namespace GameServer.Services
{
    public static class AuthHelper
    {
        /// <summary>
        /// Request Header의 Authorization (Bearer 토큰)을 검증하고 UID를 반환합니다.
        /// 실패 시 null을 반환합니다.
        /// </summary>
        public static async Task<string?> VerifyTokenAsync(string authorization)
        {
            if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer "))
            {
                Console.WriteLine("❌ 토큰이 없거나 'Bearer ' 형식이 아닙니다.");
                return null;
            }

            string idToken = authorization.Substring("Bearer ".Length);
            return await VerifyTokenStringAsync(idToken);
        }
        
        /// <summary>
        /// 오직 ID 토큰 문자열만 받아 검증하고 UID를 반환하는 public 헬퍼 함수입니다.
        /// </summary>
        public static async Task<string?> VerifyTokenStringAsync(string idToken)
        {
            if (string.IsNullOrEmpty(idToken))
            {
                Console.WriteLine("❌ 토큰 문자열이 비어있습니다.");
                return null;
            }
            
            try
            {
                // Firebase Admin SDK를 사용하여 토큰을 검증합니다.
                FirebaseToken decodedToken = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
                return decodedToken.Uid;
            }
            catch (FirebaseAuthException ex)
            {
                Console.WriteLine($"❌ 토큰 문자열 검증 실패 (Firebase): {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 토큰 문자열 검증 중 알 수 없는 오류: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 유저 접속/로그인 시 Firestore Users/{uid} 문서 및 누락된 필드를 대조하여 자동 보정 및 생성합니다.
        /// </summary>
        public static async Task EnsureUserDataIntegrityAsync(string uid, FirestoreDb db)
        {
            if (string.IsNullOrEmpty(uid)) return;

            try
            {
                DocumentReference userDocRef = db.Collection("Users").Document(uid);
                DocumentSnapshot snapshot = await userDocRef.GetSnapshotAsync();

                if (!snapshot.Exists)
                {
                    // 1. 유저 문서가 전혀 없는 경우 Auth 정보 기반으로 신규 생성
                    UserRecord? userRecord = null;
                    try
                    {
                        userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UserDataSync] Auth 유저 정보 조회 실패: {ex.Message}");
                    }

                    string displayName = !string.IsNullOrEmpty(userRecord?.DisplayName)
                        ? userRecord.DisplayName
                        : (!string.IsNullOrEmpty(userRecord?.Email) ? userRecord.Email.Split('@')[0] : "Player");

                    UserData newUserData = new UserData
                    {
                        Username = displayName,
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

                    await userDocRef.SetAsync(newUserData);
                    Console.WriteLine($"✅ [UserDataSync] 기존 유저 {uid}의 UserData 문서 신규 생성 완료 (Gold: {newUserData.Gold}, Stardust: {newUserData.Stardust}, Stellastone: {newUserData.Stellastone})");

                    // 기본 덱 생성
                    CollectionReference decksRef = userDocRef.Collection("Decks");
                    DeckData initialDeck = StarterAccountConfig.CreateStarterDeck();
                    await decksRef.Document(initialDeck.deckId).SetAsync(initialDeck);
                    Console.WriteLine($"✅ [UserDataSync] 기본 덱 생성 완료: {uid} (ID: {initialDeck.deckId})");
                }
                else
                {
                    // 2. 유저 문서가 존재할 때 누락된 필드가 있는지 확인하여 부분 갱신
                    Dictionary<string, object> updates = new Dictionary<string, object>();
                    Dictionary<string, object> currentData = snapshot.ToDictionary() ?? new Dictionary<string, object>();

                    if (!currentData.ContainsKey("Gold"))
                    {
                        updates["Gold"] = 0;
                    }

                    if (!currentData.ContainsKey("Stardust"))
                    {
                        updates["Stardust"] = 0;
                    }

                    if (!currentData.ContainsKey("Stellastone"))
                    {
                        updates["Stellastone"] = 0;
                    }

                    if (!currentData.ContainsKey("OwnedSkins") || currentData["OwnedSkins"] == null)
                    {
                        updates["OwnedSkins"] = new List<string>();
                    }

                    if (!currentData.ContainsKey("OwnedEmotes") || currentData["OwnedEmotes"] == null)
                    {
                        updates["OwnedEmotes"] = new List<string>();
                    }

                    if (!currentData.ContainsKey("OwnedCards") || currentData["OwnedCards"] == null)
                    {
                        updates["OwnedCards"] = new Dictionary<string, int>();
                    }

                    if (!currentData.ContainsKey("OwnedPrismCards") || currentData["OwnedPrismCards"] == null)
                    {
                        updates["OwnedPrismCards"] = new Dictionary<string, int>();
                    }

                    if (!currentData.ContainsKey("OwnedPacks") || currentData["OwnedPacks"] == null)
                    {
                        updates["OwnedPacks"] = new Dictionary<string, int>();
                    }

                    if (!currentData.ContainsKey("PurchaseCounts") || currentData["PurchaseCounts"] == null)
                    {
                        updates["PurchaseCounts"] = new Dictionary<string, int>();
                    }

                    if (!currentData.ContainsKey("Level") || currentData["Level"] == null)
                    {
                        updates["Level"] = 1;
                    }

                    if (!currentData.ContainsKey("Exp") || currentData["Exp"] == null)
                    {
                        updates["Exp"] = 0;
                    }

                    if (!currentData.ContainsKey("Score") || currentData["Score"] == null)
                    {
                        updates["Score"] = 1000;
                    }

                    if (!currentData.ContainsKey("WinCount") || currentData["WinCount"] == null)
                    {
                        updates["WinCount"] = 0;
                    }

                    if (!currentData.ContainsKey("LossCount") || currentData["LossCount"] == null)
                    {
                        updates["LossCount"] = 0;
                    }

                    if (!currentData.ContainsKey("CreateTime") || currentData["CreateTime"] == null)
                    {
                        updates["CreateTime"] = Timestamp.GetCurrentTimestamp();
                    }

                    if (!currentData.ContainsKey("Username") || string.IsNullOrEmpty(currentData["Username"]?.ToString()))
                    {
                        try
                        {
                            UserRecord userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(uid);
                            updates["Username"] = userRecord.DisplayName ?? (userRecord.Email?.Split('@')[0] ?? "Player");
                        }
                        catch
                        {
                            updates["Username"] = "Player";
                        }
                    }

                    if (updates.Count > 0)
                    {
                        await userDocRef.UpdateAsync(updates);
                        Console.WriteLine($"✅ [UserDataSync] 유저 {uid}의 누락된 필드 자동 보정 완료: {string.Join(", ", updates.Keys)}");
                    }

                    // 기본 덱 존재 여부 확인
                    CollectionReference decksRef = userDocRef.Collection("Decks");
                    QuerySnapshot deckSnapshot = await decksRef.Limit(1).GetSnapshotAsync();
                    if (deckSnapshot.Count == 0)
                    {
                        string initialDeckId = "testDeck_1";
                        DeckData initialDeck = new DeckData
                        {
                            deckId = initialDeckId,
                            deckName = "테스트 덱",
                            deckClass = "임시 직업",
                            cardIds = new List<string>()
                        };
                        await decksRef.Document(initialDeckId).SetAsync(initialDeck);
                        Console.WriteLine($"✅ [UserDataSync] 덱이 없어 기본 덱 자동 생성 완료: {uid}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [UserDataSync] 유저 데이터 검사/보정 중 오류: {ex.Message}");
            }
        }
    }
}
