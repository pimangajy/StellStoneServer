namespace GameServer.Effects.TargetSelector
{
    // 3-1. 무작위 적 하수인 (적 필드 중 택 1)
    public class RandomEnemyMinionSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var opp = state.GetPlayerState(context.OwnerUid, true);
            var pool = opp.Field.Where(e => e != null && e.Health > 0)
                          .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0)).ToList();

            if (pool.Count > 0) yield return pool[state.Rng.Next(pool.Count)]!;
        }
    }

    // 3-2. 무작위 아군 하수인 (아군 필드 중 택 1)
    public class RandomFriendlyMinionSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var me = state.GetPlayerState(context.OwnerUid);
            var pool = me.Field.Where(e => e != null && e.Health > 0)
                         .Concat(me.MemberZone.Where(e => e != null && e.Health > 0)).ToList();

            if (pool.Count > 0) yield return pool[state.Rng.Next(pool.Count)]!;
        }
    }

    // 3-3. 무작위 캐릭터 (양쪽 명치 + 모든 하수인 중 택 1)
    public class RandomCharacterSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var selector = new AllCharactersSelector(); // 위에서 만든 광역 셀렉터 재활용
            var pool = selector.GetTargets(state, context).ToList();

            if (pool.Count > 0) yield return pool[state.Rng.Next(pool.Count)];
        }
    }

    // 무작위 적 캐릭터 1명 (적 영웅 + 적 하수인 중 택 1)
    public class RandomEnemyCharacterSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. 상대방(Opponent) 상태 가져오기
            var opp = state.GetPlayerState(context.OwnerUid, true);

            // 2. 살아있는 적 하수인들 모두 모으기
            var pool = opp.Field.Where(e => e != null && e.Health > 0)
                          .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0))
                          .ToList();
            
            // 3. 적 영웅(명치)도 공격 후보에 추가
            pool.Add(opp.Leader); 

            // 4. 모인 후보들 중 무작위로 1명만 추첨해서 반환
            if (pool.Count > 0)
            {
                yield return pool[state.Rng.Next(pool.Count)]!;
            }
        }
    }
}