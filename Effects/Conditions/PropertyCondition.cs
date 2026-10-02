using System.Linq;

namespace GameServer.Effects.Conditions
{
    public enum CardTargetScope
    {
        Current,              // 현재 대상 또는 시전 카드 (기본값)
        PreviousCard,         // 이번 턴에 직전에 사용한 카드 (연계 / Combo)
        AnyPlayedThisTurn     // 이번 턴에 이전에 사용한 카드 중 하나라도 일치
    }

    /// <summary>
    /// 카드 종류(하수인/주문/멤버), 종족, 직업, 특정 카드 ID 일치 여부를 검사하는 통합 속성 조건입니다.
    /// CheckTarget(Source/Target/Self)을 통해 패킷의 특정 카드를 정확히 지정하여 검사할 수 있습니다.
    /// </summary>
    public class PropertyCondition : ICondition
    {
        public EventCheckTarget CheckTarget { get; set; } = EventCheckTarget.Source;
        public CardTargetScope Scope { get; set; } = CardTargetScope.Current;
        public CardType? Type { get; set; }
        public CardTribe? Tribe { get; set; }
        public CardClass? Class { get; set; }
        public string? CardId { get; set; }
        public bool Invert { get; set; } = false; // true 시 조건 불일치 검사

        /// <summary>
        /// 표준 GameActionPacket 기반 조건 검사 메서드 (타겟팅/비타겟팅 혼선 완전 제거)
        /// </summary>
        public bool Check(GameState state, GameActionPacket packet, GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            // 1. [Current] 현재 대상 / 시전 카드 검사
            if (Scope == CardTargetScope.Current)
            {
                GameCard? card = null;

                switch (CheckTarget)
                {
                    case EventCheckTarget.Source:
                        card = packet.SourceCard ?? packet.SourceEntity?.SourceCard;
                        break;
                    case EventCheckTarget.Target:
                        card = packet.TargetCard ?? packet.TargetEntity?.SourceCard;
                        break;
                    case EventCheckTarget.Self:
                        card = ownerCard ?? ownerEntity?.SourceCard;
                        break;
                    case EventCheckTarget.GameContext:
                        card = packet.SourceCard ?? packet.TargetCard ?? ownerCard;
                        break;
                }

                if (card == null) return Invert;

                bool match = MatchesFilter(card);
                return Invert ? !match : match;
            }

            // 2. [PreviousCard / AnyPlayedThisTurn] 턴 이력 검사
            string playerUid = (CheckTarget == EventCheckTarget.Source) ? packet.SourceUid : ownerCard.OwnerUid;
            var p = state.GetPlayerState(playerUid);
            if (p == null || p.CardsPlayedThisTurn.Count == 0) return Invert;

            var currentCard = packet.SourceCard ?? ownerCard;
            int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));

            if (Scope == CardTargetScope.PreviousCard)
            {
                int prevIndex = (currentIndex >= 0) ? currentIndex - 1 : p.CardsPlayedThisTurn.Count - 1;
                if (prevIndex < 0 || prevIndex >= p.CardsPlayedThisTurn.Count) return Invert;

                var prevCard = p.CardsPlayedThisTurn[prevIndex];
                bool match = MatchesFilter(prevCard);
                return Invert ? !match : match;
            }

            if (Scope == CardTargetScope.AnyPlayedThisTurn)
            {
                var candidates = (currentIndex >= 0)
                    ? p.CardsPlayedThisTurn.Take(currentIndex).ToList()
                    : p.CardsPlayedThisTurn.Where(c => c != currentCard).ToList();

                if (candidates.Count == 0) return Invert;

                bool match = candidates.Any(c => MatchesFilter(c));
                return Invert ? !match : match;
            }

            return Invert;
        }

        public bool Check(GameState state, EffectContext context)
        {
            // 1. [Current] 현재 대상 / 시전 카드 검사
            if (Scope == CardTargetScope.Current)
            {
                var card = (context.TargetEntity != null ? context.TargetEntity.SourceCard : context.TriggerCard) ?? context.SourceCard;
                if (card == null) return Invert;

                bool match = MatchesFilter(card);
                return Invert ? !match : match;
            }

            // 2. [PreviousCard / AnyPlayedThisTurn] 턴 이력 검사
            var p = state.GetPlayerState(context.OwnerUid);
            if (p == null || p.CardsPlayedThisTurn.Count == 0) return Invert;

            var currentCard = context.TriggerCard ?? context.SourceCard;
            int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));

            if (Scope == CardTargetScope.PreviousCard)
            {
                int prevIndex = (currentIndex >= 0) ? currentIndex - 1 : p.CardsPlayedThisTurn.Count - 1;
                if (prevIndex < 0 || prevIndex >= p.CardsPlayedThisTurn.Count) return Invert;

                var prevCard = p.CardsPlayedThisTurn[prevIndex];
                bool match = MatchesFilter(prevCard);
                return Invert ? !match : match;
            }

            if (Scope == CardTargetScope.AnyPlayedThisTurn)
            {
                var candidates = (currentIndex >= 0) 
                    ? p.CardsPlayedThisTurn.Take(currentIndex).ToList() 
                    : p.CardsPlayedThisTurn.Where(c => c != currentCard).ToList();

                if (candidates.Count == 0) return Invert;

                bool match = candidates.Any(c => MatchesFilter(c));
                return Invert ? !match : match;
            }

            return Invert;
        }

        private bool MatchesFilter(GameCard card)
        {
            if (Type.HasValue && card.Type != Type.Value) return false;
            if (Tribe.HasValue && card.Tribe != Tribe.Value) return false;
            if (Class.HasValue && card.Class != Class.Value) return false;
            if (!string.IsNullOrEmpty(CardId) && card.CardId != CardId) return false;
            return true;
        }
    }
}
