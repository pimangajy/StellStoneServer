namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상 카드의 현재 비용(Cost)이 특정 수치 미만(<)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetCostLessCondition : ICondition
    {
        public int Threshold { get; set; }

        public TargetCostLessCondition(int threshold)
        {
            Threshold = threshold;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null || context.TargetEntity.SourceCard == null) return false;
            return context.TargetEntity.SourceCard.CurrentCost < Threshold;
        }
    }
}