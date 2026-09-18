namespace CallLedger.Core;

public interface ICrmStore
{
    CrmDocument Load();

    CrmDocument Save(CrmDocument document);

    void ExportBackup(string path);

    CrmDocument RestoreBackup(string path);
}
