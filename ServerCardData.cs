using Google.Cloud.Firestore;
using System.Collections.Generic;

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

        [FirestoreProperty("TargetRule", ConverterType = typeof(FirestoreEnumNameConverter<TargetRule>))] 
        public TargetRule? CardTargetRule { get; set; }

        // (추가) 희귀도
        [FirestoreProperty("Rarity", ConverterType = typeof(FirestoreEnumNameConverter<CardRarity>))] 
        public CardRarity? Rarity { get; set; }

        // (추가) 확장팩
        [FirestoreProperty("Expansion", ConverterType = typeof(FirestoreEnumNameConverter<CardExpansion>))] 
        public CardExpansion? Expansion { get; set; }

        // (추가) 비고
        [FirestoreProperty("Additional")] 
        public string? Additional { get; set; }

        // 키워드 (CSV에 없다면 기본값 처리)
        public List<CardKeywords> Keywords { get; set; } = new List<CardKeywords>();

        public int AttackValue => Attack ?? 0;
        public int HealthValue => Health ?? 0;
        
        public List<ServerEffectData> GetParsedEffects()
        {
            var list = new List<ServerEffectData>();
            if (string.IsNullOrEmpty(EffectsString)) return list; // [1]

            try 
            {
                // 1. '&' 기호를 기준으로 여러 개의 효과를 분리
                var effectStrings = EffectsString.Split('&', StringSplitOptions.RemoveEmptyEntries); 

                foreach (var singleEffectStr in effectStrings)
                {
                    var cleanStr = singleEffectStr.Trim(); 
                    var effect = new ServerEffectData();
                    string detailStr = "";

                    // 2. '|' 기호가 포함되어 있는지 확인하여 트리거 유무 판단
                    if (cleanStr.Contains('|'))
                    {
                        // 트리거가 명시된 경우 (예: "ON_DEATH|DAMAGE:1:0:ALL_ENEMIES")
                        var parts = cleanStr.Split('|'); 
                        string triggerStr = parts[0]; 
                        detailStr = parts[1]; // '|' 뒷부분을 세부 내용으로 지정
                        
                        // Trigger 문자열을 EffectTriggerType Enum으로 변환
                        if (Enum.TryParse<EffectTriggerType>(triggerStr, true, out var parsedTrigger))
                        {
                            effect.Trigger = parsedTrigger;
                        }
                        else
                        {
                            effect.Trigger = EffectTriggerType.NONE;
                        }
                    }
                    else
                    {
                        // '|' 기호가 없는 경우 (예: "DAMAGE:1:0:TARGET")
                        // 트리거를 생략한 것이므로 질문자님 의도대로 기본값(ON_PLAY)을 강제 할당합니다.
                        effect.Trigger = EffectTriggerType.ON_PLAY; 
                        detailStr = cleanStr; // 문자열 전체가 세부 내용이 됨
                    }

                    // 3. 세부 효과 내용(: 기준) 파싱
                    var detailParts = detailStr.Split(':'); 
                    string[] effectNameStr = detailParts;
                    
                    // EffectName 파싱 (문자열 -> GameEventType Enum)
                    if (Enum.TryParse<GameEventType>(effectNameStr[0], true, out var parsedEffectName))
                    {
                        effect.EffectName = parsedEffectName;
                    }
                    else
                    {
                        effect.EffectName = GameEventType.NONE;
                    }
                    
                    // 4. 수치 및 타겟 정보 파싱 (기존)
                    if (detailParts.Length > 1 && int.TryParse(detailParts[1], out int v1)) effect.Value1 = v1;
                    if (detailParts.Length > 2 && int.TryParse(detailParts[2], out int v2)) effect.Value2 = v2;

                    // EffectName 파싱 (문자열 -> GameEventType Enum)
                    if (effectNameStr.Length > 3 && Enum.TryParse<TargetRule>(effectNameStr[3], true, out var parsedTargetRule))
                    {
                        effect.Target = parsedTargetRule;
                    }
                    else
                    {
                        effect.Target = TargetRule.None;
                    }

                    // 5. 조건(Condition) 파싱 (문자열 -> CardCondition Enum)
                    if (detailParts.Length > 4)
                    {
                        if (Enum.TryParse<CardCondition>(detailParts[4], true, out var parsedCondition))
                        {
                            effect.Condition = parsedCondition;
                        }
                        else
                        {
                            effect.Condition = CardCondition.NONE; // 오류 시 기본값 처리
                        }
                    }
                    else
                    {
                        effect.Condition = CardCondition.NONE; // 조건이 생략된 경우
                    }

                    // 6. 조건의 값(ConditionValue)은 string 그대로 저장!
                    effect.ConditionValue = detailParts.Length > 5 ? detailParts[5] : null;

                    // 7. 반복 횟수(Count) 파싱
                    if (detailParts.Length > 6 && int.TryParse(detailParts[6], out int count))
                    {
                        effect.Count = count;
                    }
                    else
                    {
                        effect.Count = 1; // 생략 시 기본값 1회
                    }

                    // 위에서 만든 effect 객체를 리스트에 추가합니다.
                    list.Add(effect);
                }
            }
            catch { }
            
            return list;
        }
    }
}