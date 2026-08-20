using System.Collections.Generic;
using System.Linq;
using GameServer.Effects;

namespace GameServer
{
    /// <summary>
    /// 클라이언트(유니티)의 마우스 타겟팅 하이라이트를 위해,
    /// 특정 카드가 지정할 수 있는 올바른 대상들의 EntityId를 미리 계산해주는 도우미 클래스입니다.
    /// </summary>
    public static class TargetValidator
    {
        public static List<int>? GetValidTargetIds(GameState state, GameCard card, string playerUid)
        {
            // 1. 타겟팅이 필요 없는 카드(TargetRule == false)는 계산할 필요가 없습니다! (빠른 종료)
            if (!card.TargetRule) return null;

            List<int> validIds = new List<int>();
            PlayerState me = state.GetPlayerState(playerUid);
            PlayerState opp = state.GetPlayerState(playerUid, true);

            // 2. 살아있는 양쪽 필드의 모든 개체(하수인 + 멤버 + 영웅)를 긁어모읍니다.
            var allAlive = new List<GameEntity>();
            allAlive.Add(me.Leader);
            allAlive.Add(opp.Leader);
            allAlive.AddRange(me.Field.Where(e => e != null && e.Health > 0)!);
            allAlive.AddRange(opp.Field.Where(e => e != null && e.Health > 0)!);
            allAlive.AddRange(me.MemberZone.Where(e => e != null && e.Health > 0)!);
            allAlive.AddRange(opp.MemberZone.Where(e => e != null && e.Health > 0)!);

            // 3. 수집된 모든 개체를 하나씩 확인하며 대상이 될 수 있는지 필터링합니다.
            foreach (var entity in allAlive)
            {
                // 기본적으로 '유효하다'고 가정하고, 조건에 맞지 않으면 false로 깎아나가는 방식이 유리합니다.
                bool isValid = true; 

                var playEffects = card.NewEffects.Where(e => e.Trigger == EffectTriggerType.ON_PLAY);

                foreach (var effect in playEffects)
                {
                    // 1. 임시 EffectContext 만들기
                    // - 이 카드를 낸 사람과 원본 카드를 담고, 
                    // - 현재 반복문에서 검사 중인 'entity'를 타겟으로 임시 지정합니다.
                    var tempContext = new GameServer.Effects.EffectContext(playerUid, card, EffectTriggerType.ON_PLAY)
                    {
                        TargetEntity = entity // "얘를 타겟으로 지정한다면?" 하고 가정해보는 것입니다.
                    };

                    // 2. 카드에 달린 모든 조건(Condition) 검사
                    foreach (ICondition filter in effect.Conditions)
                    {
                        // 사용자님이 만드신 Check 함수 호출!
                        if (!filter.Check(state, tempContext)) 
                        {
                            isValid = false; // 조건 중 하나라도 틀리면 타겟 불가 처리
                            break; 
                        }
                    }
                }

                // 4. 모든 조건(Filter)을 무사히 통과했다면 유효한 타겟 리스트에 넣습니다.
                if (isValid)
                {
                    validIds.Add(entity.EntityId);
                }
            }

            // 최종 계산된 타겟 목록 반환
            return validIds;
        }
    }
}