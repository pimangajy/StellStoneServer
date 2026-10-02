using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public enum DiscardTargetType
    {
        Random,          // 무작위 N장 버리기 (기본값)
        All,             // 모든 손패 버리기
        LowestCost,      // 가장 낮은 코스트 버리기
        HighestCost,     // 가장 높은 코스트 버리기
        SpecificType,    // 특정 카드 타입만 버리기 (예: 주문만 버리기)
        SpecificCardId   // 특정 카드 ID 버리기
    }

    /// <summary>
    /// 플레이어의 손패에서 카드를 버리는(묘지로 보내는) 액션입니다.
    /// </summary>
    public class DiscardAction : IAction
    {
        public int Count { get; set; } = 1;
        public ITargetSelector Target { get; set; } = new TargetSelector { Scope = TargetScope.All, Alliance = TargetAlliance.Friendly, Category = TargetCategory.Leader };
        public DiscardTargetType TargetType { get; set; } = DiscardTargetType.Random;
        public CardType? TargetCardType { get; set; }
        public string? SpecificCardId { get; set; }
        public bool ExcludeSourceCard { get; set; } = true; // 현재 시전 중인 카드는 버리기 대상에서 제외

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);

            // [ON_DRAW 연계 버리기] 드로우 연계 액션으로 호출된 경우, 방금 뽑은 바로 그 카드(TriggerCard) 1장을 버림
            if (context.Trigger == EffectTriggerType.ON_DRAW && context.TriggerCard != null)
            {
                foreach (var target in targets)
                {
                    PlayerState p = state.GetPlayerState(target.OwnerUid);
                    if (p != null && p.Hand.Contains(context.TriggerCard))
                    {
                        await state.DiscardCardsAsync(p.Uid, new List<GameCard> { context.TriggerCard });
                    }
                }
                return;
            }

            foreach (var target in targets)
            {
                PlayerState p = state.GetPlayerState(target.OwnerUid);
                if (p == null || p.Hand.Count == 0) continue;

                // 버리기 대상 후보군 선별
                var candidatePool = p.Hand.ToList();

                // 시전 중인 카드 제외 옵션
                if (ExcludeSourceCard && context.SourceCard != null)
                {
                    candidatePool.RemoveAll(c => c == context.SourceCard || c.CardId == context.SourceCard.CardId);
                }

                if (candidatePool.Count == 0) continue;

                List<GameCard> cardsToDiscard = new List<GameCard>();

                switch (TargetType)
                {
                    case DiscardTargetType.All:
                        cardsToDiscard.AddRange(candidatePool);
                        break;

                    case DiscardTargetType.LowestCost:
                        var lowestCost = candidatePool.Min(c => c.CurrentCost);
                        var lowestPool = candidatePool.Where(c => c.CurrentCost == lowestCost).ToList();
                        for (int i = 0; i < Count && lowestPool.Count > 0; i++)
                        {
                            var chosen = lowestPool[state.Rng.Next(lowestPool.Count)];
                            cardsToDiscard.Add(chosen);
                            lowestPool.Remove(chosen);
                            candidatePool.Remove(chosen);
                        }
                        break;

                    case DiscardTargetType.HighestCost:
                        var highestCost = candidatePool.Max(c => c.CurrentCost);
                        var highestPool = candidatePool.Where(c => c.CurrentCost == highestCost).ToList();
                        for (int i = 0; i < Count && highestPool.Count > 0; i++)
                        {
                            var chosen = highestPool[state.Rng.Next(highestPool.Count)];
                            cardsToDiscard.Add(chosen);
                            highestPool.Remove(chosen);
                            candidatePool.Remove(chosen);
                        }
                        break;

                    case DiscardTargetType.SpecificType:
                        if (TargetCardType.HasValue)
                        {
                            var typePool = candidatePool.Where(c => c.Type == TargetCardType.Value).ToList();
                            for (int i = 0; i < Count && typePool.Count > 0; i++)
                            {
                                var chosen = typePool[state.Rng.Next(typePool.Count)];
                                cardsToDiscard.Add(chosen);
                                typePool.Remove(chosen);
                            }
                        }
                        break;

                    case DiscardTargetType.SpecificCardId:
                        if (!string.IsNullOrEmpty(SpecificCardId))
                        {
                            var idPool = candidatePool.Where(c => c.CardId == SpecificCardId).ToList();
                            for (int i = 0; i < Count && idPool.Count > 0; i++)
                            {
                                var chosen = idPool[state.Rng.Next(idPool.Count)];
                                cardsToDiscard.Add(chosen);
                                idPool.Remove(chosen);
                            }
                        }
                        break;

                    case DiscardTargetType.Random:
                    default:
                        for (int i = 0; i < Count && candidatePool.Count > 0; i++)
                        {
                            var chosen = candidatePool[state.Rng.Next(candidatePool.Count)];
                            cardsToDiscard.Add(chosen);
                            candidatePool.Remove(chosen);
                        }
                        break;
                }

                if (cardsToDiscard.Count > 0)
                {
                    await state.DiscardCardsAsync(p.Uid, cardsToDiscard);
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "DiscardAction");
        }
    }
}
