using System.Net.WebSockets;

namespace GameServer
{
    /// <summary>
    /// 게임 룸에 참가한 플레이어 1명의 정보를 저장하는 클래스입니다.
    /// </summary>
    public class GamePlayer
    {
        public string Uid { get; private set; }
        public WebSocket WebSocket { get; private set; }
        public string ConnectionId { get; private set; }
        public DeckData? Deck { get; set; } // 게임 시작 시 DB에서 로드됩니다. (Nullable로 변경)

        // (신규) 이 플레이어가 테스트용 Bot인지 확인
        public bool IsBot { get; set; } = false;

        public GamePlayer(string uid, WebSocket webSocket, string connectionId)
        {
            Uid = uid;
            WebSocket = webSocket;
            ConnectionId = connectionId;
        }
    }
}