namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상 카드의 레어도(Rarity)가 특정 레어도와 일치하는지 확인하는 조건입니다.
    /// </summary>
    public class TargetCardRarityCondition : ICondition
    {
        public CardRarity RequiredRarity { get; set; }

        public TargetCardRarityCondition(CardRarity requiredRarity)
        {
            RequiredRarity = requiredRarity;
        }

        public bool Check(GameState state, EffectContext context)
        {
            // 대상 개체와 원본 카드 정보가 있는지 확인
            if (context.TargetEntity == null || context.TargetEntity.SourceCard == null) return false;
            
            // 대상 카드의 레어도와 요구하는 레어도가 같은지 비교
            return context.TargetEntity.SourceCard.Rarity == RequiredRarity;
        }
    }
}