using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    public class SummonFromDeckAction : IAction
    {
        public int Count { get; set; } = 1;

        // 서치용 필터 옵션들
        public string? SpecificCardId { get; set; } 
        public CardType? TargetCardType { get; set; } 
        
        // [신규 추가] 최소 비용 제한과 최대 비용 제한
        public int? MinCost { get; set; } 
        public int? MaxCost { get; set; } 
        
        public CardTribe? TargetTribe { get; set; }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            PlayerState p = state.GetPlayerState(context.OwnerUid);

            for (int i = 0; i < Count; i++)
            {
                // 1. MinCost 조건이 추가된 필터링 로직
                var validCards = p.Deck.Where(c => 
                    (c.Type == CardType.하수인 || c.Type == CardType.멤버) && 
                    (SpecificCardId == null || c.CardId == SpecificCardId) &&
                    (TargetCardType == null || c.Type == TargetCardType) &&
                    (MaxCost == null || c.CurrentCost <= MaxCost) &&      // MaxCost 이하인지
                    (MinCost == null || c.CurrentCost >= MinCost) &&      // [신규] MinCost 이상인지
                    (TargetTribe == null || c.Tribe == TargetTribe)
                ).ToList();

                if (validCards.Count > 0)
                {
                    var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                    p.Deck.Remove(selectedCard);
                    state.SummonExistingCard(p.Uid, selectedCard);
                }
                else
                {
                    break;
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "SummonFromDeckAction");

            return Task.CompletedTask;
        }
    }
}