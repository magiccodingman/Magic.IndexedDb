namespace Magic.IndexedDb.Models;
public sealed record QuotaUsage(long Quota, long Usage)
{
    private static double ConvertBytesToMegabytes(long bytes)
    {
        return (double)bytes / (1024 * 1024);
    }

    public double QuotaInMegabytes => ConvertBytesToMegabytes(this.Quota);
    public double UsageInMegabytes => ConvertBytesToMegabytes(this.Usage);

    public (double quota, double usage) InMegabytes => (this.QuotaInMegabytes, this.UsageInMegabytes);
}
