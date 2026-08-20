namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 사건을 당한 대상이 리더(영웅)인지 검사합니다.
    /// </summary>
    public class TargetIsLeaderCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            return context.TargetEntity != null && context.TargetEntity.IsLeader;
        }
    }
}
