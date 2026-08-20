namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 피해를 입은 개체가 효과를 소유한 카드 본인이 아닌 다른 개체인지 검사합니다.
    /// </summary>
    public class TargetIsNotSelfCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            // context.SourceEntity: 이번에 피해를 입은 당사자 개체
            // context.SourceCard: 이 효과를 작동시키고 있는 하수인 카드 본인
            return context.TargetEntity != null && context.TargetEntity.SourceCard != context.SourceCard;
        }
    }
}