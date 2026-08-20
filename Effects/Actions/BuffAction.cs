using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    public class BuffAction : IAction
    {
        public ITargetSelector Target { get; set; } = new ContextTargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();
        
        public int AttackBuff { get; set; } // +1, -2 등 증감치
        public int HealthBuff { get; set; }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            foreach (var target in targets)
            {
                bool pass = true;
                foreach (var f in Filters) { if (!f.Check(state, new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger) { SourceEntity = context.SourceEntity, TargetEntity = target })) { pass = false; break; } }
                if (!pass) continue;

                state.ApplyBuff(target, AttackBuff, HealthBuff, sourceId, context.Trigger);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "BuffAction");
            }
            return Task.CompletedTask;
        }
    }
}