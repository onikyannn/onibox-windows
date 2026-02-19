using System;

namespace Onibox.Services;

public sealed class BasicAuthRequiredException : Exception
{
    public Uri Uri { get; }

    public BasicAuthRequiredException(Uri uri)
        : base(Localization.GetString("Error.BasicAuth.Required"))
    {
        Uri = uri;
    }
}
