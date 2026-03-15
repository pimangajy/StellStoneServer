using Google.Cloud.Firestore;
using System.Collections.Generic;

namespace GameServer
{
    [FirestoreData]
    public class ServerCardData
    {
        // 1. 카드 ID
        [FirestoreProperty("CardID")] // (기존: CardID) -> OK
        public string? CardID { get; set; }

        // 2. 이름
        [FirestoreProperty("Name")] // (수정: name -> Name)
        public string? Name { get; set; }

        // 3. 코스트
        [FirestoreProperty("Cost")] // (수정: cost -> Cost)
        public int Cost { get; set; }

        // 4. 공격력
        [FirestoreProperty("Attack")] // (수정: attack -> Attack)
        public int? Attack { get; set; } 

        // 5. 체력
        [FirestoreProperty("Health")] // (수정: health -> Health)
        public int? Health { get; set; }

        // 6. 카드 종류
        [FirestoreProperty("Type")] // (수정: type -> Type)
        public string? CardType { get; set; }

        // 7. 직업
        [FirestoreProperty("Class")] // (수정: class -> Class)
        public string? Class { get; set; }

        // 8. 종족
        [FirestoreProperty("Tribe")] // (수정: tribe -> Tribe)
        public string? Tribe { get; set; }

        // 9. 효과 설명 텍스트
        [FirestoreProperty("Description")] // (수정: description -> Description)
        public string? Description { get; set; }

        // 10. 효과 데이터
        [FirestoreProperty("Effects")] // (수정: effects -> Effects)
        public string? EffectsString { get; set; }

        [FirestoreProperty("TargetRule")] // (수정: targetRule -> TargetRule)
        public string? TargetRule { get; set; }

        // (추가) 희귀도
        [FirestoreProperty("Rarity")] // (수정: rarity -> Rarity)
        public string? Rarity { get; set; }

        // (추가) 확장팩
        [FirestoreProperty("Expansion")] // (수정: expansion -> Expansion)
        public string? Expansion { get; set; }

        // (추가) 비고
        [FirestoreProperty("Additional")] // (수정: additional -> Additional)
        public string? Additional { get; set; }

        // 키워드 (CSV에 없다면 기본값 처리)
        public List<string> Keywords { get; set; } = new List<string>();

        public int AttackValue => Attack ?? 0;
        public int HealthValue => Health ?? 0;
        
        public List<ServerEffectData> GetParsedEffects()
        {
            var list = new List<ServerEffectData>();
            if (string.IsNullOrEmpty(EffectsString)) return list;

            try 
            {
                var parts = EffectsString.Split('|'); 
                if (parts.Length >= 2)
                {
                    string trigger = parts[0];
                    var detailParts = parts[1].Split(':'); 
                    
                    var effect = new ServerEffectData();
                    effect.Trigger = trigger;
                    effect.EffectName = detailParts.Length > 0 ? detailParts[0] : "NONE";
                    
                    if (detailParts.Length > 1 && int.TryParse(detailParts[1], out int v1)) effect.Value1 = v1;
                    if (detailParts.Length > 2 && int.TryParse(detailParts[2], out int v2)) effect.Value2 = v2;
                    
                    effect.Target = detailParts.Length > 3 ? detailParts[3] : "NONE";
                    list.Add(effect);
                }
            }
            catch { }
            return list;
        }
    }
}