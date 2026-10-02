using System.Collections.Generic;
using GameServer.Effects;

namespace GameServer
{
    /// <summary>
    /// 클라이언트(유니티)의 마우스 타겟팅 하이라이트를 위해,
    /// 특정 카드가 지정할 수 있는 올바른 대상들의 EntityId를 미리 계산해주는 도우미 클래스입니다.
    /// [P-02 성능 최적화] LINQ 제거, 임시 컬렉션 할당 제거, EffectContext 단일 인스턴스 재활용
    /// </summary>
    public static class TargetValidator
    {
        public static List<int>? GetValidTargetIds(GameState state, GameCard card, string playerUid)
        {
            // 1. 타겟팅이 필요 없는 카드(TargetRule == false)는 계산 생략 (빠른 종료)
            if (!card.TargetRule)
            {
                return null;
            }

            var newEffects = card.NewEffects;
            if (newEffects == null || newEffects.Count == 0)
            {
                return null;
            }

            PlayerState me = state.GetPlayerState(playerUid);
            PlayerState opp = state.GetPlayerState(playerUid, true);

            List<int> validIds = new List<int>();

            // 단일 EffectContext를 생성하여 대상만 교체하며 재사용 (GC 최소화)
            var context = new EffectContext(playerUid, card, EffectTriggerType.ON_PLAY);

            void EvaluateCandidate(GameEntity? entity)
            {
                if (entity == null || entity.Health <= 0 || entity.IsDestroyed) return;

                // 상대방 개체가 은신(Stealth) 상태라면 단일 타겟 지정 불가 (아군 은신은 버프 등을 위해 지정 가능)
                if (entity.OwnerUid != playerUid && entity.Keywords != null && entity.Keywords.Contains(CardKeywords.Stealth))
                {
                    return;
                }

                // 주문(Spell) 시전 시 주문 면역(Elusive) 개체는 타겟팅 불가
                if (card.Type == CardType.주문 && entity.Keywords != null && entity.Keywords.Contains(CardKeywords.Elusive))
                {
                    return;
                }

                context.TargetEntity = entity;

                for (int i = 0; i < newEffects.Count; i++)
                {
                    var effect = newEffects[i];
                    if (effect.Kind != EffectKind.Play) continue;

                    var conds = effect.TargetConditions;
                    if (conds != null && conds.Count > 0)
                    {
                        for (int j = 0; j < conds.Count; j++)
                        {
                            if (!conds[j].Check(state, context))
                            {
                                return; // 조건 불만족 시 제외
                            }
                        }
                    }
                }

                // 멤버 개체인데, 카드의 시전 액션이 하수인(Minion) 전용 대상을 요구하는 경우 조준 대상에서 제외
                if (entity.IsMember)
                {
                    for (int i = 0; i < newEffects.Count; i++)
                    {
                        var effect = newEffects[i];
                        if (effect.Kind != EffectKind.Play || effect.Actions == null) continue;

                        for (int k = 0; k < effect.Actions.Count; k++)
                        {
                            var act = effect.Actions[k];
                            if ((act is GameServer.Effects.Actions.BuffAction ba && ba.Target is GameServer.Effects.Targeting.TargetSelector bts && bts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.DestroyAction da && da.Target is GameServer.Effects.Targeting.TargetSelector dts && dts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.ReturnEntityAction ra && ra.Target is GameServer.Effects.Targeting.TargetSelector rts && rts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.CrowdControlAction ca && ca.Target is GameServer.Effects.Targeting.TargetSelector cts && cts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.DamageAction dma && dma.Target is GameServer.Effects.Targeting.TargetSelector dmts && dmts.Category == GameServer.Effects.Targeting.TargetCategory.Minion))
                            {
                                return;
                            }
                        }
                    }
                }

                // 모든 필터 통과
                validIds.Add(entity.EntityId);
            }

            // 1. 양측 리더 검사
            EvaluateCandidate(me.Leader);
            EvaluateCandidate(opp.Leader);

            // 2. 아군/적군 필드 하수인 검사 (LINQ 없이 직접 배열 인덱스 순회)
            if (me.Field != null)
            {
                for (int i = 0; i < me.Field.Length; i++)
                    EvaluateCandidate(me.Field[i]);
            }
            if (opp.Field != null)
            {
                for (int i = 0; i < opp.Field.Length; i++)
                    EvaluateCandidate(opp.Field[i]);
            }

            // 3. 아군/적군 멤버존 검사
            if (me.MemberZone != null)
            {
                for (int i = 0; i < me.MemberZone.Length; i++)
                    EvaluateCandidate(me.MemberZone[i]);
            }
            if (opp.MemberZone != null)
            {
                for (int i = 0; i < opp.MemberZone.Length; i++)
                    EvaluateCandidate(opp.MemberZone[i]);
            }

            return validIds;
        }

        /// <summary>
        /// 멤버 카드의 특정 액티브 스킬이 지정할 수 있는 올바른 대상들의 EntityId 목록을 계산합니다.
        /// </summary>
        public static List<int> GetValidMemberSkillTargetIds(GameState state, GameEntity memberEntity, MemberSkill skill, string playerUid)
        {
            List<int> validIds = new List<int>();

            // 타겟팅이 필요 없는 스킬이면 빈 리스트 반환 (하수인과 동일한 bool 체계)
            if (!skill.Targeting)
            {
                return validIds;
            }

            PlayerState me = state.GetPlayerState(playerUid);
            PlayerState opp = state.GetPlayerState(playerUid, true);

            var context = new EffectContext(playerUid, memberEntity.SourceCard, EffectTriggerType.ON_PLAY)
            {
                SourceEntity = memberEntity
            };

            void EvaluateCandidate(GameEntity? entity)
            {
                if (entity == null || entity.Health <= 0 || entity.IsDestroyed) return;

                // 상대방 개체가 은신(Stealth) 상태라면 타겟팅 불가
                if (entity.OwnerUid != playerUid && entity.Keywords != null && entity.Keywords.Contains(CardKeywords.Stealth))
                {
                    return;
                }

                // Effect 레벨 TargetConditions 검사 (하수인 효과와 완벽 동일)
                if (skill.Effects != null && skill.Effects.Count > 0)
                {
                    context.TargetEntity = entity;
                    for (int i = 0; i < skill.Effects.Count; i++)
                    {
                        var effect = skill.Effects[i];
                        var conds = effect.TargetConditions;
                        if (conds != null && conds.Count > 0)
                        {
                            for (int j = 0; j < conds.Count; j++)
                            {
                                if (!conds[j].Check(state, context))
                                {
                                    return;
                                }
                            }
                        }
                    }
                }

                // 멤버 개체인데, 스킬의 시전 액션이 하수인(Minion) 전용 대상을 요구하는 경우 조준 대상에서 제외
                if (entity.IsMember && skill.Effects != null)
                {
                    for (int i = 0; i < skill.Effects.Count; i++)
                    {
                        var effect = skill.Effects[i];
                        if (effect.Kind != EffectKind.Play || effect.Actions == null) continue;

                        for (int k = 0; k < effect.Actions.Count; k++)
                        {
                            var act = effect.Actions[k];
                            if ((act is GameServer.Effects.Actions.BuffAction ba && ba.Target is GameServer.Effects.Targeting.TargetSelector bts && bts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.DestroyAction da && da.Target is GameServer.Effects.Targeting.TargetSelector dts && dts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.ReturnEntityAction ra && ra.Target is GameServer.Effects.Targeting.TargetSelector rts && rts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.CrowdControlAction ca && ca.Target is GameServer.Effects.Targeting.TargetSelector cts && cts.Category == GameServer.Effects.Targeting.TargetCategory.Minion) ||
                                (act is GameServer.Effects.Actions.DamageAction dma && dma.Target is GameServer.Effects.Targeting.TargetSelector dmts && dmts.Category == GameServer.Effects.Targeting.TargetCategory.Minion))
                            {
                                return;
                            }
                        }
                    }
                }

                validIds.Add(entity.EntityId);
            }

            // 양측 리더 검사
            EvaluateCandidate(me.Leader);
            EvaluateCandidate(opp.Leader);

            // 필드 하수인 검사
            if (me.Field != null)
            {
                for (int i = 0; i < me.Field.Length; i++) EvaluateCandidate(me.Field[i]);
            }
            if (opp.Field != null)
            {
                for (int i = 0; i < opp.Field.Length; i++) EvaluateCandidate(opp.Field[i]);
            }

            // 멤버존 검사
            if (me.MemberZone != null)
            {
                for (int i = 0; i < me.MemberZone.Length; i++) EvaluateCandidate(me.MemberZone[i]);
            }
            if (opp.MemberZone != null)
            {
                for (int i = 0; i < opp.MemberZone.Length; i++) EvaluateCandidate(opp.MemberZone[i]);
            }

            return validIds;
        }
    }
}