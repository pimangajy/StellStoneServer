using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public class DamageAction : IAction
    {
        public ITargetSelector Target { get; set; } = new TargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();
        
        public int Amount { get; set; } // 데미지 수치
        public bool AddCustomValue { get; set; } = false; // 손패 누적 스택(CustomValue) 데미지 가산 여부

        // 분산 피해 여부 (true 시 1 데미지씩 Amount번 살아있는 대상 중 무작위로 나누어 입힘 - 신비한 화살 스타일)
        public bool IsSplit { get; set; } = false;

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            int sourceId = context.SourceEntity?.EntityId ?? 0;
            int customVal = AddCustomValue ? (context.SourceCard?.CustomValue ?? 0) : 0;
            int baseDamage = Amount + customVal;
            int finalDamage = baseDamage;

            // 시전한 카드가 '주문(Spell)'이라면 주문증폭 및 상대의 주문약화 계산
            if (context.SourceCard != null && context.SourceCard.Type == CardType.주문)
            {
                int spellAmp = state.GetSpellAmp(context.OwnerUid);
                int spellWeakness = state.GetSpellWeakness(context.OwnerUid);
                finalDamage = Math.Max(0, baseDamage + spellAmp - spellWeakness);

                if (spellAmp > 0 || spellWeakness > 0 || AddCustomValue)
                {
                    state.LogDebug("SpellAmp", $"⚡ [주문 데미지 계산] 기본({Amount}) + 스택({customVal}) + 아군증폭({spellAmp}) - 적군약화({spellWeakness}) -> 최종: {finalDamage}");
                }
            }

            if (finalDamage <= 0) return;

            // =================================================================
            // 1. [IsSplit = true] 분산 피해 (1 데미지씩 실시간 살아있는 대상에게 분할 타격)
            // =================================================================
            if (IsSplit)
            {
                for (int i = 0; i < finalDamage; i++)
                {
                    // 매 발사체마다 실시간으로 살아있는 대상 풀 재조회
                    var livingTargets = Target.GetTargets(state, context)
                        .Where(t => t != null && t.Health > 0 && !t.IsDestroyed)
                        .ToList();

                    // 필터 조건 검사
                    if (Filters.Count > 0)
                    {
                        livingTargets = livingTargets.Where(t =>
                        {
                            var tempContext = new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger)
                            {
                                SourceEntity = context.SourceEntity,
                                TargetEntity = t
                            };
                            return Filters.All(f => f.Check(state, tempContext));
                        }).ToList();
                    }

                    if (livingTargets.Count == 0) break; // 더 이상 때릴 수 있는 살아있는 대상이 없으면 종료

                    // 무작위 1명 추첨하여 1 데미지 적중
                    var chosen = livingTargets[state.Rng.Next(livingTargets.Count)];
                    await state.ApplyDamageAsync(chosen, 1, sourceId, context.Trigger, context.SourceCard?.CardId);
                    state.RaiseEffectLog(chosen.SourceCard.CardId, context.SourceCard?.CardId, "DamageAction");
                }
                return;
            }

            // =================================================================
            // 2. [IsSplit = false] 일반 단일 / 광역 피해
            // =================================================================
            var targets = Target.GetTargets(state, context);

            foreach (var target in targets)
            {
                if (target == null || target.Health <= 0 || target.IsDestroyed) continue;

                // 필터 검사
                bool pass = true;
                foreach (var f in Filters)
                {
                    if (!f.Check(state, new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger)
                    {
                        SourceEntity = context.SourceEntity,
                        TargetEntity = target
                    }))
                    {
                        pass = false;
                        break;
                    }
                }
                if (!pass) continue;

                await state.ApplyDamageAsync(target, finalDamage, sourceId, context.Trigger, context.SourceCard?.CardId);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "DamageAction");
            }
        }
    }
}
