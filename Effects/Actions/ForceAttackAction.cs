using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    public class ForceAttackAction : IAction
    {
        // 강제로 공격을 '시도할' 녀석 (기본값: 유니티가 클릭한 아군)
        public ITargetSelector Attacker { get; set; } = new ContextTargetSelector();
        
        // '맞을' 녀석 (기본값: 무작위 적 1명)
        public ITargetSelector Defender { get; set; } = new RandomEnemyCharacterSelector();

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var attackers = Attacker.GetTargets(state, context).ToList();
            var defenders = Defender.GetTargets(state, context).ToList();

            foreach (var att in attackers)
            {
                if (defenders.Count == 0) break;
                
                // 방어자가 여러 명이면 무작위 1명 선발 (1명이면 걔가 맞음)
                var def = defenders[state.Rng.Next(defenders.Count)];

                // 둘 다 살아있고, 자기 자신을 공격하는 게 아닐 때만 전투 성립
                if (att.Health > 0 && def.Health > 0 && att.EntityId != def.EntityId)
                {
                    state.LogEvent(GameEventType.ATTACK, att.EntityId, def.EntityId, 0, null, context.Trigger);
                    state.RaiseEffectLog(def.SourceCard.CardId, att.SourceCard?.CardId, "ForceAttackAction");
                    await state.ResolveCombatAsync(att, def);
                }
            }
        }
    }
}