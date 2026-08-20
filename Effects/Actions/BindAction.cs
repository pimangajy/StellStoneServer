using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;


namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 대상(들)에게 속박을 부여하는 액션입니다. (단일/광역 모두 호환)
    /// </summary>
    public class BindAction : IAction
    {
        public ITargetSelector Target { get; set; } = new ContextTargetSelector(); 
        
        public List<ICondition> Filters { get; set; } = new List<ICondition>();

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            foreach (var target in targets)
            {
                if (Filters != null && Filters.Count > 0)
                {
                    var tempContext = new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger)
                    {
                        SourceEntity = context.SourceEntity, TargetEntity = target
                    };

                    bool pass = true;
                    foreach (var filter in Filters)
                    {
                        if (!filter.Check(state, tempContext)) { pass = false; break; }
                    }
                    if (!pass) continue; // 필터 조건에 안 맞으면 건너뜀
                }

                state.ApplyBind(target, sourceId);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "BindAction");
            }

            return Task.CompletedTask;
        }
    }
}