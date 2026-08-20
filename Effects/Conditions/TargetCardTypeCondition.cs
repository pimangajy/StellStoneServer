namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상이 특정 카드 타입(예: 하수인, 멤버)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetCardTypeCondition : ICondition
    {
        public CardType RequiredType { get; set; }

        public TargetCardTypeCondition(CardType requiredType)
        {
            RequiredType = requiredType;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null || context.TargetEntity.SourceCard == null) return false;
            return context.TargetEntity.SourceCard.Type == RequiredType;
        }
    }
}