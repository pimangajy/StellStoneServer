using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public class ForceAttackAction : IAction
    {
        // 강제로 공격을 '시도할' 녀석 (기본값: 유니티가 클릭한 아군)
        public ITargetSelector Attacker { get; set; } = new TargetSelector();
        
        // '맞을' 녀석 (기본값: 무작위 적 1명)
        public ITargetSelector Defender { get; set; } = new TargetSelector { Scope = TargetScope.Random, Alliance = TargetAlliance.Enemy, Category = TargetCategory.Character };

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var attackers = Attacker.GetTargets(state, context).ToList();
            var defenders = Defender.GetTargets(state, context).ToList();

            foreach (var att in attackers)
            {
                if (defenders.Count == 0) break;
                
                // 방어자가 여러 명이면 무작위 1명 선발 (1명이면 걔가 맞음)
                var def = defenders[state.Rng.Next(defenders.Count)];

                state.LogEvent(GameEventType.FORCE_ATTACK, att.EntityId, def.EntityId, 0, context.SourceCard?.CardId);
                await state.ApplyForceAttackAsync(att, def, context.Trigger);
                state.RaiseEffectLog(att.SourceCard.CardId, context.SourceCard?.CardId, "ForceAttackAction");
            }
        }
    }
}
