using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 묘지(Graveyard)에서 조건에 맞는 하수인/멤버를 찾아 부활(필드 소환)시킵니다.
    /// </summary>
    public class ResurrectAction : IAction
    {
        public int Count { get; set; } = 1;

        // 서치용 필터 옵션들
        public string? SpecificCardId { get; set; } 
        public CardType? TargetCardType { get; set; } 
        public int? MinCost { get; set; } 
        public int? MaxCost { get; set; } 
        public CardTribe? TargetTribe { get; set; }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            PlayerState p = state.GetPlayerState(context.OwnerUid);

            for (int i = 0; i < Count; i++)
            {
                // 1. 묘지(Graveyard)에서 조건에 맞는 하수인/멤버만 걸러냅니다.
                var validCards = p.Graveyard.Where(c => 
                    (c.Type == CardType.하수인 || c.Type == CardType.멤버) && 
                    (SpecificCardId == null || c.CardId == SpecificCardId) &&
                    (TargetCardType == null || c.Type == TargetCardType) &&
                    (MaxCost == null || c.CurrentCost <= MaxCost) &&
                    (MinCost == null || c.CurrentCost >= MinCost) &&
                    (TargetTribe == null || c.Tribe == TargetTribe)
                ).ToList();

                if (validCards.Count > 0)
                {
                    // 2. 조건에 맞는 개체 중 무작위로 하나 선택
                    var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                    
                    // 3. 묘지에서 제거 후, 이전 대화에서 만들었던 소환 함수로 필드에 부활!
                    p.Graveyard.Remove(selectedCard);
                    state.SummonExistingCard(p.Uid, selectedCard);
                }
                else
                {
                    break;
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "ResurrectAction");

            return Task.CompletedTask;
        }
    }
}