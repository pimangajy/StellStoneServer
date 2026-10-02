using System;
using System.Collections.Generic;
using GameServer.Effects;

namespace GameServer
{
    /// <summary>
    /// 멤버 카드(플레인즈워커)가 보유하는 액티브 스킬 정의 모델
    /// </summary>
    public class MemberSkill
    {
        public int SkillId { get; set; }           // 스킬 식별 번호 (0, 1, 2, 3)
        public string Name { get; set; } = "";      // 스킬 이름 (예: "별의 축복")
        public string Description { get; set; } = ""; // 툴팁 설명
        
        // 체력 코스트: 양수면 +HP 충전, 음수면 -HP 소모 (예: +2, -3)
        public int HealthCost { get; set; }
        public int ManaCost { get; set; } = 0;      // 필요 시 마나 소모량
        public bool Targeting { get; set; } = false; // 대상 지정 필요 여부 (하수인과 동일한 bool 체계)
        
        // 이전 TargetRule JSON과의 하위 호환성 지원
        [Newtonsoft.Json.JsonProperty("TargetRule")]
        private string? LegacyTargetRule
        {
            set
            {
                if (!string.IsNullOrEmpty(value) && value != "None")
                {
                    Targeting = true;
                }
            }
        }
        
        // 스킬 발동 시 실행될 카드 효과 목록
        public List<CardEffect> Effects { get; set; } = new List<CardEffect>();

        /// <summary>
        /// 멤버의 현재 체력으로 이 스킬 코스트를 지불할 수 있는지 검사합니다.
        /// (체력 소모 스킬의 경우, 현재 체력이 코스트 이상이어야만 사용 가능)
        /// </summary>
        public bool CanPayCost(int currentHealth)
        {
            if (HealthCost >= 0) return true; // 체력 충전 스킬은 언제든 가능
            return currentHealth >= Math.Abs(HealthCost); // 코스트 이상의 체력 필요
        }
    }

    /// <summary>
    /// 클라이언트에게 전송되는 멤버 스킬 DTO 정보
    /// </summary>
    public class MemberSkillData
    {
        public int skillId { get; set; }
        public string name { get; set; } = "";
        public string description { get; set; } = "";
        public int healthCost { get; set; }
        public int manaCost { get; set; }
        public bool targeting { get; set; }
        public bool canUse { get; set; }
    }
}
