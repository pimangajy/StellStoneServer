using System.Linq;

namespace GameServer.Effects.Conditions
{
    /// <summary>
    /// 연계(Combo), 이번 턴/지난 턴의 행동 이력(특정 카드 사용, 사망 횟수 등)을 검사하는 통합 이력 조건입니다.
    /// </summary>
    public class HistoryCondition : ICondition
    {
        public HistoryScope Scope { get; set; } = HistoryScope.ThisTurn;
        public GameActionKind? Action { get; set; } = GameActionKind.PlayCard;
        public CardType? CardType { get; set; }
        public CardTribe? Tribe { get; set; }
        public TargetAlliance Alliance { get; set; } = TargetAlliance.Friendly;
        public int MinCount { get; set; } = 1;
        public bool Invert { get; set; } = false;

        /// <summary>
        /// 표준 GameActionPacket 기반 조건 검사 메서드
        /// </summary>
        public bool Check(GameState state, GameActionPacket packet, GameCard ownerCard, GameEntity? ownerEntity = null)
        {
            int matchCount = 0;
            string myUid = ownerCard.OwnerUid;

            // 1. [ActionHistory 기반 검사] 전체 게임 행동 기록이 있으면 최우선으로 정밀 검사
            if (state.ActionHistory != null && state.ActionHistory.Count > 0)
            {
                var history = state.ActionHistory;

                if (Scope == HistoryScope.PreviousAction)
                {
                    // 현재 패킷 직전의 패킷 찾기
                    int currentIndex = history.FindLastIndex(p => p == packet);
                    int prevIndex = (currentIndex >= 0) ? currentIndex - 1 : history.Count - 1;

                    if (prevIndex >= 0 && prevIndex < history.Count)
                    {
                        var prevPacket = history[prevIndex];
                        if (MatchesPacketFilter(prevPacket, myUid))
                        {
                            matchCount = 1;
                        }
                    }
                }
                else
                {
                    var filtered = history.AsEnumerable();

                    // 턴 범위 필터
                    if (Scope == HistoryScope.ThisTurn)
                    {
                        int currentTurn = packet.TurnNumber > 0 ? packet.TurnNumber : state._turnCount;
                        filtered = filtered.Where(p => p.TurnNumber == currentTurn && p != packet);
                    }
                    else if (Scope == HistoryScope.LastTurn)
                    {
                        int lastTurn = (packet.TurnNumber > 0 ? packet.TurnNumber : state._turnCount) - 1;
                        filtered = filtered.Where(p => p.TurnNumber == lastTurn);
                    }
                    else if (Scope == HistoryScope.Current)
                    {
                        filtered = filtered.Where(p => p == packet);
                    }

                    // 행동 종류 / 진영 / 카드 속성 필터링
                    matchCount = filtered.Count(p => MatchesPacketFilter(p, myUid));
                }

                bool res = matchCount >= MinCount;
                return Invert ? !res : res;
            }

            // 2. [Fallback] ActionHistory가 아직 비어있을 때 CardsPlayedThisTurn으로 대체 검사
            if (Scope == HistoryScope.PreviousAction)
            {
                var p = state.GetPlayerState(myUid);
                if (p == null || p.CardsPlayedThisTurn.Count == 0) return Invert;

                var currentCard = packet.SourceCard ?? ownerCard;
                int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));
                int prevIndex = (currentIndex >= 0) ? currentIndex - 1 : p.CardsPlayedThisTurn.Count - 1;

                if (prevIndex >= 0 && prevIndex < p.CardsPlayedThisTurn.Count)
                {
                    var prevCard = p.CardsPlayedThisTurn[prevIndex];
                    if (MatchesCardFilter(prevCard)) matchCount = 1;
                }

                bool res = matchCount >= MinCount;
                return Invert ? !res : res;
            }

            if (Scope == HistoryScope.ThisTurn)
            {
                if (!Action.HasValue || Action.Value == GameActionKind.PlayCard)
                {
                    var p = state.GetPlayerState(myUid);
                    if (p != null)
                    {
                        var currentCard = packet.SourceCard ?? ownerCard;
                        int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));

                        var playedCards = (currentIndex >= 0)
                            ? p.CardsPlayedThisTurn.Take(currentIndex)
                            : p.CardsPlayedThisTurn.Where(c => c != currentCard);

                        matchCount = playedCards.Count(c => MatchesCardFilter(c));
                    }
                }
            }

            bool passed = matchCount >= MinCount;
            return Invert ? !passed : passed;
        }

        private bool MatchesPacketFilter(GameActionPacket p, string myUid)
        {
            if (Action.HasValue && p.Action != Action.Value) return false;

            if (Alliance == TargetAlliance.Friendly && p.SourceUid != myUid) return false;
            if (Alliance == TargetAlliance.Enemy && (string.IsNullOrEmpty(p.SourceUid) || p.SourceUid == myUid)) return false;

            var card = p.SourceCard ?? p.TargetCard;
            if (card != null)
            {
                if (!MatchesCardFilter(card)) return false;
            }
            else if (CardType.HasValue || Tribe.HasValue)
            {
                return false;
            }

            return true;
        }

        public bool Check(GameState state, EffectContext context)
        {
            int matchCount = 0;
            string myUid = context.OwnerUid;

            if (Scope == HistoryScope.PreviousAction)
            {
                var p = state.GetPlayerState(myUid);
                if (p == null || p.CardsPlayedThisTurn.Count == 0) return Invert;

                var currentCard = context.TriggerCard ?? context.SourceCard;
                int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));
                int prevIndex = (currentIndex >= 0) ? currentIndex - 1 : p.CardsPlayedThisTurn.Count - 1;

                if (prevIndex >= 0 && prevIndex < p.CardsPlayedThisTurn.Count)
                {
                    var prevCard = p.CardsPlayedThisTurn[prevIndex];
                    if (MatchesCardFilter(prevCard)) matchCount = 1;
                }

                bool res = matchCount >= MinCount;
                return Invert ? !res : res;
            }

            if (Scope == HistoryScope.ThisTurn)
            {
                var p = state.GetPlayerState(myUid);
                if (p != null)
                {
                    var currentCard = context.TriggerCard ?? context.SourceCard;
                    int currentIndex = p.CardsPlayedThisTurn.FindLastIndex(c => c == currentCard || (currentCard != null && c.InstanceId == currentCard.InstanceId));

                    var playedCards = (currentIndex >= 0)
                        ? p.CardsPlayedThisTurn.Take(currentIndex)
                        : p.CardsPlayedThisTurn.Where(c => c != currentCard);

                    matchCount = playedCards.Count(c => MatchesCardFilter(c));
                }
            }

            bool passed = matchCount >= MinCount;
            return Invert ? !passed : passed;
        }

        private bool MatchesCardFilter(GameCard card)
        {
            if (CardType.HasValue && card.Type != CardType.Value) return false;
            if (Tribe.HasValue && card.Tribe != Tribe.Value) return false;
            return true;
        }
    }
}
