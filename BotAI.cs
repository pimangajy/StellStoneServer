using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameServer
{
    /// <summary>
    /// 봇의 의사결정 로직을 담당하는 독립 클래스입니다.
    /// 멀리건, 턴 진행 등 봇의 모든 행동 지능이 이곳에 포함됩니다.
    /// </summary>
    public class BotAI
    {
        private readonly GameState _gameState;
        private readonly string _botUid;

        public BotAI(GameState gameState, string botUid)
        {
            _gameState = gameState;
            _botUid = botUid;
        }

        /// <summary>
        /// 봇의 멀리건(시작 손패 교체)을 결정하고 서버에 전송합니다.
        /// </summary>
        public async Task ExecuteMulliganAsync()
        {
            var p = _gameState.GetPlayerState(_botUid);

            // 1. 사람처럼 생각하는 시간 연출
            await Task.Delay(2000); 

            // 2. 봇 AI 멀리건 조건: 4코스트 이상이거나 주문(Spell) 카드인 경우 교체
            var toReplace = p.Hand
                .Where(c => c.CurrentCost >= 4 || c.Type == "Spell" || c.Type == "주문")
                .Select(c => c.InstanceId)
                .ToList();
                
            // 3. 서버(GameState)로 결정 전송 (유저가 패킷을 보내는 것과 동일한 효과)
            await _gameState.ProcessMulliganDecisionAsync(_botUid, new C_MulliganDecision { cardInstanceIdsToReplace = toReplace });
        
            Console.WriteLine("봇이 멀리건을 결정");
        }

        /// <summary>
        /// 봇의 메인 턴을 실행합니다. (생각하기 -> 카드 내기 -> 공격 -> 종료)
        /// </summary>
        public async Task ExecuteTurnAsync()
        {
            var me = _gameState.GetPlayerState(_botUid);
            var opponent = _gameState.GetPlayerState(_botUid, true);

            Console.WriteLine("봇의 턴 시작");

            // 1. 턴 시작 후 생각하는 시간 연출
            await Task.Delay(2000);

            // 2. 카드 플레이 로직 (마나가 허용하는 한 반복)
            bool acted = true;
            while (acted)
            {
                acted = false;
                
                // 손패에서 낼 수 있는 가장 높은 코스트의 '하수인' 카드부터 탐색
                var playableCards = me.Hand
                    .Where(c => c.CurrentCost <= me.CurrentMana && (c.Type == "Minion" || c.Type == "하수인"))
                    .OrderByDescending(c => c.CurrentCost)
                    .ToList();

                if (playableCards.Count > 0)
                {
                    var card = playableCards[0];
                    int pos = FindEmptySlot(me);
                    
                    if (pos != -1)
                    {
                        Console.WriteLine("봇의 카드 사용");
                        // 카드 내기 액션 서버로 전송
                        await _gameState.ProcessPlayCardAsync(_botUid, new C_PlayCard {
                            handCardInstanceId = card.InstanceId,
                            position = pos,
                            targetEntityId = -1 // 단순 AI이므로 타겟 지정 생략 (전투의 함성 등 타겟팅 무시)
                        });
                        acted = true;
                        
                        // 연속으로 카드를 낼 때의 행동 지연
                        await Task.Delay(1500); 
                    }
                }
            }

            // 3. 공격 로직 (필드의 모든 하수인 동원)
            foreach (var entity in me.Field)
            {
                if (entity != null && entity.CanAttack && !entity.HasAttacked)
                {
                    Console.WriteLine("봇의 공격");
                    // 단순 AI: 적 하수인을 무시하고 무조건 상대 영웅(Leader) 공격 (명치 메타)
                    await _gameState.ProcessAttackAsync(_botUid, new C_Attack {
                        attackerEntityId = entity.EntityId,
                        defenderEntityId = opponent.Leader.EntityId
                    });
                    
                    // 연속 공격 시의 행동 지연
                    await Task.Delay(1000);
                }
            }

            // 4. 할 일을 모두 마쳤으므로 턴 종료
            await _gameState.ProcessEndTurnAsync(_botUid);
        }

        /// <summary>
        /// 필드에서 가장 빠른 빈자리(인덱스)를 찾습니다.
        /// </summary>
        private int FindEmptySlot(PlayerState p)
        {
            for (int i = 0; i < p.Field.Length; i++)
            {
                if (p.Field[i] == null) return i;
            }
            return -1; // 필드가 꽉 참
        }
    }
}