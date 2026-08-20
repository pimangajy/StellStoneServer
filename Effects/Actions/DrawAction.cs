using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 카드를 드로우합니다. 필터 옵션을 넣으면 덱에서 조건에 맞는 카드를 서치하여 뽑습니다.
    /// </summary>
    public class DrawAction : IAction
    {
        public int Count { get; set; } = 1;
        
        // 대상 지정: 이전 대화에서 만든 아군 리더(명치)를 기본값으로 사용
        public ITargetSelector Target { get; set; } = new TargetSelector.FriendlyLeaderSelector(); 

        // ==========================================
        // 덱 서치용 필터 옵션들 (null이면 무시)
        // ==========================================
        public string? SpecificCardId { get; set; } // 특정 카드 ID 지정 (예: "cards-gangzi-001")
        public CardType? TargetCardType { get; set; } // 종류 지정 (예: 하수인, 주문)
        public int? MaxCost { get; set; } // 코스트 제한 (예: 2코스트 이하)
        public CardTribe? TargetTribe { get; set; } // 종족 지정 (예: 강도단)

        // 1. [수정] 비동기 대기를 지원하기 위해 Task -> async Task로 선언을 변경합니다.
        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context);
            
            foreach (var target in targets)
            {
                PlayerState p = state.GetPlayerState(target.OwnerUid);
                
                for (int i = 0; i < Count; i++)
                {
                    // 필터가 하나라도 적혀 있는지 확인
                    bool hasFilter = SpecificCardId != null || TargetCardType != null || MaxCost != null || TargetTribe != null;

                    if (!hasFilter)
                    {
                        // 2-A. [수정] 일반 드로우
                        // GameState의 공용 비동기 드로우&동기화 메서드를 호출하여 클라이언트에 패킷을 전송합니다.
                        await state.DrawCardWithSyncAsync(target.OwnerUid);
                    }
                    else
                    {
                        // 2-B. 서치 드로우 (조건이 있음) [1]
                        // 덱을 순회하며 조건에 모두 맞는 카드들을 걸러냅니다 [2].
                        var validCards = p.Deck.Where(c => 
                            (SpecificCardId == null || c.CardId == SpecificCardId) &&
                            (TargetCardType == null || c.Type == TargetCardType) &&
                            (MaxCost == null || c.CurrentCost <= MaxCost) &&
                            (TargetTribe == null || c.Tribe == TargetTribe)
                        ).ToList();

                        if (validCards.Count > 0)
                        {
                            // 조건에 맞는 카드가 여러 장이면 무작위로 하나 선택
                            var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                            
                            // [핵심 로직] 해당 카드를 덱 맨 위(마지막)로 끌어올림 [3]
                            p.Deck.Remove(selectedCard);
                            p.Deck.Add(selectedCard);
                            
                            // [수정] 서치된 카드가 덱 맨 위로 올라갔으므로, 공용 동기화 드로우를 실행합니다.
                            await state.DrawCardWithSyncAsync(target.OwnerUid);
                        }
                        else
                        {
                            // 조건에 맞는 카드가 없으면 아무것도 뽑지 않습니다.
                        }
                    }
                }
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "DrawAction");
            }
        }

        // 3. [삭제] p.DrawCard() 및 UpdateZone을 동기식으로 처리하던 DrawAndSync 헬퍼 함수는 
        // 이제 GameState.DrawCardWithSyncAsync 내부에서 더 안전하게 공통 처리되므로 깔끔하게 제거합니다.
    }
}