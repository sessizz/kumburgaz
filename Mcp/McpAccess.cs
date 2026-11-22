using System.Security.Claims;
using Kumburgaz.Web.Services;
using ModelContextProtocol;

namespace Kumburgaz.Web.Mcp;

// Araçların modül yetki kontrolü: ekrandakiyle aynı yetki matrisi.
public static class McpAccess
{
    public static void Require(ClaimsPrincipal user, PermissionService permissions, string module, bool write)
    {
        if (!permissions.HasAccess(user, module, write))
        {
            throw new McpException($"Bu işlem için yetkiniz yok ({module}).");
        }
    }
}
