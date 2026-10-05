namespace NotificationHistory.Core.Services;

public readonly record struct IconDrawRectangle(double X, double Y, double Width, double Height);
public static class IconCropGeometry
{
    public static IconDrawRectangle DrawRectangle(double width, double height, double viewport,
        double zoom, double offsetX, double offsetY, double output = 512)
    {
        if (new[] { width, height, viewport, zoom, offsetX, offsetY, output }.Any(x => !double.IsFinite(x)) ||
            width <= 0 || height <= 0 || viewport <= 0 || zoom <= 0 || output <= 0)
            throw new ArgumentException("Invalid image crop dimensions.");
        zoom = Math.Max(zoom, Math.Max(viewport / width, viewport / height));
        offsetX = Math.Clamp(offsetX, 0, Math.Max(0, width * zoom - viewport));
        offsetY = Math.Clamp(offsetY, 0, Math.Max(0, height * zoom - viewport));
        var scale = output / viewport;
        return new(-offsetX * scale, -offsetY * scale, width * zoom * scale, height * zoom * scale);
    }
}
