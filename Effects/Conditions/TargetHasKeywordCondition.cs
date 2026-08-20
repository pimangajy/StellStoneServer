namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상이 특정 키워드(도발, 속공 등)를 가지고 있는지 확인하는 조건입니다. (기존 HAS_KEYWORD 대체)
    /// </summary>
    public class TargetHasKeywordCondition : ICondition
    {
        public CardKeywords RequiredKeyword { get; set; }

        public TargetHasKeywordCondition(CardKeywords keyword)
        {
            RequiredKeyword = keyword;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null || context.TargetEntity.Keywords == null) return false;
            return context.TargetEntity.Keywords.Contains(RequiredKeyword);
        }
    }
}