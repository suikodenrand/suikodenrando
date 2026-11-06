using System;
using System.Collections.Generic;
using System.Linq;

namespace SuikodenRandomizer.Core;

public sealed class SeedValidationException : Exception
{
    public SeedValidationException(IEnumerable<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors.ToArray();
    }

    public IReadOnlyList<string> Errors { get; }

    private static string BuildMessage(IEnumerable<string> errors)
    {
        return "The active seed is invalid: " + string.Join("; ", errors);
    }
}

