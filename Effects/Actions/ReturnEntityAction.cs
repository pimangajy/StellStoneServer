using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public enum ReturnDestination
    {
        Hand, // 손패로 되돌리기 (기본값)
        Deck  // 덱으로 섞어 넣기
    }

    /// <summary>
    /// 필드의 하수인을 주인의 손패 또는 덱으로 되돌리는(바운스 / 셔플) 액션입니다.
    /// </summary>
    public class ReturnEntityAction : IAction
    {
        public ReturnDestination Destination { get; set; } = ReturnDestination.Hand;
        public ITargetSelector Target { get; set; } = new TargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();

        // 손패로 되돌아갈 때의 코스트 증감치 (예: 그림자 밟기 -2, 기본값 0)
        public int CostModifier { get; set; } = 0;

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);

            foreach (var target in targets)
            {
                if (target == null || target.IsLeader) continue; // 리더(명치)는 대상 제외

                // 필터 조건 검사
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

                if (Destination == ReturnDestination.Hand)
                {
                    await state.ReturnEntityToHandAsync(target, CostModifier);
                }
                else if (Destination == ReturnDestination.Deck)
                {
                    await state.ShuffleEntityToDeckAsync(target);
                }

                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "ReturnEntityAction");
            }
        }
    }
}
