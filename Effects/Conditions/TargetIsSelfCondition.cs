namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 피해를 받거나 사건이 일어난 주체 개체가 효과 소유자 본인인지 검사합니다.
    /// </summary>
    public class TargetIsSelfCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            // 이번 이벤트의 대상 엔티티가 효과 원본 카드와 일치하는지 확인
            return context.TargetEntity?.SourceCard == context.SourceCard;
        }
    }
}