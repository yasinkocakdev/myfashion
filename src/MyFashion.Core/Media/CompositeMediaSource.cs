using System.Runtime.CompilerServices;

namespace MyFashion.Core.Media;

/// <summary>Reads several sources one after another, for example API posts/reels plus a folder of highlight recordings.</summary>
public sealed class CompositeMediaSource(IReadOnlyList<IMediaSource> sources) : IMediaSource
{
    public async IAsyncEnumerable<MediaFile> GetMediaAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            await foreach (var file in source.GetMediaAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return file;
            }
        }
    }
}
