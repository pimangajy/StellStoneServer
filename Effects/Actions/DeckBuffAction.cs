using System.Threading.Tasks;
using GameServer.Effects.TargetSelector;

namespace GameServer.Effects.Actions
{
    public class DeckBuffAction : IAction
    {
        public ITargetSelector Target { get; set; } = new SelfSelector();
        public int AttackBuff { get; set; }
        public int HealthBuff { get; set; }
        public int CostBuff { get; set; }

        // 덱 카드를 선별하기 위한 조립식 조건 리스트
        public List<ICondition> Filters { get; set; } = new List<ICondition>();

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context).ToList();
            
            // 주문 카드 등 SourceEntity가 없어 SelfSelector가 비어있는 경우 시전자 본인의 리더를 기본 타겟으로 설정
            if (targets.Count == 0)
            {
                PlayerState me = state.GetPlayerState(context.OwnerUid);
                if (me?.Leader != null) targets.Add(me.Leader);
            }
            
            foreach (var target in targets)
            {
                PlayerState p = state.GetPlayerState(target.OwnerUid);
                
                // 💡 [사용자님의 아이디어가 실현되는 구간] 조건에 맞는 카드들을 담을 바구니를 만듭니다.
                List<GameCard> cardsToBuff = new List<GameCard>();

                foreach (var card in p.Deck)
                {
                    if (card.Type != CardType.하수인) continue;

                    // 기존 조건(Condition) 판정기들과의 호환을 위해 손패 카드를 임시 개체(Mock)로 포장합니다.
                    var mockEntity = new GameEntity(0, card, p.Uid);
                    var tempContext = new EffectContext(p.Uid, context.SourceCard, EffectTriggerType.NONE)
                    {
                        TargetEntity = mockEntity,
                        SourceEntity = context.SourceEntity
                    };

                    bool isValid = true;
                    foreach (var filter in Filters)
                    {
                        if (!filter.Check(state, tempContext))
                        {
                            isValid = false; // 조건 중 하나라도 만족 못 하면 탈락
                            break;
                        }
                    }

                    if (isValid)
                    {
                        // 조건 관문을 모두 통과한 카드를 바구니에 담습니다.
                        cardsToBuff.Add(card);
                    }
                }

                // 디버그 확인용 로그 작성
                Console.WriteLine($"[DeckBuffAction] 🃏 {p.Uid}의 덱 하수인 {cardsToBuff.Count}장에게 버프를 전송합니다.");

                // 💡 걸러진 카드 리스트를 한 번에 전달하여 일괄 처리합니다!
                await state.ApplyDeckBuffAsync(p.Uid, AttackBuff, HealthBuff, CostBuff, cardsToBuff);
                
                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "DeckBuffAction");
            }
        }
    }
}