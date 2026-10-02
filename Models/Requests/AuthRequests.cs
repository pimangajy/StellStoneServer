namespace GameServer
{
    public class SignupRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
        public string? username { get; set; }
    }

    public class LoginRequest
    {
        public string? email { get; set; }
        public string? password { get; set; }
    }
}
