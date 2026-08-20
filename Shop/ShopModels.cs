using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace GameServer.Shop
{
    /// <summary>
    /// 상점 카테고리 / UI 탭 대분류
    /// </summary>
    public enum ShopCategory
    {
        LeaderSkin = 0, // 리더 스킨 (Leader Skins)
        Emote = 1,      // 이모티콘 (Emotes)
        CardBack = 2,   // 카드 뒷면 (Card Backs)
        MapDecor = 3,   // 맵 꾸미기 (Map Decors)
        PrismCard = 4,  // 프리즘 카드 (Prism Cards)
        Profile = 5,    // 프로필 (Profile)
        SeasonPass = 6, // 시즌 패스 (Season Pass)
        Package = 7,     // 패키지 (Packages)
        CardPack = 8,       // 카드 팩
        Cards = 9,          // 카드 낱장
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
        public ShopCategory category_Id { get; set; }           // 카테고리 (LeaderSkin, Emote, CardBack 등)

        [FirestoreProperty]
        public string productId { get; set; } = string.Empty;   // 아이템 고유 아이디 ("nomal_Card_1000")

        [FirestoreProperty]
        public string productName { get; set; } = string.Empty; // 아이템 이름 ("강도단")

        [FirestoreProperty]
        public string description { get; set; } = string.Empty; // 아이템 설명

        [FirestoreProperty]
        public string image_url { get; set; } = string.Empty;   // 아이템 이미지 위치

        [FirestoreProperty]
        public int price { get; set; }                          // 아이템 가격 (100)

        [FirestoreProperty]
        public PriceCurrency currency { get; set; }             // 결제 재화 종류 (Gold = 0, Stellastone = 1, Stardust = 2)

        [FirestoreProperty]
        public bool isActive { get; set; } = true;              // 판매 활성화 여부

        [FirestoreProperty]
        public string? sale_Start_Date { get; set; }            // 할인 시작일

        [FirestoreProperty]
        public string? sale_End_Date { get; set; }              // 할인 종료기간
    }

    /// <summary>
    /// 획득한 보상 단위 정보 (카드, 스킨, 이모티콘, 재화 등 범용 보상 모델)
    /// </summary>
    public class RewardItem
    {
        public ShopCategory itemCategory { get; set; } = ShopCategory.Cards;    // "Card", "Skin", "Emote", "CardBack", "Gold" 등
        public string ItemId { get; set; } = string.Empty;      // "skin_01", "card_1001", "emote_03" 등
        public int Count { get; set; } = 1;                     // 수량
    }

    /// <summary>
    /// 클라이언트 -> 서버 상품 구매 요청 DTO
    /// </summary>
    public class PurchaseRequest
    {
        public string productId { get; set; } = string.Empty;
        public int amount { get; set; } = 1;
    }

    /// <summary>
    /// 서버 -> 클라이언트 상품 구매 응답 DTO
    /// </summary>
    public class PurchaseResponse
    {
        public bool success { get; set; }
        public string message { get; set; } = string.Empty;
        public int remainingCurrency { get; set; }              // 결제 후 남은 잔액
        public PriceCurrency currency { get; set; }             // 결제에 사용된 재화
        public List<RewardItem>? rewards { get; set; }          // 획득한 보상 목록
        public List<string>? rewardedCardIds { get; set; }      // (호환용) 획득한 카드 ID 목록
        public int rewardedGold { get; set; }                   // (호환용) 획득한 골드량
    }
}
