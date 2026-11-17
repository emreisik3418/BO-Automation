namespace BoAdmin.Application.Common.Exceptions;

/// <summary>Kullanıcı girdisi/işlem kuralı hatası. Mesaj doğrudan ekranda gösterilir.</summary>
public sealed class AppValidationException : Exception
{
    public AppValidationException(string userMessage) : base(userMessage)
    {
        UserMessage = userMessage;
    }

    public string UserMessage { get; }
}
