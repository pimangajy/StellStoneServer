namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 효과 대상이 일반 하수인(Minion)이거나 리더(Leader)인지 검사합니다. (하수인, 리더를 포함)
    /// </summary>
    public class TargetIsMinionOrLeaderCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;

            // 일반 하수인 판별 (!IsLeader && !IsMember)
            bool isMinion = !context.TargetEntity.IsLeader && !context.TargetEntity.IsMember;
            bool isLeader = context.TargetEntity.IsLeader;

            return isMinion || isLeader; // 논리적으로 !context.SourceEntity.IsMember 와 동일합니다.
        }
    }
}