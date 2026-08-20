using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    public class GrantKeywordAction : IAction
    {
        public ITargetSelector Target { get; set; } = new ContextTargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();
        
        public List<string> Keywords { get; set; } = new List<string>(); // 부여할 키워드 배열 (예: ["Rush", "Taunt"])

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            foreach (var target in targets)
            {
                bool pass = true;
                foreach (var f in Filters) { if (!f.Check(state, new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger) { SourceEntity = context.SourceEntity, TargetEntity = target })) { pass = false; break; } }
                if (!pass) continue;

                if (Keywords.Count > 0)
                {
                    state.GrantKeyword(target, Keywords, sourceId);
                }
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "GrantKeywordAction");
            }
            return Task.CompletedTask;
        }
    }
}