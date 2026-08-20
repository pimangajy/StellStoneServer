namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 체력이 특정 수치 이상(>=)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetHealthMoreCondition : ICondition
    {
        public int Threshold { get; set; }

        public TargetHealthMoreCondition(int threshold)
        {
            Threshold = threshold;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;
            return context.TargetEntity.Health >= Threshold;
        }
    }
}