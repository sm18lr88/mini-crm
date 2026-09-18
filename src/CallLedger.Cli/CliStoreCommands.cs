using CallLedger.Core;
using CallLedger.Vault;

namespace CallLedger.Cli;

public static partial class CliApplication
{
    private static void Backup(CliCommand command, ICrmStore store, TextWriter output, string dataDirectory)
    {
        var outputPath = GetBackupOutputPath(command.OutputPath!, dataDirectory);
        if (File.Exists(outputPath))
        {
            throw new CliException(6, "OUTPUT_EXISTS", "Backup output already exists.");
        }

        var stagePath = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.stage");
        var staged = false;
        try
        {
            store.ExportBackup(stagePath);
            staged = true;
            File.Move(stagePath, outputPath, overwrite: false);
        }
        catch (CliException)
        {
            throw;
        }
        catch (IOException)
        {
            throw new CliException(6, "OUTPUT_EXISTS", "Backup output could not be created without overwriting an existing file.");
        }
        finally
        {
            if (staged && File.Exists(stagePath))
            {
                File.Delete(stagePath);
            }
        }

        WriteSuccess(output, null, new { backup = true }, []);
    }

    private static void Restore(CliCommand command, ICrmStore store, TextWriter output)
    {
        if (command.ExpectedRevision is not null)
        {
            CheckExpectedRevision(store.Load(), command.ExpectedRevision);
        }

        var restored = store.RestoreBackup(command.InputPath!);
        WriteSuccess(output, restored.Revision, new { restored = true }, []);
    }

    private static string GetBackupOutputPath(string outputPath, string dataDirectory)
    {
        try
        {
            var fullOutput = Path.GetFullPath(outputPath);
            var fullData = Path.GetFullPath(dataDirectory);
            var relative = Path.GetRelativePath(fullData, fullOutput);
            if (relative == "." || (!relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && relative != ".." && !Path.IsPathRooted(relative)))
            {
                throw new CliException(2, "USAGE", "Backup output cannot be inside the CRM data folder.");
            }

            if (Path.GetDirectoryName(fullOutput) is not { Length: > 0 })
            {
                throw new CliException(6, "SAFE_IO", "Backup output directory could not be used.");
            }

            return fullOutput;
        }
        catch (CliException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            throw new CliException(6, "SAFE_IO", "Backup output directory could not be used.");
        }
    }
}
