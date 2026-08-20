using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 서버 DB에 존재하는 모든 카드 중 조건에 맞는 카드를 무작위로 창조하여 필드에 소환합니다.
    /// </summary>
    public class RandomSummonAction : IAction
    {
        public int Count { get; set; } = 1;

        // 서치용 필터 옵션들 (null이면 무시)
        public CardType? TargetCardType { get; set; } 
        public int? MinCost { get; set; } 
        public int? MaxCost { get; set; } 
        public CardTribe? TargetTribe { get; set; }

        public Task ExecuteAsync(GameState state, EffectContext context)
        {
            // 1. 서버 DB에서 모든 카드의 설계도(ServerCardData)를 가져옵니다.
            var allCards = ServerCardDatabase.Instance.GetAllCards();

            // 2. 필터 조건에 맞는 카드만 추려냅니다.
            var validCards = allCards.Where(c => 
                // 기본적으로 하수인이나 멤버만 소환 대상 (주문 방어)
                (c.CardType == CardType.하수인 || c.CardType == CardType.멤버) && 
                (TargetCardType == null || c.CardType == TargetCardType) &&
                (MaxCost == null || c.Cost <= MaxCost) &&
                (MinCost == null || c.Cost >= MinCost) &&
                (TargetTribe == null || c.Tribe == TargetTribe)
            ).ToList();

            // 조건에 맞는 카드가 아예 없다면 종료
            if (validCards.Count == 0) return Task.CompletedTask;

            // 3. 지정된 횟수(Count)만큼 무작위로 카드를 뽑아 소환!
            for (int i = 0; i < Count; i++)
            {
                // 걸러진 리스트 중 하나를 랜덤으로 선택
                var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                
                if (selectedCard.CardID != null)
                {
                    // GameState에 이미 만들어둔 '카드 ID로 토큰 생성 및 소환' 함수 활용
                    state.SummonEntityByEffect(context.OwnerUid, selectedCard.CardID);
                }
            }

            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "RandomSummonAction");

            return Task.CompletedTask;
        }
    }
}