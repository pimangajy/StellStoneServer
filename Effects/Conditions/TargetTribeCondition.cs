namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상의 종족이 특정 종족(강도단, 아르냥 등)인지 확인하는 조건입니다.
    /// </summary>
    public class TargetTribeCondition : ICondition
    {
        public CardTribe Tribe { get; set; }

        public TargetTribeCondition(CardTribe tribe)
        {
            Tribe = tribe;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null) return false;
            return context.TargetEntity.Tribe == Tribe;
        }
    }
}