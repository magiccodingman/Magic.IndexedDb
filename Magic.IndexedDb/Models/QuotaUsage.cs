namespace Magic.IndexedDb.Models;
public sealed record QuotaUsage(long Quota, long Usage)
{
    private static double ConvertBytesToMegabytes(long bytes)
    {
        return (double)bytes / (1024 * 1024);
    }

    public double QuotaInMegabytes => ConvertBytesToMegabytes(Quota);
    public double UsageInMegabytes => ConvertBytesToMegabytes(Usage);

    public (double quota, double usage) InMegabytes => (QuotaInMegabytes, UsageInMegabytes);
}
