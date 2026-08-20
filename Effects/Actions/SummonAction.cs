using System.Threading.Tasks;

namespace GameServer.Effects.Actions
{
    /// <summary>
    /// 특정 카드(토큰 등)를 필드에 소환하는 액션입니다.
    /// </summary>
    public class SummonAction : IAction                                                                                                                 
        {
            public string? CardId { get; set; } // 소환할 카드 ID
            public int Count { get; set; } = 1; // 소환할 마릿수
            public bool IsPositionChoice { get; set; } = false; // ★ 위치 지정 소환 여부 플래그

            public SummonAction() { }

            public async Task ExecuteAsync(GameState state, EffectContext context)
            {
                string targetCardId = string.IsNullOrEmpty(CardId) ? context.SourceCard!.CardId : CardId;

                // 1. [위치 지정 소환인 경우]: 플레이어에게 슬롯 선택 요청 (S_RequestChoice)
                if (IsPositionChoice)
                {
                    int sourceId = context.SourceEntity?.EntityId ?? 0;
                    await state.RequestPlayerChoiceAsync(
                        context.OwnerUid,
                        "POSITION",
                        targetCardId,
                        "하수인을 소환할 필드 위치를 선택해주세요.",
                        sourceId,
                        Count
                    );
                }
                // 2. [자동 소환인 경우]: 빈 슬롯에 순서대로 자동 소환
                else
                {
                    for (int i = 0; i < Count; i++)
                    {
                        state.SummonEntityByEffect(context.OwnerUid, targetCardId);
                    }
                }

                state.RaiseEffectLog(targetCardId, context.SourceCard?.CardId, "SummonAction");
            }
        }      
}