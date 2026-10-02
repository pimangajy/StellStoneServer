using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using GameServer.Models;

namespace GameServer.Services
{
    /// <summary>
    /// Firestore의 MatchmakingQueue 컬렉션을 기반으로 동작하는 서버 매치메이킹 서비스
    /// - appsettings.json의 EnableSinglePlayerBot이 true이면: 2.5초 대기 후 봇 매칭
    /// - appsettings.json의 EnableSinglePlayerBot이 false이면: 실제 다른 유저가 들어올 때까지 무한 대기
    /// </summary>
    public class ServerMatchmakingService
    {
        private readonly FirestoreDb _db;
        private const string MatchmakingCollection = "MatchmakingQueue";
        private const int BotMatchDelayMs = 2500; // 2.5초 대기

        // 대기 중인 유저의 봇 타이머 CancellationTokenSource 관리 (UID -> CTS)
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _botTimers = new();

        public ServerMatchmakingService(FirestoreDb db)
        {
            _db = db;
        }

        /// <summary>
        /// 매칭 요청 처리
        /// 1. 덱 구성 유효성 검증 (30장 필수, 동일 카드 2장 제한, 직업 규칙 등)
        /// 2. 대기열(MatchmakingQueue)에서 다른 대기 유저 탐색
        /// 3. 상대가 있으면 즉시 1:1 유저 매칭 성사
        /// 4. 상대가 없으면 대기열 등록 후 (EnableSinglePlayerBot ? 2.5초 후 봇 매칭 : 무한 대기)
        /// </summary>
        public async Task<MatchResponseDto> RequestMatchAsync(string uid, string? deckId)
        {
            // 기존에 대기 중이던 타이머가 있다면 먼저 취소
            CancelPendingBotTimer(uid);

            // =================================================================
            // 0. 덱 구성 유효성 검증 (30장 미만 또는 잘못된 구성 시 즉시 에러 패킷 반환)
            // =================================================================
            MatchDeckErrorResponse? validationError = await ValidateDeckAsync(uid, deckId);
            if (validationError != null)
            {
                Console.WriteLine($"[Matchmaking] ❌ 덱 검증 실패 ({uid}): {validationError.ErrorCode} - {validationError.Message}");
                return validationError;
            }

            CollectionReference queueCol = _db.Collection(MatchmakingCollection);

            // 1. 유저 닉네임 조회 (Users 컬렉션의 Username 필드)
            string playerName = "Player";
            try
            {
                DocumentSnapshot userSnap = await _db.Collection("Users").Document(uid).GetSnapshotAsync();
                if (userSnap.Exists)
                {
                    if (userSnap.ContainsField("Username") && !string.IsNullOrEmpty(userSnap.GetValue<string>("Username")))
                    {
                        playerName = userSnap.GetValue<string>("Username");
                    }
                    else if (userSnap.ContainsField("username") && !string.IsNullOrEmpty(userSnap.GetValue<string>("username")))
                    {
                        playerName = userSnap.GetValue<string>("username");
                    }
                    else if (userSnap.ContainsField("nickname") && !string.IsNullOrEmpty(userSnap.GetValue<string>("nickname")))
                    {
                        playerName = userSnap.GetValue<string>("nickname");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] 유저 닉네임 로드 경고: {ex.Message}");
            }

            // 2. 대기 중인 다른 유저 탐색
            Query waitingQuery = queueCol
                .WhereEqualTo("status", "waiting")
                .WhereNotEqualTo(FieldPath.DocumentId, uid)
                .Limit(1);

            QuerySnapshot waitingSnap = await waitingQuery.GetSnapshotAsync();
            DocumentSnapshot? opponentDoc = waitingSnap.Documents.FirstOrDefault();

            // 3. 대기 중인 상대 유저를 발견한 경우 -> 트랜잭션으로 매칭 체결
            if (opponentDoc != null)
            {
                string opponentUid = opponentDoc.Id;
                DocumentReference opponentRef = opponentDoc.Reference;
                DocumentReference myRef = queueCol.Document(uid);
                string newGameId = Guid.NewGuid().ToString();

                try
                {
                    bool matchSuccess = await _db.RunTransactionAsync(async transaction =>
                    {
                        DocumentSnapshot oppLatest = await transaction.GetSnapshotAsync(opponentRef);
                        if (!oppLatest.Exists) return false;

                        string oppStatus = oppLatest.GetValue<string>("status");
                        if (oppStatus != "waiting") return false;

                        // 상대방 대기 문서 업데이트 (matched)
                        transaction.Update(opponentRef, new Dictionary<string, object>
                        {
                            { "status", "matched" },
                            { "opponentUid", uid },
                            { "gameId", newGameId }
                        });

                        // 내 대기 문서 생성/업데이트 (matched)
                        transaction.Set(myRef, new MatchmakingQueueEntry
                        {
                            Status = "matched",
                            DeckId = deckId,
                            PlayerName = playerName,
                            OpponentUid = opponentUid,
                            GameId = newGameId,
                            CreatedAt = DateTime.UtcNow
                        });

                        return true;
                    });

                    if (matchSuccess)
                    {
                        // 상대방의 봇 타이머가 돌고 있었다면 취소
                        CancelPendingBotTimer(opponentUid);

                        string opponentName = opponentDoc.ContainsField("playerName")
                            ? opponentDoc.GetValue<string>("playerName")
                            : "Opponent";

                        Console.WriteLine($"[Matchmaking] ⚔️ 실제 유저 1:1 매칭 성사! ({uid} vs {opponentUid}) | GameId: {newGameId}");

                        return new MatchResponseDto
                        {
                            Status = "matched",
                            GameId = newGameId,
                            OpponentUid = opponentUid,
                            OpponentName = opponentName,
                            Message = "Match found!"
                        };
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Matchmaking] 트랜잭션 매칭 경합 실패, 대기열 등록으로 전환: {ex.Message}");
                }
            }

            // 4. 대기 중인 상대가 없거나 경합 실패 시 -> 대기열에 등록
            DocumentReference selfRef = queueCol.Document(uid);
            var myEntry = new MatchmakingQueueEntry
            {
                Status = "waiting",
                DeckId = deckId,
                PlayerName = playerName,
                OpponentUid = null,
                GameId = null,
                CreatedAt = DateTime.UtcNow
            };

            await selfRef.SetAsync(myEntry);

            // 5. 봇 모드 설정에 따른 분기
            bool isBotEnabled = GameServerSettings.EnableSinglePlayerBot;
            if (isBotEnabled)
            {
                Console.WriteLine($"[Matchmaking] ⏳ {uid} 대기열 등록됨. (봇 모드 ON: {BotMatchDelayMs / 1000.0:F1}초 후 상대 없으면 봇 매칭)");

                var cts = new CancellationTokenSource();
                _botTimers[uid] = cts;

                // 2.5초 대기 후 봇 매칭을 수행하는 백그라운드 태스크
                _ = Task.Run(async () => await ProcessBotMatchFallbackAsync(uid, selfRef, cts.Token));
            }
            else
            {
                Console.WriteLine($"[Matchmaking] ⏳ {uid} 대기열 등록됨. (봇 모드 OFF: 실제 유저가 들어올 때까지 무한 대기)");
            }

            return new MatchResponseDto
            {
                Status = "waiting",
                Message = "Waiting for an opponent..."
            };
        }

        /// <summary>
        /// 2.5초 동안 대기 후 상대가 없으면 봇 대전으로 전환
        /// </summary>
        private async Task ProcessBotMatchFallbackAsync(string uid, DocumentReference docRef, CancellationToken token)
        {
            try
            {
                await Task.Delay(BotMatchDelayMs, token);

                if (token.IsCancellationRequested) return;

                // 트랜잭션으로 아직도 waiting 상태인지 확인
                await _db.RunTransactionAsync(async transaction =>
                {
                    DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(docRef);
                    if (!snapshot.Exists) return;

                    string status = snapshot.GetValue<string>("status");
                    if (status == "waiting")
                    {
                        string botGameId = "bot_" + Guid.NewGuid().ToString();

                        transaction.Update(docRef, new Dictionary<string, object>
                        {
                            { "status", "matched" },
                            { "opponentUid", "BOT_UID" },
                            { "gameId", botGameId }
                        });

                        Console.WriteLine($"[Matchmaking] 🤖 2.5초 타임아웃: 유저 {uid} 봇 매칭 완료! (GameId: {botGameId})");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // 취소됨 (정상 흐름)
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] 봇 매칭 폴백 처리 중 오류 ({uid}): {ex.Message}");
            }
            finally
            {
                _botTimers.TryRemove(uid, out _);
            }
        }

        /// <summary>
        /// 현재 매칭 상태 조회 (클라이언트 폴링 백업 지원)
        /// </summary>
        public async Task<MatchResponseDto> GetMatchStatusAsync(string uid)
        {
            DocumentReference docRef = _db.Collection(MatchmakingCollection).Document(uid);
            DocumentSnapshot snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists)
            {
                return new MatchResponseDto
                {
                    Status = "none",
                    Message = "Not in matchmaking queue"
                };
            }

            string status = snapshot.GetValue<string>("status");
            string? gameId = snapshot.ContainsField("gameId") ? snapshot.GetValue<string>("gameId") : null;
            string? opponentUid = snapshot.ContainsField("opponentUid") ? snapshot.GetValue<string>("opponentUid") : null;

            return new MatchResponseDto
            {
                Status = status,
                GameId = gameId,
                OpponentUid = opponentUid,
                Message = status == "matched" ? "Match found!" : "Waiting..."
            };
        }

        /// <summary>
        /// 매칭 취소
        /// </summary>
        public async Task<bool> CancelMatchAsync(string uid)
        {
            CancelPendingBotTimer(uid);

            try
            {
                DocumentReference docRef = _db.Collection(MatchmakingCollection).Document(uid);
                await docRef.DeleteAsync();
                Console.WriteLine($"[Matchmaking] 🚫 유저 {uid} 매칭 대기열 취소 완료");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] 매칭 취소 오류 ({uid}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 대기 중인 봇 타이머 취소
        /// </summary>
        private void CancelPendingBotTimer(string uid)
        {
            if (_botTimers.TryRemove(uid, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch { }
            }
        }

        /// <summary>
        /// 유저의 덱 유효성을 검증합니다.
        /// - 30장 미만 또는 초과 (정확히 30장 필수)
        /// - 동일 카드 최대 2장 제한 (메인 + 사이드)
        /// - 직업 적합성 (중립 또는 해당 직업 카드만 가능)
        /// - 사이드 덱 규칙 (0장이거나 5장, 하수인/주문만 가능)
        /// - 토큰 카드 포함 여부 및 유효한 카드 ID 검사
        /// </summary>
        public async Task<MatchDeckErrorResponse?> ValidateDeckAsync(string uid, string? deckId)
        {
            try
            {
                DocumentReference userDocRef = _db.Collection("Users").Document(uid);

                // 1. deckId가 없으면 유저 대표 덱 조회
                string? targetDeckId = deckId;
                if (string.IsNullOrEmpty(targetDeckId))
                {
                    DocumentSnapshot userSnap = await userDocRef.GetSnapshotAsync();
                    if (!userSnap.Exists)
                    {
                        return new MatchDeckErrorResponse
                        {
                            ErrorCode = "DECK_NOT_FOUND",
                            Message = "유저 계정 정보를 찾을 수 없습니다."
                        };
                    }
                    targetDeckId = userSnap.ContainsField("SelectDeck") ? userSnap.GetValue<string>("SelectDeck") : null;
                }

                if (string.IsNullOrEmpty(targetDeckId))
                {
                    return new MatchDeckErrorResponse
                    {
                        ErrorCode = "DECK_NOT_FOUND",
                        Message = "선택된 덱이 없습니다. 먼저 덱을 선택해주세요."
                    };
                }

                // 2. Firestore에서 덱 문서 조회
                DocumentReference deckDocRef = userDocRef.Collection("Decks").Document(targetDeckId);
                DocumentSnapshot deckSnap = await deckDocRef.GetSnapshotAsync();
                if (!deckSnap.Exists)
                {
                    return new MatchDeckErrorResponse
                    {
                        ErrorCode = "DECK_NOT_FOUND",
                        Message = $"선택된 덱({targetDeckId})을 찾을 수 없습니다."
                    };
                }

                DeckData deckData = deckSnap.ConvertTo<DeckData>();
                deckData.deckId = deckSnap.Id;

                int mainCount = deckData.cardIds?.Count ?? 0;

                // 3. 메인 덱 장수 검증 (정확히 30장 필수)
                if (mainCount != 30)
                {
                    return new MatchDeckErrorResponse
                    {
                        ErrorCode = "DECK_COUNT_MISMATCH",
                        Message = $"메인 덱은 정확히 30장이어야 매칭을 시작할 수 있습니다. (현재: {mainCount}장)",
                        CurrentCount = mainCount,
                        RequiredCount = 30
                    };
                }

                // 4. 사이드 덱 장수 검증 (0장이거나 5장)
                int sideCount = deckData.sideDeckCardIds?.Count ?? 0;
                if (sideCount > 0 && sideCount != 5)
                {
                    return new MatchDeckErrorResponse
                    {
                        ErrorCode = "DECK_SIDE_DECK_INVALID",
                        Message = $"사이드 덱은 0장이거나 5장이어야 합니다. (현재: {sideCount}장)",
                        CurrentCount = mainCount,
                        RequiredCount = 30
                    };
                }

                // 5. 카드 데이터베이스 참조 검증
                var cardDb = ServerCardDatabase.Instance;
                var combinedCardIds = new List<string>();
                if (deckData.cardIds != null) combinedCardIds.AddRange(deckData.cardIds);
                if (deckData.sideDeckCardIds != null) combinedCardIds.AddRange(deckData.sideDeckCardIds);

                // 5-1. 동일 카드 2장 초과 검증 (메인 + 사이드 합산)
                var cardCounts = combinedCardIds.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
                foreach (var kvp in cardCounts)
                {
                    if (kvp.Value > 2)
                    {
                        var card = cardDb.GetCardData(kvp.Key);
                        string cardName = card?.Name ?? kvp.Key;
                        return new MatchDeckErrorResponse
                        {
                            ErrorCode = "DECK_DUPLICATE_CARD",
                            Message = $"동일한 카드는 메인과 사이드 덱을 합쳐 최대 2장까지만 포함할 수 있습니다. ({cardName}: {kvp.Value}장)",
                            CurrentCount = mainCount,
                            RequiredCount = 30,
                            InvalidCardIds = new List<string> { kvp.Key }
                        };
                    }
                }

                // 5-2. 개별 카드 유효성 및 토큰 검사
                foreach (var cardId in combinedCardIds)
                {
                    var card = cardDb.GetCardData(cardId);
                    if (card == null)
                    {
                        return new MatchDeckErrorResponse
                        {
                            ErrorCode = "DECK_INVALID_CARD",
                            Message = $"존재하지 않거나 유효하지 않은 카드가 포함되어 있습니다. (ID: {cardId})",
                            CurrentCount = mainCount,
                            RequiredCount = 30,
                            InvalidCardIds = new List<string> { cardId }
                        };
                    }

                    if (card.IsToken)
                    {
                        return new MatchDeckErrorResponse
                        {
                            ErrorCode = "DECK_INVALID_CARD",
                            Message = $"토큰 카드는 덱에 직접 편성할 수 없습니다. ({card.Name})",
                            CurrentCount = mainCount,
                            RequiredCount = 30,
                            InvalidCardIds = new List<string> { cardId }
                        };
                    }
                }

                // 5-3. 직업(Class) 제한 검사
                string? deckClass = deckData.deckClass;
                if (!string.IsNullOrEmpty(deckClass))
                {
                    foreach (var cardId in combinedCardIds)
                    {
                        var card = cardDb.GetCardData(cardId);
                        if (card != null && card.Class.HasValue)
                        {
                            // CardClass.Gangzi는 공용 카드이므로 모든 직업 덱에 투입 가능
                            if (card.Class.Value != CardClass.Gangzi && !string.Equals(card.Class.Value.ToString(), deckClass, StringComparison.OrdinalIgnoreCase))
                            {
                                return new MatchDeckErrorResponse
                                {
                                    ErrorCode = "DECK_CLASS_MISMATCH",
                                    Message = $"'{deckClass}' 덱에 포함될 수 없는 타 직업 카드가 포함되어 있습니다. ({card.Name})",
                                    CurrentCount = mainCount,
                                    RequiredCount = 30,
                                    InvalidCardIds = new List<string> { cardId }
                                };
                            }
                        }
                    }
                }

                // 5-4. 사이드 덱 하수인/주문 전용 검사
                if (deckData.sideDeckCardIds != null)
                {
                    foreach (var cardId in deckData.sideDeckCardIds)
                    {
                        var card = cardDb.GetCardData(cardId);
                        if (card != null && card.CardType.HasValue && card.CardType.Value != CardType.하수인 && card.CardType.Value != CardType.주문)
                        {
                            return new MatchDeckErrorResponse
                            {
                                ErrorCode = "DECK_SIDE_DECK_INVALID",
                                Message = $"사이드 덱에는 하수인과 주문 카드만 포함할 수 있습니다. ({card.Name})",
                                CurrentCount = mainCount,
                                RequiredCount = 30,
                                InvalidCardIds = new List<string> { cardId }
                            };
                        }
                    }
                }

                // 모든 검증 통과
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Matchmaking] 덱 검증 중 오류 발생 ({uid}): {ex.Message}");
                return new MatchDeckErrorResponse
                {
                    ErrorCode = "DECK_VALIDATION_ERROR",
                    Message = $"덱 검증 중 서버 오류가 발생했습니다: {ex.Message}"
                };
            }
        }
    }
}
