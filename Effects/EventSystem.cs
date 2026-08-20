using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer.Effects
{
    public class EventSystem
    {
        private readonly GameState _gameState;

        // [수정됨] 효과(CardEffect)와 그 효과를 가진 원본 카드(GameCard)를 함께 튜플로 묶어서 저장합니다!
        private readonly Dictionary<EffectTriggerType, List<(CardEffect effect, GameCard sourceCard)>> _subscribers = new();

        public EventSystem(GameState gameState)
        {
            _gameState = gameState;
        }

        // [수정됨] 매개변수에 GameCard 추가
        public void Subscribe(CardEffect effect, GameCard sourceCard) 
        {
            if (!_subscribers.ContainsKey(effect.Trigger))
                _subscribers[effect.Trigger] = new List<(CardEffect, GameCard)>();

            if (!_subscribers[effect.Trigger].Any(x => x.effect == effect))
                _subscribers[effect.Trigger].Add((effect, sourceCard));
        }

        public void Unsubscribe(CardEffect effect)
        {
            if (_subscribers.ContainsKey(effect.Trigger))
            {
                // 이 효과와 일치하는 등록 정보 삭제
                _subscribers[effect.Trigger].RemoveAll(x => x.effect == effect);
            }
        }

        public async Task PublishAsync(EffectTriggerType trigger, EffectContext eventContext)
        {
            if(trigger == EffectTriggerType.ON_DAMAGE) Console.WriteLine("ON_DAMAGE 발생");

            if (!_subscribers.ContainsKey(trigger)) return;

            var effectsToRun = _subscribers[trigger].ToList();

            foreach (var tuple in effectsToRun)
            {
                var effect = tuple.effect;
                var ownerCard = tuple.sourceCard; // 이 효과를 등록한 카드

                // -----------------------------------------------------------------
                // [신규 추가] 로컬 트리거(ON_PLAY, ON_DEATH) 주체 검증
                // -----------------------------------------------------------------
                if (trigger == EffectTriggerType.ON_PLAY || trigger == EffectTriggerType.ON_DEATH)
                {
                    // 이벤트를 발생시킨 원본 카드(eventContext.SourceCard)가 
                    // 이 효과를 소유한 카드(ownerCard)와 다르면, 남의 카드이므로 실행을 건너뜁니다.
                    if (ownerCard.InstanceId != eventContext.SourceCard?.InstanceId)
                    {
                        // 로그를 좀 더 명확하게 고쳐 어떤 카드가 무시되었는지 파악하기 쉽게 만듭니다.
                        Console.WriteLine($"[PublishAsync] {ownerCard.CardName}은 사용자가 아니므로 스킵 ({ownerCard.InstanceId} != {eventContext.SourceCard?.InstanceId})");
                        continue; 
                    }
                }
                // -----------------------------------------------------------------

                var ownerEntity = _gameState.FindEntityByCard(ownerCard);

                // [핵심] 방송을 듣고 발동하는 카드의 '주인 시점'으로 컨텍스트를 스마트하게 재구성!
                var actionContext = new EffectContext(ownerCard.OwnerUid, ownerCard, trigger)
                {
                    TargetEntity = eventContext.TargetEntity, // 방금 소환된 하수인 정보 유지
                    SourceEntity = ownerEntity,
                    EventData = eventContext.EventData
                };

                bool allConditionsMet = true;
                foreach (var condition in effect.Conditions)
                {
                    if (!condition.Check(_gameState, actionContext))
                    {
                        allConditionsMet = false;
                        break;
                    }
                }

                if (allConditionsMet)
                {
                    for (int i = 0; i < effect.RepeatCount; i++)
                    {
                        foreach (var action in effect.Actions)
                            await action.ExecuteAsync(_gameState, actionContext);
                    }
                }
            }
        }
    }
}