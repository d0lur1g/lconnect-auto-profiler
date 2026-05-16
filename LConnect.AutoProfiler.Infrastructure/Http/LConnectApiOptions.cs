namespace LConnect.AutoProfiler.Infrastructure.Http;

/// <summary>
/// Options de configuration pour le client HTTP L-Connect.
/// Binding depuis appsettings.json → LConnectApi:BaseUrl
/// </summary>
public sealed class LConnectApiOptions
{
    public const string SectionName = "LConnectApi";

    /// <summary>URL de base du service local L-Connect.</summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11021/";
}
