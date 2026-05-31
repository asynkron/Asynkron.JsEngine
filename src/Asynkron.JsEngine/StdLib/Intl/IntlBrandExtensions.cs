using Asynkron.JsEngine.Runtime;

namespace Asynkron.JsEngine.StdLib.Intl;

internal static class IntlBrandExtensions
{
    public static JsObject EnsureBrand(this JsValue candidate, string brandKey, RealmState realm,
        string errorMessage)
    {
        if (candidate.TryGetObject(out var obj) && obj!.TryGetProperty(brandKey, out var marker) &&
            marker.TryGetBoolean(out var flag) && flag)
        {
            return obj;
        }

        throw StandardLibrary.ThrowTypeError(errorMessage, realm: realm);
    }
}
