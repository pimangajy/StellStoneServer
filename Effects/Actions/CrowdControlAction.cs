using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public enum CrowdControlType
    {
        Silence, // 침묵 (모든 버프/키워드 초기화 및 효과 발동 차단)
        Bind     // 속박 (다음 턴 공격권 박탈)
    }

    /// <summary>
    /// 침묵, 속박(빙결) 등 하수인에게 상태 이상(CC기)을 부여하는 만능 군중 제어 액션입니다.
    /// </summary>
    public class CrowdControlAction : IAction
    {
        // 1. 적용할 CC기 종류 (단수형 / 복수형 모두 지원)
        public CrowdControlType Type { get; set; } = CrowdControlType.Silence;
        public List<CrowdControlType> Types { get; set; } = new List<CrowdControlType>();

        // 2. 타겟 셀렉터 및 조건 필터
        public ITargetSelector Target { get; set; } = new TargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            List<CrowdControlType> allTypes = new List<CrowdControlType>(Types);
            if (!allTypes.Contains(Type))
            {
                allTypes.Add(Type);
            }

            foreach (var target in targets)
            {
                if (target == null) continue;

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

                foreach (var cc in allTypes)
                {
                    switch (cc)
                    {
                        case CrowdControlType.Silence:
                            // 리더(영웅)는 침묵 대상 제외
                            if (!target.IsLeader)
                            {
                                state.ApplySilence(target, sourceId, context.Trigger);
                            }
                            break;

                        case CrowdControlType.Bind:
                            state.ApplyBind(target, sourceId, context.Trigger);
                            break;
                    }
                }

                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "CrowdControlAction");
            }

            return Task.CompletedTask;
        }
    }
}
