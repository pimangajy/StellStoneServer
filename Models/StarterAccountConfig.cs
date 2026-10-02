using System.Collections.Generic;

namespace GameServer.Models
{
    /// <summary>
    /// 신규 계정 생성(회원가입) 시 지급되는 초기 재화 및 기본 보유 카드 설정 클래스입니다.
    /// 카드 ID와 수량을 원하는 대로 자유롭게 채워 넣으시면 됩니다.
    /// </summary>
    public static class StarterAccountConfig
    {
        // =========================================================================
        // 1. 초기 재화 설정 (각 500씩 지급)
        // =========================================================================
        public const int InitialGold = 500;         // 골드
        public const int InitialStardust = 500;     // 스타더스트
        public const int InitialStellastone = 500;  // 스텔라스톤

        // =========================================================================
        // 2. 기본 덱 설정
        // =========================================================================
        public const string InitialDeckId = "starter_deck_1";
        public const string InitialDeckName = "기본 덱";
        public const string InitialDeckClass = "Gangzi"; // 기본 직업 (예: Gangzi, Huya, Yuni 등)

        /// <summary>
        /// 계정 생성 시 지급할 보유 카드 목록 (카드 ID : 지급 장수)
        /// 💡 아래 딕셔너리에 원하는 카드 ID와 지급할 수량을 채워 넣으세요!
        /// </summary>
        public static Dictionary<string, int> GetStarterCards()
        {
            return new Dictionary<string, int>
            {
                // 강지 직업 기본 지급 카드
                { "cards-gangzi-001", 2 },                { "cards-gangzi-002", 2 },
                { "cards-gangzi-004", 2 },                { "cards-gangzi-005", 1 },
                { "cards-gangzi-006", 2 },                { "cards-gangzi-007", 2 },
                { "cards-gangzi-009", 1 },                { "cards-gangzi-010", 1 },
                { "cards-gangzi-011", 1 },                { "cards-gangzi-013", 1 },
                { "cards-gangzi-014", 1 },                { "cards-gangzi-016", 1 },
                { "cards-gangzi-017", 2 },                { "cards-gangzi-018", 1 },
                { "cards-gangzi-019", 1 },                { "cards-gangzi-020", 1 },
                { "cards-gangzi-021", 1 },

                // 유니 직업 기본 지급 카드
                { "cards-yuni-001", 2 },                { "cards-yuni-002", 2 },
                { "cards-yuni-003", 2 },                { "cards-yuni-005", 2 },
                { "cards-yuni-006", 2 },                { "cards-yuni-008", 1 },
                { "cards-yuni-010", 2 },                { "cards-yuni-013", 1 },
                { "cards-yuni-014", 1 },                { "cards-yuni-015", 2 },
                { "cards-yuni-017", 1 },                { "cards-yuni-018", 1 },
                { "cards-yuni-019", 1 },                { "cards-yuni-020", 1 },
                { "cards-yuni-021", 1 },

                // 후야 직업 기본 지급 카드
                { "cards-huya-001", 2 },                { "cards-huya-002", 2 },
                { "cards-huya-003", 1 },                { "cards-huya-004", 2 },
                { "cards-huya-005", 1 },                { "cards-huya-006", 2 },
                { "cards-huya-007", 2 },                { "cards-huya-008", 1 },
                { "cards-huya-010", 1 },                { "cards-huya-013", 1 },
                { "cards-huya-015", 1 },                { "cards-huya-016", 1 },
                { "cards-huya-017", 1 },                { "cards-huya-018", 1 },
                { "cards-huya-021", 1 }
            };
        }

        /// <summary>
        /// 계정 생성 시 자동으로 만들어줄 기본 덱의 30장 카드 ID 목록
        /// 💡 기본 덱에 넣고 싶은 카드 ID를 리스트에 추가하세요.
        /// </summary>
        public static List<string> GetStarterDeckCardIds()
        {
            return new List<string>
            {
                // [예시] 기본 덱에 들어갈 카드 ID 목록
                // "cards-gangzi-001",
                // "cards-gangzi-001",
                // "cards-gangzi-002",
            };
        }

        /// <summary>
        /// 신규 유저에게 생성해줄 기본 덱 객체를 생성합니다.
        /// </summary>
        public static DeckData CreateStarterDeck()
        {
            return new DeckData
            {
                deckId = InitialDeckId,
                deckName = InitialDeckName,
                deckClass = InitialDeckClass,
                cardIds = GetStarterDeckCardIds(),
                sideDeckCardIds = new List<string>()
            };
        }
    }
}
