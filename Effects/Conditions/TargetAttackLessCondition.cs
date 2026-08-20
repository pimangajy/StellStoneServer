namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 공격력이 특정 수치 미만(<)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetAttackLessCondition : ICondition
    {
        public int Threshold { get; set; }
        public TargetAttackLessCondition(int threshold) { Threshold = threshold; }
        
        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;
            return context.TargetEntity.Attack <= Threshold;
        }
    }
}