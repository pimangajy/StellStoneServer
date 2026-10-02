using System.Collections.Generic;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public class HealAction : IAction
    {
        public ITargetSelector Target { get; set; } = new TargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();
        
        public int Amount { get; set; }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            foreach (var target in targets)
            {
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

                state.ApplyHeal(target, Amount, sourceId, context.SourceCard?.CardId, context.Trigger);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "HealAction");
            }
            return Task.CompletedTask;
        }
    }
}
