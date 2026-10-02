using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects
{
    /// <summary>
    /// 차세대 5W1H 표준 행동 패킷(GameActionPacket) 기반 사건 분배기(Event Dispatcher)입니다.
    /// 하드코딩된 예외 분기 없이, 모든 카드의 반응 효과가 자신의 TriggerConditions에 의해 순수 데이터 주도형으로 판정됩니다.
    /// </summary>
    public class EventSystem
    {
        private readonly GameState _gameState;

        // (사건 종류, 발생 타이밍)을 키로 구독자를 관리합니다.
        private readonly Dictionary<(GameActionKind action, ActionTiming timing), List<(CardEffect effect, GameCard sourceCard)>> _subscribers = new();

        public EventSystem(GameState gameState)
        {
            _gameState = gameState;
        }

        /// <summary>
        /// 반응형(Reactive) 카드 효과를 구독 등록합니다.
        /// </summary>
        public void Subscribe(CardEffect effect, GameCard sourceCard) 
        {
            var key = (effect.GetTriggerAction(), effect.Timing);
            if (!_subscribers.ContainsKey(key))
                _subscribers[key] = new List<(CardEffect, GameCard)>();

            if (!_subscribers[key].Any(x => x.effect == effect && x.sourceCard == sourceCard))
                _subscribers[key].Add((effect, sourceCard));
        }

        /// <summary>
        /// 반응형(Reactive) 카드 효과 구독을 해지합니다.
        /// </summary>
        public void Unsubscribe(CardEffect effect)
        {
            var key = (effect.GetTriggerAction(), effect.Timing);
            if (_subscribers.ContainsKey(key))
            {
                _subscribers[key].RemoveAll(x => x.effect == effect);
            }
        }

        /// <summary>
        /// 표준 사건 패킷(GameActionPacket)을 전장의 모든 구독자에게 브로드캐스팅합니다.
        /// </summary>
        public async Task PublishAsync(GameActionPacket packet)
        {
            // 모든 사건은 즉시 게임 이력(ActionHistory)에 기록되어 HistoryCondition 등에서 조회 가능하게 합니다.
            _gameState.RecordAction(packet);

            var key = (packet.Action, packet.Timing);
            if (!_subscribers.TryGetValue(key, out var subscriberList) || subscriberList.Count == 0)
                return;

            var effectsToRun = subscriberList.ToList();

            foreach (var tuple in effectsToRun)
            {
                var effect = tuple.effect;
                var ownerCard = tuple.sourceCard; // 이 효과를 소유한 원본 카드

                var ownerEntity = _gameState.FindEntityByCard(ownerCard);
                // 사망(Death) 액션 처리 시 슬롯에서 이미 제거된 사망 하수인/멤버인 경우, 패킷의 TargetEntity로부터 복원
                if (ownerEntity == null && packet.Action == GameActionKind.Death && packet.TargetEntity?.SourceCard?.InstanceId == ownerCard.InstanceId)
                {
                    ownerEntity = packet.TargetEntity;
                }

                // [침묵 검사] 침묵(Silence) 상태인 하수인의 모든 패시브/트리거 효과는 발동 차단
                if (ownerEntity != null && ownerEntity.Keywords != null && ownerEntity.Keywords.Contains(CardKeywords.Silence))
                {
                    _gameState.LogDebug("EventSystem", $"🔇 [침묵 무효화] 하수인 '{ownerEntity.SourceCard?.CardName}'(ID:{ownerEntity.EntityId})은 침묵 상태이므로 효과({packet.Action}) 발동이 취소되었습니다.");
                    continue;
                }

                // [하드코딩 제거 - 순수 조건 기반 판정]
                // 본인 카드 시전 제외, 본인 사망 여부(죽메 vs 복수) 등 모든 판정은
                // 카드가 가진 TriggerConditions(RelationCondition, PropertyCondition 등)가 완벽하게 수행합니다.
                bool allConditionsMet = true;
                if (effect.TriggerConditions != null && effect.TriggerConditions.Count > 0)
                {
                    foreach (var condition in effect.TriggerConditions)
                    {
                        if (!condition.Check(_gameState, packet, ownerCard, ownerEntity))
                        {
                            allConditionsMet = false;
                            break;
                        }
                    }
                }

                if (!allConditionsMet)
                    continue;

                // 실행 컨텍스트 생성 (스마트 컨텍스트)
                var actionContext = packet.ToEffectContext(ownerCard, ownerEntity);
                if (ownerEntity != null)
                {
                    actionContext.SourceEntity = ownerEntity;
                }

                // 반복 횟수 실행 (브란/리븐데어 등 증폭 호환 기반)
                int repeatCount = Math.Max(1, effect.RepeatCount);

                for (int i = 0; i < repeatCount; i++)
                {
                    if (_gameState.CurrentPhase == "AWAITING_CHOICE")
                    {
                        break;
                    }

                    // 다단 히트/반복 메타데이터 전달
                    packet.SequenceIndex = i;
                    packet.TotalSequenceCount = repeatCount;

                    foreach (var action in effect.Actions)
                    {
                        await action.ExecuteAsync(_gameState, actionContext);
                    }
                }
            }
        }

        /// <summary>
        /// 구형 EffectTriggerType 기반 호출을 지원하기 위한 표준 어댑터 브릿지 메서드입니다.
        /// </summary>
        public async Task PublishAsync(EffectTriggerType trigger, EffectContext eventContext)
        {
            int eventAmount = eventContext.EventData is int val ? val : (int.TryParse(eventContext.EventData?.ToString(), out int parsed) ? parsed : 0);

            GameActionPacket packet = trigger switch
            {
                EffectTriggerType.ON_PLAY => GameActionPacket.CreatePlayCard(
                    eventContext.OwnerUid, 
                    eventContext.SourceCard ?? eventContext.TriggerCard!, 
                    eventContext.TargetEntity, 
                    eventContext.TargetPosition),

                EffectTriggerType.ON_SUMMON => GameActionPacket.CreateSummon(
                    eventContext.OwnerUid, 
                    eventContext.SourceCard ?? eventContext.TriggerCard!, 
                    eventContext.TargetEntity ?? eventContext.SourceEntity!, 
                    eventContext.TargetPosition),

                EffectTriggerType.ON_DAMAGE => GameActionPacket.CreateDamage(
                    eventContext.SourceEntity, 
                    eventContext.SourceCard ?? eventContext.TriggerCard, 
                    eventContext.OwnerUid, 
                    eventContext.TargetEntity ?? eventContext.SourceEntity!, 
                    eventAmount),

                EffectTriggerType.ON_HEAL => GameActionPacket.CreateHeal(
                    eventContext.SourceEntity, 
                    eventContext.SourceCard ?? eventContext.TriggerCard, 
                    eventContext.OwnerUid, 
                    eventContext.TargetEntity ?? eventContext.SourceEntity!, 
                    eventAmount),

                EffectTriggerType.ON_DEATH => GameActionPacket.CreateDeath(
                    (eventContext.TargetEntity ?? eventContext.SourceEntity)!, 
                    eventContext.SourceEntity, 
                    eventContext.SourceCard ?? eventContext.TriggerCard),

                EffectTriggerType.ON_DRAW => GameActionPacket.CreateDraw(
                    eventContext.OwnerUid, 
                    eventContext.SourceCard ?? eventContext.TriggerCard!),

                EffectTriggerType.ON_TURN_START => new GameActionPacket
                {
                    Action = GameActionKind.TurnStart,
                    Timing = ActionTiming.After,
                    SourceUid = eventContext.OwnerUid,
                    TurnPlayerUid = eventContext.OwnerUid
                },

                EffectTriggerType.ON_TURN_END => new GameActionPacket
                {
                    Action = GameActionKind.TurnEnd,
                    Timing = ActionTiming.After,
                    SourceUid = eventContext.OwnerUid,
                    TurnPlayerUid = eventContext.OwnerUid
                },

                _ => new GameActionPacket
                {
                    Action = GameActionKind.None,
                    Timing = ActionTiming.After,
                    SourceUid = eventContext.OwnerUid,
                    SourceCard = eventContext.SourceCard,
                    SourceEntity = eventContext.SourceEntity,
                    TargetEntity = eventContext.TargetEntity,
                    Amount = eventAmount
                }
            };

            await PublishAsync(packet);
        }
    }
}