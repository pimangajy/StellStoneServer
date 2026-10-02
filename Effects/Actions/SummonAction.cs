using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public enum SummonSource
    {
        Create,        // 새 토큰/카드 창조 소환 (기본값)
        Token = Create,// 토큰 표기 호환용
        Deck,          // 덱에서 특수 소환 (리크루트)
        Hand,          // 손패에서 특수 소환
        Graveyard      // 묘지에서 부활 소환 (Resurrect)
    }

    public enum SummonPlacement
    {
        Auto,       // 빈자리 순서대로 자동 소환 (기본값)
        BothSides,  // 시전자 기준 좌우 양옆에 1마리씩 소환 (기사단장/박사 붐 호위 연출)
        Left,       // 시전자 바로 왼쪽에 소환
        Right,      // 시전자 바로 오른쪽에 소환
        Choice      // 유저가 직접 필드 슬롯 클릭 선택
    }

    /// <summary>
    /// 토큰 창조, 덱 특수 소환, 손패 특수 소환, 묘지 부활 및 위치 지정(좌/우/양옆) 배치를 모두 처리하는 통합 만능 소환 액션입니다.
    /// </summary>
    public class SummonAction : IAction
    {
        // 1. 소환 출처 (Create, Deck, Hand, Graveyard)
        public SummonSource Source { get; set; } = SummonSource.Create;

        // 2. 소환 배치 위치 (Auto, BothSides, Left, Right, Choice)
        public SummonPlacement Placement { get; set; } = SummonPlacement.Auto;

        // 3. 소환할 마릿수 및 대상 플레이어
        public int Count { get; set; } = 1;
        public ITargetSelector Target { get; set; } = new TargetSelector { Scope = TargetScope.All, Alliance = TargetAlliance.Friendly, Category = TargetCategory.Leader };

        // 4. 특정 카드 지정 옵션
        public string? SpecificCardId { get; set; }
        public string? CardId { get => SpecificCardId; set => SpecificCardId = value; } // JSON 호환

        // 5. 특정 후보군 목록 중 무작위 소환
        public List<string>? CandidateCardIds { get; set; }

        // 6. 조건부 서치/소환 필터 옵션
        public CardType? TargetCardType { get; set; }
        public int? MinCost { get; set; }
        public int? MaxCost { get; set; }
        public CardTribe? TargetTribe { get; set; }

        // 7. 위치 직접 지정 소환 여부 (Placement = Choice와 동일)
        public bool IsPositionChoice 
        { 
            get => Placement == SummonPlacement.Choice; 
            set { if (value) Placement = SummonPlacement.Choice; } 
        }

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            var targets = Target.GetTargets(state, context).ToList();
            if (targets.Count == 0)
            {
                PlayerState me = state.GetPlayerState(context.OwnerUid);
                if (me?.Leader != null) targets.Add(me.Leader);
            }

            foreach (var target in targets)
            {
                PlayerState p = state.GetPlayerState(target.OwnerUid);
                if (p == null) continue;

                switch (Source)
                {
                    // =========================================================
                    // 1. [Create] 새로운 카드/토큰 창조 소환
                    // =========================================================
                    case SummonSource.Create:
                        await ExecuteCreateSummonAsync(state, context, p);
                        break;

                    // =========================================================
                    // 2. [Deck] 덱에서 특수 소환 (리크루트)
                    // =========================================================
                    case SummonSource.Deck:
                        ExecuteDeckSummon(state, p);
                        break;

                    // =========================================================
                    // 3. [Hand] 손패에서 특수 소환
                    // =========================================================
                    case SummonSource.Hand:
                        ExecuteHandSummon(state, context, p);
                        break;

                    // =========================================================
                    // 4. [Graveyard] 묘지에서 부활 소환 (Resurrect)
                    // =========================================================
                    case SummonSource.Graveyard:
                        ExecuteGraveyardSummon(state, p);
                        break;
                }
            }

            // 소환으로 인한 필드/손패 변화 -> 동적 스탯 실시간 동기화
            await state.RefreshAllDynamicHandStatsAsync();
            state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "SummonAction");
        }

        private async Task ExecuteCreateSummonAsync(GameState state, EffectContext context, PlayerState p)
        {
            string targetCardId = string.IsNullOrEmpty(SpecificCardId) ? (context.SourceCard?.CardId ?? "") : SpecificCardId;

            // 1. [Choice] 위치 직접 지정 소환인 경우
            if (Placement == SummonPlacement.Choice && !string.IsNullOrEmpty(targetCardId))
            {
                if (!state.HasValidChoices(p.Uid, "POSITION", targetCardId))
                {
                    state.AddLog("System", "SUMMON_FAIL", $"{p.Uid}의 필드에 빈자리가 없어 소환에 실패했습니다.");
                    await state.SendActionFailAsync(p.Uid, "소환할 수 있는 빈 자리가 없습니다.");
                    return;
                }

                int sourceId = context.SourceEntity?.EntityId ?? 0;
                int summonCount = (Placement == SummonPlacement.BothSides && Count <= 1) ? 2 : Count;
                await state.RequestPlayerChoiceAsync(p.Uid, "POSITION", targetCardId, "하수인을 소환할 필드 위치를 선택해주세요.", sourceId, summonCount);
                return;
            }

            int finalCount = (Placement == SummonPlacement.BothSides && Count <= 1) ? 2 : Count;

            // 2. [SpecificCardId] 특정 CardId가 확정 지정된 경우
            if (!string.IsNullOrEmpty(SpecificCardId))
            {
                for (int i = 0; i < finalCount; i++)
                {
                    SummonCardWithPlacement(state, context, p, SpecificCardId, i, finalCount);
                }
                return;
            }

            // 3. [CandidateCardIds] 특정 후보 카드 ID 리스트가 지정된 경우
            if (CandidateCardIds != null && CandidateCardIds.Count > 0)
            {
                for (int i = 0; i < finalCount; i++)
                {
                    string chosenId = CandidateCardIds[state.Rng.Next(CandidateCardIds.Count)];
                    SummonCardWithPlacement(state, context, p, chosenId, i, finalCount);
                }
                return;
            }

            // 4. 전체 DB 조건부 무작위 창조 소환
            var allCards = ServerCardDatabase.Instance.GetAllCards();
            var validCards = allCards.Where(c =>
                (c.CardType == CardType.하수인 || c.CardType == CardType.멤버) &&
                (TargetCardType == null || c.CardType == TargetCardType) &&
                (MaxCost == null || c.Cost <= MaxCost) &&
                (MinCost == null || c.Cost >= MinCost) &&
                (TargetTribe == null || c.Tribe == TargetTribe)
            ).ToList();

            if (validCards.Count == 0) return;

            for (int i = 0; i < finalCount; i++)
            {
                var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                if (selectedCard.CardID != null)
                {
                    SummonCardWithPlacement(state, context, p, selectedCard.CardID, i, finalCount);
                }
            }
        }

        private void SummonCardWithPlacement(GameState state, EffectContext context, PlayerState p, string cardId, int tokenIndex, int totalTokens)
        {
            // [BothSides] 시전자 기준 좌우 양옆 소환 (0번=왼쪽, 1번=오른쪽)
            if (Placement == SummonPlacement.BothSides && context.SourceEntity != null)
            {
                int center = context.SourceEntity.Position;
                int targetPos = (tokenIndex == 0) ? center - 1 : (tokenIndex == 1) ? center + 1 : -1;
                if (targetPos >= 0 && targetPos < p.Field.Length && p.Field[targetPos] == null)
                {
                    state.SummonEntityAtPosition(p.Uid, cardId, targetPos);
                    return;
                }
            }
            // [Left] 시전자 바로 왼쪽 소환
            else if (Placement == SummonPlacement.Left && context.SourceEntity != null)
            {
                int leftPos = context.SourceEntity.Position - 1;
                if (leftPos >= 0 && leftPos < p.Field.Length && p.Field[leftPos] == null)
                {
                    state.SummonEntityAtPosition(p.Uid, cardId, leftPos);
                    return;
                }
            }
            // [Right] 시전자 바로 오른쪽 소환
            else if (Placement == SummonPlacement.Right && context.SourceEntity != null)
            {
                int rightPos = context.SourceEntity.Position + 1;
                if (rightPos >= 0 && rightPos < p.Field.Length && p.Field[rightPos] == null)
                {
                    state.SummonEntityAtPosition(p.Uid, cardId, rightPos);
                    return;
                }
            }

            // 기본/Fallback: 빈자리 순서대로 자동 소환
            state.SummonEntityByEffect(p.Uid, cardId);
        }

        private void ExecuteDeckSummon(GameState state, PlayerState p)
        {
            for (int i = 0; i < Count; i++)
            {
                var validCards = p.Deck.Where(c =>
                    (c.Type == CardType.하수인 || c.Type == CardType.멤버) &&
                    (SpecificCardId == null || c.CardId == SpecificCardId) &&
                    (TargetCardType == null || c.Type == TargetCardType) &&
                    (MaxCost == null || c.CurrentCost <= MaxCost) &&
                    (MinCost == null || c.CurrentCost >= MinCost) &&
                    (TargetTribe == null || c.Tribe == TargetTribe)
                ).ToList();

                if (validCards.Count > 0)
                {
                    var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                    p.Deck.Remove(selectedCard);
                    state.SummonExistingCard(p.Uid, selectedCard);
                }
                else
                {
                    break;
                }
            }
        }

        private void ExecuteHandSummon(GameState state, EffectContext context, PlayerState p)
        {
            // [ON_DRAW 연계 소환] 드로우 연계 액션으로 호출된 경우, 방금 뽑은 바로 그 카드(TriggerCard) 1장을 즉시 소환
            if (context.Trigger == EffectTriggerType.ON_DRAW && context.TriggerCard != null && p.Hand.Contains(context.TriggerCard))
            {
                p.Hand.Remove(context.TriggerCard);
                state.SummonExistingCard(p.Uid, context.TriggerCard);
                return;
            }

            for (int i = 0; i < Count; i++)
            {
                var validCards = p.Hand.Where(c =>
                    (c.Type == CardType.하수인 || c.Type == CardType.멤버) &&
                    (SpecificCardId == null || c.CardId == SpecificCardId) &&
                    (TargetCardType == null || c.Type == TargetCardType) &&
                    (MaxCost == null || c.CurrentCost <= MaxCost) &&
                    (MinCost == null || c.CurrentCost >= MinCost) &&
                    (TargetTribe == null || c.Tribe == TargetTribe)
                ).ToList();

                if (validCards.Count > 0)
                {
                    var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                    p.Hand.Remove(selectedCard);
                    state.SummonExistingCard(p.Uid, selectedCard);
                }
                else
                {
                    break;
                }
            }
        }

        private void ExecuteGraveyardSummon(GameState state, PlayerState p)
        {
            for (int i = 0; i < Count; i++)
            {
                var validCards = p.Graveyard.Where(c =>
                    (c.Type == CardType.하수인 || c.Type == CardType.멤버) &&
                    (SpecificCardId == null || c.CardId == SpecificCardId) &&
                    (TargetCardType == null || c.Type == TargetCardType) &&
                    (MaxCost == null || c.CurrentCost <= MaxCost) &&
                    (MinCost == null || c.CurrentCost >= MinCost) &&
                    (TargetTribe == null || c.Tribe == TargetTribe)
                ).ToList();

                if (validCards.Count > 0)
                {
                    var selectedCard = validCards[state.Rng.Next(validCards.Count)];
                    p.Graveyard.Remove(selectedCard);
                    state.SummonExistingCard(p.Uid, selectedCard);
                }
                else
                {
                    break;
                }
            }
        }
    }
}
