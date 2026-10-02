using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Linq;
using GameServer;

namespace GameServer.Shop
{
    /// <summary>
    /// 상점 카테고리 / UI 탭 대분류
    /// </summary>
    public enum ShopCategory
    {
        LeaderSkin = 0, // 리더 스킨
        Emote = 1,      // 이모티콘
        CardBack = 2,   // 카드 뒷면
        MapDecor = 3,   // 맵 꾸미기
        PrismCard = 4,  // 프리즘 카드
        Profile = 5,    // 프로필
        SeasonPass = 6, // 시즌 패스
        Package = 7,    // 패키지
        CardPack = 8,   // 카드 팩
        Cards = 9,      // 카드 낱장
    }

    /// <summary>
    /// 결제/구매에 사용되는 화폐 종류
    /// </summary>
    public enum PriceCurrency
    {
        Gold = 0,        // 골드 (일반 인게임 재화)
        Stellastone = 1, // 성석 (유료 결제 재화)
        Stardust = 2     // 별가루 (카드 제작/분해 재화)
    }

    /// <summary>
    /// Firestore에 저장되는 상점 상품 데이터 모델
    /// </summary>
    [FirestoreData]
    public class ProductData
    {
        [FirestoreProperty]
        public ShopCategory category_Id { get; set; }           // 카테고리 (LeaderSkin, Emote, CardPack, Package 등)

        [FirestoreProperty]
        public string productId { get; set; } = string.Empty;   // 아이템 고유 아이디

        [FirestoreProperty]
        public string productName { get; set; } = string.Empty; // 아이템 이름

        [FirestoreProperty]
        public string description { get; set; } = string.Empty; // 아이템 설명

        [FirestoreProperty]
        public string image_url { get; set; } = string.Empty;   // 아이템 이미지 위치

        [FirestoreProperty]
        public int price { get; set; }                          // 아이템 가격

        [FirestoreProperty]
        public PriceCurrency currency { get; set; }             // 결제 재화 종류 (Gold = 0, Stellastone = 1, Stardust = 2)

        [FirestoreProperty]
        public bool isActive { get; set; } = true;              // 판매 활성화 여부

        [FirestoreProperty]
        public string sale_Start_Date { get; set; } = "2026-01-01 00:00:00"; // 판매/할인 시작일

        [FirestoreProperty]
        public string sale_End_Date { get; set; } = "2099-12-31 23:59:59";   // 판매/할인 종료기간

        // 1. 특정 확장팩 필터 (예: 기본 등)
        [FirestoreProperty("targetExpansion", ConverterType = typeof(FirestoreEnumNameConverter<CardExpansion>))]
        public CardExpansion? targetExpansion { get; set; }

        // 2. 다중 직업 필터 (예: ["Gangzi", "Yuni"])
        [FirestoreProperty]
        public List<string>? targetClasses { get; set; }

        // 3. 확정 지급 카드 목록 (스타터 팩 / 완성 덱 패키지용 카드 ID 리스트)
        [FirestoreProperty]
        public List<string>? fixedCardIds { get; set; }

        // 4. 특정 후보 목록 중 랜덤 N장 추첨 (랜덤 티켓 / 보너스 랜덤 카드용)
        [FirestoreProperty]
        public List<string>? randomPickPoolIds { get; set; }

        [FirestoreProperty]
        public int randomPickCount { get; set; } = 1;

        // 5. 커스텀 카드 풀 (특정 픽업/이벤트 전용 카드 ID 리스트)
        [FirestoreProperty]
        public List<string>? customCardPoolIds { get; set; }

        // 6. 팩당 지급 카드 장수 (기본값: 5장)
        [FirestoreProperty]
        public int cardsPerPack { get; set; } = 5;

        // 7. 카드팩 내 레어도별 확정 보장 수량 (예: 전설 1장, 에픽 2장 확정 등)
        [FirestoreProperty]
        public int guaranteedLegendaryCount { get; set; } = 0;

        [FirestoreProperty]
        public int guaranteedEpicCount { get; set; } = 0;

        [FirestoreProperty]
        public int guaranteedRareCount { get; set; } = 0;

        // 8. 계정당 최대 구매 가능 횟수 (0: 무제한, 1: 1회 한정, N: N회 한정)
        [FirestoreProperty]
        public int purchaseLimit { get; set; } = 0;

        [FirestoreProperty]
        public bool isVisibleInShop { get; set; } = true;                     // 상점 진열 노출 여부 (false: 기본스킨, 비매품, 비공개 상품)

        // --- C# 편의 헬퍼 메서드 ---

        /// <summary>
        /// targetClasses 문자열 목록을 CardClass Enum 리스트로 변환하여 반환합니다.
        /// 영문(Yuni, Gangzi, Huya) 및 한글(유니, 강지, 후야) 모두 지원합니다.
        /// </summary>
        public List<CardClass> GetTargetClasses()
        {
            if (targetClasses == null || targetClasses.Count == 0) return new List<CardClass>();

            var list = new List<CardClass>();
            foreach (var clsStr in targetClasses)
            {
                if (string.IsNullOrWhiteSpace(clsStr)) continue;

                if (Enum.TryParse<CardClass>(clsStr.Trim(), true, out var cls))
                {
                    if (!list.Contains(cls)) list.Add(cls);
                }
                else
                {
                    string trimmed = clsStr.Trim();
                    if (trimmed == "강지" || trimmed.Equals("Gangzi", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!list.Contains(CardClass.Gangzi)) list.Add(CardClass.Gangzi);
                    }
                    else if (trimmed == "유니" || trimmed.Equals("Yuni", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!list.Contains(CardClass.Yuni)) list.Add(CardClass.Yuni);
                    }
                    else if (trimmed == "후야" || trimmed.Equals("Huya", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!list.Contains(CardClass.Huya)) list.Add(CardClass.Huya);
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// fixedCardIds에 해당하는 ServerCardData 목록을 카드 DB에서 조회하여 반환합니다.
        /// </summary>
        public List<ServerCardData> GetFixedCards()
        {
            if (fixedCardIds == null || fixedCardIds.Count == 0) return new List<ServerCardData>();
            return fixedCardIds
                .Select(id => ServerCardDatabase.Instance.GetCard(id))
                .Where(card => card != null)
                .ToList()!;
        }

        /// <summary>
        /// randomPickPoolIds에 해당하는 ServerCardData 목록을 카드 DB에서 조회하여 반환합니다.
        /// </summary>
        public List<ServerCardData> GetRandomPickPool()
        {
            if (randomPickPoolIds == null || randomPickPoolIds.Count == 0) return new List<ServerCardData>();
            return randomPickPoolIds
                .Select(id => ServerCardDatabase.Instance.GetCard(id))
                .Where(card => card != null)
                .ToList()!;
        }

        /// <summary>
        /// customCardPoolIds에 해당하는 ServerCardData 목록을 카드 DB에서 조회하여 반환합니다.
        /// </summary>
        public List<ServerCardData> GetCustomCardPool()
        {
            if (customCardPoolIds == null || customCardPoolIds.Count == 0) return new List<ServerCardData>();
            return customCardPoolIds
                .Select(id => ServerCardDatabase.Instance.GetCard(id))
                .Where(card => card != null)
                .ToList()!;
        }
    }

    /// <summary>
    /// 클라이언트 -> 서버 상품 구매 요청 DTO
    /// </summary>
    public class PurchaseRequest
    {
        public string productId { get; set; } = string.Empty;
        public int quantity { get; set; } = 1;

        // 하위 호환용 amount 프로퍼티
        public int amount
        {
            get => quantity;
            set => quantity = value;
        }
    }

    /// <summary>
    /// 서버 -> 클라이언트 상품 구매 응답 DTO
    /// </summary>
    public class PurchaseResponse
    {
        public string status { get; set; } = "success";         // "success" | "error"
        public string message { get; set; } = string.Empty;
        public string productId { get; set; } = string.Empty;
        public int quantity { get; set; }
        public int remainingGold { get; set; }
        public int remainingStellastone { get; set; }
        public int remainingStardust { get; set; }
        public int remainingPacks { get; set; }
        public List<string> obtainedCardIds { get; set; } = new List<string>();
        public string? obtainedItemId { get; set; }
    }

    /// <summary>
    /// 클라이언트 -> 서버 카드팩 개봉 요청 DTO
    /// </summary>
    public class OpenPackRequest
    {
        public string packProductId { get; set; } = string.Empty;
        public int count { get; set; } = 1;
    }

    /// <summary>
    /// 서버 -> 클라이언트 카드팩 개봉 응답 DTO
    /// </summary>
    public class OpenPackResponse
    {
        public string status { get; set; } = "success";         // "success" | "error"
        public string message { get; set; } = string.Empty;
        public string packProductId { get; set; } = string.Empty;
        public int remainingPacks { get; set; }
        public List<string> obtainedCardIds { get; set; } = new List<string>();
    }

    /// <summary>
    /// 카드팩 개봉 서비스 처리 결과 모델
    /// </summary>
    public class ShopOpenPackResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public string ErrorMessage { get; set; } = string.Empty;
        public OpenPackResponse? Response { get; set; }
    }

    /// <summary>
    /// ShopService 내부 처리 결과 모델
    /// </summary>
    public class ShopPurchaseResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public string ErrorMessage { get; set; } = string.Empty;
        public PurchaseResponse? Response { get; set; }
    }

    /// <summary>
    /// 클라이언트 -> 서버 카드 분해 요청 DTO
    /// </summary>
    public class DisenchantCardRequest
    {
        public string cardId { get; set; } = string.Empty;
        public int count { get; set; } = 1;
    }

    /// <summary>
    /// 서버 -> 클라이언트 카드 분해 응답 DTO
    /// </summary>
    public class DisenchantCardResponse
    {
        public string status { get; set; } = "success";         // "success" | "error"
        public string message { get; set; } = string.Empty;
        public string cardId { get; set; } = string.Empty;
        public int disenchantedCount { get; set; }
        public int gainedStardust { get; set; }
        public int remainingCardCount { get; set; }
        public int remainingStardust { get; set; }
    }

    /// <summary>
    /// 카드 분해 서비스 처리 결과 모델
    /// </summary>
    public class ShopDisenchantResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public string ErrorMessage { get; set; } = string.Empty;
        public DisenchantCardResponse? Response { get; set; }
    }

    /// <summary>
    /// 클라이언트 -> 서버 카드 제작 요청 DTO
    /// </summary>
    public class CraftCardRequest
    {
        public string cardId { get; set; } = string.Empty;
        public int count { get; set; } = 1;
    }

    /// <summary>
    /// 서버 -> 클라이언트 카드 제작 응답 DTO
    /// </summary>
    public class CraftCardResponse
    {
        public string status { get; set; } = "success";         // "success" | "error"
        public string message { get; set; } = string.Empty;
        public string cardId { get; set; } = string.Empty;
        public int craftedCount { get; set; }
        public int consumedStardust { get; set; }
        public int remainingCardCount { get; set; }
        public int remainingStardust { get; set; }
    }

    /// <summary>
    /// 카드 제작 서비스 처리 결과 모델
    /// </summary>
    public class ShopCraftResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; } = 200;
        public string ErrorMessage { get; set; } = string.Empty;
        public CraftCardResponse? Response { get; set; }
    }

    /// <summary>
    /// 클라이언트로 전달되는 상점 상품 DTO (유저 맞춤 구매 현황 및 잔여 수량 포함)
    /// </summary>
    public class ShopProductDto
    {
        public ShopCategory category_Id { get; set; }
        public string productId { get; set; } = string.Empty;
        public string productName { get; set; } = string.Empty;
        public string description { get; set; } = string.Empty;
        public string image_url { get; set; } = string.Empty;
        public int price { get; set; }
        public PriceCurrency currency { get; set; }
        public bool isActive { get; set; } = true;
        public string sale_Start_Date { get; set; } = "2026-01-01 00:00:00";
        public string sale_End_Date { get; set; } = "2099-12-31 23:59:59";
        public CardExpansion? targetExpansion { get; set; }
        public List<string>? targetClasses { get; set; }
        public List<string>? fixedCardIds { get; set; }
        public List<string>? randomPickPoolIds { get; set; }
        public int randomPickCount { get; set; } = 1;
        public List<string>? customCardPoolIds { get; set; }
        public int cardsPerPack { get; set; } = 5;
        public int guaranteedLegendaryCount { get; set; }
        public int guaranteedEpicCount { get; set; }
        public int guaranteedRareCount { get; set; }
        public int purchaseLimit { get; set; }

        // --- 유저 맞춤 구매 현황 정보 ---
        public int myPurchaseCount { get; set; } = 0;              // 내가 구매한 누적 횟수
        public int remainingPurchaseLimit { get; set; } = -1;       // 남은 구매 가능 횟수 (-1: 무제한, 0: 품절)
        public bool isPurchasable { get; set; } = true;            // 구매 가능 여부

        public ShopProductDto() { }

        public ShopProductDto(ProductData product, UserData? user = null)
        {
            category_Id = product.category_Id;
            productId = product.productId;
            productName = product.productName;
            description = product.description;
            image_url = product.image_url;
            price = product.price;
            currency = product.currency;
            isActive = product.isActive;
            sale_Start_Date = product.sale_Start_Date;
            sale_End_Date = product.sale_End_Date;
            targetExpansion = product.targetExpansion;
            targetClasses = product.targetClasses;
            fixedCardIds = product.fixedCardIds;
            randomPickPoolIds = product.randomPickPoolIds;
            randomPickCount = product.randomPickCount;
            customCardPoolIds = product.customCardPoolIds;
            cardsPerPack = product.cardsPerPack;
            guaranteedLegendaryCount = product.guaranteedLegendaryCount;
            guaranteedEpicCount = product.guaranteedEpicCount;
            guaranteedRareCount = product.guaranteedRareCount;
            purchaseLimit = product.purchaseLimit;

            if (user != null)
            {
                // 스킨 중복 보유 확인
                if (product.category_Id == ShopCategory.LeaderSkin && user.OwnedSkins.Contains(product.productId))
                {
                    myPurchaseCount = 1;
                    remainingPurchaseLimit = 0;
                    isPurchasable = false;
                }
                // 이모티콘 중복 보유 확인
                else if (product.category_Id == ShopCategory.Emote && user.OwnedEmotes.Contains(product.productId))
                {
                    myPurchaseCount = 1;
                    remainingPurchaseLimit = 0;
                    isPurchasable = false;
                }
                // 구매 제한 상품인 경우
                else if (product.purchaseLimit > 0)
                {
                    myPurchaseCount = user.PurchaseCounts.GetValueOrDefault(product.productId, 0);
                    remainingPurchaseLimit = Math.Max(0, product.purchaseLimit - myPurchaseCount);
                    isPurchasable = remainingPurchaseLimit > 0;
                }
                // 무제한 상품인 경우
                else
                {
                    myPurchaseCount = user.PurchaseCounts.GetValueOrDefault(product.productId, 0);
                    remainingPurchaseLimit = -1; // 무제한
                    isPurchasable = true;
                }
            }
            else
            {
                // 비로그인 상태 기본값
                myPurchaseCount = 0;
                remainingPurchaseLimit = product.purchaseLimit > 0 ? product.purchaseLimit : -1;
                isPurchasable = true;
            }
        }
    }
}
