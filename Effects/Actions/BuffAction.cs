using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameServer.Effects.Conditions;
using GameServer.Effects.Targeting;

namespace GameServer.Effects.Actions
{
    public enum BuffZone
    {
        Field, // 필드 하수인/영웅 (기본값)
        Hand,  // 손패 카드
        Deck,  // 덱 카드
        All    // 필드 + 손패 + 덱 전체
    }

    /// <summary>
    /// 필드, 손패, 덱에 존재하는 하수인에게 스탯(공/체/코스트), 특수 오라(주문공격력/주문약화/능력강화) 및 키워드(도발, 속공 등)를 부여하는 통합 만능 버프 액션입니다.
    /// </summary>
    public class BuffAction : IAction
    {
        // 1. 버프 적용 구역 (Field, Hand, Deck, All)
        public BuffZone Zone { get; set; } = BuffZone.Field;

        // 2. 타겟 셀렉터 및 조건 필터
        public ITargetSelector Target { get; set; } = new TargetSelector();
        public List<ICondition> Filters { get; set; } = new List<ICondition>();

        // 3. 스탯 버프 수치
        public int AttackBuff { get; set; } = 0;
        public int HealthBuff { get; set; } = 0;
        public int CostBuff { get; set; } = 0; // 손패/덱 전용 코스트 증감 (예: -1)

        // 4. 특수 스탯(주문공격력/주문약화/능력강화) 버프 수치
        public int SpellAmp { get; set; } = 0;       // 주문 공격력 / 증폭 (+1, +2 등)
        public int SpellWeakness { get; set; } = 0;  // 주문 약화 (피격 주문 피해 감소)
        public int BuffAmpAttack { get; set; } = 0;  // 아군 주문 공격력 버프 증폭
        public int BuffAmpHealth { get; set; } = 0;  // 아군 주문 체력 버프 증폭

        // 5. 키워드 부여 (단수형/복수형 모두 지원)
        public string? GrantedKeyword { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();

        // 6. 지속 턴 수 (0 = 영구 지속, 1 = 이번 턴 동안만, 2 = 2턴 동안 등)
        public int Duration { get; set; } = 0;

        // 7. 손패/카드 누적 스택(CustomValue) 버프 수치
        public int CustomValueBuff { get; set; } = 0;

        public async Task ExecuteAsync(GameState state, EffectContext context)
        {
            // 0. [CustomValueBuff] 손패 카드 본체의 누적 스택 증가 (주문석 / 손패 충전형 카드)
            if (CustomValueBuff != 0 && context.SourceCard != null)
            {
                context.SourceCard.CustomValue += CustomValueBuff;
                state.LogDebug("CustomValue", $"🔮 [손패 스택 누적] '{context.SourceCard.CardName}'({context.SourceCard.CardId}) 누적치 +{CustomValueBuff} -> 현재: {context.SourceCard.CustomValue}");
                await state.SyncHandCardAsync(context.OwnerUid, context.SourceCard);
            }

            // 키워드 목록 합산
            List<string> allKeywords = new List<string>(Keywords);
            if (!string.IsNullOrEmpty(GrantedKeyword) && !allKeywords.Contains(GrantedKeyword))
            {
                allKeywords.Add(GrantedKeyword);
            }

            // 0-1. [손패 본체 Self 버프 처리]
            // 카드가 손패에 머무는 상태에서 시전자 본체(Scope == Self)를 대상으로 코스트나 스탯/키워드 버프를 주는 경우
            // (예: 거인 바쿠의 손패 비용 감소, 손패 하수인 자체 성장 효과 등)
            bool isSelfTarget = Target is TargetSelector ts && ts.Scope == TargetScope.Self;
            bool isSourceInHand = context.SourceCard != null && (context.SourceCard.CurrentZone == GameServer.Effects.Zone.Hand || state.GetPlayerState(context.OwnerUid)?.Hand.Contains(context.SourceCard) == true);

            if (isSelfTarget && isSourceInHand && context.SourceEntity == null)
            {
                if (CostBuff != 0 || AttackBuff != 0 || HealthBuff != 0 || allKeywords.Count > 0 || SpellAmp != 0 || SpellWeakness != 0 || BuffAmpAttack != 0 || BuffAmpHealth != 0)
                {
                    await state.ApplyHandBuffAsync(context.OwnerUid, AttackBuff, HealthBuff, CostBuff, new List<GameCard> { context.SourceCard! }, allKeywords, SpellAmp, SpellWeakness, BuffAmpAttack, BuffAmpHealth);
                    state.RaiseEffectLog(context.OwnerUid, context.SourceCard?.CardId, "BuffAction");
                }
                return;
            }

            // =================================================================
            // 1. [필드 버프] Zone == Field 또는 All
            // =================================================================
            if (Zone == BuffZone.Field || Zone == BuffZone.All)
            {
                await ExecuteFieldBuffAsync(state, context, allKeywords);
            }

            // =================================================================
            // 2. [손패 버프] Zone == Hand 또는 All
            // =================================================================
            if (Zone == BuffZone.Hand || Zone == BuffZone.All)
            {
                await ExecuteHandBuffAsync(state, context, allKeywords);
            }

            // =================================================================
            // 3. [덱 버프] Zone == Deck 또는 All
            // =================================================================
            if (Zone == BuffZone.Deck || Zone == BuffZone.All)
            {
                await ExecuteDeckBuffAsync(state, context, allKeywords);
            }
        }

        private Task ExecuteFieldBuffAsync(GameState state, EffectContext context, List<string> keywords)
        {
            var targets = Target.GetTargets(state, context);
            int sourceId = context.SourceEntity?.EntityId ?? 0;

            foreach (var target in targets)
            {
                if (target == null) continue;

                // 필터 검사
                bool pass = true;
                foreach (var f in Filters)
                {
                    if (!f.Check(state, new EffectContext(context.OwnerUid, context.SourceCard, context.Trigger)
                    {
                        SourceEntity = context.SourceEntity,
                        TargetEntity = target
                    }))
                    {
                        pass = false;
                        break;
                    }
                }
                if (!pass) continue;

                // 🌟 [멤버 보호]: 멤버는 일반 하수인/캐릭터 스탯 버프를 받지 않으며, 오직 TargetCategory.Member 전용 효과일 때만 영향받음
                if (target.IsMember && Target is GameServer.Effects.Targeting.TargetSelector ts && ts.Category != GameServer.Effects.Targeting.TargetCategory.Member)
                {
                    continue;
                }

                int finalAtkBuff = AttackBuff;
                int finalHpBuff = HealthBuff;

                // 주문 시전 시 아군 능력강화(BuffAmp) 증폭 적용
                if (context.SourceCard != null && context.SourceCard.Type == CardType.주문)
                {
                    var (atkAmp, hpAmp) = state.GetBuffAmp(context.OwnerUid);
                    if (AttackBuff > 0) finalAtkBuff += atkAmp;
                    if (HealthBuff > 0) finalHpBuff += hpAmp;

                    if (atkAmp > 0 || hpAmp > 0)
                    {
                        state.LogDebug("BuffAmp", $"💪 [능력강화 계산] 기본 버프({AttackBuff}/{HealthBuff}) + 강화({atkAmp}/{hpAmp}) -> 최종 버프({finalAtkBuff}/{finalHpBuff})");
                    }
                }

                // 스탯 및 특수 오라(주문공격력/약화) 버프 적용
                if (finalAtkBuff != 0 || finalHpBuff != 0 || SpellAmp != 0 || SpellWeakness != 0 || BuffAmpAttack != 0 || BuffAmpHealth != 0)
                {
                    state.ApplyBuff(target, finalAtkBuff, finalHpBuff, sourceId, context.Trigger, context.SourceCard?.CardId, SpellAmp, SpellWeakness, BuffAmpAttack, BuffAmpHealth, Duration);
                }

                // 키워드 부여 적용
                if (keywords.Count > 0)
                {
                    state.GrantKeyword(target, keywords, sourceId, context.Trigger, Duration);
                }

                state.RaiseEffectLog(target.SourceCard.CardId, context.SourceCard?.CardId, "BuffAction");
            }

            return Task.CompletedTask;
        }

        private async Task ExecuteHandBuffAsync(GameState state, EffectContext context, List<string> keywords)
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

                // [ON_DRAW 집중 버프] 드로우 연계 액션으로 호출된 경우, 방금 뽑은 바로 그 카드(TriggerCard) 1장에 버프 적용
                if (context.Trigger == EffectTriggerType.ON_DRAW && context.TriggerCard != null && p.Hand.Contains(context.TriggerCard))
                {
                    var mockEntity = new GameEntity(0, context.TriggerCard, p.Uid);
                    var tempContext = new EffectContext(p.Uid, context.SourceCard, EffectTriggerType.NONE)
                    {
                        TargetEntity = mockEntity,
                        SourceEntity = context.SourceEntity
                    };

                    bool isValid = true;
                    foreach (var filter in Filters)
                    {
                        if (!filter.Check(state, tempContext)) { isValid = false; break; }
                    }

                    if (isValid)
                    {
                        await state.ApplyHandBuffAsync(p.Uid, AttackBuff, HealthBuff, CostBuff, new List<GameCard> { context.TriggerCard }, keywords, SpellAmp, SpellWeakness, BuffAmpAttack, BuffAmpHealth);
                    }
                    continue;
                }

                List<GameCard> cardsToBuff = new List<GameCard>();

                // [Scope == Self] 시전자 카드 본체만 대상으로 하는 경우
                if (Target is TargetSelector selfSelector && selfSelector.Scope == TargetScope.Self)
                {
                    if (context.SourceCard != null && p.Hand.Contains(context.SourceCard))
                    {
                        cardsToBuff.Add(context.SourceCard);
                    }
                }
                else
                {
                    foreach (var card in p.Hand)
                    {
                        if (card.Type != CardType.하수인 && CostBuff == 0) continue;

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
                                isValid = false;
                                break;
                            }
                        }

                        if (isValid)
                        {
                            cardsToBuff.Add(card);
                        }
                    }
                }

                if (cardsToBuff.Count > 0)
                {
                    await state.ApplyHandBuffAsync(p.Uid, AttackBuff, HealthBuff, CostBuff, cardsToBuff, keywords, SpellAmp, SpellWeakness, BuffAmpAttack, BuffAmpHealth);
                }
            }
        }

        private async Task ExecuteDeckBuffAsync(GameState state, EffectContext context, List<string> keywords)
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

                List<GameCard> cardsToBuff = new List<GameCard>();

                foreach (var card in p.Deck)
                {
                    if (card.Type != CardType.하수인) continue;

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
                            isValid = false;
                            break;
                        }
                    }

                    if (isValid)
                    {
                        cardsToBuff.Add(card);
                    }
                }

                if (cardsToBuff.Count > 0)
                {
                    await state.ApplyDeckBuffAsync(p.Uid, AttackBuff, HealthBuff, CostBuff, cardsToBuff, keywords, SpellAmp, SpellWeakness, BuffAmpAttack, BuffAmpHealth);
                }
            }
        }
    }
}
