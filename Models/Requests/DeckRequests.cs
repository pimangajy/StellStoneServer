namespace GameServer
{
    public class CreateDeckRequest
    {
        public string? className { get; set; }
    }

    public class SelectDeckRequest
    {
        public string? DeckId { get; set; }
    }
}
