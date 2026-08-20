using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 타겟 지정 없이, 이 카드를 사용한 플레이어에게 즉시 마나를 부여하는 액션입니다.
    /// </summary>
    public class GainManaAction : IAction
    {
        public int Amount { get; set; }

        public GainManaAction() { }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            // 타겟(TargetEntity) 검사를 아예 하지 않습니다!
            
            // 1. 카드를 낸 주인의 상태를 가져옵니다.
            PlayerState p = state.GetPlayerState(context.OwnerUid);

            // 2. 최대 마나를 초과하여 임시 마나를 얻을 수 있도록 직접 더해줍니다.
            p.CurrentMana += Amount;

            // 참고: 마나 수치가 바뀌면 효과 처리가 모두 끝난 직후 
            // GameState.BroadcastUpdatesAsync에서 알아서 클라이언트(S_UpdateMana)로 동기화해 줍니다.
            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "GainManaAction");
            return Task.CompletedTask;
        }
    }
}