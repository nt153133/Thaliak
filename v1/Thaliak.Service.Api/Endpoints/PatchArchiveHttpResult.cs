using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Thaliak.Service.Api.Services;

namespace Thaliak.Service.Api.Endpoints;

internal sealed class PatchArchiveHttpResult(PatchArchiveLookup lookup) : IResult
{
    private const string ContentType = "application/octet-stream";
    private const int CopyBufferSize = 128 * 1024;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        var response = httpContext.Response;
        var request = httpContext.Request;
        var file = lookup.File;

        response.Headers.AcceptRanges = "bytes";
        response.Headers.CacheControl = "public, max-age=31536000, immutable";
        response.Headers.LastModified = file.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);
        response.Headers.ETag = $"W/\"{file.Length:x}-{file.LastWriteTimeUtc.Ticks:x}\"";
        response.ContentType = ContentType;

        var ranges = ParseRanges(request.Headers.Range, file.Length);
        if (ranges is null) {
            response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            response.Headers.ContentRange = $"bytes */{file.Length}";
            return;
        }

        if (ranges.Count == 0) {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentLength = file.Length;
            if (!HttpMethods.IsHead(request.Method)) {
                await using var stream = OpenFile(file.FullName);
                await stream.CopyToAsync(response.Body, httpContext.RequestAborted);
            }
            return;
        }

        if (ranges.Count == 1) {
            var range = ranges[0];
            response.StatusCode = StatusCodes.Status206PartialContent;
            response.Headers.ContentRange = $"bytes {range.From}-{range.To}/{file.Length}";
            response.ContentLength = range.Length;
            if (!HttpMethods.IsHead(request.Method)) {
                await using var stream = OpenFile(file.FullName);
                await CopyRangeAsync(stream, response.Body, range, httpContext.RequestAborted);
            }
            return;
        }

        response.StatusCode = StatusCodes.Status206PartialContent;
        var boundary = $"thaliak-{Guid.NewGuid():N}";
        response.ContentType = $"multipart/byteranges; boundary={boundary}";
        if (HttpMethods.IsHead(request.Method)) {
            return;
        }

        await using var patchStream = OpenFile(file.FullName);
        foreach (var range in ranges) {
            await WriteAsciiAsync(
                response.Body,
                $"--{boundary}\r\nContent-Type: {ContentType}\r\n"
                + $"Content-Range: bytes {range.From}-{range.To}/{file.Length}\r\n\r\n",
                httpContext.RequestAborted);
            await CopyRangeAsync(patchStream, response.Body, range, httpContext.RequestAborted);
            await WriteAsciiAsync(response.Body, "\r\n", httpContext.RequestAborted);
        }
        await WriteAsciiAsync(response.Body, $"--{boundary}--\r\n", httpContext.RequestAborted);
    }

    private static IReadOnlyList<RequestedRange>? ParseRanges(string rangeHeader, long fileLength)
    {
        if (string.IsNullOrWhiteSpace(rangeHeader)) {
            return [];
        }
        if (!RangeHeaderValue.TryParse(rangeHeader, out var parsed)
            || !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase)
            || parsed.Ranges.Count > PatchArchiveService.MaxRangesPerRequest) {
            return null;
        }

        var ranges = new List<RequestedRange>(parsed.Ranges.Count);
        long totalBytes = 0;
        foreach (var item in parsed.Ranges) {
            long from;
            long to;
            if (item.From.HasValue) {
                from = item.From.Value;
                if (from >= fileLength) {
                    return null;
                }
                to = Math.Min(item.To ?? fileLength - 1, fileLength - 1);
            }
            else if (item.To is > 0) {
                var suffixLength = Math.Min(item.To.Value, fileLength);
                from = fileLength - suffixLength;
                to = fileLength - 1;
            }
            else {
                return null;
            }

            if (to < from) {
                return null;
            }

            var range = new RequestedRange(from, to);
            totalBytes = checked(totalBytes + range.Length);
            if (totalBytes > PatchArchiveService.MaxRequestBytes) {
                return null;
            }
            ranges.Add(range);
        }
        return ranges;
    }

    private static FileStream OpenFile(string path) =>
        new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);

    private static async Task CopyRangeAsync(
        FileStream source,
        Stream destination,
        RequestedRange range,
        CancellationToken cancellationToken)
    {
        source.Position = range.From;
        var remaining = range.Length;
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try {
            while (remaining > 0) {
                var requested = (int)Math.Min(buffer.Length, remaining);
                var read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read == 0) {
                    throw new EndOfStreamException("Patch archive ended before the requested range.");
                }
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }
        }
        finally {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Task WriteAsciiAsync(
        Stream destination,
        string value,
        CancellationToken cancellationToken) =>
        destination.WriteAsync(Encoding.ASCII.GetBytes(value), cancellationToken).AsTask();

    private readonly record struct RequestedRange(long From, long To)
    {
        public long Length => To - From + 1;
    }
}
