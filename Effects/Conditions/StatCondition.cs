namespace GameServer.Effects.Conditions
{
    public enum StatType
    {
        Attack,
        Health,
        Cost,
        MaxHealth,
        Stacks          // 손패/필드 누적 스택 (CustomValue)
    }

    public enum ComparisonType
    {
        GreaterOrEqual, // 이상 (>=)
        LessOrEqual,    // 이하 (<=)
        Equal           // 같음 (==)
    }

    /// <summary>
    /// 대상의 스탯(공격력, 체력, 코스트, 최대체력, 스택)을 이상/이하/같음으로 비교하는 만능 조건입니다.
    /// CheckTarget(Self/Source/Target)을 통해 누구의 스탯을 비교할지 명시할 수 있습니다.
    /// </summary>
    public class StatCondition : ICondition
    {
        public EventCheckTarget CheckTarget { get; set; } = EventCheckTarget.Self;
        public StatType Stat { get; set; } = StatType.Health;
        public ComparisonType Comparison { get; set; } = ComparisonType.GreaterOrEqual;
        public int Value { get; set; }

        /// <summary>
        /// 표준 GameActionPacket 기반 조건 검사 메서드
        /// </summary>
        public bool Check(GameState state, GameActionPacket packet, GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            GameEntity? entity = null;
            GameCard? card = null;

            switch (CheckTarget)
            {
                case EventCheckTarget.Source:
                    entity = packet.SourceEntity;
                    card = packet.SourceCard ?? packet.SourceEntity?.SourceCard;
                    break;
                case EventCheckTarget.Target:
                    entity = packet.TargetEntity;
                    card = packet.TargetCard ?? packet.TargetEntity?.SourceCard;
                    break;
                case EventCheckTarget.Self:
                    entity = ownerEntity;
                    card = ownerCard;
                    break;
                case EventCheckTarget.GameContext:
                    entity = packet.SourceEntity ?? packet.TargetEntity ?? ownerEntity;
                    card = packet.SourceCard ?? packet.TargetCard ?? ownerCard;
                    break;
            }

            int actualVal = 0;
            switch (Stat)
            {
                case StatType.Attack:
                    actualVal = entity?.Attack ?? card?.CurrentAttack ?? 0;
                    break;
                case StatType.Health:
                    actualVal = entity?.Health ?? card?.CurrentHealth ?? 0;
                    break;
                case StatType.Cost:
                    actualVal = card?.CurrentCost ?? entity?.SourceCard?.CurrentCost ?? 0;
                    break;
                case StatType.MaxHealth:
                    actualVal = entity?.MaxHealth ?? card?.CurrentHealth ?? 0;
                    break;
                case StatType.Stacks:
                    actualVal = card?.CustomValue ?? entity?.SourceCard?.CustomValue ?? 0;
                    break;
            }

            switch (Comparison)
            {
                case ComparisonType.GreaterOrEqual:
                    return actualVal >= Value;
                case ComparisonType.LessOrEqual:
                    return actualVal <= Value;
                case ComparisonType.Equal:
                    return actualVal == Value;
                default:
                    return false;
            }
        }

        public bool Check(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            var card = (target != null ? target.SourceCard : context.TriggerCard) ?? context.SourceCard;

            int actualVal = 0;
            switch (Stat)
            {
                case StatType.Attack:
                    actualVal = target?.Attack ?? card?.CurrentAttack ?? 0;
                    break;
                case StatType.Health:
                    actualVal = target?.Health ?? card?.CurrentHealth ?? 0;
                    break;
                case StatType.Cost:
                    actualVal = card?.CurrentCost ?? target?.SourceCard?.CurrentCost ?? 0;
                    break;
                case StatType.MaxHealth:
                    actualVal = target?.MaxHealth ?? card?.CurrentHealth ?? 0;
                    break;
                case StatType.Stacks:
                    actualVal = card?.CustomValue ?? target?.SourceCard?.CustomValue ?? 0;
                    break;
            }

            switch (Comparison)
            {
                case ComparisonType.GreaterOrEqual:
                    return actualVal >= Value;
                case ComparisonType.LessOrEqual:
                    return actualVal <= Value;
                case ComparisonType.Equal:
                    return actualVal == Value;
                default:
                    return false;
            }
        }
    }
}
