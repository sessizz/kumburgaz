using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace Kumburgaz.Web.Services;

public static class EnumDisplayHelper
{
    public static string Display(Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();
        return member?.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
    }
}
