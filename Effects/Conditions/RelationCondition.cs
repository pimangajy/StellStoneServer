namespace GameServer.Effects.Conditions
{
    public enum TargetAlliance
    {
        Any,
        Friendly,
        Enemy
    }

    /// <summary>
    /// 피아 식별(아군/적군), 시전자 본인 여부, 영웅/하수인 여부, 부상(피해) 상태 여부를 검사하는 통합 관계 조건입니다.
    /// CheckTarget을 통해 주체(Source: 공격자/시전자), 대상(Target: 피격자/수혜자), 본체(Self: 이 카드)를 지정할 수 있습니다.
    /// </summary>
    public class RelationCondition : ICondition
    {
        public EventCheckTarget CheckTarget { get; set; } = EventCheckTarget.Target;
        public TargetAlliance Alliance { get; set; } = TargetAlliance.Any;
        public bool? IsSelf { get; set; }      // true: 본인, false: 본인 제외
        public bool? IsLeader { get; set; }    // true: 영웅(리더), false: 일반 하수인/멤버
        public bool? IsDamaged { get; set; }   // true: 피해 입은 상태, false: 풀피

        /// <summary>
        /// 표준 GameActionPacket 기반 조건 검사 메서드
        /// </summary>
        public bool Check(GameState state, GameActionPacket packet, GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            string targetUid = "";
            GameEntity? targetEntity = null;
            GameCard? targetCard = null;

            switch (CheckTarget)
            {
                case EventCheckTarget.Source:
                    targetUid = packet.SourceUid;
                    targetEntity = packet.SourceEntity;
                    targetCard = packet.SourceCard ?? packet.SourceEntity?.SourceCard;
                    break;
                case EventCheckTarget.Target:
                    targetUid = packet.TargetUid ?? packet.TargetEntity?.OwnerUid ?? "";
                    targetEntity = packet.TargetEntity;
                    targetCard = packet.TargetCard ?? packet.TargetEntity?.SourceCard;
                    break;
                case EventCheckTarget.Self:
                    targetUid = ownerCard.OwnerUid;
                    targetEntity = ownerEntity;
                    targetCard = ownerCard;
                    break;
                case EventCheckTarget.GameContext:
                    targetUid = packet.TurnPlayerUid ?? "";
                    break;
            }

            // 1. 피아 식별 검사 (Alliance)
            string myUid = ownerCard.OwnerUid;
            if (Alliance == TargetAlliance.Friendly && targetUid != myUid)
                return false;
            if (Alliance == TargetAlliance.Enemy && (string.IsNullOrEmpty(targetUid) || targetUid == myUid))
                return false;

            // 2. 본인 여부 검사 (IsSelf)
            if (IsSelf.HasValue)
            {
                bool isSame = false;
                if (targetEntity != null && ownerEntity != null && targetEntity.EntityId == ownerEntity.EntityId)
                    isSame = true;
                else if (targetCard != null && ownerCard != null && targetCard.InstanceId == ownerCard.InstanceId)
                    isSame = true;
                else if (targetEntity != null && targetEntity.SourceCard != null && targetEntity.SourceCard?.InstanceId == ownerCard.InstanceId)
                    isSame = true;

                if (IsSelf.Value && !isSame) return false;
                if (!IsSelf.Value && isSame) return false;
            }

            // 3. 영웅(리더) 여부 검사 (IsLeader)
            if (IsLeader.HasValue)
            {
                if (targetEntity == null) return false;
                if (IsLeader.Value && !targetEntity.IsLeader) return false;
                if (!IsLeader.Value && targetEntity.IsLeader) return false;
            }

            // 4. 피해(부상) 여부 검사 (IsDamaged)
            if (IsDamaged.HasValue)
            {
                if (targetEntity == null) return false;
                bool damaged = targetEntity.Health < targetEntity.MaxHealth;
                if (IsDamaged.Value && !damaged) return false;
                if (!IsDamaged.Value && damaged) return false;
            }

            return true;
        }

        public bool Check(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;

            // 1. 피아 식별 검사
            string targetUid = target?.OwnerUid ?? context.TriggerOwnerUid ?? context.OwnerUid;
            if (Alliance == TargetAlliance.Friendly && targetUid != context.OwnerUid)
                return false;
            if (Alliance == TargetAlliance.Enemy && targetUid == context.OwnerUid)
                return false;

            // 2. 본인 여부 검사
            if (IsSelf.HasValue)
            {
                if (target == null && context.TriggerCard == null) return false;
                bool isSame = (target != null && context.SourceEntity != null && context.SourceEntity.EntityId == target.EntityId)
                           || (target != null && context.SourceCard != null && target.SourceCard == context.SourceCard)
                           || (context.TriggerCard != null && context.SourceCard != null && context.TriggerCard.InstanceId == context.SourceCard.InstanceId);

                if (IsSelf.Value && !isSame) return false;
                if (!IsSelf.Value && isSame) return false;
            }

            // 3. 영웅(리더) 여부 검사
            if (IsLeader.HasValue)
            {
                if (target == null) return false;
                if (IsLeader.Value && !target.IsLeader) return false;
                if (!IsLeader.Value && target.IsLeader) return false;
            }

            // 4. 피해(부상) 여부 검사
            if (IsDamaged.HasValue)
            {
                if (target == null) return false;
                bool damaged = target.Health < target.MaxHealth;
                if (IsDamaged.Value && !damaged) return false;
                if (!IsDamaged.Value && damaged) return false;
            }

            return true;
        }
    }
}
