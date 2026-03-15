using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GameServer
{
    public class ServerCardDatabase
    {
        // 싱글톤 인스턴스
        public static ServerCardDatabase Instance { get; private set; } = new ServerCardDatabase();

        // 카드 데이터를 저장할 딕셔너리 (Key: CardID)
        private Dictionary<string, ServerCardData> _cardCache = new Dictionary<string, ServerCardData>();

        private ServerCardDatabase() { }

        /// <summary>
        /// 서버 시작 시 호출되어 Firestore에서 모든 카드를 로드합니다.
        /// </summary>
        public async Task InitializeAsync(FirestoreDb db)
        {
            Console.WriteLine("[ServerCardDatabase] 📥 카드 데이터 로딩 시작...");
            try
            {
                CollectionReference cardsRef = db.Collection("Cards");
                QuerySnapshot snapshot = await cardsRef.GetSnapshotAsync();

                _cardCache.Clear();

                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    if (_cardCache.Count == 1) // 딱 1번만 출력
                    {
                        Dictionary<string, object> fields = document.ToDictionary();
                        Console.WriteLine("[DEBUG] Firestore 문서 필드 목록:");
                        foreach (var kvp in fields)
                        {
                            Console.WriteLine($" - Key: {kvp.Key}, Value: {kvp.Value}");
                        }
                    }
                    // Firestore 데이터를 객체로 변환
                    ServerCardData card = document.ConvertTo<ServerCardData>();
                    
                    // CardID가 비어있으면 문서 ID를 사용
                    if (string.IsNullOrEmpty(card.CardID))
                    {
                        card.CardID = document.Id;
                    }

                    if (!_cardCache.ContainsKey(card.CardID))
                    {
                        _cardCache.Add(card.CardID, card);
                        // (디버그) 처음 5개 정도만 상세 로그 출력 (너무 많으면 콘솔 도배됨)
                        if (_cardCache.Count <= 5)
                        {
                            // Console.WriteLine($"[DB Load] ID: {card.CardID}, Cost: {card.Cost}, Atk: {card.Attack}, HP: {card.Health}, Name: {card.Name}, Description: {card.Description}");
                        }
                    }
                }

                Console.WriteLine($"[ServerCardDatabase] ✅ 총 {_cardCache.Count}장의 카드 로드 완료.");

                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerCardDatabase] ❌ 카드 로딩 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// CardID를 통해 카드 원본 데이터를 반환합니다.
        /// </summary>
        public ServerCardData? GetCardData(string cardId)
        {
            if (_cardCache.TryGetValue(cardId, out ServerCardData? data))
            {
                // (디버그) 조회 성공 로그 (값이 0인지 확인용)
                // Console.WriteLine($"[DB Get] 성공: {cardId} -> Cost: {data.Cost}"); 
                return data;
            }
            
            Console.WriteLine($"[ServerCardDatabase] ⚠️ 알 수 없는 카드 ID 요청됨: {cardId}");
            return null;
        }
    }
}