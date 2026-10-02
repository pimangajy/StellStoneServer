using System;
using System.Collections.Generic;

namespace GameServer
{
    /// <summary>
    /// 플레이어의 단일 행동(카드 사용, 소환, 스킬 발동 등) 중에 발생하는
    /// 모든 하위 결과(다단 피해, 회복, 드로우, 토큰 소환, 사망 등)를 수집하여
    /// 최종적으로 단 1개의 통합 히스토리 로그로 번들링하는 트랜잭션 스코프입니다.
    /// </summary>
    public class ActionLogScope
    {
        public string PlayerUid { get; }
        public string ActorName { get; }
        public string ActionType { get; set; } // "PLAY_CARD", "SUMMON", "EFFECT"
        public string SourceCardId { get; set; }
        public string SourceCardName { get; set; }
        public int SourceEntityId { get; set; }
        public int PrimaryPosition { get; set; }
        public int PrimaryTargetEntityId { get; set; }
        public string? PrimaryTargetName { get; set; }

        public int DrawnCardCount { get; private set; }
        public List<(string target, int damage)> DamageResults { get; } = new();
        public List<(string target, int heal)> HealResults { get; } = new();
        public List<(string minionName, int slot)> SummonResults { get; } = new();
        public List<string> DeathResults { get; } = new();
        public List<string> OtherNotes { get; } = new();

        public ActionLogScope(string playerUid, string actorName, string actionType, string cardId, string cardName, int position = 0, int targetEntityId = 0)
        {
            PlayerUid = playerUid;
            ActorName = actorName;
            ActionType = actionType;
            SourceCardId = cardId;
            SourceCardName = cardName;
            PrimaryPosition = position;
            PrimaryTargetEntityId = targetEntityId;
        }

        public void RecordDamage(string target, int amount)
        {
            if (amount > 0)
            {
                DamageResults.Add((target, amount));
            }
        }

        public void RecordHeal(string target, int amount)
        {
            if (amount > 0)
            {
                HealResults.Add((target, amount));
            }
        }

        public void RecordDraw(string targetPlayerUid)
        {
            DrawnCardCount++;
        }

        public void RecordSummon(string minionName, int slot)
        {
            SummonResults.Add((minionName, slot));
        }

        public void RecordDeath(string target)
        {
            DeathResults.Add(target);
        }

        public void RecordNote(string note)
        {
            OtherNotes.Add(note);
        }
    }
}
