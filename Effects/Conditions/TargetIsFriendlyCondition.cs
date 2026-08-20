namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 사건을 당한 대상(피해를 입은 대상 등)이 아군 진영인지 검사합니다.
    /// </summary>
    public class TargetIsFriendlyCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            return context.TargetEntity != null && context.TargetEntity.OwnerUid == context.OwnerUid;
        }
    }
}