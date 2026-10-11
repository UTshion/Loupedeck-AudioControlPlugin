namespace Loupedeck.AudioControlPlugin;

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

internal class ActionImageStore<T> where T : IActionImageData
{
    private readonly IActionImageFactory<T> actionImageFactory;
    private readonly ConcurrentDictionary<string, T> actionImageData;
    private readonly ConcurrentDictionary<string, IActionImageFactory<T>> actionImageFactories;
    private readonly ConcurrentDictionary<string, BitmapImage> actionWidth60Images;
    private readonly ConcurrentDictionary<string, BitmapImage> actionWidth90Images;

    public ICollection<string> ActionImageIds
    {
        get
        {
            return this.actionImageData.Keys;
        }
    }

    public ActionImageStore(IActionImageFactory<T> actionImageFactory)
    {
        this.actionImageFactory = actionImageFactory;
        this.actionImageData = new ConcurrentDictionary<string, T>();
        this.actionImageFactories = new ConcurrentDictionary<string, IActionImageFactory<T>>();
        this.actionWidth60Images = new ConcurrentDictionary<string, BitmapImage>();
        this.actionWidth90Images = new ConcurrentDictionary<string, BitmapImage>();
    }

    public bool TryGetImage(string id, PluginImageSize imageSize, [NotNullWhen(true)] out BitmapImage? bitmapImage)
    {
        ConcurrentDictionary<string, BitmapImage> actionImages;
        if (imageSize == PluginImageSize.Width60)
        {
            actionImages = this.actionWidth60Images;
        }
        else
        {
            actionImages = this.actionWidth90Images;
        }
        bool exist = actionImages.TryGetValue(id, out BitmapImage? storedBitmapImage);
        if (exist && storedBitmapImage is not null)
        {
            bitmapImage = BitmapImage.FromArray(storedBitmapImage.ToArray());
            return true;
        }
        else
        {
            bitmapImage = null;
            return false;
        }
    }

    public bool UpdateImage(string id, T newActionData)
    {
        bool exist = this.actionImageData.TryGetValue(id, out T? lastActionData);
        if (!exist || !newActionData.Equals(lastActionData))
        {
            // The value factory overload matters: passing Create() directly would build (and leak) a new
            // factory with its bitmaps, fonts and brushes on every redraw.
            IActionImageFactory<T> factory = this.actionImageFactories.GetOrAdd(id, _ => this.actionImageFactory.Create());

            BitmapImage bitmapWidth60Image = factory.DrawBitmapImage(newActionData, PluginImageSize.Width60);
            BitmapImage bitmapWidth90Image = factory.DrawBitmapImage(newActionData, PluginImageSize.Width90);

            // Store the data last, so that a failed draw is retried on the next refresh.
            this.actionWidth60Images[id] = bitmapWidth60Image;
            this.actionWidth90Images[id] = bitmapWidth90Image;
            this.actionImageData[id] = newActionData;

            return true;
        }
        return false;
    }

    public bool UpdateImage(T newActionData)
    {
        return this.UpdateImage(newActionData.Id, newActionData);
    }

    // Drops everything not in the given ids (sessions come and go; each id owns a factory with GDI+ resources).
    public void Retain(IEnumerable<string> ids)
    {
        HashSet<string> keep = new HashSet<string>(ids);
        foreach (string id in this.actionImageFactories.Keys.Concat(this.actionImageData.Keys).Distinct().ToArray())
        {
            if (!keep.Contains(id))
            {
                this.Remove(id);
            }
        }
    }

    public void Clear()
    {
        this.Retain(Array.Empty<string>());
    }

    public void Remove(string id)
    {
        this.actionImageData.TryRemove(id, out _);
        this.actionWidth60Images.TryRemove(id, out _);
        this.actionWidth90Images.TryRemove(id, out _);
        if (this.actionImageFactories.TryRemove(id, out IActionImageFactory<T>? factory))
        {
            factory.Dispose();
        }
    }
}
