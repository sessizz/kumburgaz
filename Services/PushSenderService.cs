using System.Net;
using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using WebPushSubscription = Lib.Net.Http.WebPush.PushSubscription;

namespace Kumburgaz.Web.Services;

public enum PushSendResult { Sent, Gone, Failed, Skipped }

/// <summary>
/// VAPID Web Push gonderimi. Push__Subject/PublicKey/PrivateKey config anahtarlari tanimli
/// degilse Enabled=false doner ve gonderim sessizce atlanir (zil/polling etkilenmez).
/// </summary>
public sealed class PushSenderService
{
    private readonly VapidAuthentication? _vapid;
    private readonly PushServiceClient _client = new();

    public PushSenderService(IConfiguration configuration)
    {
        var subject = configuration["Push:Subject"];
        var publicKey = configuration["Push:PublicKey"];
        var privateKey = configuration["Push:PrivateKey"];

        PublicKey = string.IsNullOrWhiteSpace(publicKey) ? null : publicKey;

        if (!string.IsNullOrWhiteSpace(subject) && !string.IsNullOrWhiteSpace(publicKey) && !string.IsNullOrWhiteSpace(privateKey))
        {
            _vapid = new VapidAuthentication(publicKey, privateKey) { Subject = subject };
            _client.DefaultAuthentication = _vapid;
        }
    }

    public bool Enabled => _vapid is not null;

    public string? PublicKey { get; }

    public async Task<PushSendResult> SendAsync(Models.PushSubscription subscription, string title, string? body, string? url, CancellationToken ct = default)
    {
        if (_vapid is null)
        {
            return PushSendResult.Skipped;
        }

        var target = new WebPushSubscription { Endpoint = subscription.Endpoint };
        target.SetKey(PushEncryptionKeyName.P256DH, subscription.P256dh);
        target.SetKey(PushEncryptionKeyName.Auth, subscription.Auth);

        var payload = JsonSerializer.Serialize(new { title, body, url });
        var message = new PushMessage(payload);

        try
        {
            await _client.RequestPushMessageDeliveryAsync(target, message, ct);
            return PushSendResult.Sent;
        }
        catch (PushServiceClientException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return PushSendResult.Gone;
        }
        catch
        {
            return PushSendResult.Failed;
        }
    }
}
