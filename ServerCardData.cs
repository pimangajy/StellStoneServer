using Google.Cloud.Firestore;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace GameServer
{
    [FirestoreData]
    public class ServerCardData
    {
        // 1. 카드 ID
        [FirestoreProperty("CardID")] 
        public string? CardID { get; set; }

        // 2. 이름
        [FirestoreProperty("Name")] 
        public string? Name { get; set; }

        // 3. 코스트
        [FirestoreProperty("Cost")] 
        public int Cost { get; set; }

        // 4. 공격력
        [FirestoreProperty("Attack")] 
        public int? Attack { get; set; } 

        // 5. 체력
        [FirestoreProperty("Health")] 
        public int? Health { get; set; }

        // 6. 카드 종류
        // FirestoreEnumNameConverter를 사용하여 문자열을 Enum으로 매핑
        [FirestoreProperty("Type", ConverterType = typeof(FirestoreEnumNameConverter<CardType>))]
        public CardType? CardType { get; set; }
        // 7. 직업
        [FirestoreProperty("Class", ConverterType = typeof(FirestoreEnumNameConverter<CardClass>))] 
        public CardClass? Class { get; set; }

        // 8. 종족
        [FirestoreProperty("Tribe", ConverterType = typeof(FirestoreEnumNameConverter<CardTribe>))] 
        public CardTribe? Tribe { get; set; }

        // 9. 효과 설명 텍스트
        [FirestoreProperty("Description")]
        public string? Description { get; set; }

        // 10. 효과 데이터
        [FirestoreProperty("Effects")]
        public string? EffectsString { get; set; }

        [FirestoreProperty("Targeting")] 
        public bool? Targeting { get; set; }

        // (추가) 희귀도
        [FirestoreProperty("Rarity", ConverterType = typeof(FirestoreEnumNameConverter<CardRarity>))] 
        public CardRarity? Rarity { get; set; }

        // (추가) 확장팩
        [FirestoreProperty("Expansion", ConverterType = typeof(FirestoreEnumNameConverter<CardExpansion>))] 
        public CardExpansion? Expansion { get; set; }

        // (추가) 비고
        [FirestoreProperty("Additional")] 
        public string? Additional { get; set; }

        public int AttackValue => Attack ?? 0;
        public int HealthValue => Health ?? 0;

        [FirestoreProperty("Keywords")]
        public string? KeywordsString { get; set; }

        private List<CardKeywords>? _cachedKeywords = null;

        public List<CardKeywords> Keywords
        {
            get
            {
                // 1. 만약 이미 계산해둔 리스트가 있다면, 매번 연산하지 않고 그걸 그대로 반환!
                if (_cachedKeywords != null)
                {
                    return _cachedKeywords;
                }

                // 2. 아직 한 번도 계산을 안 했다면, 여기서 딱 한 번만 문자열을 분리해서 리스트로 만듭니다.
                _cachedKeywords = new List<CardKeywords>();
                
                if (!string.IsNullOrEmpty(KeywordsString))
                {
                    string[] splits = KeywordsString.Split(',');
                    foreach (var s in splits)
                    {
                        if (Enum.TryParse<CardKeywords>(s.Trim(), true, out var kw))
                        {
                            _cachedKeywords.Add(kw);
                        }
                    }
                }
                
                return _cachedKeywords;
            }
        }
        

        // 새로운 구조(JSON)용 데이터 파싱 함수
        public List<GameServer.Effects.CardEffect> GetNewParsedEffects()
        {
            if (string.IsNullOrEmpty(EffectsString)) return new List<GameServer.Effects.CardEffect>();

            try
            {
                // 데이터가 JSON 배열 형태('[')로 시작하는지 확인하여 신규 시스템 데이터인지 판별합니다.
                if (EffectsString.TrimStart().StartsWith("["))
                {
                    // [핵심] 인터페이스(IAction, ICondition)를 다형성에 맞게 파싱하기 위한 설정
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto
                    };

                    return JsonConvert.DeserializeObject<List<GameServer.Effects.CardEffect>>(EffectsString, settings) 
                           ?? new List<GameServer.Effects.CardEffect>();
                }
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[JSON 파싱 에러] 카드ID '{CardID}': {ex.Message}");
            }

            // 구형 문자열 데이터이거나 파싱에 실패하면 빈 리스트 반환
            return new List<GameServer.Effects.CardEffect>();
        }
    }
}