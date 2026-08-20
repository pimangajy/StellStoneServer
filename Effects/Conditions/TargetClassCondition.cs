namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 직업군(Class)이 특정 직업과 일치하는지 확인하는 조건입니다.
    /// </summary>
    public class TargetClassCondition : ICondition
    {
        public CardClass RequiredClass { get; set; }

        public TargetClassCondition(CardClass requiredClass)
        {
            RequiredClass = requiredClass;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null || context.TargetEntity.SourceCard == null) return false;
            return context.TargetEntity.SourceCard.Class == RequiredClass;
        }
    }
}