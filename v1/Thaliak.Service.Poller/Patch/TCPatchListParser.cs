using System.Globalization;

namespace Thaliak.Service.Poller.Patch;

public static class TCPatchListParser
{
    public static PatchListEntry[] Parse(string list)
    {
        var lines = list.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var output = new List<PatchListEntry>();

        foreach (var line in lines) {
            var fields = line.Split('\t');
            if (fields.Length != 9) {
                continue;
            }

            var checksum = ulong.Parse(fields[7], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            output.Add(new PatchListEntry
            {
                Length = long.Parse(fields[0], CultureInfo.InvariantCulture),
                VersionId = fields[4],
                HashType = fields[5],
                HashBlockSize = 0,
                Hashes = [checksum.ToString("X8", CultureInfo.InvariantCulture).ToLowerInvariant()],
                Url = fields[8]
            });
        }

        return output.ToArray();
    }
}
