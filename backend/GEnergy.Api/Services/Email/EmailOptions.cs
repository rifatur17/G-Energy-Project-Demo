using MimeKit;
namespace GEnergy.Api.Services.Email;

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public bool WorkerEnabled { get; set; } = true;
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "G Energy";
    public string LoginUrl { get; set; } = "http://localhost:5173/login";

    public bool IsValid(bool development)
    {
        if (!Enabled) return true;
        return !string.IsNullOrWhiteSpace(Host) && Port is >= 1 and <= 65535 &&
            Security is "StartTls" or "SslOnConnect" && MailboxAddress.TryParse(FromAddress, out _) &&
            (string.IsNullOrWhiteSpace(Username) == string.IsNullOrWhiteSpace(Password)) &&
            Uri.TryCreate(LoginUrl, UriKind.Absolute, out var uri) && string.IsNullOrEmpty(uri.UserInfo) &&
            (uri.Scheme == "https" || development && uri.Scheme == "http" && uri.IsLoopback);
    }
}
