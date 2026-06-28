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
                    if (target.Health <= 0 && effect.EffectName != GameEventType.DEATH) continue;

                    // 만약 이전 효과 처리 중 대기 상태(AWAITING)로 빠졌다면 남은 효과 실행 중단
                    if (_gameState.CurrentPhase == "AWAITING_CHOICE") return;

                    // await 추가
                    await ApplyEffectLogic(effect, target, ownerUid, sourceEntity);
                }
            }
        }

        // ==================================================================
        // 2. 개별 효과 로직 (구현부)
        // ==================================================================

        private async Task ApplyEffectLogic(ServerEffectData effect, GameEntity target, string ownerUid, GameEntity? sourceEntity)
        {
            switch (effect.EffectName)
            {
                case GameEventType.DAMAGE:
                    // [신규 추가] 이 효과의 타겟팅 룰이 '광역기(AoE)'인지 검사합니다.
                bool isAoE = (effect.Target == TargetRule.All_Characters ||
                            effect.Target == TargetRule.All_Minions ||
                            effect.Target == TargetRule.All_Enemies ||
                            effect.Target == TargetRule.All_Enemy_Minions ||
                            effect.Target == TargetRule.All_Friends ||
                            effect.Target == TargetRule.All_Friendly_Minions);

                // 광역기가 아닐 때(단일 지정이거나 랜덤 타겟일 때)만 개별 투사체용 ATTACK 패킷을 생성합니다.
                if (!isAoE)
                {
                    _gameState.LogEvent(GameEventType.ATTACK, sourceEntity!.EntityId, target.EntityId, 0, null, effect.Trigger);
                }
                
                // 실제 데미지 적용은 광역/단일 상관없이 타겟별로 정상 처리됩니다.
                _gameState.ApplyDamage(target, effect.Value1, sourceEntity!.EntityId, effect.Trigger);
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

                case GameEventType.BIND: // 속박
                _gameState.ApplyBind(target, effect.Value1);
                break;

                case GameEventType.SILENCE: // 침묵
                    _gameState.ApplySilence(target, effect.Value1);
                    break;

                case GameEventType.GRANT_KEYWORD: // 키워드 부여 (ConditionValue에 부여할 키워드 문자열이 있다고 가정)
                    if (!string.IsNullOrEmpty(effect.ConditionValue))
                    {
                        _gameState.GrantKeyword(target, effect.ConditionValue, effect.Value1);
                    }
                    break;

                case GameEventType.MANA_MOD: // 마나 조작 (Value1에 증감 수치)
                    _gameState.ApplyManaMod(target.OwnerUid, effect.Value1);
                    break;

                case GameEventType.FORCE_ATTACK: // 강제 공격
                    // target이 효과에 지정된 개체(effect.Value1 등)를 강제로 공격하게 함
                    // GameState의 ResolveCombatAsync와 ProcessDeathsAsync를 비동기로 호출해야 합니다.
                    // (사전 타겟 검증 로직 추가 필요)
                    // await _gameState.ResolveCombatAsync(target, 방어자개체);
                    // await _gameState.ProcessDeathsAsync();
                    break;

                case GameEventType.SUMMON:
                string targetCardId = effect.ConditionValue ?? "Token_001";
                PlayerState opp = _gameState.GetPlayerState(ownerUid, true); // 상대방 정보

                // 💡 Target_Friend_All이 여기에 포함되어야 클라이언트에게 위치 선택을 요구합니다!
                if (effect.Target == TargetRule.Target_Friend_All || effect.Target == TargetRule.Target_All || effect.Target == TargetRule.Target_Minion)
                {
                    string message = $"아군 필드에 토큰({targetCardId})을 소환할 위치를 선택해 주세요.";
                    await _gameState.RequestPlayerChoiceAsync(ownerUid, "POSITION", targetCardId, message, sourceEntity!.EntityId);
                }
                else if (effect.Target == TargetRule.Target_Enemy_All)
                {
                    string message = $"적 필드에 토큰({targetCardId})을 소환할 위치를 선택해 주세요.";
                    await _gameState.RequestPlayerChoiceAsync(ownerUid, "POSITION_ENEMY", targetCardId, message, sourceEntity!.EntityId);
                }
                else if (effect.Target == TargetRule.All_Enemies)
                {
                    _gameState.SummonEntityByEffect(opp.Uid, targetCardId);
                }
                else
                {
                    _gameState.SummonEntityByEffect(ownerUid, targetCardId);
                }
                break;
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
                // ========================================================
                // 1. 단일 지정 타겟 (플레이어가 지정한 대상을 그대로 사용)
                // ========================================================
                case TargetRule.Target_All:
                case TargetRule.Target_Minion:
                case TargetRule.Target_Enemy_All:
                case TargetRule.Target_Enemy_Minion:
                case TargetRule.Target_Friend_All:
                case TargetRule.Target_Friend_Minion:
                    // 클라이언트에서 넘겨준 타겟이 있으면 결과에 추가
                    if (manualTarget != null) results.Add(manualTarget);
                    break;

                // ========================================================
                // 2. 자동 고정 타겟
                // ========================================================
                case TargetRule.Self:
                    if (sourceEntity != null) results.Add(sourceEntity);
                    break;
                case TargetRule.Target_Enemy_Leader:
                    results.Add(opp.Leader);
                    break;
                case TargetRule.Target_Friend_Leader:
                    results.Add(me.Leader);
                    break;

                // ========================================================
                // 3. 광역 효과 타겟 (지정 없이 조건에 맞는 대상을 모두 긁어옴)
                // ========================================================
                case TargetRule.All_Characters:
                    results.Add(me.Leader);
                    results.AddRange(me.Field.Where(e => e != null)!);
                    results.AddRange(me.MemberZone.Where(e => e != null)!);
                    results.Add(opp.Leader);
                    results.AddRange(opp.Field.Where(e => e != null)!);
                    results.AddRange(opp.MemberZone.Where(e => e != null)!);
                    break;

                case TargetRule.All_Enemies: // 기존 Target_Enemy_All 에서 All_Enemies 로 수정
                    results.Add(opp.Leader);
                    results.AddRange(opp.Field.Where(e => e != null)!);
                    results.AddRange(opp.MemberZone.Where(e => e != null)!);
                    break;

                case TargetRule.All_Friends: // 기존 Target_Friend_All 에서 All_Friends 로 수정
                    results.Add(me.Leader);
                    results.AddRange(me.Field.Where(e => e != null)!);
                    results.AddRange(me.MemberZone.Where(e => e != null)!);
                    break;

                // ========================================================
                // 4. 랜덤 효과 (기존 로직 유지)
                // ========================================================
                case TargetRule.Random:
                    List<GameEntity> enemies = new List<GameEntity>();
                    enemies.Add(opp.Leader);
                    enemies.AddRange(opp.Field.Where(e => e != null)!);
                    enemies.AddRange(opp.MemberZone.Where(e => e != null)!);
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