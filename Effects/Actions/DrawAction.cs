using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;
using Newtonsoft.Json;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 카드를 드로우합니다. 필터 옵션을 넣으면 덱에서 조건에 맞는 카드를 서치하여 뽑습니다.
    /// OnDrawnFilters / OnDrawnActions를 통해 카드를 뽑을 때마다 실시간 연계 액션(버프, 파괴, 즉시 소환, 데미지 등)을 실행할 수 있습니다.
    /// </summary>
    public class DrawAction : IAction
    {
        public int Count { get; set; } = 1;
        
        // 대상 지정: 아군 리더(명치)를 기본값으로 사용
        public ITargetSelector Target { get; set; } = new TargetSelector { Scope = TargetScope.All, Alliance = GameServer.Effects.Targeting.TargetAlliance.Friendly, Category = TargetCategory.Leader }; 

        // 덱 서치용 필터 옵션들 (null이면 일반 드로우)
        public string? SpecificCardId { get; set; }
        public CardType? TargetCardType { get; set; }
        public int? MaxCost { get; set; }
        public CardTribe? TargetTribe { get; set; }

        // 드로우 연계 옵션: 각 카드가 뽑힐 때마다 조건 검사 후 실행할 액션 목록
        public List<ICondition> OnDrawnFilters { get; set; } = new List<ICondition>();
        public List<IAction> OnDrawnActions { get; set; } = new List<IAction>();

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);

            foreach (var target in targets)
            {
                PlayerState p = state.GetPlayerState(target.OwnerUid);
                if (p == null) continue;

                for (int i = 0; i < Count; i++)
                {
                    bool hasFilter = SpecificCardId != null || TargetCardType != null || MaxCost != null || TargetTribe != null;
                    GameCard? drawnCard = null;

                    if (hasFilter)
                    {
                        var validCards = p.Deck.Where(c =>
                            (SpecificCardId == null || c.CardId == SpecificCardId) &&
                            (TargetCardType == null || c.Type == TargetCardType) &&
                            (MaxCost == null || c.CurrentCost <= MaxCost) &&
                            (TargetTribe == null || c.Tribe == TargetTribe)
                        ).ToList();

                        if (validCards.Count > 0)
                        {
                            var chosen = validCards[state.Rng.Next(validCards.Count)];
                            drawnCard = await state.DrawSpecificCardFromDeckAsync(p.Uid, chosen.CardId);
                        }
                        else
                        {
                            drawnCard = await state.DrawCardWithSyncAsync(p.Uid);
                        }
                    }
                    else
                    {
                        drawnCard = await state.DrawCardWithSyncAsync(p.Uid);
                    }

                    // 카드를 정상적으로 뽑았고, 드로우 연계 액션이 정의되어 있다면 실행
                    if (drawnCard != null && OnDrawnActions != null && OnDrawnActions.Count > 0)
                    {
                        // 1. [필터 검사용 컨텍스트] 뽑힌 카드(TriggerCard) 자체를 검사하도록 TargetEntity를 비워둠
                        var filterContext = new EffectContext(p.Uid, context.SourceCard, EffectTriggerType.ON_DRAW)
                        {
                            SourceEntity = context.SourceEntity,
                            TargetEntity = null,
                            TriggerCard = drawnCard,
                            TriggerOwnerUid = p.Uid
                        };

                        // 필터 조건 검사
                        bool pass = true;
                        foreach (var filter in OnDrawnFilters)
                        {
                            if (!filter.Check(state, filterContext))
                            {
                                pass = false;
                                break;
                            }
                        }

                        // 2. [액션 실행용 컨텍스트] 필터를 통과했을 때 리더를 대상으로 액션 실행
                        if (pass)
                        {
                            var actionContext = new EffectContext(p.Uid, context.SourceCard, EffectTriggerType.ON_DRAW)
                            {
                                SourceEntity = context.SourceEntity,
                                TargetEntity = p.Leader,
                                TriggerCard = drawnCard,
                                TriggerOwnerUid = p.Uid
                            };

                            foreach (var action in OnDrawnActions)
                            {
                                await action.ExecuteAsync(state, actionContext);
                            }
                        }
                    }
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "DrawAction");
        }
    }
}
