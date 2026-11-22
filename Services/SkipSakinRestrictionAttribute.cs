namespace Kumburgaz.Web.Services;

/// <summary>
/// <see cref="SakinAreaRestrictionFilter"/>'ın atlayacağı controller/action'ları işaretler.
/// OAuth yetkilendirme ucu Sakin kullanıcıyı /m'e yönlendirmek yerine istemciye
/// access_denied döndürmelidir; aksi halde Claude'un bağlantı akışı yarıda kalır.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class SkipSakinRestrictionAttribute : Attribute;
