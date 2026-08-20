namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상 카드의 고유 ID가 특정 카드의 ID(예: 'cards-gangzi-008')와 일치하는지 확인하는 조건입니다.
    /// </summary>
    public class TargetCardIdCondition : ICondition
    {
        public string TargetCardId { get; set; }

        public TargetCardIdCondition(string targetCardId)
        {
            TargetCardId = targetCardId;
        }

        public bool Check(GameState state, EffectContext context)
        {
            if (context.TargetEntity == null || context.TargetEntity.SourceCard == null) return false;
            return context.TargetEntity.SourceCard.CardId == TargetCardId;
        }
    }
}