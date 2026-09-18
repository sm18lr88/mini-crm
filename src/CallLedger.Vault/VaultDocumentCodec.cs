using System.Security.Cryptography;
using System.Text.Json;
using CallLedger.Core;

namespace CallLedger.Vault;

internal static class VaultDocumentCodec
{
    private const int EnvelopeVersion = 1;
    private const int MaximumPlaintextBytes = 8 * 1024 * 1024;
    internal const int MaximumEncryptedFileBytes = 16 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    public static byte[] Encrypt(CrmDocument document)
    {
        ValidateDocument(document);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        try
        {
            if (plaintext.Length > MaximumPlaintextBytes)
            {
                throw new CrmStoreException("The CRM data is too large to save.");
            }

            var protectedBytes = NativeDpapi.Protect(plaintext);
            var envelope = new VaultEnvelope(EnvelopeVersion, Convert.ToBase64String(protectedBytes));
            return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        }
        catch (CrmStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new CrmStoreException("The CRM data could not be encrypted.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public static CrmDocument DecryptAndValidate(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length is 0 or > MaximumEncryptedFileBytes)
        {
            throw new CrmStoreException("The stored CRM data has an invalid size.");
        }

        byte[]? plaintext = null;
        try
        {
            var envelope = JsonSerializer.Deserialize<VaultEnvelope>(encryptedBytes, JsonOptions)
                ?? throw new JsonException("The vault envelope is empty.");
            if (envelope.Version != EnvelopeVersion || string.IsNullOrWhiteSpace(envelope.Payload))
            {
                throw new CrmStoreException("The stored CRM data uses an unsupported format.");
            }

            plaintext = NativeDpapi.Unprotect(Convert.FromBase64String(envelope.Payload));
            if (plaintext.Length is 0 or > MaximumPlaintextBytes)
            {
                throw new CrmStoreException("The stored CRM data has an invalid size.");
            }

            RequireDocumentShape(plaintext);
            var document = JsonSerializer.Deserialize<CrmDocument>(plaintext, JsonOptions)
                ?? throw new JsonException("The CRM document is empty.");
            ValidateDocument(document);
            return document;
        }
        catch (CrmStoreException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            throw new CrmStoreException("The stored CRM data could not be read.", exception);
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public static void ValidateDocument(CrmDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.SchemaVersion != 1 || document.Revision < 0 || document.Customers is null || document.Preferences is null)
        {
            throw new CrmStoreException("The CRM data has an invalid structure.");
        }

        if (document.Customers.Count > 100_000)
        {
            throw new CrmStoreException("The CRM data contains too many customers.");
        }

        var identifiers = new HashSet<Guid>();
        foreach (var customer in document.Customers)
        {
            if (customer is null || customer.Id == Guid.Empty || !identifiers.Add(customer.Id)
                || HasNullText(customer) || CustomerValidator.Validate(customer).Count > 0)
            {
                throw new CrmStoreException("The CRM data has an invalid customer record.");
            }
        }
    }

    private static bool HasNullText(Customer customer) => ReferenceEquals(customer.Name, null)
        || ReferenceEquals(customer.Ssn, null)
        || ReferenceEquals(customer.MedicareId, null)
        || ReferenceEquals(customer.Phone, null)
        || ReferenceEquals(customer.CallReason, null)
        || ReferenceEquals(customer.Notes, null);

    private static void RequireDocumentShape(byte[] plaintext)
    {
        using var json = JsonDocument.Parse(plaintext);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new CrmStoreException("The stored CRM data has an invalid structure.");
        }

        RequireProperty(root, nameof(CrmDocument.SchemaVersion), JsonValueKind.Number);
        RequireProperty(root, nameof(CrmDocument.Revision), JsonValueKind.Number);
        var customers = RequireProperty(root, nameof(CrmDocument.Customers), JsonValueKind.Array);
        var preferences = RequireProperty(root, nameof(CrmDocument.Preferences), JsonValueKind.Object);
        RequireProperty(preferences, nameof(Preferences.ShowSensitiveByDefault), JsonValueKind.True, JsonValueKind.False);
        RequireProperty(preferences, nameof(Preferences.CompactMode), JsonValueKind.True, JsonValueKind.False);
        RequireProperty(preferences, nameof(Preferences.AlwaysOnTop), JsonValueKind.True, JsonValueKind.False);

        foreach (var customer in customers.EnumerateArray())
        {
            if (customer.ValueKind != JsonValueKind.Object)
            {
                throw new CrmStoreException("The stored CRM data has an invalid structure.");
            }

            RequireProperty(customer, nameof(Customer.Id), JsonValueKind.String);
            RequireProperty(customer, nameof(Customer.Name), JsonValueKind.String);
            RequireNullableString(customer, nameof(Customer.DateOfBirth));
            RequireProperty(customer, nameof(Customer.Ssn), JsonValueKind.String);
            RequireProperty(customer, nameof(Customer.MedicareId), JsonValueKind.String);
            RequireProperty(customer, nameof(Customer.Phone), JsonValueKind.String);
            RequireNullableString(customer, nameof(Customer.FollowUpAt));
            RequireProperty(customer, nameof(Customer.CallReason), JsonValueKind.String);
            RequireProperty(customer, nameof(Customer.Notes), JsonValueKind.String);
            RequireNullableString(customer, nameof(Customer.LastCalledAt));
        }
    }

    private static JsonElement RequireProperty(JsonElement parent, string name, params JsonValueKind[] allowedKinds)
    {
        if (!parent.TryGetProperty(name, out var property) || !allowedKinds.Contains(property.ValueKind))
        {
            throw new CrmStoreException("The stored CRM data has an invalid structure.");
        }

        return property;
    }

    private static void RequireNullableString(JsonElement parent, string name) =>
        RequireProperty(parent, name, JsonValueKind.String, JsonValueKind.Null);

    private sealed record VaultEnvelope(int Version, string Payload);
}
