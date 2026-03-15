using Google.Cloud.Firestore;

namespace GameServer
{
    /// <summary>
    /// 클라이언트의 'EffectInstance'와 대응되는 서버 데이터입니다.
    /// 카드의 구체적인 능력(피해, 힐, 버프 등)을 정의합니다.
    /// </summary>
    [FirestoreData]
    public class ServerEffectData
    {
        [FirestoreProperty]
        public string? Trigger { get; set; } // 예: "ON_PLAY", "ON_DEATH"

        [FirestoreProperty]
        public string? EffectName { get; set; } // 예: "DAMAGE", "HEAL", "DRAW"

        [FirestoreProperty]
        public int Value1 { get; set; } // 데미지 양, 드로우 수 등

        [FirestoreProperty]
        public int Value2 { get; set; } // 체력 버프량 등

        [FirestoreProperty]
        public string? Target { get; set; } // 예: "TARGET_ENEMY", "SELF", "ALL_MINIONS"

        [FirestoreProperty]
        public string? Condition { get; set; } // 예: "TRIBE", "IF_COMBO"

        [FirestoreProperty]
        public string? ConditionValue { get; set; } // 예: "MEMBER", "BEAST"

        [FirestoreProperty]
        public int Count { get; set; } = 1; // 반복 횟수

        // 재귀적 효과 (Else 상황 등)
        // Firestore는 깊은 재귀 저장을 주의해야 하지만, 1~2단계는 문제없습니다.
        [FirestoreProperty]
        public ServerEffectData? ElseEffect { get; set; }
    }
}