using System.Collections.Generic;
using Google.Cloud.Firestore;

namespace GameServer
{
    [FirestoreData]
    public class DeckData
    {
        // deckId는 Firestore 문서의 ID이므로, Firestore 필드 속성을 붙이지 않습니다.
        // (클라이언트로 보낼 때만 이 속성에 ID를 채워줍니다.)
        public string? deckId { get; set; }

        [FirestoreProperty]
        public string? deckName { get; set; }
        [FirestoreProperty]
        public string? deckClass { get; set; }

        [FirestoreProperty]
        public string? leaderSkinId { get; set; }

        [FirestoreProperty]
        public List<string>? cardIds { get; set; }

        [FirestoreProperty]
        public List<string>? sideDeckCardIds { get; set; }
    
        [FirestoreProperty]
        public List<string>? sideDeckFirstTurnCardIds { get; set; }

        // Firestore 변환을 위한 기본 생성자
        public DeckData()
        {
            sideDeckCardIds = new List<string>();
            sideDeckFirstTurnCardIds = new List<string>();
        }

        // 서버 코드 내에서 객체 생성을 위한 생성자
        public DeckData(string name, string className)
        {
            deckName = name;
            deckClass = className;
            cardIds = new List<string>(); // 새 덱은 항상 비어있는 카드 리스트로 시작
            sideDeckCardIds = new List<string>();
            sideDeckFirstTurnCardIds = new List<string>();
            leaderSkinId = GetEquippedSkinId();
        }

        /// <summary>
        /// 장착된 스킨 ID를 반환합니다. 지정된 스킨이 없으면 직업의 기본 스킨을 반환합니다.
        /// </summary>
        public string GetEquippedSkinId()
        {
            if (!string.IsNullOrEmpty(leaderSkinId))
            {
                return leaderSkinId;
            }

            return !string.IsNullOrEmpty(deckClass) ? $"Skin_{deckClass}_Default" : "Skin_Common_Default";
        }
    }
}
