using System;
using System.Collections.Generic;
using System.Linq;

namespace GameServer
{
    /// <summary>
    /// [필드 개체 클래스]
    /// 필드 위에 소환된 하수인이나 영웅을 나타냅니다.
    /// 'EntityId'라는 고유 번호(정수)를 가집니다.
    /// </summary>
    public class GameEntity
    {
        public int EntityId { get; private set; } // 필드 위에서의 고유 번호 (100, 101...)
        public GameCard SourceCard { get; private set; } // 이 하수인을 만든 원본 카드 정보
        public string OwnerUid { get; private set; } // 누구 소유인지
        
        // 전투 관련 스탯
        public int Attack { get; set; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }
        public bool CanAttack { get; set; }   // 이번 턴에 공격 가능한가?
        public bool HasAttacked { get; set; } // 이번 턴에 이미 공격했는가?
        public int AttacksThisTurn { get; set; } = 0; // 이번 턴 누적 공격 횟수 (질풍 처리용)
        public int SummonedTurn { get; set; } = 0;   // 소환된 턴 번호 (속공 처리용)
        public List<CardKeywords>? Keywords { get; set; }
        public CardTribe? Tribe { get; set; }
        // 필드 개체의 버프 기록 (보통 SourceCard의 Enchantments와 동기화하거나 별도 관리)
        public List<EnchantmentInfo> Enchantments { get; set; } = new List<EnchantmentInfo>();
        public int Position { get; set; }
        public bool IsMember { get; set; }
        public bool IsLeader { get; set; }
        public string? SkinId { get; set; } // 장착된 리더/유닛 스킨 ID
        public List<MemberSkill> MemberSkills { get; set; } = new List<MemberSkill>();
        public bool HasUsedSkillThisTurn { get; set; } = false; // 이번 턴 액티브 스킬 사용 여부

        // 특수 오라 능력 4종
        public int SpellAmp { get; set; } = 0;              // 아군 주문 데미지 증폭
        public int SpellWeakness { get; set; } = 0;          // 적군 주문 데미지 약화
        public bool HasDrawSeal { get; set; } = false;       // 드로우 봉인 여부
        public int BuffAmpAttack { get; set; } = 0;          // 아군 주문 공격력 버프량 증폭
        public int BuffAmpHealth { get; set; } = 0;          // 아군 주문 체력 버프량 증폭

        // 체력과 무관하게 강제 파괴(처치) 대상이 되었는지를 나타내는 플래그
        public bool IsDestroyed { get; set; } = false; 

        public GameEntity(int entityId, GameCard sourceCard, string ownerUid)
        {
            EntityId = entityId;
            SourceCard = sourceCard;
            OwnerUid = ownerUid;
            
            // 소환 시점의 카드 스탯을 가져옴
            Attack = sourceCard.CurrentAttack;
            Health = sourceCard.CurrentHealth;
            MaxHealth = sourceCard.CurrentHealth;
            Keywords = new List<CardKeywords>(sourceCard.CurrentKeywords);
            Tribe = sourceCard.Tribe; 
            Enchantments = new List<EnchantmentInfo>(sourceCard.Enchantments);

            // 특수 오라 능력 복사
            SpellAmp = sourceCard.SpellAmp;
            SpellWeakness = sourceCard.SpellWeakness;
            HasDrawSeal = sourceCard.DrawSeal;
            BuffAmpAttack = sourceCard.BuffAmpAttack;
            BuffAmpHealth = sourceCard.BuffAmpHealth;
            
            // 속공(Rush)이나 돌진(Charge)이 있으면 바로 공격 가능
            bool hasCharge = Keywords.Contains(CardKeywords.Charge);
            bool hasRush = Keywords.Contains(CardKeywords.Rush);
            CanAttack = hasCharge || hasRush;

            // 멤버 카드인 경우 스킬 복사 및 공격 불가 설정
            if (sourceCard.MemberSkills != null)
            {
                MemberSkills = new List<MemberSkill>(sourceCard.MemberSkills);
            }
            if (sourceCard.Type == CardType.멤버)
            {
                IsMember = true;
                Attack = 0;
                CanAttack = false;
            }

            HasAttacked = false;
        }

        // 클라이언트에게 보낼 데이터(EntityData)로 변환
        public EntityData ToEntityData()
        {
            var triggers = new List<EffectTriggerType>();
            bool isSilenced = this.Keywords != null && this.Keywords.Contains(CardKeywords.Silence);
            if (!isSilenced && this.SourceCard != null && this.SourceCard.NewEffects != null)
            {
                triggers = this.SourceCard.NewEffects.Select(e => e.Trigger).Distinct().ToList();
            }

            List<MemberSkillData>? skillDataList = null;
            if (this.IsMember && this.MemberSkills != null && this.MemberSkills.Count > 0)
            {
                skillDataList = new List<MemberSkillData>(this.MemberSkills.Count);
                foreach (var sk in this.MemberSkills)
                {
                    skillDataList.Add(new MemberSkillData
                    {
                        skillId = sk.SkillId,
                        name = sk.Name,
                        description = sk.Description,
                        healthCost = sk.HealthCost,
                        manaCost = sk.ManaCost,
                        targeting = sk.Targeting,
                        canUse = !this.HasUsedSkillThisTurn && sk.CanPayCost(this.Health)
                    });
                }
            }

            return new EntityData
            {
                entityId = this.EntityId,
                cardId = this.SourceCard?.CardId,
                cardName = this.SourceCard?.CardName,
                ownerUid = this.OwnerUid,
                attack = this.Attack,
                health = this.Health,
                maxHealth = this.MaxHealth,
                canAttack = this.CanAttack,
                hasAttacked = this.HasAttacked,
                keywords = this.Keywords,
                activeTriggers = triggers,
                enchantments = new List<EnchantmentInfo>(this.Enchantments),
                position = this.Position, 
                isMember = this.IsMember,
                isLeader = this.IsLeader,
                skinId = this.SkinId,
                memberSkills = skillDataList,
                hasUsedSkillThisTurn = this.HasUsedSkillThisTurn
            };
        }
    }
}
