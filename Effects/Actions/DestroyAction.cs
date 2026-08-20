using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 대상을 데미지나 체력 계산 없이 즉시 처치(파괴)하는 액션입니다.
    /// </summary>
    public class DestroyAction : IAction
    {
        public ITargetSelector Target { get; set; } = new ContextTargetSelector();

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);

            foreach (var target in targets)
            {
                // 영웅(명치)은 즉사기로 죽일 수 없도록 방어
                if (target.IsLeader) continue;

                // 핵심: 체력을 0으로 조작하지 않고 '파괴 상태'로 만듭니다.
                target.IsDestroyed = true;

                // 신규 DESTROY 이벤트를 로그에 남겨 유니티에서 전용 이펙트를 틀 수 있게 합니다.
                int sourceId = context.SourceEntity?.EntityId ?? 0;
                state.LogEvent(GameEventType.DESTROY, sourceId, target.EntityId);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "DestroyAction");
            }

            return Task.CompletedTask;
        }
    }
}