namespace GameServer.Effects.TargetSelector
{
    // 2-1. 모든 적 하수인 (광역 적)
    public class AllEnemyMinionsSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var opp = state.GetPlayerState(context.OwnerUid, true);
            return opp.Field.Where(e => e != null && e.Health > 0)
                       .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0))!;
        }
    }

    // 2-2. 모든 아군 하수인 (광역 아군)
    public class AllFriendlyMinionsSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var me = state.GetPlayerState(context.OwnerUid);
            return me.Field.Where(e => e != null && e.Health > 0)
                     .Concat(me.MemberZone.Where(e => e != null && e.Health > 0))!;
        }
    }

    // 2-3. 모든 하수인 (공용 - 아군/적 필드 전부)
    public class AllMinionsSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var me = state.GetPlayerState(context.OwnerUid);
            var opp = state.GetPlayerState(context.OwnerUid, true);
            
            return me.Field.Where(e => e != null && e.Health > 0)
                     .Concat(me.MemberZone.Where(e => e != null && e.Health > 0))
                     .Concat(opp.Field.Where(e => e != null && e.Health > 0))
                     .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0))!;
        }
    }

    // 2-4. 모든 적 (적 필드 전체 + 적 영웅)
    public class AllEnemiesSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var opp = state.GetPlayerState(context.OwnerUid, true);
            var list = opp.Field.Where(e => e != null && e.Health > 0)
                          .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0)).ToList();
            list.Add(opp.Leader); // 명치 추가
            return list!;
        }
    }

    // 2-5. 모든 아군 (적 필드 전체 + 적 영웅)
    public class AllFriendlySelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var me = state.GetPlayerState(context.OwnerUid);
            var list = me.Field.Where(e => e != null && e.Health > 0)
                          .Concat(me.MemberZone.Where(e => e != null && e.Health > 0)).ToList();
            list.Add(me.Leader); // 명치 추가
            return list!;
        }
    }

    // 2-6. 모든 캐릭터 (모든 하수인 + 양쪽 영웅)
    public class AllCharactersSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var me = state.GetPlayerState(context.OwnerUid);
            var opp = state.GetPlayerState(context.OwnerUid, true);

            var list = me.Field.Where(e => e != null && e.Health > 0)
                         .Concat(me.MemberZone.Where(e => e != null && e.Health > 0))
                         .Concat(opp.Field.Where(e => e != null && e.Health > 0))
                         .Concat(opp.MemberZone.Where(e => e != null && e.Health > 0)).ToList();
            list.Add(me.Leader);
            list.Add(opp.Leader);
            return list!;
        }
    }
}
