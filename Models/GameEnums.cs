namespace GameServer
{
    public enum CardType
    {
        UNKNOWN = 0,
        하수인,
        주문,
        멤버,
        READER
    }

    public enum CardClass
    {
        Gangzi,
        Yuni,
        Huya
    }

    public enum CardTribe
    {
        무소속,
        강도단,
        아르냥,
        바쿠,
        멤버
    }

    public enum CardKeywords
    {
        Default,
        Charge,       // 돌진
        Rush,         // 속공
        Taunt,        // 도발
        DivineShield, // 천보
        Poisonous,    // 독성
        Stealth,      // 은신
        Lifesteal,    // 생흡
        Windfury,     // 질풍
        Bind,         // 속박
        Silence,      // 침묵
        Elusive,      // 주문 면역 (주문 카드의 단일 타겟팅 대상이 되지 않음)
        Bodyguard     // 리더 수호 (리더가 피해를 입을 때 대신 피해를 받음)
    }

    public enum TargetRule
    {
        None = 0,               // 대상 없음 (자동/광역/랜덤 등)

        // --- 단일 지정 (플레이어가 직접 선택) ---
        Target_All = 1,                 // 모든 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Minion = 2,              // 모든 하수인 중 1개 선택
        Target_Enemy_All = 3,           // 적 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Enemy_Minion = 4,        // 적 하수인 중 1개 선택
        Target_Friend_All = 5,          // 아군 캐릭터(리더+멤버+하수인) 중 1개 선택
        Target_Friend_Minion = 6,       // 아군 하수인 중 1개 선택
        Target_Member = 7,              // 멤버중 하나
    }

    public enum CardRarity
    {
        common, 
        rare, 
        epic, 
        legendary
    }

    public enum CardExpansion
    {
        기본,
    }
}
