using System.Globalization;
using CallLedger.Core;
using CallLedger.Desktop.ViewModels;

namespace CallLedger.Desktop;

internal static class SmokeLocaleChecks
{
    public static void Exercise()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "th-TH" })
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                ExerciseDraftParsing(cultureName);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static void ExerciseDraftParsing(string cultureName)
    {
        var dateOfBirth = new DateOnly(1985, 6, 1);
        var source = new Customer
        {
            Name = "Locale source",
            DateOfBirth = dateOfBirth,
        };
        var draft = new CustomerDraft();
        draft.Load(source);
        Require(!draft.HasChanges, $"Loading an ISO DOB marked the {cultureName} draft as dirty.");

        draft.Notes = "Locale smoke note";
        Require(draft.TryBuild(out var edited, out _) && edited?.DateOfBirth == dateOfBirth,
            $"Saving notes changed the ISO DOB under {cultureName}.");
        draft.SetActualValue(CustomerFields.DateOfBirth, " 1985-06-01 ");
        Require(draft.TryBuild(out edited, out _) && edited?.DateOfBirth == dateOfBirth,
            $"Whitespace around an ISO DOB changed its calendar under {cultureName}.");

        var followUpDraft = new CustomerDraft();
        followUpDraft.CreateNew();
        foreach (var followUpText in new[] { "2030-01-15 09:30", "2030-01-15 09:30:00", " 2030-01-15 09:30 " })
        {
            followUpDraft.FollowUpAt = followUpText;
            Require(followUpDraft.TryBuild(out var created, out _)
                && created?.FollowUpAt is { Year: 2030, Month: 1, Day: 15, Hour: 9, Minute: 30 },
                $"An ISO follow-up used a non-Gregorian year under {cultureName}.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
