using System;

namespace Onibox.Services;

public sealed class BasicAuthInvalidException : Exception
{
    public Uri Uri { get; }

    public BasicAuthInvalidException(Uri uri)
        : base(Localization.GetString("Error.BasicAuth.Invalid"))
    {
        Uri = uri;
    }
}
