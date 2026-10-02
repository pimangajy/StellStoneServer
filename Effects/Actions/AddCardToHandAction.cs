using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// ?±ì—??ë½‘ëŠ” ê²ƒì´ ?„ë‹ˆ?? ?ˆë¡œ??ì¹´ë“œ(? í° ?ëŠ” ?¹ì • ì¹´ë“œ)ë¥?ì§ì ‘ ì°½ì¡°?˜ì—¬ ?Œë ˆ?´ì–´???íŒ¨??ì¶”ê??˜ëŠ” ?¡ì…˜?…ë‹ˆ??
    /// </summary>
    public class AddCardToHandAction : IAction
    {
        public int Count { get; set; } = 1;

        // ?¹ì • ì¹´ë“œ ?•ì • ?ì„±
        public string? SpecificCardId { get; set; }
        public string? CardId { get => SpecificCardId; set => SpecificCardId = value; } // JSON?ì„œ CardIdë¡??ì–´???¸í™˜

        // ?¹ì • ?„ë³´ ì¹´ë“œ ëª©ë¡ ì¤?ë¬´ì‘???ë“
        public List<string>? CandidateCardIds { get; set; }

        // ?œì¹˜/?„í„°??ë¬´ì‘???ì„± ?µì…˜ (SpecificCardId/CandidateCardIdsê°€ ?†ì„ ???„ì²´ DB?ì„œ ?œë¤ ì¶”ì¶œ)
        public CardType? TargetCardType { get; set; }
        public int? MinCost { get; set; }
        public int? MaxCost { get; set; }
        public CardTribe? TargetTribe { get; set; }

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            // 1. ?¹ì • ì¹´ë“œ IDê°€ ì§€?•ëœ ê²½ìš°
            if (!string.IsNullOrEmpty(SpecificCardId))
            {
                for (int i = 0; i < Count; i++)
                {
                    await state.AddCardToHandAsync(context.OwnerUid, SpecificCardId);
                }
                state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "AddCardToHandAction");
                return;
            }

            // 2. ?¹ì • ?„ë³´êµ?CandidateCardIds) ì¤?ë¬´ì‘???ë“
            if (CandidateCardIds != null && CandidateCardIds.Count > 0)
            {
                for (int i = 0; i < Count; i++)
                {
                    string chosenId = CandidateCardIds[state.Rng.Next(CandidateCardIds.Count)];
                    await state.AddCardToHandAsync(context.OwnerUid, chosenId);
                }
                state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "AddCardToHandAction");
                return;
            }

            // 3. ?œë²„ DB ?„ì²´?ì„œ ì¡°ê±´??ë§ëŠ” ì¹´ë“œë¥?ë¬´ì‘?„ë¡œ ì¶”ì¶œ?˜ì—¬ ?íŒ¨ë¡??ë“
            var allCards = ServerCardDatabase.Instance.GetAllCards();
            var validCards = allCards.Where(c =>
                (TargetCardType == null || c.CardType == TargetCardType) &&
                (MaxCost == null || c.Cost <= MaxCost) &&
                (MinCost == null || c.Cost >= MinCost) &&
                (TargetTribe == null || c.Tribe == TargetTribe)
            ).ToList();

            if (validCards.Count == 0) return;

            for (int i = 0; i < Count; i++)
            {
                var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                if (selectedCard.CardID != null)
                {
                    await state.AddCardToHandAsync(context.OwnerUid, selectedCard.CardID);
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "AddCardToHandAction");
        }
    }
}
