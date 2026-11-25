using System.Diagnostics;
using System.Text;
using Magic.IndexedDb;
using Magic.IndexedDb.Helpers;
using Magic.IndexedDb.Models;

namespace TestBase.Numeric;

/// <summary>Deterministic synthetic comparison. Timing is reported, never used as a pass/fail threshold.</summary>
public static class NumericBenchmark
{
    public const int RowCount = 5_000;
    public const int Repetitions = 3;

    public static async Task<List<NumericBenchmarkResult>> RunAsync(IMagicIndexedDb magic,
        Func<Task>? verifyNativeExecution = null)
    {
        // Integers and quarter increments are exactly representable in both modes.
        // This isolates the cost of the representation without comparing rounded results.
        var rows = Enumerable.Range(0, RowCount).Select(index => new NumericValueRecord
        {
            Id = index + 1, Signed = index - 2_500, Unsigned = (ulong)index,
            Amount = (index - 2_500) * 0.25m, OptionalLong = index % 2 == 0 ? null : index
        }).ToArray();
        var expectedRange = rows.Where(row => row.Signed >= 1_000 && row.Signed < 1_200)
            .Select(row => row.Id).ToArray();
        var expectedOrder = rows.OrderBy(row => row.Amount).Select(row => row.Id).ToArray();
        var results = new List<NumericBenchmarkResult>();

        foreach (var mode in new[] { MagicNumericStorageMode.Legacy, MagicNumericStorageMode.Lossless })
        {
            var query = await magic.Query<NumericValueRecord>(_ =>
                mode == MagicNumericStorageMode.Legacy ? NumericRepository.Legacy : NumericRepository.Lossless);
            var settings = new MagicJsonSerializationSettings { UseCamelCase = true, NumericStorageMode = mode };
            var result = new NumericBenchmarkResult
            {
                Mode = mode.ToString(), RowCount = RowCount, Repetitions = Repetitions,
                SerializedUtf8Bytes = Encoding.UTF8.GetByteCount(MagicSerializationHelper.SerializeObject(rows, settings))
            };

            for (var repetition = 0; repetition < Repetitions; repetition++)
            {
                await query.ClearTable();
                var watch = Stopwatch.StartNew();
                await query.AddRangeAsync(rows);
                result.WriteMilliseconds.Add(watch.Elapsed.TotalMilliseconds);

                watch.Restart();
                var range = await query.Where(row => row.Signed >= 1_000 && row.Signed < 1_200).ToListAsync();
                result.RangeMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                if (!expectedRange.SequenceEqual(range.Select(row => row.Id).Order()))
                    throw new InvalidOperationException("The " + mode + " benchmark range returned incorrect values.");
                if (verifyNativeExecution is not null) await verifyNativeExecution();

                watch.Restart();
                var ordered = await query.OrderBy(row => row.Amount).ToListAsync();
                result.OrderMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                if (!expectedOrder.SequenceEqual(ordered.Select(row => row.Id)))
                    throw new InvalidOperationException("The " + mode + " benchmark ordering returned incorrect values.");
                if (verifyNativeExecution is not null) await verifyNativeExecution();
            }

            results.Add(result);
        }
        return results;
    }
}

public sealed class NumericBenchmarkResult
{
    public string Mode { get; set; } = "";
    public int RowCount { get; set; }
    public int Repetitions { get; set; }
    public int SerializedUtf8Bytes { get; set; }
    public List<double> WriteMilliseconds { get; set; } = [];
    public List<double> RangeMilliseconds { get; set; } = [];
    public List<double> OrderMilliseconds { get; set; } = [];
    public double MedianWriteMilliseconds => Median(WriteMilliseconds);
    public double MedianRangeMilliseconds => Median(RangeMilliseconds);
    public double MedianOrderMilliseconds => Median(OrderMilliseconds);
    private static double Median(List<double> samples) => samples.Count == 0 ? 0 : samples.Order().ElementAt(samples.Count / 2);
}
