namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 효과 대상이 멤버(Member)이거나 리더(Leader)인지 검사합니다. (멤버, 리더를 포함)
    /// </summary>
    public class TargetIsMemberOrLeaderCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;

            bool isMember = context.TargetEntity.IsMember;
            bool isLeader = context.TargetEntity.IsLeader;

            return isMember || isLeader;
        }
    }
}