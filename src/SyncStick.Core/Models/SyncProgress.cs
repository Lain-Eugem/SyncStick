namespace SyncStick.Core.Models;

public enum SyncStage
{
    Preparing,
    Copying,
    Cleaning,
    Completed
}

public sealed record SyncProgress(
    SyncStage Stage,
    string? CurrentFile,
    int FilesDone,
    int TotalFiles,
    long BytesDone,
    long BytesTotal)
{
    public double Fraction
    {
        get
        {
            if (Stage is SyncStage.Completed)
            {
                return 1;
            }

            if (BytesTotal <= 0)
            {
                return TotalFiles <= 0 ? 0 : (double)FilesDone / TotalFiles;
            }

            return Math.Clamp((double)BytesDone / BytesTotal, 0, 1);
        }
    }

    public int Percent => (int)Math.Round(Fraction * 100);
}
