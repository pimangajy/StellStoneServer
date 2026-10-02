using System;

namespace GameServer
{
    /// <summary>
    /// 게임 내 액션 및 디버그 이벤트 로그 데이터 모델
    /// </summary>
    public class GameLogEvent
    {
        public DateTime Timestamp { get; set; }
        public string Actor { get; set; } = "";     // 행동한 주체 (예: "PlayerA", "PlayerB", "System")
        public string ActionType { get; set; } = "";// 액션 종류 (예: "PLAY_CARD", "ATTACK", "DAMAGE", "HEAL", "PHASE_CHANGE")
        public string Message { get; set; } = "";   // 사람이 읽기 쉬운 요약 메세지 (대시보드 출력용)
        public object? Details { get; set; }        // 구체적인 타겟 ID나 데미지 수치 등 (인게임 UI 처리용 JSON 객체)
    }
}
