using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    public class ManaModAction : IAction
    {
        public int Amount { get; set; } // +1(마나 펌핑), -1(마나 번)
        public ITargetSelector Target { get; set; } = new SelfSelector();

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            
            foreach (var target in targets)
            {
                state.ApplyManaMod(target.OwnerUid, Amount);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "ManaModAction");
            }
            return Task.CompletedTask;
        }
    }
}