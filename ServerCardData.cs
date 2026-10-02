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

        // 10. 효과 데이터 (문자열 또는 Firestore Array/List 모두 지원)
        [FirestoreProperty("Effects", ConverterType = typeof(FirestoreFlexibleStringConverter))]
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

        // (추가) 토큰 카드 여부 (덱 편성 및 상점 팩 뽑기 제외)
        [FirestoreProperty("IsToken")]
        public bool IsToken { get; set; } = false;

        // (추가) 기본 카드 여부 (기본 지급 카드, 분해 불가)
        [FirestoreProperty("IsDefaultCard")]
        public bool IsDefaultCard { get; set; } = false;

        // [특수 오라 능력 4종]
        // 1. 주문증폭: 아군 리더의 데미지 주문 피해량 증가
        [FirestoreProperty("SpellAmp")]
        public int SpellAmp { get; set; } = 0;

        // 2. 주문약화: 적군의 주문 피해량 감소
        [FirestoreProperty("SpellWeakness")]
        public int SpellWeakness { get; set; } = 0;

        // 3. 드로우봉인: 필드에 있는 동안 아군/적군 전체 드로우 전면 차단
        [FirestoreProperty("DrawSeal")]
        public bool DrawSeal { get; set; } = false;

        // 4. 능력강화: 아군 리더의 주문 버프량 증폭 (공격력/체력)
        [FirestoreProperty("BuffAmpAttack")]
        public int BuffAmpAttack { get; set; } = 0;

        [FirestoreProperty("BuffAmpHealth")]
        public int BuffAmpHealth { get; set; } = 0;

        // 5. 멤버 카드 액티브 스킬 데이터 (2~4개 스킬)
        [FirestoreProperty("MemberSkills", ConverterType = typeof(FirestoreFlexibleStringConverter))]
        public string? MemberSkillsString { get; set; }

        public int AttackValue => Attack ?? 0;
        public int HealthValue => Health ?? 0;

        [FirestoreProperty("Keywords", ConverterType = typeof(FirestoreFlexibleStringConverter))]
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

                // 2. 아직 한 번도 계산을 안 했다면, 여기서 딱 한 번만 분리해서 리스트로 만듭니다.
                _cachedKeywords = new List<CardKeywords>();
                
                if (!string.IsNullOrEmpty(KeywordsString))
                {
                    string trimmed = KeywordsString.Trim();

                    // JSON 배열 형태인 경우 (예: ["Taunt", "Rush"])
                    if (trimmed.StartsWith("["))
                    {
                        try
                        {
                            var list = JsonConvert.DeserializeObject<List<string>>(trimmed);
                            if (list != null)
                            {
                                foreach (var s in list)
                                {
                                    if (Enum.TryParse<CardKeywords>(s.Trim(), true, out var kw))
                                    {
                                        _cachedKeywords.Add(kw);
                                    }
                                }
                                return _cachedKeywords;
                            }
                        }
                        catch { }
                    }

                    // 쉼표 구분 형태인 경우 (예: "Taunt, Rush")
                    string[] splits = trimmed.Split(new[] { ',', '[', ']', '"', '\'' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var s in splits)
                    {
                        if (Enum.TryParse<CardKeywords>(s.Trim(), true, out var kw))
                        {
                            if (!_cachedKeywords.Contains(kw))
                            {
                                _cachedKeywords.Add(kw);
                            }
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

        private List<MemberSkill>? _cachedMemberSkills = null;

        /// <summary>
        /// 멤버 카드 액티브 스킬 리스트를 파싱하여 반환합니다.
        /// </summary>
        public List<MemberSkill> GetMemberSkills()
        {
            if (_cachedMemberSkills != null) return _cachedMemberSkills;

            if (string.IsNullOrEmpty(MemberSkillsString))
            {
                _cachedMemberSkills = new List<MemberSkill>();
                return _cachedMemberSkills;
            }

            try
            {
                if (MemberSkillsString.TrimStart().StartsWith("["))
                {
                    var settings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto
                    };

                    _cachedMemberSkills = JsonConvert.DeserializeObject<List<MemberSkill>>(MemberSkillsString, settings)
                                          ?? new List<MemberSkill>();
                    return _cachedMemberSkills;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[JSON 파싱 에러 (MemberSkills)] 카드ID '{CardID}': {ex.Message}");
            }

            _cachedMemberSkills = new List<MemberSkill>();
            return _cachedMemberSkills;
        }
    }

    /// <summary>
    /// Firestore에서 String 또는 List/Array(또는 Map)로 저장된 데이터를
    /// C#의 string 문자열로 안전하게 변환해 주는 컨버터입니다.
    /// </summary>
    public class FirestoreFlexibleStringConverter : IFirestoreConverter<string>
    {
        public object ToFirestore(string value) => value;

        public string FromFirestore(object value)
        {
            if (value == null) return string.Empty;

            if (value is string s)
            {
                return s;
            }

            if (value is System.Collections.IEnumerable && !(value is string))
            {
                return JsonConvert.SerializeObject(value);
            }

            if (value is System.Collections.IDictionary)
            {
                return JsonConvert.SerializeObject(value);
            }

            return value.ToString() ?? string.Empty;
        }
    }
}