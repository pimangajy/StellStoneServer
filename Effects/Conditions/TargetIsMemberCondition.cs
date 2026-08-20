namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 사건을 당한 대상이 멤버 존(MemberZone)에 배치된 멤버인지 검사합니다.
    /// </summary>
    public class TargetIsMemberCondition : ICondition
    {
        public bool Check(GameState state, EffectContext context)
        {
            return context.TargetEntity != null && context.TargetEntity.IsMember;
        }
    }
}