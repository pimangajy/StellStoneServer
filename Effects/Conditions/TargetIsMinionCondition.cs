namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 피해를 입은 개체가 영웅(Leader)이 아닌 일반 하수인(Minion)인지 검사합니다.
    /// </summary>
    public class TargetIsMinionCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            // IsLeader가 false이고 IsMember가 false인 일반 하수인인지 판별
            return context.TargetEntity != null && !context.TargetEntity.IsLeader && !context.TargetEntity.IsMember;
        }
    }
}