namespace Loupedeck.AudioControlPlugin;

internal interface IActionImageFactory<T> : IDisposable where T : IActionImageData
{
    IActionImageFactory<T> Create();
    BitmapImage DrawBitmapImage(T actionImageData, PluginImageSize imageSize);
}
