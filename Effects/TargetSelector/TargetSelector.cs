using System;
using System.Collections.Generic;
using System.Linq;

namespace GameServer.Effects.Targeting
{
    public enum TargetScope
    {
        Context,   // 플레이어가 클릭한 지정 대상 (기본값)
        Self,      // 효과를 발동한 시전자 본체
        All,       // 조건에 맞는 모든 대상 (광역)
        Random     // 조건에 맞는 대상 중 무작위 N개 추첨
    }

    public enum TargetAlliance
    {
        Any,       // 양쪽 전체 (기본값)
        Friendly,  // 아군
        Enemy      // 적군
    }

    public enum TargetCategory
    {
        Character, // 영웅 + 하수인 + 멤버 전체 (광역 피해 등)
        Minion,    // 일반 하수인만 (영웅 및 멤버 제외)
        Leader,    // 영웅(명치)만
        Member     // 멤버 카드 전용
    }

    /// <summary>
    /// 단일 지정, 시전자 본인, 광역, 무작위 등 모든 대상을 선택하는 만능 타겟 셀렉터입니다.
    /// </summary>
    public class TargetSelector : GameServer.Effects.ITargetSelector
    {
        public TargetScope Scope { get; set; } = TargetScope.Context;
        public TargetAlliance Alliance { get; set; } = TargetAlliance.Any;
        public TargetCategory Category { get; set; } = TargetCategory.Character;
        public bool ExcludeSelf { get; set; } = false;
        public int Count { get; set; } = 1;

        public IEnumerable<GameEntity> GetTargets(GameState state, EffectContext context)
        {
            // =================================================================
            // 1. [Self] 시전자 자신
            // =================================================================
            if (Scope == TargetScope.Self)
            {
                if (context.SourceEntity != null && context.SourceEntity.Health > 0)
                {
                    yield return context.SourceEntity;
                }
                yield break;
            }

            // =================================================================
            // 2. [Context] 플레이어가 클릭한 지정 대상 (또는 Category = Leader 스마트 보정)
            // =================================================================
            if (Scope == TargetScope.Context)
            {
                var target = context.TargetEntity;
                if (target != null && target.Health > 0)
                {
                    // 피아 검사
                    if (Alliance == TargetAlliance.Friendly && target.OwnerUid != context.OwnerUid) yield break;
                    if (Alliance == TargetAlliance.Enemy && target.OwnerUid == context.OwnerUid) yield break;

                    // 종류 검사
                    if (Category == TargetCategory.Leader && !target.IsLeader) yield break;
                    if (Category == TargetCategory.Minion && (target.IsLeader || target.IsMember)) yield break;
                    if (Category == TargetCategory.Member && !target.IsMember) yield break;

                    // 은신 상태인 적 개체는 지정 불가
                    if (target.OwnerUid != context.OwnerUid && target.Keywords != null && target.Keywords.Contains(CardKeywords.Stealth))
                    {
                        yield break;
                    }

                    // 주문 면역(Elusive) 개체는 주문 카드의 단일 지정 대상이 될 수 없음
                    if (context.SourceCard != null && context.SourceCard.Type == CardType.주문 && target.Keywords != null && target.Keywords.Contains(CardKeywords.Elusive))
                    {
                        yield break;
                    }

                    yield return target;
                    yield break;
                }

                // [스마트 보정] 플레이어가 대상을 마우스로 찍지 않는 비타겟팅 카드인데 Category가 Leader 또는 Member인 경우:
                // Alliance에 맞춰 내 영웅/멤버(Friendly) 또는 상대 영웅/멤버(Enemy)를 자동으로 반환!
                if (Category == TargetCategory.Leader)
                {
                    var myState = state.GetPlayerState(context.OwnerUid);
                    var oppState = state.GetPlayerState(context.OwnerUid, true);

                    if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Friendly)
                    {
                        if (myState?.Leader != null && myState.Leader.Health > 0)
                        {
                            yield return myState.Leader;
                        }
                    }

                    if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Enemy)
                    {
                        if (oppState?.Leader != null && oppState.Leader.Health > 0)
                        {
                            yield return oppState.Leader;
                        }
                    }
                }
                else if (Category == TargetCategory.Member)
                {
                    var myState = state.GetPlayerState(context.OwnerUid);
                    var oppState = state.GetPlayerState(context.OwnerUid, true);

                    if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Friendly)
                    {
                        var myMember = myState?.MemberZone.FirstOrDefault(e => e != null && e.Health > 0);
                        if (myMember != null) yield return myMember;
                    }

                    if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Enemy)
                    {
                        var oppMember = oppState?.MemberZone.FirstOrDefault(e => e != null && e.Health > 0);
                        if (oppMember != null) yield return oppMember;
                    }
                }

                yield break;
            }

            // =================================================================
            // 3. [All / Random] 전체 후보군 풀(Pool) 생성
            // =================================================================
            var pool = new List<GameEntity>();
            var me = state.GetPlayerState(context.OwnerUid);
            var opp = state.GetPlayerState(context.OwnerUid, true);

            // 아군 추가
            if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Friendly)
            {
                if (Category == TargetCategory.Character || Category == TargetCategory.Leader)
                {
                    if (me?.Leader != null && me.Leader.Health > 0) pool.Add(me.Leader);
                }
                if (Category == TargetCategory.Character || Category == TargetCategory.Minion)
                {
                    if (me != null)
                    {
                        pool.AddRange(me.Field.Where(e => e != null && e.Health > 0)!);
                    }
                }
                if (Category == TargetCategory.Character || Category == TargetCategory.Member)
                {
                    if (me != null)
                    {
                        pool.AddRange(me.MemberZone.Where(e => e != null && e.Health > 0)!);
                    }
                }
            }

            // 적군 추가
            if (Alliance == TargetAlliance.Any || Alliance == TargetAlliance.Enemy)
            {
                if (Category == TargetCategory.Character || Category == TargetCategory.Leader)
                {
                    if (opp?.Leader != null && opp.Leader.Health > 0) pool.Add(opp.Leader);
                }
                if (Category == TargetCategory.Character || Category == TargetCategory.Minion)
                {
                    if (opp != null)
                    {
                        pool.AddRange(opp.Field.Where(e => e != null && e.Health > 0)!);
                    }
                }
                if (Category == TargetCategory.Character || Category == TargetCategory.Member)
                {
                    if (opp != null)
                    {
                        pool.AddRange(opp.MemberZone.Where(e => e != null && e.Health > 0)!);
                    }
                }
            }

            // 시전자 자신 제외
            if (ExcludeSelf && context.SourceEntity != null)
            {
                pool.RemoveAll(e => e.EntityId == context.SourceEntity.EntityId);
            }

            // 광역 반환
            if (Scope == TargetScope.All)
            {
                foreach (var e in pool)
                {
                    yield return e;
                }
                yield break;
            }

            // 무작위 N개 추첨 반환
            if (Scope == TargetScope.Random)
            {
                int takeCount = Math.Min(Count, pool.Count);
                for (int i = 0; i < takeCount; i++)
                {
                    int index = state.Rng.Next(pool.Count);
                    yield return pool[index];
                    pool.RemoveAt(index);
                }
            }
        }
    }
}
