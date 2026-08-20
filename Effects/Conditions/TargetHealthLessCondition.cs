namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 체력이 특정 수치 미만인지 확인하는 조건입니다. (기존 HEALTH_LESS 대체)
    /// </summary>
    public class TargetHealthLessCondition : ICondition
    {
        public int Threshold { get; set; }

        public TargetHealthLessCondition(int threshold)
        {
            Threshold = threshold;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;
            return context.TargetEntity.Health < Threshold;
        }
    }
}