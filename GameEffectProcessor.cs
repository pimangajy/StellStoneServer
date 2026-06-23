using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer
{
    /// <summary>
    /// 게임의 구체적인 '효과(Effect)'와 '로직'을 처리하는 전담 클래스입니다.
    /// GameState가 '상태'를 관리한다면, 이 클래스는 '행동'을 실행합니다.
    /// </summary>
    public class GameEffectProcessor
    {
        private readonly GameState _gameState;

        public GameEffectProcessor(GameState gameState)
        {
            _gameState = gameState;
        }

        // ==================================================================
        // 1. 트리거 및 효과 실행 진입점
        // ==================================================================

        public async Task ExecuteEffectsAsync(GameCard sourceCard, GameEntity? sourceEntity, GameEntity? mainTarget, EffectTriggerType triggerType, string ownerUid)
        {
            if (sourceCard.Effects == null) return;

            foreach (var effect in sourceCard.Effects)
            {
                // Trigger가 null이거나 다르면 패스
                if (effect.Trigger != triggerType) continue;

                // 효과 실행 (비동기)
                await ProcessSingleEffectAsync(effect, sourceEntity, mainTarget, ownerUid);
            }
        }

        private async Task ProcessSingleEffectAsync(ServerEffectData effect, GameEntity? sourceEntity, GameEntity? mainTarget, string ownerUid)
        {
            // 1. 타겟 찾기
            List<GameEntity> targets = ResolveTargets(effect.Target, mainTarget, ownerUid, sourceEntity);

            // 2. 조건 확인
            if (!CheckCondition(effect, targets, ownerUid))
            {
                // 조건 불만족 시 대체 효과 실행 (ElseEffect)
                if (effect.ElseEffect != null)
                {
                    await ProcessSingleEffectAsync(effect.ElseEffect, sourceEntity, mainTarget, ownerUid);
                }
                return;
            }

            // 3. 반복 실행 (Count)
            for (int i = 0; i < effect.Count; i++)
            {
                // (참고) 만약 "랜덤 적에게 3회 피해" 같은 효과라면
                // 반복문 안에서 ResolveTargets를 호출해야 매번 다른 적이 맞을 수 있습니다.
                // 현재 구조는 "타겟을 정해두고 N번 때리기"입니다.
                
                foreach (var target in targets)
                {
                    // (중요) 이미 죽은 대상은 효과를 받지 않음 (부활 등 예외 제외)
                    if (target.Health <= 0 && effect.EffectName != GameEventType.DEATH) continue;

                    ApplyEffectLogic(effect, target, ownerUid);
                }
            }
        }

        // ==================================================================
        // 2. 개별 효과 로직 (구현부)
        // ==================================================================

        private void ApplyEffectLogic(ServerEffectData effect, GameEntity target, string ownerUid)
        {
            switch (effect.EffectName)
            {
                case GameEventType.DAMAGE:
                    _gameState.ApplyDamage(target, effect.Value1);
                    break;

                case GameEventType.HEAL:
                    _gameState.ApplyHeal(target, effect.Value1);
                    break;

                case GameEventType.BUFF:
                    _gameState.ApplyBuff(target, effect.Value1, effect.Value2);
                    break;

                case GameEventType.DRAW:
                    // 타겟의 주인(OwnerUid)이 카드를 뽑음
                    PlayerState p = _gameState.GetPlayerState(target.OwnerUid);
                    p.DrawCard(); 
                    // TODO: GameState 업데이트 메시지는 BroadcastUpdatesAsync에서 처리됨
                    break;
                
                case GameEventType.DEATH:
                    _gameState.ApplyDamage(target, 9999); // 즉사 처리
                    break;

                // 추가: 소환(SUMMON), 침묵(SILENCE), 빙결(FREEZE) 등
            }
        }

        // ==================================================================
        // 3. 타겟팅 및 조건 로직
        // ==================================================================

        private List<GameEntity> ResolveTargets(TargetRule? targetRule, GameEntity? manualTarget, string ownerUid, GameEntity? sourceEntity)
        {
            List<GameEntity> results = new List<GameEntity>();
            PlayerState me = _gameState.GetPlayerState(ownerUid);
            PlayerState opp = _gameState.GetPlayerState(ownerUid, true);

            switch (targetRule)
            {
                case TargetRule.All_Characters:
                    if (manualTarget != null) results.Add(manualTarget);
                    break;
                case TargetRule.Self:
                    if (sourceEntity != null) results.Add(sourceEntity);
                    break;
                case TargetRule.Target_Enemy_Leader:
                    results.Add(opp.Leader);
                    break;
                case TargetRule.Target_Friend_Leader:
                    results.Add(me.Leader);
                    break;
                case TargetRule.Target_Enemy_All:
                    results.Add(opp.Leader);
                    results.AddRange(opp.Field.Where(e => e != null)!);
                    results.AddRange(opp.MemberZone.Where(e => e != null)!);
                    results.AddRange(opp.MemberZone!);
                    break;
                case TargetRule.Target_Friend_All:
                    results.AddRange(me.Field!);
                    results.AddRange(me.MemberZone!);
                    break;
                case TargetRule.Random:
                    List<GameEntity> enemies = new List<GameEntity>();
                    enemies.Add(opp.Leader);
                    enemies.AddRange(opp.Field.Where(e => e != null)!);
                    enemies.AddRange(opp.MemberZone.Where(e => e != null)!);
                    enemies.AddRange(opp.MemberZone!);
                    
                    // 살아있는 적만 타겟팅
                    enemies = enemies.Where(e => e.Health > 0).ToList();

                    if (enemies.Count > 0)
                    {
                        int idx = _gameState.Rng.Next(enemies.Count);
                        results.Add(enemies[idx]);
                    }
                    break;
            }
            return results;
        }

        private bool CheckCondition(ServerEffectData effect, List<GameEntity> targets, string ownerUid)
        {
            if (effect.Condition == CardCondition.NONE) return true;

            // 1. 공통 처리: 정수형을 사용하는 조건이 많으므로, 미리 한 번만 int로 변환해 둡니다.
            // 만약 ConditionValue가 "강도단" 같은 문자열이라면 변환에 실패하여 자연스럽게 0이 됩니다. (에러 안 남)
            int numericValue = 0;
            int.TryParse(effect.ConditionValue, out numericValue);

            switch (effect.Condition)
            {
                // ==========================================
                // [정수(int) 비교 조건들] - 미리 파싱해둔 numericValue를 그대로 사용! (1줄 컷)
                // ==========================================
                case CardCondition.HEALTH_LESS: return targets.Any(t => t.Health < numericValue);
                case CardCondition.HEALTH_MORE: return targets.Any(t => t.Health > numericValue);
                case CardCondition.ATTACK_LESS: return targets.Any(t => t.Attack < numericValue);
                case CardCondition.ATTACK_MORE: return targets.Any(t => t.Attack > numericValue);
                case CardCondition.COST_LESS:   return targets.Any(t => t.SourceCard.CurrentCost < numericValue);
                case CardCondition.COST_MORE:   return targets.Any(t => t.SourceCard.CurrentCost > numericValue);

                // ==========================================
                // [Enum 비교 조건들] - 얘네들은 각자 타입이 다르므로 케이스 안에서 개별 파싱
                // ==========================================
                case CardCondition.TRIBE:
                    if (Enum.TryParse<CardTribe>(effect.ConditionValue, true, out var requiredTribe))
                        return targets.Any(t => t.Tribe == requiredTribe || t.SourceCard.Tribe == requiredTribe);
                    else 
                    {
                    Console.WriteLine($"[EffectProcessor] ⚠️ 알 수 없는 종족 조건값 파싱 실패: {effect.ConditionValue}");
                    return false;
                    }

                case CardCondition.HAS_KEYWORD:
                    if (Enum.TryParse<CardKeywords>(effect.ConditionValue, true, out var requiredKeyword))
                        return targets.Any(t => t.Keywords != null && t.Keywords.Contains(requiredKeyword));
                    else 
                    {
                    Console.WriteLine($"[EffectProcessor] ⚠️ 알 수 없는 조건값 파싱 실패: {effect.ConditionValue}");
                    return false;
                    }
                    
                case CardCondition.CARD_TYPE:
                    if (Enum.TryParse<CardType>(effect.ConditionValue, true, out var requiredType))
                        return targets.Any(t => t.SourceCard.Type == requiredType);
                    else 
                    {
                    Console.WriteLine($"[EffectProcessor] ⚠️ 알 수 없는 카드 타입 조건값 파싱 실패: {effect.ConditionValue}");
                    return false;
                    }
            }
            
            return true;
        }
    }
}