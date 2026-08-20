using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 살아있는 무작위 적(영웅 포함)에게 피해를 주는 액션입니다.
    /// </summary>
    public class RandomDamageAction : IAction
    {
        public int Amount { get; set; }

        public RandomDamageAction(int amount)
        {
            Amount = amount;
        }

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            // 1. context.OwnerUid는 2단계의 스마트 시스템 덕분에 이 효과를 가진 '원래 카드 주인'을 정확히 가리킵니다.
            PlayerState opponent = state.GetPlayerState(context.OwnerUid, true); // true = 상대방 찾기

            // 2. 살아있는 적(영웅 + 하수인) 목록 만들기
            var enemies = new List<GameEntity> { opponent.Leader };
            enemies.AddRange(opponent.Field.Where(e => e != null && e.Health > 0)!);
            enemies.AddRange(opponent.MemberZone.Where(e => e != null && e.Health > 0)!);

            // 3. 무작위로 하나 선택해서 데미지 주기
            if (enemies.Count > 0)
            {
                int randomIndex = state.Rng.Next(enemies.Count);
                GameEntity target = enemies[randomIndex];

                // 4. (선택) 클라이언트 연출을 위해 이 효과를 쏜 본체(Entity) 찾기
                int sourceId = 0;
                PlayerState me = state.GetPlayerState(context.OwnerUid);
                var myEntity = me.Field.FirstOrDefault(e => e != null && e.SourceCard == context.SourceCard);
                if (myEntity != null) sourceId = myEntity.EntityId;

                // 5. 데미지 쾅!
                await state.ApplyDamageAsync(target, Amount, context.SourceEntity?.EntityId ?? 0);
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "RandomDamageAction");
            }
        }
    }
}