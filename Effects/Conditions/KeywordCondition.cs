using System;
using System.Collections.Generic;

namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 대상이 특정 키워드(도발, 속공, 천보 등) 또는 상태이상(침묵, 속박 등)을 보유하고 있는지 검사하는 통합 조건입니다.
    /// CheckTarget(Source/Target/Self)을 통해 대상 개체나 시전자, 본체를 지정하여 검사할 수 있습니다.
    /// </summary>
    public class KeywordCondition : ICondition
    {
        public EventCheckTarget CheckTarget { get; set; } = EventCheckTarget.Target;
        public CardKeywords? Keyword { get; set; }
        public List<CardKeywords> Keywords { get; set; } = new List<CardKeywords>();
        public bool HasKeyword { get; set; } = true; // true: 보유/걸림 여부, false: 미보유 여부

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

            var list = new List<CardKeywords>(Keywords);
            if (Keyword.HasValue && !list.Contains(Keyword.Value))
            {
                list.Add(Keyword.Value);
            }

            if (list.Count == 0) return true;

            var targetKeywords = entity?.Keywords ?? card?.CurrentKeywords ?? new List<CardKeywords>();

            foreach (var kw in list)
            {
                bool contains = targetKeywords.Contains(kw);
                if (HasKeyword && !contains) return false;
                if (!HasKeyword && contains) return false;
            }

            return true;
        }

        public bool Check(GameState state, EffectContext context)
        {
            var target = context.TargetEntity;
            if (target == null) return false;

            var list = new List<CardKeywords>(Keywords);
            if (Keyword.HasValue && !list.Contains(Keyword.Value))
            {
                list.Add(Keyword.Value);
            }

            if (list.Count == 0) return true;

            var targetKeywords = target.Keywords ?? target.SourceCard.CurrentKeywords ?? new List<CardKeywords>();

            foreach (var kw in list)
            {
                bool contains = targetKeywords.Contains(kw);
                if (HasKeyword && !contains) return false;
                if (!HasKeyword && contains) return false;
            }

            return true;
        }
    }
}
