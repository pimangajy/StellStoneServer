namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 공격력이 특정 수치 이상(>=)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetAttackMoreCondition : ICondition
    {
        public int Threshold { get; set; }

        public TargetAttackMoreCondition(int threshold)
        {
            Threshold = threshold;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;
            return context.TargetEntity.Attack >= Threshold;
        }
    }
}