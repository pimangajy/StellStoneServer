namespace GameServer
{
    /// <summary>
    /// 서버 런타임 전역 게임 설정 (appsettings.json의 "GameSettings:EnableSinglePlayerBot"이 단일 원천)
    /// </summary>
    public static class GameServerSettings
    {
        /// <summary>
        /// 매칭 요청 시 봇 자동 매칭 여부 (기본값은 appsettings.json에서 로드됨)
        /// true: 매칭 큐 진입 후 2.5초 대기 시 봇과 매칭
        /// false: 실제 다른 유저가 매칭될 때까지 무한 대기 (정상 PvP 모드)
        /// </summary>
        public static bool EnableSinglePlayerBot { get; set; } = true;
    }
}
