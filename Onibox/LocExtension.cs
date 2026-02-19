using Microsoft.UI.Xaml.Markup;
using Onibox.Services;

namespace Onibox;

[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    protected override object ProvideValue()
    {
        return Localization.GetString(Key);
    }
}
