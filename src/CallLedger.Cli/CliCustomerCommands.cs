using CallLedger.Core;
using CallLedger.Vault;

namespace CallLedger.Cli;

public static partial class CliApplication
{
    private static void ListCustomers(CliCommand command, CrmDocument document, string query, TextWriter output, Func<DateTimeOffset> clock)
    {
        var reveal = ShouldReveal(command, document);
        var all = CustomerQuery.Search(document.Customers, query, command.Filter, clock());
        var customers = all.Skip(command.Offset).Take(command.Limit).Select(customer => CliJson.ToOutput(customer, reveal)).ToArray();
        WriteSuccess(output, document.Revision, new { customers, offset = command.Offset, limit = command.Limit, total = all.Count }, RedactedFields(reveal));
    }

    private static void GetCustomer(CliCommand command, CrmDocument document, TextWriter output)
    {
        var customer = FindCustomer(document, command.CustomerId!.Value);
        var reveal = ShouldReveal(command, document);
        WriteSuccess(output, document.Revision, new { customer = CliJson.ToOutput(customer, reveal) }, RedactedFields(reveal));
    }

    private static void AddCustomer(CliCommand command, CustomerPatch patch, ICrmStore store, TextWriter output)
    {
        var document = store.Load();
        CheckExpectedRevision(document, command.ExpectedRevision);
        var customer = ApplyPatch(new Customer(), patch, isNew: true);
        Validate(customer);
        var saved = store.Save(document with { Customers = [.. document.Customers, customer] });
        WriteCustomerMutation(command, saved, customer.Id, output);
    }

    private static void UpdateCustomer(CliCommand command, CustomerPatch patch, ICrmStore store, TextWriter output)
    {
        var document = store.Load();
        CheckExpectedRevision(document, command.ExpectedRevision);
        var current = FindCustomer(document, command.CustomerId!.Value);
        var updated = ApplyPatch(current, patch, isNew: false);
        Validate(updated);
        var saved = store.Save(document with { Customers = document.Customers.Select(customer => customer.Id == updated.Id ? updated : customer).ToList() });
        WriteCustomerMutation(command, saved, updated.Id, output);
    }

    private static void DeleteCustomer(CliCommand command, ICrmStore store, TextWriter output)
    {
        var document = store.Load();
        CheckExpectedRevision(document, command.ExpectedRevision);
        var customer = FindCustomer(document, command.CustomerId!.Value);
        var saved = store.Save(document with { Customers = document.Customers.Where(item => item.Id != customer.Id).ToList() });
        WriteSuccess(output, saved.Revision, new { deletedId = customer.Id }, []);
    }

    private static void MarkCalled(CliCommand command, MarkCalledPatch? patch, ICrmStore store, TextWriter output, Func<DateTimeOffset> clock)
    {
        var document = store.Load();
        CheckExpectedRevision(document, command.ExpectedRevision);
        var current = FindCustomer(document, command.CustomerId!.Value);
        var marked = current with
        {
            LastCalledAt = patch?.At.IsPresent is true ? patch.At.Value ?? clock().ToUniversalTime() : clock().ToUniversalTime(),
            FollowUpAt = patch?.FollowUpAt.IsPresent is true ? patch.FollowUpAt.Value : null,
            Notes = patch?.Notes.IsPresent is true ? patch.Notes.Value ?? "" : current.Notes,
            CallReason = patch?.CallReason.IsPresent is true ? patch.CallReason.Value ?? "" : current.CallReason
        };
        Validate(marked);
        var saved = store.Save(document with { Customers = document.Customers.Select(customer => customer.Id == marked.Id ? marked : customer).ToList() });
        WriteCustomerMutation(command, saved, marked.Id, output);
    }

    private static void GetPreferences(CrmDocument document, TextWriter output) =>
        WriteSuccess(output, document.Revision, new { preferences = PreferencesOutput.From(document.Preferences) }, []);

    private static void UpdatePreferences(CliCommand command, PreferencesPatch patch, ICrmStore store, TextWriter output)
    {
        var document = store.Load();
        CheckExpectedRevision(document, command.ExpectedRevision);
        var preferences = document.Preferences with
        {
            ShowSensitiveByDefault = patch.ShowSensitiveByDefault.IsPresent ? patch.ShowSensitiveByDefault.Value : document.Preferences.ShowSensitiveByDefault,
            CompactMode = patch.CompactMode.IsPresent ? patch.CompactMode.Value : document.Preferences.CompactMode,
            AlwaysOnTop = patch.AlwaysOnTop.IsPresent ? patch.AlwaysOnTop.Value : document.Preferences.AlwaysOnTop
        };
        var saved = store.Save(document with { Preferences = preferences });
        WriteSuccess(output, saved.Revision, new { preferences = PreferencesOutput.From(saved.Preferences) }, []);
    }

    private static Customer ApplyPatch(Customer current, CustomerPatch patch, bool isNew) => current with
    {
        Id = isNew ? Guid.NewGuid() : current.Id,
        Name = patch.Name.IsPresent ? patch.Name.Value! : current.Name,
        DateOfBirth = patch.DateOfBirth.IsPresent ? patch.DateOfBirth.Value : current.DateOfBirth,
        Ssn = patch.Ssn.IsPresent ? patch.Ssn.Value ?? "" : current.Ssn,
        MedicareId = patch.MedicareId.IsPresent ? patch.MedicareId.Value ?? "" : current.MedicareId,
        Phone = patch.Phone.IsPresent ? patch.Phone.Value ?? "" : current.Phone,
        FollowUpAt = patch.FollowUpAt.IsPresent ? patch.FollowUpAt.Value : current.FollowUpAt,
        CallReason = patch.CallReason.IsPresent ? patch.CallReason.Value ?? "" : current.CallReason,
        Notes = patch.Notes.IsPresent ? patch.Notes.Value ?? "" : current.Notes,
        LastCalledAt = patch.LastCalledAt.IsPresent ? patch.LastCalledAt.Value : current.LastCalledAt
    };

    private static void WriteCustomerMutation(CliCommand command, CrmDocument saved, Guid customerId, TextWriter output)
    {
        var reveal = ShouldReveal(command, saved);
        var customer = FindCustomer(saved, customerId);
        WriteSuccess(output, saved.Revision, new { customer = CliJson.ToOutput(customer, reveal) }, RedactedFields(reveal));
    }

    private static Customer FindCustomer(CrmDocument document, Guid id) => document.Customers.FirstOrDefault(customer => customer.Id == id)
        ?? throw new CliException(3, "NOT_FOUND", "Customer was not found.", new { fields = new[] { "id" } });

    private static bool ShouldReveal(CliCommand command, CrmDocument document) => command.Reveal || (!command.Redact && document.Preferences.ShowSensitiveByDefault);
    private static string[] RedactedFields(bool reveal) => reveal ? [] : ["dateOfBirth", "ssn", "medicareId"];

    private static void CheckExpectedRevision(CrmDocument document, long? expectedRevision)
    {
        if (expectedRevision is not null && expectedRevision.Value != document.Revision)
        {
            throw new CliException(5, "REVISION_CONFLICT", "The expected revision does not match the current store revision.", new { expectedRevision = expectedRevision.Value, currentRevision = document.Revision });
        }
    }

    private static void Validate(Customer customer)
    {
        if (CustomerValidator.Validate(customer).Count != 0)
        {
            throw new CliException(2, "VALIDATION_FAILED", "Customer data is invalid.");
        }
    }
}
