using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer;

namespace GameServer.Shop
{
    /// <summary>
    /// 상점 구매 처리, 재화 차감, 트랜잭션, 카드팩 가챠 및 보상 지급 비즈니스 로직을 전담하는 서비스
    /// </summary>
    public class ShopService
    {
        public static ShopService Instance { get; private set; } = new ShopService();

        private ShopService() { }

        // ==================================================================
        // [설정] 희귀도별 분해 스타더스트(별가루) 지급량
        // 나중에 수치를 수정할 때는 아래 수치를 변경하시면 됩니다.
        // ==================================================================
        public static readonly Dictionary<CardRarity, int> DisenchantDustRates = new Dictionary<CardRarity, int>
        {
            { CardRarity.common, 5 },       // 일반
            { CardRarity.rare, 20 },        // 희귀
            { CardRarity.epic, 100 },       // 특급
            { CardRarity.legendary, 400 }   // 전설
        };

        /// <summary>
        /// 카드의 희귀도에 따른 분해 스타더스트 지급량을 반환합니다.
        /// </summary>
        public static int GetDisenchantDustAmount(CardRarity? rarity)
        {
            if (rarity.HasValue && DisenchantDustRates.TryGetValue(rarity.Value, out int dust))
            {
                return dust;
            }
            return DisenchantDustRates[CardRarity.common];
        }

        // ==================================================================
        // [설정] 희귀도별 제작 스타더스트(별가루) 소모량
        // 나중에 수치를 수정할 때는 아래 수치를 변경하시면 됩니다.
        // ==================================================================
        public static readonly Dictionary<CardRarity, int> CraftDustRates = new Dictionary<CardRarity, int>
        {
            { CardRarity.common, 40 },      // 일반 (Common)
            { CardRarity.rare, 100 },       // 희귀 (Rare)
            { CardRarity.epic, 400 },       // 특급 (Epic)
            { CardRarity.legendary, 1600 }  // 전설 (Legendary)
        };

        /// <summary>
        /// 카드의 희귀도에 따른 제작 스타더스트 소모량을 반환합니다.
        /// </summary>
        public static int GetCraftDustAmount(CardRarity? rarity)
        {
            if (rarity.HasValue && CraftDustRates.TryGetValue(rarity.Value, out int dust))
            {
                return dust;
            }
            return CraftDustRates[CardRarity.common];
        }

        // ==================================================================
        // [설정] 카드팩 개봉 시 기본 레어도별 등장 확률 (%)
        // 나중에 확률을 수정할 때는 아래 수치를 변경하시면 됩니다. (가중치 방식, 소수점 가능)
        // ==================================================================
        public static readonly Dictionary<CardRarity, double> CardRarityDropRates = new Dictionary<CardRarity, double>
        {
            { CardRarity.common, 70.0 },      // 🤍 일반 (Common): 70.0%
            { CardRarity.rare, 21.5 },        // 💙 희귀 (Rare): 21.5%
            { CardRarity.epic, 6.5 },         // 💜 특급 (Epic): 6.5%
            { CardRarity.legendary, 2.0 }     // 🌟 전설 (Legendary): 2.0%
        };

        /// <summary>
        /// CardRarityDropRates 설정에 따라 레어도를 가중치 기반으로 무작위 추첨합니다.
        /// </summary>
        public static CardRarity RollRandomRarity(Random rng)
        {
            double totalWeight = CardRarityDropRates.Values.Sum();
            if (totalWeight <= 0) return CardRarity.common;

            double roll = rng.NextDouble() * totalWeight;
            double cumulative = 0;

            foreach (var kvp in CardRarityDropRates)
            {
                cumulative += kvp.Value;
                if (roll < cumulative)
                {
                    return kvp.Key;
                }
            }

            return CardRarity.common;
        }

        /// <summary>
        /// 유저의 상품 구매 요청을 처리하고 결과를 반환합니다.
        /// </summary>
        public async Task<ShopPurchaseResult> PurchaseProductAsync(string uid, PurchaseRequest req, FirestoreDb db)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.productId))
            {
                return new ShopPurchaseResult
                {
                    Success = false,
                    StatusCode = 400,
                    ErrorMessage = "잘못된 구매 요청입니다."
                };
            }

            if (req.quantity <= 0) req.quantity = 1;

            try
            {
                DocumentReference userRef = db.Collection("Users").Document(uid);

                // [C-02 동시성 최적화] 원자적 트랜잭션을 적용하여 다중 요청/광클 시 재화 복사 및 중복 구매 충돌 차단
                return await db.RunTransactionAsync(async transaction =>
                {
                    // 1. 유저 정보 조회 (트랜잭션 내부)
                    DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(userRef);
                    if (!snapshot.Exists)
                    {
                        return new ShopPurchaseResult
                        {
                            Success = false,
                            StatusCode = 404,
                            ErrorMessage = "유저 정보를 찾을 수 없습니다."
                        };
                    }

                    UserData userData = snapshot.ConvertTo<UserData>();
                    if (userData.PurchaseCounts == null) userData.PurchaseCounts = new Dictionary<string, int>();
                    if (userData.OwnedSkins == null) userData.OwnedSkins = new List<string>();
                    if (userData.OwnedEmotes == null) userData.OwnedEmotes = new List<string>();
                    if (userData.OwnedCards == null) userData.OwnedCards = new Dictionary<string, int>();
                    if (userData.OwnedPrismCards == null) userData.OwnedPrismCards = new Dictionary<string, int>();
                    if (userData.OwnedPacks == null) userData.OwnedPacks = new Dictionary<string, int>();

                    // 2. 상품 정보 조회 및 판매 가능 여부 검증
                    ProductData? product = ServerProductDatabase.Instance.GetProduct(req.productId);
                    if (product == null || !product.isActive)
                    {
                        return new ShopPurchaseResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = "존재하지 않거나 판매가 종료된 상품입니다."
                        };
                    }

                    // 3. 계정당 최대 구매 횟수 제한 검증 (purchaseLimit > 0)
                    if (product.purchaseLimit > 0)
                    {
                        int alreadyPurchased = userData.PurchaseCounts.GetValueOrDefault(req.productId, 0);
                        if (alreadyPurchased + req.quantity > product.purchaseLimit)
                        {
                            int remaining = Math.Max(0, product.purchaseLimit - alreadyPurchased);
                            return new ShopPurchaseResult
                            {
                                Success = false,
                                StatusCode = 400,
                                ErrorMessage = remaining > 0
                                    ? $"구매 제한을 초과했습니다. (구매 가능 잔여 수량: {remaining}개)"
                                    : "이미 최대 구매 횟수에 도달한 상품입니다."
                            };
                        }
                    }

                    int totalCost = product.price * req.quantity;
                    PriceCurrency currency = product.currency;

                    string? skinToGrant = null;
                    string? emoteToGrant = null;
                    int goldToGrant = 0;

                    // 4. 중복 보유 제한 상품 검사 (스킨, 이모티콘 등)
                    if (product.category_Id == ShopCategory.LeaderSkin)
                    {
                        skinToGrant = product.productId;
                        if (userData.OwnedSkins.Contains(skinToGrant))
                        {
                            return new ShopPurchaseResult
                            {
                                Success = false,
                                StatusCode = 400,
                                ErrorMessage = "이미 보유 중인 스킨입니다."
                            };
                        }
                    }
                    else if (product.category_Id == ShopCategory.Emote)
                    {
                        emoteToGrant = product.productId;
                        if (userData.OwnedEmotes.Contains(emoteToGrant))
                        {
                            return new ShopPurchaseResult
                            {
                                Success = false,
                                StatusCode = 400,
                                ErrorMessage = "이미 보유 중인 이모티콘입니다."
                            };
                        }
                    }

                    // 4. 재화 잔액 검증
                    if (currency == PriceCurrency.Gold && userData.Gold < totalCost)
                    {
                        return new ShopPurchaseResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = $"골드가 부족합니다. (필요: {totalCost}, 보유: {userData.Gold})"
                        };
                    }
                    if (currency == PriceCurrency.Stellastone && userData.Stellastone < totalCost)
                    {
                        return new ShopPurchaseResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = $"성석이 부족합니다. (필요: {totalCost}, 보유: {userData.Stellastone})"
                        };
                    }
                    if (currency == PriceCurrency.Stardust && userData.Stardust < totalCost)
                    {
                        return new ShopPurchaseResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = $"별가루가 부족합니다. (필요: {totalCost}, 보유: {userData.Stardust})"
                        };
                    }

                    // 5. 재화 차감
                    if (currency == PriceCurrency.Gold) userData.Gold -= totalCost;
                    else if (currency == PriceCurrency.Stellastone) userData.Stellastone -= totalCost;
                    else if (currency == PriceCurrency.Stardust) userData.Stardust -= totalCost;

                    // 6. 스킨 / 이모티콘 / 골드 보상 적용
                    if (goldToGrant > 0)
                    {
                        userData.Gold += goldToGrant;
                    }

                    if (!string.IsNullOrEmpty(skinToGrant) && !userData.OwnedSkins.Contains(skinToGrant))
                    {
                        userData.OwnedSkins.Add(skinToGrant);
                    }

                    if (!string.IsNullOrEmpty(emoteToGrant) && !userData.OwnedEmotes.Contains(emoteToGrant))
                    {
                        userData.OwnedEmotes.Add(emoteToGrant);
                    }

                    List<string> obtainedCards = new List<string>();
                    Random rng = new Random();

                    // 7-1. 프리즘 카드 단품 / 프리즘 세트 구매 (ShopCategory.PrismCard)
                    if (product.category_Id == ShopCategory.PrismCard)
                    {
                        var prismCards = product.GetFixedCards();
                        if (prismCards.Count == 0)
                        {
                            var directCard = ServerCardDatabase.Instance.GetCard(product.productId);
                            if (directCard != null) prismCards.Add(directCard);
                        }

                        for (int q = 0; q < req.quantity; q++)
                        {
                            foreach (var card in prismCards)
                            {
                                if (!string.IsNullOrEmpty(card.CardID))
                                {
                                    obtainedCards.Add(card.CardID);
                                    userData.OwnedPrismCards[card.CardID] = userData.OwnedPrismCards.GetValueOrDefault(card.CardID, 0) + 1;
                                }
                            }
                        }
                    }
                    // 7-2. 일반 카드 낱장 구매 (ShopCategory.Cards)
                    else if (product.category_Id == ShopCategory.Cards)
                    {
                        var singleCards = product.GetFixedCards();
                        if (singleCards.Count == 0)
                        {
                            var directCard = ServerCardDatabase.Instance.GetCard(product.productId);
                            if (directCard != null) singleCards.Add(directCard);
                        }

                        for (int q = 0; q < req.quantity; q++)
                        {
                            foreach (var card in singleCards)
                            {
                                if (!string.IsNullOrEmpty(card.CardID))
                                {
                                    obtainedCards.Add(card.CardID);
                                    userData.OwnedCards[card.CardID] = userData.OwnedCards.GetValueOrDefault(card.CardID, 0) + 1;
                                }
                            }
                        }
                    }
                    // 7-3. 스타터 팩 / 완성 덱 패키지 (고정 지급 카드 목록 포함)
                    else if (product.fixedCardIds != null && product.fixedCardIds.Count > 0)
                    {
                        for (int q = 0; q < req.quantity; q++)
                        {
                            foreach (var cardId in product.fixedCardIds)
                            {
                                if (!string.IsNullOrEmpty(cardId))
                                {
                                    obtainedCards.Add(cardId);
                                    userData.OwnedCards[cardId] = userData.OwnedCards.GetValueOrDefault(cardId, 0) + 1;
                                }
                            }
                        }
                    }
                    // 7-4. 랜덤 티켓 / 확정 후보 뽑기 (randomPickPoolIds 중 randomPickCount개 무작위 추첨)
                    else if (product.randomPickPoolIds != null && product.randomPickPoolIds.Count > 0)
                    {
                        var pickPool = product.randomPickPoolIds;
                        int picksPerUnit = product.randomPickCount > 0 ? product.randomPickCount : 1;
                        int totalPicks = picksPerUnit * req.quantity;

                        for (int i = 0; i < totalPicks; i++)
                        {
                            string chosenCardId = pickPool[rng.Next(pickPool.Count)];
                            if (!string.IsNullOrEmpty(chosenCardId))
                            {
                                obtainedCards.Add(chosenCardId);
                                userData.OwnedCards[chosenCardId] = userData.OwnedCards.GetValueOrDefault(chosenCardId, 0) + 1;
                            }
                        }
                    }

                    // 7-5. 카드팩 구매 (ShopCategory.CardPack): 즉시 개봉하지 않고 유저의 OwnedPacks 인벤토리에 보관
                    if (product.category_Id == ShopCategory.CardPack)
                    {
                        userData.OwnedPacks[product.productId] = userData.OwnedPacks.GetValueOrDefault(product.productId, 0) + req.quantity;
                        Console.WriteLine($"🎁 [ShopService] 카드팩 획득(인벤토리 보관): 유저 {uid} | 팩ID: {product.productId} | 수량: {req.quantity} (총 보유: {userData.OwnedPacks[product.productId]}개)");
                    }
                    // 패키지 상품 중 고정카드나 랜덤티켓이 없는 경우에만 즉시 개봉 팩 롤링 적용
                    else if (product.category_Id == ShopCategory.Package && product.GetFixedCards().Count == 0 && (product.randomPickPoolIds == null || product.randomPickPoolIds.Count == 0))
                    {
                        var rolledCards = RollCardsForPack(product, req.quantity, userData);
                        obtainedCards.AddRange(rolledCards);
                    }

                    // 8. 구매 횟수 누적 기록
                    userData.PurchaseCounts[req.productId] = userData.PurchaseCounts.GetValueOrDefault(req.productId, 0) + req.quantity;

                    // 9. Firestore 유저 문서 저장 (트랜잭션 쓰기)
                    transaction.Set(userRef, userData, SetOptions.MergeAll);
                    Console.WriteLine($"✅ [ShopService] 상품 구매 완료(트랜잭션): 유저 {uid} | 상품ID: {req.productId} | 수량: {req.quantity} | 획득 카드 수: {obtainedCards.Count}");

                    PurchaseResponse response = new PurchaseResponse
                    {
                        status = "success",
                        message = "구매가 완료되었습니다.",
                        productId = req.productId,
                        quantity = req.quantity,
                        remainingGold = userData.Gold,
                        remainingStellastone = userData.Stellastone,
                        remainingStardust = userData.Stardust,
                        remainingPacks = (userData.OwnedPacks != null) ? userData.OwnedPacks.GetValueOrDefault(req.productId, 0) : 0,
                        obtainedCardIds = obtainedCards,
                        obtainedItemId = (product.category_Id == ShopCategory.CardPack) ? product.productId : (skinToGrant ?? emoteToGrant)
                    };

                    return new ShopPurchaseResult
                    {
                        Success = true,
                        StatusCode = 200,
                        Response = response
                    };
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ShopService] 상품 구매 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                return new ShopPurchaseResult
                {
                    Success = false,
                    StatusCode = 500,
                    ErrorMessage = "구매 처리 중 서버 오류가 발생했습니다."
                };
            }
        }

        /// <summary>
        /// 인벤토리/창고에서 보유한 카드팩을 개봉하여 카드를 추첨하고 유저 카드 컬렉션에 추가합니다.
        /// </summary>
        public async Task<ShopOpenPackResult> OpenPackAsync(string uid, OpenPackRequest req, FirestoreDb db)
        {
            try
            {
                if (string.IsNullOrEmpty(req.packProductId))
                {
                    return new ShopOpenPackResult { Success = false, StatusCode = 400, ErrorMessage = "개봉할 카드팩 ID가 필요합니다." };
                }

                int openCount = req.count > 0 ? req.count : 1;
                DocumentReference userRef = db.Collection("Users").Document(uid);

                // [C-02 동시성 최적화] 카드팩 개봉 동시성 트랜잭션 적용
                return await db.RunTransactionAsync(async transaction =>
                {
                    DocumentSnapshot userSnap = await transaction.GetSnapshotAsync(userRef);
                    if (!userSnap.Exists)
                    {
                        return new ShopOpenPackResult { Success = false, StatusCode = 404, ErrorMessage = "유저 정보를 찾을 수 없습니다." };
                    }

                    UserData? userData = userSnap.ConvertTo<UserData>();
                    if (userData == null)
                    {
                        return new ShopOpenPackResult { Success = false, StatusCode = 500, ErrorMessage = "유저 데이터 변환 실패" };
                    }

                    if (userData.OwnedPacks == null)
                    {
                        userData.OwnedPacks = new Dictionary<string, int>();
                    }
                    if (userData.OwnedCards == null)
                    {
                        userData.OwnedCards = new Dictionary<string, int>();
                    }

                    int currentPacks = userData.OwnedPacks.GetValueOrDefault(req.packProductId, 0);
                    if (currentPacks < openCount)
                    {
                        return new ShopOpenPackResult { Success = false, StatusCode = 400, ErrorMessage = "개봉할 카드팩이 부족합니다." };
                    }

                    // 상품 DB에서 팩 정보 조회 (없으면 기본 설정)
                    ProductData? product = ServerProductDatabase.Instance.GetProduct(req.packProductId);
                    if (product == null)
                    {
                        Console.WriteLine($"⚠️ [ShopService] ServerProductDatabase에서 '{req.packProductId}'를 찾지 못하여 기본 카드팩 설정으로 폴백합니다.");
                        product = new ProductData
                        {
                            productId = req.packProductId,
                            productName = "기본 카드팩",
                            category_Id = ShopCategory.CardPack,
                            cardsPerPack = 5
                        };
                    }

                    // 팩 차감
                    userData.OwnedPacks[req.packProductId] = currentPacks - openCount;

                    // 카드 추첨 및 지급
                    List<string> obtainedCards = RollCardsForPack(product, openCount, userData);

                    // Firestore 트랜잭션 저장
                    transaction.Set(userRef, userData, SetOptions.MergeAll);

                    Console.WriteLine($"🎉 [ShopService] 카드팩 개봉 성공(트랜잭션): 유저 {uid} | 팩ID: {req.packProductId} | 개봉수: {openCount} | 남은팩: {userData.OwnedPacks[req.packProductId]} | 획득 카드: [{string.Join(", ", obtainedCards)}]");

                    return new ShopOpenPackResult
                    {
                        Success = true,
                        StatusCode = 200,
                        Response = new OpenPackResponse
                        {
                            status = "success",
                            message = "카드팩 개봉 완료!",
                            packProductId = req.packProductId,
                            remainingPacks = userData.OwnedPacks[req.packProductId],
                            obtainedCardIds = obtainedCards
                        }
                    };
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ShopService] 카드팩 개봉 중 오류: {ex.Message}\n{ex.StackTrace}");
                return new ShopOpenPackResult
                {
                    Success = false,
                    StatusCode = 500,
                    ErrorMessage = $"개봉 처리 중 서버 오류: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 카드팩 추첨 룰(확장팩 필터, 직업 필터, 레어도 보장 등)에 따라 카드를 추첨하고 유저의 OwnedCards에 추가합니다.
        /// </summary>
        private static List<string> RollCardsForPack(ProductData product, int packCountToOpen, UserData userData)
        {
            List<string> obtainedCards = new List<string>();
            Random rng = new Random();
            int cardsPerPack = product.cardsPerPack > 0 ? product.cardsPerPack : 5;

            // 1) 기본 카드 풀 구성 (커스텀 풀 우선 -> 없으면 전체 카드 목록)
            List<ServerCardData> cardPool = product.GetCustomCardPool();
            if (cardPool.Count == 0)
            {
                cardPool = ServerCardDatabase.Instance.GetAllCards();
            }

            // 토큰/소환 전용 카드는 팩 추첨 풀에서 제외
            cardPool = cardPool.Where(c => !c.IsToken).ToList();

            // 2) 확장팩 필터링
            if (product.targetExpansion.HasValue)
            {
                cardPool = cardPool.Where(c => c.Expansion == product.targetExpansion.Value).ToList();
            }

            // 3) 직업 필터링 (다중 직업 지원)
            var allowedClasses = product.GetTargetClasses();
            if (allowedClasses.Count > 0)
            {
                cardPool = cardPool.Where(c => c.Class.HasValue && allowedClasses.Contains(c.Class.Value)).ToList();
            }

            Console.WriteLine($"🃏 [ShopService.RollCardsForPack] 상품 '{product.productId}' ('{product.productName}') 카드 풀 구성 완료 | 확장팩: {(product.targetExpansion.HasValue ? product.targetExpansion.Value.ToString() : "전체")} | 직업: {(allowedClasses.Count > 0 ? string.Join(",", allowedClasses) : "전체")} | 유효 카드 수: {cardPool.Count}장");

            if (cardPool.Count == 0)
            {
                Console.WriteLine($"⚠️ [ShopService] 상품 '{product.productId}'에 지정된 조건의 카드 풀이 비어있습니다.");
                return obtainedCards;
            }

            var commonCards = cardPool.Where(c => c.Rarity == CardRarity.common).ToList();
            var rareCards = cardPool.Where(c => c.Rarity == CardRarity.rare).ToList();
            var epicCards = cardPool.Where(c => c.Rarity == CardRarity.epic).ToList();
            var legCards = cardPool.Where(c => c.Rarity == CardRarity.legendary).ToList();

            int legGuaranteed = Math.Max(0, product.guaranteedLegendaryCount);
            int epicGuaranteed = Math.Max(0, product.guaranteedEpicCount);
            int rareGuaranteed = Math.Max(0, product.guaranteedRareCount);
            int totalGuaranteed = legGuaranteed + epicGuaranteed + rareGuaranteed;

            if (totalGuaranteed > cardsPerPack)
            {
                cardsPerPack = totalGuaranteed;
            }

            int normalRollSlots = cardsPerPack - totalGuaranteed;

            for (int p = 0; p < packCountToOpen; p++)
            {
                // 🌟 1) 전설 확정 슬롯 추첨
                for (int i = 0; i < legGuaranteed; i++)
                {
                    ServerCardData? chosen = (legCards.Count > 0) 
                        ? legCards[rng.Next(legCards.Count)] 
                        : (cardPool.Count > 0 ? cardPool[rng.Next(cardPool.Count)] : null);

                    AddCardToResult(chosen, obtainedCards, userData);
                }

                // 💜 2) 특급(에픽) 확정 슬롯 추첨
                for (int i = 0; i < epicGuaranteed; i++)
                {
                    ServerCardData? chosen = (epicCards.Count > 0) 
                        ? epicCards[rng.Next(epicCards.Count)] 
                        : (cardPool.Count > 0 ? cardPool[rng.Next(cardPool.Count)] : null);

                    AddCardToResult(chosen, obtainedCards, userData);
                }

                // 💙 3) 희귀(레어) 확정 슬롯 추첨
                for (int i = 0; i < rareGuaranteed; i++)
                {
                    ServerCardData? chosen = (rareCards.Count > 0) 
                        ? rareCards[rng.Next(rareCards.Count)] 
                        : (cardPool.Count > 0 ? cardPool[rng.Next(cardPool.Count)] : null);

                    AddCardToResult(chosen, obtainedCards, userData);
                }

                // 🎲 4) 나머지 일반 슬롯 추첨 (기본 확률 테이블 적용)
                for (int i = 0; i < normalRollSlots; i++)
                {
                    CardRarity targetRarity = RollRandomRarity(rng);
                    ServerCardData? chosen = null;

                    switch (targetRarity)
                    {
                        case CardRarity.legendary:
                            if (legCards.Count > 0) chosen = legCards[rng.Next(legCards.Count)];
                            break;
                        case CardRarity.epic:
                            if (epicCards.Count > 0) chosen = epicCards[rng.Next(epicCards.Count)];
                            break;
                        case CardRarity.rare:
                            if (rareCards.Count > 0) chosen = rareCards[rng.Next(rareCards.Count)];
                            break;
                        case CardRarity.common:
                            if (commonCards.Count > 0) chosen = commonCards[rng.Next(commonCards.Count)];
                            break;
                    }

                    // 해당 희귀도의 카드가 풀에 없을 경우(예: 전설 카드가 없는 팩), 전체 유효 카드 풀에서 추첨
                    if (chosen == null)
                    {
                        chosen = cardPool.Count > 0 ? cardPool[rng.Next(cardPool.Count)] : null;
                    }

                    AddCardToResult(chosen, obtainedCards, userData);
                }
            }

            return obtainedCards;
        }

        private static void AddCardToResult(ServerCardData? card, List<string> obtainedCards, UserData userData)
        {
            if (card != null && !string.IsNullOrEmpty(card.CardID))
            {
                obtainedCards.Add(card.CardID);
                userData.OwnedCards[card.CardID] = userData.OwnedCards.GetValueOrDefault(card.CardID, 0) + 1;
            }
        }

        /// <summary>
        /// 유저가 보유한 카드를 분해하여 카드 수량을 차감하고 스타더스트를 지급합니다.
        /// </summary>
        public async Task<ShopDisenchantResult> DisenchantCardAsync(string uid, DisenchantCardRequest req, FirestoreDb db)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.cardId))
            {
                return new ShopDisenchantResult
                {
                    Success = false,
                    StatusCode = 400,
                    ErrorMessage = "잘못된 카드 분해 요청입니다."
                };
            }

            int countToDisenchant = req.count > 0 ? req.count : 1;

            try
            {
                DocumentReference userRef = db.Collection("Users").Document(uid);

                // [C-02 동시성 최적화] 카드 분해 동시성 트랜잭션 적용
                return await db.RunTransactionAsync(async transaction =>
                {
                    // 1. 유저 정보 조회
                    DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(userRef);
                    if (!snapshot.Exists)
                    {
                        return new ShopDisenchantResult
                        {
                            Success = false,
                            StatusCode = 404,
                            ErrorMessage = "유저 정보를 찾을 수 없습니다."
                        };
                    }

                    UserData userData = snapshot.ConvertTo<UserData>();
                    if (userData.OwnedCards == null)
                    {
                        userData.OwnedCards = new Dictionary<string, int>();
                    }

                    // 2. 카드 메타데이터 확인
                    ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(req.cardId);
                    if (cardData == null)
                    {
                        return new ShopDisenchantResult
                        {
                            Success = false,
                            StatusCode = 404,
                            ErrorMessage = $"카드 정보를 찾을 수 없습니다. (ID: {req.cardId})"
                        };
                    }

                    // 2-1. 기본 카드 분해 차단 검증
                    if (cardData.IsDefaultCard)
                    {
                        return new ShopDisenchantResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = "기본 카드는 분해할 수 없습니다."
                        };
                    }

                    // 3. 유저 보유 수량 검증
                    int currentOwned = userData.OwnedCards.GetValueOrDefault(req.cardId, 0);
                    if (currentOwned < countToDisenchant)
                    {
                        return new ShopDisenchantResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = $"분해할 카드를 충분히 보유하고 있지 않습니다. (보유: {currentOwned}장, 요청: {countToDisenchant}장)"
                        };
                    }

                    // 4. 수량 차감 및 스타더스트 가산
                    int remainingCount = currentOwned - countToDisenchant;
                    userData.OwnedCards[req.cardId] = remainingCount;

                    int dustPerCard = GetDisenchantDustAmount(cardData.Rarity);
                    int totalGainedDust = dustPerCard * countToDisenchant;
                    userData.Stardust += totalGainedDust;

                    // 5. Firestore 트랜잭션 저장
                    transaction.Set(userRef, userData, SetOptions.MergeAll);
                    Console.WriteLine($"🔨 [ShopService] 카드 분해 완료(트랜잭션): 유저 {uid} | 카드: {cardData.Name}({req.cardId}) | 분해수: {countToDisenchant} | 획득 가루: +{totalGainedDust} | 남은 카드: {remainingCount}장 | 총 가루: {userData.Stardust}");

                    DisenchantCardResponse response = new DisenchantCardResponse
                    {
                        status = "success",
                        message = $"'{cardData.Name}' 카드가 분해되었습니다.",
                        cardId = req.cardId,
                        disenchantedCount = countToDisenchant,
                        gainedStardust = totalGainedDust,
                        remainingCardCount = remainingCount,
                        remainingStardust = userData.Stardust
                    };

                    return new ShopDisenchantResult
                    {
                        Success = true,
                        StatusCode = 200,
                        Response = response
                    };
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ShopService] 카드 분해 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                return new ShopDisenchantResult
                {
                    Success = false,
                    StatusCode = 500,
                    ErrorMessage = "카드 분해 처리 중 서버 오류가 발생했습니다."
                };
            }
        }

        /// <summary>
        /// 스타더스트를 소모하여 유저가 원하는 카드를 제작하고 인벤토리에 추가합니다.
        /// </summary>
        public async Task<ShopCraftResult> CraftCardAsync(string uid, CraftCardRequest req, FirestoreDb db)
        {
            if (req == null || string.IsNullOrWhiteSpace(req.cardId))
            {
                return new ShopCraftResult
                {
                    Success = false,
                    StatusCode = 400,
                    ErrorMessage = "잘못된 카드 제작 요청입니다."
                };
            }

            int countToCraft = req.count > 0 ? req.count : 1;

            try
            {
                DocumentReference userRef = db.Collection("Users").Document(uid);

                // 카드 제작 동시성 트랜잭션 적용
                return await db.RunTransactionAsync(async transaction =>
                {
                    // 1. 유저 정보 조회
                    DocumentSnapshot snapshot = await transaction.GetSnapshotAsync(userRef);
                    if (!snapshot.Exists)
                    {
                        return new ShopCraftResult
                        {
                            Success = false,
                            StatusCode = 404,
                            ErrorMessage = "유저 정보를 찾을 수 없습니다."
                        };
                    }

                    UserData userData = snapshot.ConvertTo<UserData>();
                    if (userData.OwnedCards == null)
                    {
                        userData.OwnedCards = new Dictionary<string, int>();
                    }

                    // 2. 카드 메타데이터 확인
                    ServerCardData? cardData = ServerCardDatabase.Instance.GetCardData(req.cardId);
                    if (cardData == null)
                    {
                        return new ShopCraftResult
                        {
                            Success = false,
                            StatusCode = 404,
                            ErrorMessage = $"카드 정보를 찾을 수 없습니다. (ID: {req.cardId})"
                        };
                    }

                    // 3. 필요 스타더스트 계산 및 검증
                    int dustPerCard = GetCraftDustAmount(cardData.Rarity);
                    int totalRequiredDust = dustPerCard * countToCraft;

                    if (userData.Stardust < totalRequiredDust)
                    {
                        return new ShopCraftResult
                        {
                            Success = false,
                            StatusCode = 400,
                            ErrorMessage = $"스타더스트가 부족합니다. (보유: {userData.Stardust}, 필요: {totalRequiredDust})"
                        };
                    }

                    // 4. 스타더스트 차감 및 카드 수량 가산
                    userData.Stardust -= totalRequiredDust;
                    int currentOwned = userData.OwnedCards.GetValueOrDefault(req.cardId, 0);
                    int remainingCount = currentOwned + countToCraft;
                    userData.OwnedCards[req.cardId] = remainingCount;

                    // 5. Firestore 트랜잭션 저장
                    transaction.Set(userRef, userData, SetOptions.MergeAll);
                    Console.WriteLine($"✨ [ShopService] 카드 제작 완료(트랜잭션): 유저 {uid} | 카드: {cardData.Name}({req.cardId}) | 제작수: {countToCraft} | 소모 가루: -{totalRequiredDust} | 보유 카드: {remainingCount}장 | 남은 가루: {userData.Stardust}");

                    CraftCardResponse response = new CraftCardResponse
                    {
                        status = "success",
                        message = $"'{cardData.Name}' 카드를 제작했습니다.",
                        cardId = req.cardId,
                        craftedCount = countToCraft,
                        consumedStardust = totalRequiredDust,
                        remainingCardCount = remainingCount,
                        remainingStardust = userData.Stardust
                    };

                    return new ShopCraftResult
                    {
                        Success = true,
                        StatusCode = 200,
                        Response = response
                    };
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [ShopService] 카드 제작 처리 중 오류: {ex.Message}\n{ex.StackTrace}");
                return new ShopCraftResult
                {
                    Success = false,
                    StatusCode = 500,
                    ErrorMessage = "카드 제작 처리 중 서버 오류가 발생했습니다."
                };
            }
        }
    }
}
