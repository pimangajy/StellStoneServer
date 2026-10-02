using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace GameServer
{
    public class ServerCardDatabase
    {
        // 싱글톤 인스턴스
        public static ServerCardDatabase Instance { get; private set; } = new ServerCardDatabase();

        // 카드 데이터를 저장할 딕셔너리 (Key: CardID)
        private Dictionary<string, ServerCardData> _cardCache = new Dictionary<string, ServerCardData>();
        // [P-03 성능 최적화] GetAllCards 호출 시 반복적인 .ToList() 힙 할당을 방지하기 위한 캐싱 리스트
        private List<ServerCardData> _cachedCardList = new List<ServerCardData>();

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

                var allDump = new Dictionary<string, object>();

                foreach (DocumentSnapshot document in snapshot.Documents)
                {
                    try
                    {
                        var fields = document.ToDictionary();
                        allDump[document.Id] = fields;

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
                        }
                    }
                    catch (Exception docEx)
                    {
                        Console.WriteLine($"[ServerCardDatabase] ⚠️ 개별 카드 로딩 실패 (문서 ID: '{document.Id}'): {docEx.Message}");
                    }
                }

                _cachedCardList = _cardCache.Values.ToList();
                Console.WriteLine($"[ServerCardDatabase] ✅ 총 {_cardCache.Count}장의 카드 로드 완료 (캐싱 완료).");

                // Firestore 문서 전체를 텍스트 JSON 파일로 덤프하여 개발자가 직접 확인할 수 있도록 저장
                try
                {
                    string dumpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firestore_cards_dump.json");
                    File.WriteAllText(dumpPath, JsonConvert.SerializeObject(allDump, Formatting.Indented));
                    Console.WriteLine($"[ServerCardDatabase] 📄 Firestore 문서 덤프 생성 완료 -> {dumpPath}");
                }
                catch (Exception dumpEx)
                {
                    Console.WriteLine($"[ServerCardDatabase] ⚠️ 덤프 파일 저장 실패: {dumpEx.Message}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ServerCardDatabase] ❌ 카드 로딩 전체 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 데이터베이스의 카드를 반환합니다. (캐싱 리스트 반환으로 무할당)
        /// </summary>
        public List<ServerCardData> GetAllCards()
        {
            return _cachedCardList;
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

        public ServerCardData? GetCard(string cardId) => GetCardData(cardId);
    }
}