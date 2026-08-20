namespace GameServer.Effects.TargetSelector
{
    /// // 자기 자신 (효과를 발동한 주체)
    public class SelfSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 효과를 발생시킨 원본 개체가 존재하고 살아있을 때만 타겟으로 반환
            if (context.SourceEntity != null && context.SourceEntity.Health > 0)
            {
                yield return context.SourceEntity;
            }
        }
    }

    // 1-1. 단일 대상 (공용 - 아무나 클릭한 대상 그대로 적용)
    public class ContextTargetSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            if (context.TargetEntity != null && context.TargetEntity.Health > 0) 
                yield return context.TargetEntity;
        }
    }

    /// <summary>
    /// 유저가 지정한 단일 대상 중, 리더(영웅)를 제외한 '하수인'만 타겟으로 인정합니다.
    /// </summary>
    public class ContextMinionTargetSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. 타겟이 존재하고 살아있는지 확인
            // 2. 타겟이 리더(명치)가 아닌지 확인 (!IsLeader)
            if (context.TargetEntity != null && 
                context.TargetEntity.Health > 0 && 
                !context.TargetEntity.IsLeader && 
                !context.TargetEntity.IsMember) 
            {
                yield return context.TargetEntity;
            }
        }
    }

    // 1-2. 단일 적 하수인 (클릭한 대상이 적이고 하수인(영웅 아님)일 때만 적용)
    public class ContextEnemyMinionSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            if (target != null && target.Health > 0 && target.OwnerUid != context.OwnerUid && !target.IsLeader)
                yield return target;
        }
    }

    // 1-3. 단일 아군 하수인 (클릭한 대상이 내 것이고 하수인일 때만 적용)
    public class ContextFriendlyMinionSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            if (target != null && target.Health > 0 && target.OwnerUid == context.OwnerUid && !target.IsLeader)
                yield return target;
        }
    }
    
    // 1-4. 단일 적 캐릭터 (명치 포함, 내 것이 아니기만 하면 됨)
    public class ContextEnemyCharacterSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            if (target != null && target.Health > 0 && target.OwnerUid != context.OwnerUid)
                yield return target;
        }
    }
        
    // 1-4. 단일 아군 캐릭터 (명치 포함, 내 것이 아니기만 하면 됨)
    public class ContextFriendlyCharacterSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            // 내 소유(아군)이기만 하면 리더든 하수인이든 전부 통과!
            if (target != null && target.Health > 0 && target.OwnerUid == context.OwnerUid)
                yield return target;
        }
    }

    /// <summary>
    /// 마우스로 지정한 대상 중, 오직 '멤버(Member)'만 타겟으로 인정합니다. (아군/적군 무관)
    /// </summary>
    public class ContextMemberTargetSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            
            // 1. 타겟이 존재하고 살아있는지 확인
            // 2. 타겟이 '멤버(IsMember)'인지 확인
            if (target != null && target.Health > 0 && target.IsMember)
            {
                yield return target;
            }
        }
    }

    /// <summary>
    /// 아군 멤버 존(MemberZone)에 배치된 멤버 개체를 타겟으로 지정합니다.
    /// </summary>
    public class FriendlyMemberSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. 내 UID를 기반으로 내 상태(PlayerState)를 가져옵니다.
            PlayerState me = state.GetPlayerState(context.OwnerUid);
            
            // 2. 내 MemberZone에서 빈자리가 아니고(null이 아님), 살아있는 개체만 가져옵니다.
            return me.MemberZone.Where(e => e != null && e.Health > 0).ToList()!;
        }
    }

    /// <summary>
    /// 상대방 멤버 존(MemberZone)에 배치된 멤버 개체를 타겟으로 지정합니다.
    /// </summary>
    public class EnemyMemberSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. 상대방의 상태(PlayerState)를 가져옵니다. (opp = true)
            PlayerState opp = state.GetPlayerState(context.OwnerUid, true);
            
            // 2. 상대방의 MemberZone에서 빈자리가 아니고, 살아있는 개체만 가져옵니다.
            return opp.MemberZone.Where(e => e != null && e.Health > 0).ToList()!;
        }
    }

    /// <summary>
    /// 효과를 시전한 플레이어(나)의 영웅(리더)을 타겟으로 지정합니다.
    /// </summary>
    public class FriendlyLeaderSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. 내 UID를 기반으로 내 상태(PlayerState)를 가져옵니다.
            PlayerState me = state.GetPlayerState(context.OwnerUid);
            
            // 2. 내 리더 객체를 리스트에 담아 반환합니다.
            return new List<GameEntity> { me.Leader };
        }
    }

    /// <summary>
    /// 상대방 플레이어의 영웅(리더)을 타겟으로 지정합니다.
    /// </summary>
    public class EnemyLeaderSelector : ITargetSelector
    {
        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // 1. GetPlayerState의 두 번째 파라미터(opp)를 true로 주어 '상대방'의 상태를 가져옵니다.
            PlayerState opp = state.GetPlayerState(context.OwnerUid, true);
            
            // 2. 상대방의 리더 객체를 리스트에 담아 반환합니다.
            return new List<GameEntity> { opp.Leader };
        }
    }

}